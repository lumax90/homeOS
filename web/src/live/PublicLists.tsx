import {useState} from 'react';
import {Button} from '@heroui/react';
import {useMutation,useQuery} from '@tanstack/react-query';
import {Check,ChevronRight,Globe2,Layers3,LoaderCircle,Plus,SatelliteDish} from 'lucide-react';
import {request,mutate} from '../api';
import {runtimeText,translate as t,useUiLanguage} from '../i18n';
import {Dialog} from '../components/Dialogs';
import {useSession} from '../components/SessionContext';
import {tvGroup,useLiveRefresh} from './LiveShared';
import type{TvPublicList}from'./types';

export function PublicLists({onAdded,onCustom}:{onAdded:()=>void;onCustom:()=>void}){
  useUiLanguage();const session=useSession(),refresh=useLiveRefresh(),[category,setCategory]=useState('country'),[preview,setPreview]=useState<TvPublicList|null>(null);
  const lists=useQuery({queryKey:['live-public-lists'],queryFn:({signal})=>request<TvPublicList[]>('/live/public-lists',{signal}),enabled:session.role==='admin'});
  const add=useMutation({mutationFn:(id:string)=>mutate(`/live/public-lists/${id}/add`),onSuccess:async()=>{await refresh();setPreview(null);onAdded();}});
  if(session.role!=='admin')return <div className="live-empty"><SatelliteDish size={35}/><h2>Kanallarına yer aç.</h2><p>Yöneticinin bağladığı TV kaynakları burada paylaşılır.</p></div>;
  return <section className="live-public-lists"><header className="live-section-heading"><div><p className="eyebrow">AÇIK KANAL LİSTELERİ</p><h2>Dünyaya bir pencere.</h2><p>IPTV-org’un herkese açık kanal listelerinden seç.</p></div><Button variant="secondary" onPress={onCustom}><Plus size={17}/> IPTV bağlantısı ekle</Button></header>
    <div className="live-public-tabs"><Button variant={category==='country'?'secondary':'ghost'} aria-pressed={category==='country'} onPress={()=>setCategory('country')}><Globe2 size={17}/> Ülkeler</Button><Button variant={category==='category'?'secondary':'ghost'} aria-pressed={category==='category'} onPress={()=>setCategory('category')}><Layers3 size={17}/> Kategoriler</Button></div>
    <div className="live-public-grid">{lists.data?.filter(row=>row.list.category===category).map(row=><article key={row.list.id} className="live-public-card" data-preset={row.list.id}><div className="live-public-art" aria-hidden="true">{row.list.category==='country'?<Globe2 size={45} strokeWidth={1}/>:<SatelliteDish size={45} strokeWidth={1}/>}<span>{row.list.id.replace('iptv-','').toUpperCase()}</span></div><div className="live-public-copy"><strong>{t(row.list.name)}</strong><p>{t(row.list.description)}</p><small>{row.source?t('{{v0}} kanal · Kaynak kayıtlı',{v0:row.source.channels}):'IPTV-org'}</small></div><div className="live-public-actions"><Button variant="ghost" onPress={()=>setPreview(row)}>Listeye göz at <ChevronRight size={15}/></Button><Button isIconOnly variant="secondary" aria-label={t('Listeyi ekle: {{v0}}',{v0:t(row.list.name)})} isDisabled={!!row.source||add.isPending} onPress={()=>add.mutate(row.list.id)}>{row.source?<Check size={18}/>:add.isPending&&add.variables===row.list.id?<LoaderCircle className="spin" size={18}/>:<Plus size={18}/>}</Button></div></article>)}</div>
    {lists.isPending&&<p className="live-inline-note"><LoaderCircle size={17} className="spin"/> Listeler yükleniyor…</p>}{(lists.error||add.error)&&<p className="form-error">{runtimeText((lists.error??add.error)!.message)}</p>}
    <p className="live-source-note"><a href="https://github.com/iptv-org/iptv" target="_blank" rel="noreferrer">IPTV-org · Unlicense</a> · {t('Kanal listeleri güncellenebilir; yayınların erişimi bölgeye ve yayıncıya bağlıdır.')} {t('Listelerden HTTP/HTTPS yayın adresleri alınır.')}</p>
    {preview&&<PublicPreview item={preview} onClose={()=>setPreview(null)} onAdd={()=>add.mutate(preview.list.id)} pending={add.isPending} error={add.error}/>} 
  </section>;
}
function PublicPreview({item,onClose,onAdd,pending,error}:{item:TvPublicList;onClose:()=>void;onAdd:()=>void;pending:boolean;error:Error|null}){
  const query=useQuery({queryKey:['live-public-preview',item.list.id],queryFn:({signal})=>request<{total:number;groups:{name:string;count:number}[];channels:{name:string;group:string}[]}>(`/live/public-lists/${item.list.id}`,{signal}),staleTime:1800000});
  return <Dialog open onClose={onClose} title={t(item.list.name)} className="live-preview-container"><div className="live-surface live-preview"><p className="eyebrow">IPTV-ORG</p><h2>{t(item.list.name)}</h2><p className="live-help">{t(item.list.description)}</p>{query.isPending&&<p className="live-inline-note"><LoaderCircle className="spin" size={18}/> Kanal listesi okunuyor…</p>}{query.data&&<><strong className="live-preview-count">{query.data.total} kanal</strong><div className="live-preview-groups">{query.data.groups.map(group=><span key={group.name}>{tvGroup(group.name)} · {group.count}</span>)}</div><div className="live-preview-channels">{query.data.channels.map((channel,index)=><div key={index}><span>{index+1}</span><strong title={channel.name}>{channel.name}</strong><small>{tvGroup(channel.group)}</small></div>)}</div><p className="live-help">Önizleme yalnız kanal adlarını okur. İzle demeden yayın başlamaz.</p></>}{(query.error||error)&&<p className="form-error">{runtimeText((query.error??error)!.message)}</p>}<Button variant="primary" onPress={onAdd} isDisabled={pending||!!item.source||!query.data}>{item.source?<Check size={17}/>:<Plus size={17}/>} {item.source?'Kaynak kayıtlı':'Listeyi kanallarıma ekle'}</Button></div></Dialog>;
}
