import {useState,type ReactNode} from 'react';
import {Button} from '@heroui/react';
import {VinylBackdrop} from './VinylRecord';
import {galleryWorks,type StockVideo} from './sceneSources';
import {translate as t,runtimeText} from '../i18n';

export interface ExternalScene{media:ReactNode;ready:boolean;pending:boolean;error:string;poster:string|null;onRetry:()=>void}

/** Remount per selected resource: late load/error events cannot hide the next scene's record fallback. */
export function SceneBackdrop({video,art,playing,credits,reduced,external}:{video?:StockVideo;art?:typeof galleryWorks[number];playing:boolean;credits:boolean;reduced:boolean;external?:ExternalScene}){
  const [ready,setReady]=useState(false),[failed,setFailed]=useState(false),[attempt,setAttempt]=useState(0);
  const showVideo=!!video&&!failed,showArt=!!art&&!failed;
  return <>
    {(video&&external? !external.ready&&!external.poster:!ready||failed||!video&&!art)&&<VinylBackdrop playing={playing}/>}
    {showVideo&&external&&<>{external.poster&&<img className="music-stock-scene" src={external.poster} alt="" aria-hidden="true"/>}<div className="radio-scene-media" style={{opacity:external.ready?1:0}}>{external.media}</div></>}
    {showVideo&&!external&&<video key={attempt} className="music-stock-scene" style={{opacity:ready?1:0}} src={video.url} autoPlay muted loop playsInline disablePictureInPicture aria-hidden="true" onPlaying={()=>setReady(true)} onError={()=>{setReady(false);setFailed(true);}}/>}
    {showArt&&<img key={attempt} className="music-gallery-scene" style={{opacity:ready?1:0}} src={`https://www.artic.edu/iiif/2/${art.image}/full/1686,/0/default.jpg`} alt={`${art.title} — ${art.artist}`} onLoad={()=>setReady(true)} onError={()=>{setReady(false);setFailed(true);}}/>}
    {video&&external?.pending&&<p className="music-visual-error" role="status">Görsel hazırlanıyor; radyo çalmaya devam edebilir.</p>}
    {video&&external?.error&&<div className="music-visual-error" role="status"><p>{runtimeText(external.error)}</p><Button variant="ghost" onPress={external.onRetry}>Görüntüyü yeniden dene</Button></div>}
    {credits&&<aside className="music-visual-credits">{video?<><strong>{t(video.label)}</strong><a href={video.source} target="_blank" rel="noreferrer">{video.author} · Wikimedia Commons</a><a href={video.licenseUrl} target="_blank" rel="noreferrer">{video.license}</a><span>Sessiz, döngülü ve ekrana uyarlanmış gösterim.</span></>:art?<><a href={`https://www.artic.edu/artworks/${art.id}`} target="_blank" rel="noreferrer">{art.title} — {art.artist}</a><span>Art Institute of Chicago · Public domain / CC0</span></>:<span>Home · Plak</span>}{failed&&<><p>Görüntü açılamadı; plak gösteriliyor.</p><Button variant="ghost" onPress={()=>{setFailed(false);setAttempt(value=>value+1);}}>Görüntüyü yeniden dene</Button></>}{reduced&&<p>Azaltılmış hareket tercihin uygulanıyor.</p>}</aside>}
  </>;
}
