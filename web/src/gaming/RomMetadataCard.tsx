import {useState} from 'react';
import {Button} from '@heroui/react';
import {useMutation,useQuery,useQueryClient} from '@tanstack/react-query';
import {RefreshCw,Search,Check,LoaderCircle,Settings2,CheckCircle2,Info} from 'lucide-react';
import {mutate,request} from '../api';
import {runtimeText,translate as t,useUiLanguage} from '../i18n';
import type {Game,RomMetadataCandidate,RomMetadataDocument} from './model';
import './game-settings.css';

interface MetadataStatus{data:RomMetadataDocument|null;candidates:RomMetadataCandidate[];status:string|null;message:string|null}
export function RomMetadataCard({game}:{game:Game}){
  useUiLanguage();const cache=useQueryClient(),[matching,setMatching]=useState(false),[query,setQuery]=useState(game.title);
  const info=useQuery({queryKey:['games','metadata',game.id],queryFn:()=>request<MetadataStatus>(`/games/${game.id}/metadata`),refetchInterval:query=>['queued','running'].includes(query.state.data?.status??'')?2500:false});
  const refresh=useMutation({mutationFn:()=>mutate(`/games/${game.id}/metadata/refresh`),onSuccess:()=>cache.invalidateQueries({queryKey:['games']})});
  const search=useMutation({mutationFn:()=>mutate<RomMetadataCandidate[]>(`/games/${game.id}/metadata/search`,{query}),onSuccess:()=>cache.invalidateQueries({queryKey:['games','metadata',game.id]})});
  const select=useMutation({mutationFn:(id:number)=>mutate(`/games/${game.id}/metadata`,{id,revision:info.data?.data?.revision??0},'PUT'),onSuccess:async()=>{await cache.invalidateQueries({queryKey:['games']});setMatching(false);}});
  const metadata=info.data?.data??game.metadata,busy=['queued','running'].includes(info.data?.status??''),attention=!metadata&&!busy&&!!info.data?.candidates.length;
  return <section className="rom-metadata-card"><header><h2>Oyun hakkında</h2><Button isIconOnly variant="ghost" size="sm" aria-label="Oyun eşleşmesini düzenle" aria-expanded={matching} onPress={()=>setMatching(value=>!value)}><Settings2 size={17}/></Button></header>
    {metadata?<><div className="rom-metadata-facts">{metadata.game.publisher&&<span>{t('Yayıncı')}: {metadata.game.publisher}</span>}{metadata.game.year&&<span>{t('Yıl')}: {metadata.game.year}</span>}{metadata.game.regions.length>0&&<span>{t('Bölge')}: {metadata.game.regions.join(', ')}</span>}{metadata.game.languages.length>0&&<span>{t('Dil')}: {metadata.game.languages.join(', ')}</span>}</div>{metadata.game.description?<p className="rom-metadata-synopsis">{metadata.game.description}</p>:<p className="rom-metadata-synopsis">Bu oyun tanındı; kaynakta henüz ayrıntılı bir açıklama yok.</p>}<p className="games-inline-status"><CheckCircle2 size={15}/>{metadata.provider} · {metadata.matchMethod==='hash'?t('Dosya özetiyle eşleşti'):metadata.matchMethod==='name'?t('Oyun adı ve konsolla otomatik eşleşti'):t('Eşleşmeyi sen seçtin')}</p></>:
      <div className="rom-metadata-state">{busy?<LoaderCircle className="spin" size={18}/>:<Info size={18}/>}<span>{busy?t('Oyun bilgisi ve kapağı arka planda hazırlanıyor.'):attention?t('Bu oyun için birkaç olası eşleşme var. Bir kez incelemen gerekiyor.'):t('Otomatik kütüphane taraması bu oyunun bilgilerini tamamlayacak.')}</span></div>}
    {attention&&!matching&&<div className="rom-metadata-tools"><Button variant="secondary" onPress={()=>setMatching(true)}><Search size={16}/> Eşleşmeyi incele</Button></div>}
    {info.data?.status==='failed'&&<p className="games-help">{runtimeText(info.data.message)} {t('Kütüphane taraması yeniden deneyecek.')}</p>}{info.error&&<p className="form-error">{runtimeText(info.error.message)}</p>}
    {matching&&<section className="rom-metadata-matching"><form onSubmit={event=>{event.preventDefault();if(query.trim().length>=3)search.mutate();}}><label className="games-field">Oyun adı<input value={query} maxLength={160} onChange={event=>setQuery(event.target.value)}/></label><Button type="submit" variant="secondary" isDisabled={query.trim().length<3||search.isPending}><Search size={15}/> Ara</Button></form>
      <div className="rom-metadata-results">{info.data?.candidates.map(candidate=><article className="rom-metadata-result" key={candidate.id}><div><strong>{candidate.name}</strong><small>{candidate.platform}{candidate.year?' · '+candidate.year:''}{candidate.regions.length?' · '+candidate.regions.join(', '):''}{candidate.edition?' · '+candidate.edition:''}</small><small>{candidate.hashMatch?t('Dosya özetiyle eşleşti'):t('Ada göre öneri; doğrulayıp seç')}</small></div><Button variant="secondary" isDisabled={select.isPending} onPress={()=>select.mutate(candidate.id)}>{select.isPending?<LoaderCircle className="spin" size={15}/>:<Check size={15}/>} Bu oyun</Button></article>)}</div>
      <div className="rom-metadata-tools"><Button variant="ghost" size="sm" isDisabled={busy||refresh.isPending} onPress={()=>refresh.mutate()}><RefreshCw size={15}/> Bu oyunu yeniden eşleştir</Button><Button variant="ghost" size="sm" onPress={()=>setMatching(false)}>Kapat</Button></div>
      {(refresh.error||search.error||select.error)&&<p className="form-error" role="alert">{runtimeText((refresh.error??search.error??select.error)!.message)}</p>}
      <p className="games-help">Eşleşmeyi değiştirmek ROM dosyanı veya oyun kayıtlarını değiştirmez.</p>
    </section>}
  </section>;
}
