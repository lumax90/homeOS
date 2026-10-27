import {useEffect,useState}from'react';
import {useQuery}from'@tanstack/react-query';
import {Button,Modal}from'@heroui/react';
import {ExternalLink,Image as ImageIcon,Play}from'lucide-react';
import {appUrl,duration,request,type CatalogMedia,type ContentMatch}from'../api';
import {useUiLanguage,runtimeText}from'../i18n';
import {youtubeTrailerUrl}from'../contentMedia';
import {Dialog}from'./Dialogs';

type Identity=Pick<ContentMatch,'provider'|'kind'|'providerId'|'title'>;
function useCloseMedia(close:()=>void){
  useEffect(()=>{
    const hidden=()=>{if(document.hidden)close();};
    window.addEventListener('home-watch-stop',close);window.addEventListener('home-session-expired',close);window.addEventListener('popstate',close);document.addEventListener('visibilitychange',hidden);
    return()=>{window.removeEventListener('home-watch-stop',close);window.removeEventListener('home-session-expired',close);window.removeEventListener('popstate',close);document.removeEventListener('visibilitychange',hidden);};
  },[close]);
}

/** Hero-card action; metadata and YouTube are connected only after a click. */
export function TrailerAction({metadata}:{metadata:Identity}){
  const language=useUiLanguage();const identity=`${metadata.provider}:${metadata.kind}:${metadata.providerId}`;
  const [opened,setOpened]=useState<string|null>(null),[selected,setSelected]=useState<string|null>(null);
  const open=opened===identity;
  const close=()=>{setOpened(null);setSelected(null);};useCloseMedia(close);
  const query=useQuery({queryKey:['content-trailers',metadata.provider,metadata.kind,metadata.providerId],enabled:open,staleTime:4*60*60*1000,retry:1,
    queryFn:({signal})=>request<CatalogMedia>(`/discovery/${metadata.provider}/${metadata.kind}/${metadata.providerId}/media?trailersOnly=true`,{signal})});
  const trailers=query.data?.trailers.filter(value=>value.site==='YouTube'&&youtubeTrailerUrl(value.key,language))??[];
  const chosen=trailers.find(value=>value.key===selected)??trailers[0];const url=open&&chosen?youtubeTrailerUrl(chosen.key,language):null;
  return <>
    <Button className="title-source-button title-trailer-action" variant="secondary" onPress={()=>setOpened(identity)}><Play size={16}/>Fragman</Button>
    <Dialog open={open} onClose={close} title={`${metadata.title} fragmanı`} className="title-trailer-modal">
      {open&&<div className="title-trailer-content"><Modal.Heading>{metadata.title}</Modal.Heading>
        {query.isPending?<div className="title-media-loading home-skeleton"/>:url?<>
          <div className="title-trailer-stage"><iframe key={chosen.key} src={url} title={`${metadata.title} fragmanı`} allow="encrypted-media; picture-in-picture; fullscreen" allowFullScreen referrerPolicy="strict-origin-when-cross-origin"/></div>
          {trailers.length>1&&<div className="title-trailer-list">{trailers.map(value=><Button key={value.key} variant={chosen.key===value.key?'secondary':'ghost'} onPress={()=>setSelected(value.key)} aria-pressed={chosen.key===value.key}>{value.name}</Button>)}</div>}
          <a className="content-source" href={`https://www.youtube.com/watch?v=${chosen.key}`} target="_blank" rel="noopener">YouTube'da aç <ExternalLink size={12}/></a>
        </>:<p className="field-hint">{query.data?.trailerError?runtimeText(query.data.trailerError):'Bu içerik için fragman bulunamadı.'}</p>}
        {query.error&&<p className="form-error" role="alert">{query.error.message}</p>}
        {(query.error||query.data?.trailerError)&&<Button variant="ghost" onPress={()=>query.refetch()}>Yeniden dene</Button>}
      </div>}
    </Dialog>
  </>;
}

interface StillGallery {mediaId:string;fingerprint:string;images:{index:number;positionSeconds:number;url:string}[];reason:string|null}
/** Actual local video frames. No provider posters, logos, banners or fanart. */
export function ContentMedia({metadata,mediaId,title}:{metadata?:Identity;mediaId?:string|null;title?:string}){
  const name=title??metadata?.title??'';
  const [selection,setSelection]=useState<{id:string;index:number}|null>(null),[more,setMore]=useState(false),[retry,setRetry]=useState(0),[failed,setFailed]=useState(false);
  const close=()=>setSelection(null);useCloseMedia(close);
  useEffect(()=>{setSelection(null);setMore(false);setRetry(0);setFailed(false);},[mediaId]);
  const query=useQuery({queryKey:['media-stills',mediaId],enabled:!!mediaId,staleTime:60*60*1000,retry:1,
    queryFn:({signal})=>request<StillGallery>(`/media/${mediaId}/stills`,{signal})});
  const data=query.data;const images=data&&data.mediaId===mediaId?data.images:[];
  const selectedIndex=selection&&selection.id===mediaId?selection.index:null;
  const selected=selectedIndex!==null?images.find(value=>value.index===selectedIndex):undefined;
  const refresh=()=>{setFailed(false);setRetry(value=>value+1);void query.refetch();};
  return <section className="title-media">
    <div className="section-heading"><h2><ImageIcon size={18}/>Sahne fotoğrafları</h2></div>
    <p className="field-hint">Kütüphanendeki video dosyasından alınan gerçek kareler. Afiş veya çekim arkası fotoğrafı değildir.</p>
    {!mediaId?<p className="field-hint">Sahne fotoğrafları için kütüphanende erişilebilir bir video dosyası gerekli.</p>:query.isPending?<div className="title-media-loading home-skeleton"/>:images.length?<>
      <div className="title-image-grid">{images.slice(0,more?6:3).map(value=><button key={`${value.url}:${retry}`} onClick={()=>setSelection({id:mediaId,index:value.index})} aria-label={`${name} sahnesi · ${duration(value.positionSeconds)}`}>
        <img src={appUrl(`${value.url}&retry=${retry}`)} alt={`${name} sahnesi`} loading="lazy" decoding="async" onError={()=>setFailed(true)}/><span>{duration(value.positionSeconds)}</span>
      </button>)}</div>
      {images.length>3&&<Button variant="ghost" onPress={()=>setMore(value=>!value)} aria-expanded={more}>{more?'Daha az göster':'Daha fazlasını göster'}</Button>}
    </>:!query.error&&<p className="field-hint">{query.data?.reason?runtimeText(query.data.reason):'Bu dosyada sahne fotoğrafı hazırlanamadı.'}</p>}
    {(query.error||failed)&&<><p className="form-error" role="alert">{query.error?.message??'Bazı sahne fotoğrafları hazırlanamadı. Yeniden deneyebilirsin.'}</p><Button variant="ghost" onPress={refresh}>Yeniden dene</Button></>}
    <Dialog open={!!selected} onClose={close} title={`${name} sahne fotoğrafı`} className="title-trailer-modal">
      {selected&&<div className="title-trailer-content"><Modal.Heading>{name}</Modal.Heading><img className="title-still-large" src={appUrl(`${selected.url}&retry=${retry}`)} alt={`${name} sahnesi`}/><p className="field-hint">{duration(selected.positionSeconds)} · Kaynak: kütüphanendeki video</p></div>}
    </Dialog>
  </section>;
}
