import { useEffect, useRef, useState, type ChangeEvent } from 'react';
import { Button } from '@heroui/react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ArrowLeft, ArrowRight, Check, ExternalLink, Globe, KeyRound, LoaderCircle, Radio, Search, X } from 'lucide-react';
import { date, mutate, request, randomId, type IndexerInfo } from '../api';
import './source-catalog.css';
import {runtimeText} from '../i18n';

interface Provider { id:string; name:string; description:string; protocol:string; access:string; kinds:string[]; defaultEndpoint:string|null; adapterId:string|null; homepage:string; helpUrl:string; requiresApiKey:boolean; installed:boolean }
interface Network { proxyUrl:string|null; proxyUsername:string|null; proxyPassword?:string|null; cookie?:string|null; headers?:Record<string,string>|null; dailyQueries:number; dailyGrabs:number }
interface Editing { provider:Provider; indexer:IndexerInfo; network:Network; apiKeyConfigured:boolean; proxyPasswordConfigured:boolean; cookieConfigured:boolean; headersConfigured:boolean }
interface Preview { id:string; providerId:string; name:string; endpoint:string; status:string; revision:number; expiresAt:string; searchTypes:string[]; categoryCount:number; error:string|null; indexerId:string|null; needsUsenetServer:boolean }
const kindNames:Record<string,string>={movie:'Film',show:'Dizi',anime:'Anime',music:'Müzik',book:'Kitap'};
const searchNames:Record<string,string>={search:'Genel arama','movie-search':'Film','tv-search':'Dizi','music-search':'Müzik','audio-search':'Ses','book-search':'Kitap'};
type Common={onDone:()=>void;notify:(message:string)=>void;onUsenet:()=>void};

export function SourceCatalogPicker({sources,...actions}:Common&{sources:IndexerInfo[]}){
  const [selected,setSelected]=useState<Provider|null>(null);const [query,setQuery]=useState('');const [family,setFamily]=useState('all');const [kind,setKind]=useState('all');
  const catalog=useQuery({queryKey:['source-catalog'],queryFn:({signal})=>request<Provider[]>('/source-catalog',{signal}),staleTime:5*60_000});
  if(selected)return <SourceConnectionForm key={selected.id} provider={selected} onBack={()=>setSelected(null)} {...actions}/>;
  const rows=(catalog.data??[]).filter(value=>(family==='all'||value.protocol===family)&&(kind==='all'||value.kinds.includes(kind))&&(value.name+' '+runtimeText(value.description)).toLocaleLowerCase('tr').includes(query.toLocaleLowerCase('tr')));
  return <section className="source-catalog"><div className="settings-heading"><div><p className="eyebrow">1 · SİTE SEÇ</p><h3>İsteklerini nerede arayalım?</h3></div><Button isIconOnly variant="ghost" aria-label="Kaynak seçimini kapat" onPress={actions.onDone}><X size={17}/></Button></div>
    <p className="field-hint">Siteyi seç; bağlantı adresini ve yöntemini Home hazırlasın. Üyelikli sitelerde kendi hesabının API anahtarı gerekir.</p>
    <div className="source-catalog-filters"><div className="search-box"><Search size={16}/><input aria-label="Kaynak sitesi ara" placeholder="Site adı veya içerik türü…" value={query} onChange={event=>setQuery(event.target.value)}/></div><label><span className="sr-only">Kaynak ailesi</span><select value={family} onChange={event=>setFamily(event.target.value)}><option value="all">Tüm kaynaklar</option><option value="torznab">Torrent</option><option value="newznab">Usenet</option></select></label><label><span className="sr-only">Aranacak içerik</span><select value={kind} onChange={event=>setKind(event.target.value)}><option value="all">Tüm içerikler</option>{Object.entries(kindNames).map(([id,name])=><option key={id} value={id}>{name}</option>)}</select></label></div>
    {catalog.isPending?<p className="field-hint" role="status">Kaynak listesi yükleniyor…</p>:<><p className="field-hint">{rows.length} hazır bağlantı</p><div className="source-provider-grid">{rows.map(provider=>{
      const connected=sources.some(source=>source.providerId===provider.id);
      return <button type="button" className="source-provider-card" key={provider.id} disabled={connected} onClick={()=>setSelected(provider)}><div><span className="source-symbol">{provider.requiresApiKey?<KeyRound size={20}/>:<Globe size={20}/>}</span><strong>{provider.name}</strong>{connected?<span className="source-connected"><Check size={13}/> Bağlı</span>:<ArrowRight size={16}/>}</div><p>{runtimeText(provider.description)}</p><div className="source-provider-tags"><span>{provider.protocol==='torznab'?'Torrent':'Usenet'}</span><span>{provider.requiresApiKey?'Üyelik / API anahtarı':'Üyeliksiz'}</span>{provider.installed&&<span>Yüklenen adaptör</span>}{provider.kinds.slice(0,4).map(value=><span key={value}>{kindNames[value]??value}</span>)}</div></button>;
    })}</div>{rows.length===0&&<p className="field-hint">Bu filtreyle kaynak bulunamadı. Aramayı veya içerik türünü değiştirebilirsin.</p>}</>}
    {catalog.error&&<p className="form-error" role="alert">{catalog.error.message}</p>}
  </section>;
}
export function SourceConnectionEditor({id,...actions}:Common&{id:string}){
  const edit=useQuery({queryKey:['source-edit',id],queryFn:({signal})=>request<Editing>(`/indexers/${id}/setup`,{signal})});
  return edit.data?<SourceConnectionForm key={id+':'+edit.data.indexer.revision} provider={edit.data.provider} initial={edit.data} onBack={actions.onDone} {...actions}/>:<section className="source-catalog"><Button variant="ghost" onPress={actions.onDone}><ArrowLeft size={15}/> Geri</Button>{edit.error?<p className="form-error">{edit.error.message}</p>:<p className="field-hint">Kaynak ayarları yükleniyor…</p>}</section>;
}
function SourceConnectionForm({provider,initial,onBack,onDone,notify,onUsenet}:Common&{provider:Provider;initial?:Editing;onBack:()=>void}){
  const client=useQueryClient();const [name,setName]=useState(initial?.indexer.name??provider.name);const [endpoint,setEndpoint]=useState(initial?.indexer.endpoint??provider.defaultEndpoint??'');const [apiKey,setApiKey]=useState('');
  const [enabled,setEnabled]=useState(initial?.indexer.enabled??true);const [priority,setPriority]=useState(initial?.indexer.priority??25);const [proxy,setProxy]=useState(initial?.network.proxyUrl??'');const [username,setUsername]=useState(initial?.network.proxyUsername??'');const [password,setPassword]=useState('');const [cookie,setCookie]=useState('');
  const [preview,setPreview]=useState<Preview|null>(null);const [error,setError]=useState('');const [now,setNow]=useState(Date.now());
  const controller=useRef<AbortController|null>(null);const command=useRef<string|null>(null);const generation=useRef(0);const alive=useRef(true);const saved=useRef(false);const heading=useRef<HTMLHeadingElement>(null);
  const cancelCurrent=()=>{generation.current++;controller.current?.abort();controller.current=null;const id=command.current;command.current=null;if(id&&!saved.current)void mutate(`/source-setups/${id}`,undefined,'DELETE',true).catch(()=>{});};
  const changed=()=>{cancelCurrent();setPreview(null);setError('');};
  useEffect(()=>{alive.current=true;heading.current?.focus();return()=>{alive.current=false;cancelCurrent();};},[]);
  useEffect(()=>{if(preview?.status!=='ready')return;const timer=setInterval(()=>setNow(Date.now()),15000);return()=>clearInterval(timer);},[preview?.id,preview?.status]);
  const test=useMutation({mutationFn:async()=>{
    if(preview?.status==='ready'){cancelCurrent();setPreview(null);}
    const revision=++generation.current;const abort=new AbortController();controller.current=abort;const id=command.current??=randomId();setError('');
    try{
      const value=await request<Preview>('/source-setups/test',{method:'POST',signal:abort.signal,body:JSON.stringify({commandId:id,providerId:provider.id,name,endpoint:endpoint||null,apiKey:apiKey||null,priority,enabled,existingIndexerId:initial?.indexer.id??null,expectedRevision:initial?.indexer.revision??null,
        network:{proxyUrl:proxy||null,proxyUsername:username||null,proxyPassword:password||null,cookie:cookie||null,headers:null,dailyQueries:initial?.network.dailyQueries??1000,dailyGrabs:initial?.network.dailyGrabs??100}})});
      if(alive.current&&generation.current===revision){setPreview(value);setNow(Date.now());if(value.status==='failed')setError(value.error??'Bağlantı testi tamamlanamadı.');}
    }catch(cause){if(alive.current&&generation.current===revision&&!abort.signal.aborted)setError((cause as Error).message);}
    finally{if(controller.current===abort)controller.current=null;}
  }});
  const connect=useMutation({mutationFn:async()=>{
    if(!preview||preview.status!=='ready')throw new Error('Önce bağlantıyı test et.');
    return mutate<IndexerInfo>('/source-setups/connect',{id:preview.id,revision:preview.revision});
  },onSuccess:async value=>{
    saved.current=true;command.current=null;await Promise.all(['indexers','source-edit','source-transport','source-catalog','release-search','music-release-search'].map(key=>client.invalidateQueries({queryKey:[key]})));
    notify(value.name+(preview?.needsUsenetServer?' bağlandı. İndirme için Usenet sunucu hesabını da tanımla.':' bağlandı. İsteğinin kaynak aramasına dönebilirsin.'));
    onDone();if(preview?.needsUsenetServer)onUsenet();
  },onError:cause=>setError(cause.message)});
  const ready=preview?.status==='ready'&&Date.parse(preview.expiresAt)>now;const needsKey=provider.requiresApiKey&&!apiKey&&!initial?.apiKeyConfigured;
  const field=(setter:(value:string)=>void)=>(event:ChangeEvent<HTMLInputElement>)=>{changed();setter(event.target.value);};
  return <section className="source-catalog"><Button variant="ghost" size="sm" onPress={()=>{cancelCurrent();onBack();}} isDisabled={connect.isPending}><ArrowLeft size={15}/> {initial?'Kaynaklara dön':'Başka site seç'}</Button>
    <div className="source-setup-heading"><span className="source-symbol"><Radio size={23}/></span><div><p className="eyebrow">2 · BAĞLANTIYI HAZIRLA</p><h3 ref={heading} tabIndex={-1}>{provider.name}</h3></div><span className="field-hint">{provider.protocol==='torznab'?'Torrent':'Usenet'}</span></div><p className="field-hint">{runtimeText(provider.description)}</p>
    {!!provider.homepage&&<a className="content-source" href={provider.helpUrl||provider.homepage} target="_blank" rel="noreferrer">{provider.requiresApiKey?'Site hesabını / API bilgilerini aç':'Kaynağın sitesini aç'} <ExternalLink size={13}/></a>}
    <form className="home-form source-setup-form" onSubmit={event=>{event.preventDefault();test.mutate();}}>
      {(provider.requiresApiKey||provider.access==='custom')&&<label className="field"><span>{provider.name} API anahtarı{provider.requiresApiKey?' *':' · gerekiyorsa'}</span><input type="password" autoComplete="off" maxLength={4096} value={apiKey} onChange={field(setApiKey)} disabled={connect.isPending} placeholder={initial?.apiKeyConfigured?'Kayıtlı · boşsa korunur':'Sitedeki hesabının API anahtarı'}/><small className="field-hint">Home giriş şifreni değil, bu sitenin hesabındaki API anahtarını kullan.</small></label>}
      {!provider.requiresApiKey&&provider.access!=='custom'&&<p className="source-public-message"><Check size={16}/> Bu kaynak için hesap veya API anahtarı gerekmiyor.</p>}
      {!provider.defaultEndpoint&&<label className="field"><span>Kaynağın bağlantı adresi</span><input type="url" required value={endpoint} onChange={field(setEndpoint)} disabled={connect.isPending} autoComplete="off" spellCheck={false}/></label>}
      <details className="advanced-fields"><summary>Ad, bağlantı ve gelişmiş tercihler</summary><div className="home-form"><label className="field"><span>Home'da görünen ad</span><input value={name} onChange={field(setName)} maxLength={80} disabled={connect.isPending}/></label>{provider.defaultEndpoint&&<label className="field"><span>Otomatik bağlantı adresi</span><input type="url" value={endpoint} onChange={field(setEndpoint)} autoComplete="off" spellCheck={false} disabled={connect.isPending}/></label>}<label className="field"><span>Öncelik · 1 en yüksek</span><input type="number" min={1} max={50} value={priority} onChange={event=>{changed();setPriority(Number(event.target.value));}} disabled={connect.isPending}/></label><label className="check-field"><input type="checkbox" checked={enabled} onChange={event=>{changed();setEnabled(event.target.checked);}} disabled={connect.isPending}/> Aramalarda etkin</label>
      <label className="field"><span>Proxy adresi · gerekiyorsa</span><input value={proxy} onChange={field(setProxy)} placeholder="socks5://127.0.0.1:1080" disabled={connect.isPending}/></label><div className="form-columns"><label className="field"><span>Proxy kullanıcısı</span><input value={username} onChange={field(setUsername)} autoComplete="off" disabled={connect.isPending}/></label><label className="field"><span>Proxy parolası</span><input type="password" value={password} onChange={field(setPassword)} autoComplete="off" disabled={connect.isPending} placeholder={initial?.proxyPasswordConfigured?'Kayıtlı · boşsa korunur':''}/></label></div><label className="field"><span>Kaynak oturum cookie'si · gerekiyorsa</span><input type="password" value={cookie} onChange={field(setCookie)} autoComplete="off" maxLength={8192} disabled={connect.isPending} placeholder={initial?.cookieConfigured?'Kayıtlı · boşsa korunur':''}/></label></div></details>
      <Button className="secondary-button" type="submit" isDisabled={test.isPending||connect.isPending||!endpoint||needsKey}>{test.isPending?<><LoaderCircle className="spin" size={16}/> Bağlantı kontrol ediliyor…</>:<><Check size={16}/> {ready?'Bağlantıyı yeniden test et':'Bağlantıyı test et'}</>}</Button>
      {test.isPending&&<Button variant="ghost" onPress={()=>changed()} isDisabled={connect.isPending}>Testi iptal et</Button>}
      {ready&&preview&&<div className="source-setup-ready" role="status"><p className="eyebrow">3 · KAYNAĞI EKLE</p><strong><Check size={17}/> Bağlantı ve arama doğrulandı</strong><p>{preview.searchTypes.map(value=>searchNames[value]??value).join(' · ')}{preview.categoryCount?` · ${preview.categoryCount} kaynak kategorisi`:''}</p><small>{preview.endpoint}<br/>Test geçerliliği: {date(preview.expiresAt)}</small>{preview.needsUsenetServer&&<p>Bu kaynak Usenet araması sağlar. Ekledikten sonra dosya aktarımı için Usenet sunucu hesabını tanımlayabilirsin.</p>}<Button className="primary-button" onPress={()=>connect.mutate()} isDisabled={connect.isPending}>{connect.isPending?'Kaydediliyor…':initial?'Değişiklikleri uygula':'Kaynağı ekle'}<ArrowRight size={16}/></Button></div>}
      {preview?.status==='ready'&&!ready&&<p className="field-hint">Bağlantı testinin süresi doldu. Yeniden test ederek devam et.</p>}
      {error&&<p className="form-error" role="alert">{error}</p>}
    </form>
  </section>;
}
