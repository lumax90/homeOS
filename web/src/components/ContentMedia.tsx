import {useEffect,useState}from'react';
import {useQuery}from'@tanstack/react-query';
import {Button}from'@heroui/react';
import {ExternalLink,Image as ImageIcon,Play,X}from'lucide-react';
import {request,type CatalogMedia,type ContentMatch}from'../api';
import {useUiLanguage,runtimeText}from'../i18n';
import {youtubeTrailerUrl}from'../contentMedia';

/** Real provider media. A YouTube connection starts only after the user's click. */
export function ContentMedia({metadata}:{metadata:Pick<ContentMatch,'provider'|'kind'|'providerId'|'title'>}){
  const language=useUiLanguage();const [selected,setSelected]=useState<string|null>(null),[image,setImage]=useState<string|null>(null),[moreImages,setMoreImages]=useState(false);
  const query=useQuery({queryKey:['content-media',metadata.provider,metadata.kind,metadata.providerId],staleTime:4*60*60*1000,retry:1,
    queryFn:({signal})=>request<CatalogMedia>(`/discovery/${metadata.provider}/${metadata.kind}/${metadata.providerId}/media`,{signal})});
  const trailers=query.data?.trailers.filter(value=>value.site==='YouTube'&&youtubeTrailerUrl(value.key,language))??[];
  const images=query.data?.images??[];const url=selected?youtubeTrailerUrl(selected,language):null;
  useEffect(()=>{
    const close=()=>{setSelected(null);setImage(null);};const hidden=()=>{if(document.hidden)close();};
    window.addEventListener('home-watch-stop',close);window.addEventListener('home-navigation-interaction',close);document.addEventListener('visibilitychange',hidden);
    return()=>{window.removeEventListener('home-watch-stop',close);window.removeEventListener('home-navigation-interaction',close);document.removeEventListener('visibilitychange',hidden);};
  },[]);
  useEffect(()=>{setSelected(null);setImage(null);setMoreImages(false);},[metadata.provider,metadata.kind,metadata.providerId]);
  return <section className="title-media">
    <div className="section-heading"><h2>Fragmanlar</h2></div>
    {query.isPending?<div className="title-media-loading home-skeleton"/>:trailers.length?<>
      <div className="title-trailer-list">{trailers.map(value=><Button key={value.key} variant={selected===value.key?'secondary':'ghost'} onPress={()=>setSelected(value.key)} aria-pressed={selected===value.key}><Play size={16}/><span>Fragmanı izle · {value.name}</span><small>{value.site}</small></Button>)}</div>
      {url&&<div className="title-trailer"><div className="title-media-toolbar"><strong>{metadata.title}</strong><Button isIconOnly variant="ghost" aria-label="Fragmanı kapat" onPress={()=>setSelected(null)}><X size={17}/></Button></div>
        <iframe key={selected} src={url} title={`${metadata.title} fragmanı`} allow="encrypted-media; picture-in-picture; fullscreen" allowFullScreen referrerPolicy="strict-origin-when-cross-origin"/>
        <a className="content-source" href={`https://www.youtube.com/watch?v=${selected}`} target="_blank" rel="noopener">YouTube'da aç <ExternalLink size={12}/></a>
      </div>}
    </>:<p className="field-hint">{query.data?.trailerError?runtimeText(query.data.trailerError):'Bu içerik için fragman bulunamadı.'}</p>}
    {images.length>0&&<><div className="section-heading"><h2><ImageIcon size={18}/> Görseller</h2></div>
      {image&&<div className="title-image-preview"><Button isIconOnly variant="ghost" aria-label="Görseli kapat" onPress={()=>setImage(null)}><X size={17}/></Button><img src={image} alt={`${metadata.title} görseli`}/></div>}
      <div className="title-image-grid">{images.slice(0,moreImages?24:3).map((value,index)=><button key={value.url} onClick={()=>setImage(value.url)} aria-label={`${metadata.title} görseli ${index+1}`}><img src={value.previewUrl} alt="" loading="lazy" decoding="async"/></button>)}</div>
      {images.length>3&&<Button variant="ghost" onPress={()=>setMoreImages(value=>!value)} aria-expanded={moreImages}>{moreImages?'Daha az göster':'Daha fazlasını göster'}</Button>}
      <a className="content-source" href={`https://${metadata.provider==='tmdb'?'www.themoviedb.org':'www.tvmaze.com'}/${metadata.provider==='tmdb'?(metadata.kind==='movie'?'movie':'tv'):'shows'}/${metadata.providerId}`} target="_blank" rel="noreferrer">Görsel kaynağı: {metadata.provider==='tmdb'?'TMDB':'TVmaze · CC BY-SA'}</a>
    </>}
    {(query.error||query.data?.imageError)&&<p className="form-error" role="alert">{query.error?.message??runtimeText(query.data?.imageError)}</p>}
    {(query.error||query.data?.imageError||query.data?.trailerError)&&<Button variant="ghost" onPress={()=>query.refetch()}>Yeniden dene</Button>}
  </section>;
}
