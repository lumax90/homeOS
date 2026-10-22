import {useEffect,useRef,useState} from 'react';
import {Button,Modal} from '@heroui/react';
import {useMutation,useQuery,useQueryClient} from '@tanstack/react-query';
import {ChevronDown,Expand,Info,Pause,Play,SkipBack,SkipForward,Volume2} from 'lucide-react';
import {request,mutate} from '../api';
import {translate as t,runtimeText,useUiLanguage} from '../i18n';
import {useControllerCommands} from '../controller/react';
import {SceneBackdrop} from './SceneBackdrop';
import {defaultMusicExperience,musicScenes,type MusicExperienceOptions} from './experience';
import {galleryWorks,selectedVideo,stockVideos} from './sceneSources';
import './music-experience.css';

export interface VisualControls{title:string;subtitle?:string;playing:boolean;onToggle:()=>void;onPrevious?:()=>void;onNext?:()=>void;onClose:()=>void;volume?:number;onVolume?:(value:number)=>void}
export function MusicVisualMode(controls:VisualControls){
  useUiLanguage();const root=useRef<HTMLDivElement>(null),cache=useQueryClient();
  // Keep the portal host stable when this dialog itself enters fullscreen.
  const [portal]=useState(()=>document.fullscreenElement??document.body),[awake,setAwake]=useState(true),[work,setWork]=useState(0),[credits,setCredits]=useState(false),[motionOff,setMotionOff]=useState(()=>matchMedia('(prefers-reduced-motion: reduce)').matches),[fullscreenError,setFullscreenError]=useState(false);
  const timer=useRef<ReturnType<typeof setTimeout>|null>(null),creditRef=useRef(credits);creditRef.current=credits;
  const settings=useQuery({queryKey:['music-experience'],queryFn:({signal})=>request<MusicExperienceOptions>('/music/experience',{signal})}),options=settings.data??defaultMusicExperience;
  const change=useMutation({mutationFn:(input:Partial<MusicExperienceOptions>)=>mutate<MusicExperienceOptions>('/music/experience',{...options,...input},'PUT'),onSuccess:value=>cache.setQueryData(['music-experience'],value)});
  const selected=selectedVideo(options.scene,options.sceneVideo),stock=options.onlineScenes&&!motionOff?selected:undefined,gallery=options.onlineScenes&&options.scene==='gallery',art=gallery?galleryWorks[work%galleryWorks.length]:undefined;
  const selectScene=(id:string)=>{const video=stockVideos.find(item=>item.id===id);change.mutate(video?{scene:video.scene as MusicExperienceOptions['scene'],sceneVideo:video.id}:{scene:id as MusicExperienceOptions['scene'],sceneVideo:''});};
  function wake(){setAwake(true);if(timer.current)clearTimeout(timer.current);timer.current=setTimeout(()=>{const active=document.activeElement;if(!creditRef.current&&!(active instanceof HTMLElement&&root.current?.contains(active)&&active!==root.current&&(active.matches(':focus-visible')||active.hasAttribute('data-focus-visible')||document.documentElement.dataset.homeInput==='controller')))setAwake(false);},5500);}
  useEffect(()=>{const frame=requestAnimationFrame(()=>root.current?.focus());wake();return()=>{cancelAnimationFrame(frame);if(timer.current)clearTimeout(timer.current);};},[]);
  useEffect(()=>{const media=matchMedia('(prefers-reduced-motion: reduce)'),update=()=>setMotionOff(media.matches);media.addEventListener('change',update);return()=>media.removeEventListener('change',update);},[]);
  useEffect(()=>{if(!gallery||motionOff)return;const interval=setInterval(()=>{if(!document.hidden)setWork(value=>value+1);},30000);return()=>clearInterval(interval);},[gallery,motionOff]);
  useEffect(()=>{
    if(!controls.playing||!('wakeLock'in navigator))return;let alive=true,pending=false,lock:WakeLockSentinel|undefined;
    const acquire=()=>{if(document.hidden||pending||lock&&!lock.released)return;pending=true;void navigator.wakeLock.request('screen').then(value=>{if(!alive)void value.release();else lock=value;}).catch(()=>{}).finally(()=>{pending=false;});};
    acquire();document.addEventListener('visibilitychange',acquire);return()=>{alive=false;document.removeEventListener('visibilitychange',acquire);void lock?.release();};
  },[controls.playing]);
  const close=()=>{if(document.fullscreenElement===root.current)void document.exitFullscreen().catch(()=>{});controls.onClose();};
  useControllerCommands(root,action=>{wake();if(action==='back'){close();return true;}if(action==='playpause'){controls.onToggle();return true;}if(action==='next'&&controls.onNext){controls.onNext();return true;}if(action==='previous'&&controls.onPrevious){controls.onPrevious();return true;}return false;});
  return <Modal.Backdrop isOpen isDismissable={false} onOpenChange={open=>{if(!open)close();}} className="music-visual-backdrop" UNSTABLE_portalContainer={portal}><Modal.Container className="music-visual-container" size="full"><Modal.Dialog className="music-visual-dialog" aria-label="Görsel müzik modu"><div ref={root} tabIndex={-1} className={`music-visual-mode${awake?' is-awake':''}`} onPointerMove={wake} onPointerDown={wake} onFocusCapture={wake} onKeyDownCapture={wake}>
    <Modal.Heading className="sr-only">Görsel müzik modu</Modal.Heading>
    <SceneBackdrop key={stock?.id??art?.image??'vinyl'} video={stock} art={art} playing={controls.playing} credits={credits} reduced={motionOff}/>
    <div className="music-visual-shade"/>
    <header className="music-visual-toolbar"><Button variant="ghost" onPress={close}><ChevronDown size={20}/><span>Müziğe dön</span></Button><label><span>{t('Sahne')}</span><select aria-label="Görsel sahne" value={selected?.id??options.scene} disabled={change.isPending||!settings.data} onChange={event=>selectScene(event.target.value)}>{musicScenes.flatMap(scene=>{const videos=stockVideos.filter(video=>video.scene===scene.id);return videos.length?videos.map(video=><option key={video.id} value={video.id}>{t(scene.name)} · {t(video.label)}</option>):[<option key={scene.id} value={scene.id}>{t(scene.name)}</option>];})}</select></label><Button isIconOnly variant="ghost" aria-label="Tam ekran" onPress={()=>{setFullscreenError(false);if(document.fullscreenElement===root.current)void document.exitFullscreen().catch(()=>setFullscreenError(true));else if(root.current?.requestFullscreen)void root.current.requestFullscreen().catch(()=>setFullscreenError(true));else setFullscreenError(true);}}><Expand size={21}/></Button><Button isIconOnly variant="ghost" aria-label="Görsel kaynak bilgisi" aria-expanded={credits} onPress={()=>setCredits(value=>!value)}><Info size={19}/></Button></header>
    {!options.onlineScenes&&options.scene!=='vinyl'&&<div className="music-visual-stock-off"><span>Stok görüntüler kapalı · Plak</span><Button variant="secondary" isDisabled={change.isPending||!settings.data} onPress={()=>change.mutate({onlineScenes:true})}>Stok görüntüleri aç</Button></div>}
    {options.showNowPlaying&&<div className="music-visual-now"><span>{controls.subtitle}</span><h1 title={controls.title}>{controls.title}</h1></div>}
    <div className="music-visual-controls">{controls.onPrevious&&<Button isIconOnly variant="ghost" aria-label="Önceki parça" onPress={controls.onPrevious}><SkipBack size={24}/></Button>}<Button isIconOnly variant="secondary" aria-label={controls.playing?t('Duraklat'):t('Oynat')} onPress={controls.onToggle}>{controls.playing?<Pause size={26}/>:<Play size={26}/>}</Button>{controls.onNext&&<Button isIconOnly variant="ghost" aria-label="Sonraki parça" onPress={controls.onNext}><SkipForward size={24}/></Button>}{controls.onVolume&&<label className="music-visual-volume"><Volume2 size={19}/><input type="range" min="0" max="1" step=".05" aria-label="Ses düzeyi" value={controls.volume??1} onChange={event=>controls.onVolume?.(Number(event.target.value))}/></label>}</div>
    {(change.error||fullscreenError)&&<p className="music-visual-error" role="status">{change.error?runtimeText(change.error.message):t('Tarayıcı tam ekrana geçemedi; görsel mod açık.')}</p>}
  </div></Modal.Dialog></Modal.Container></Modal.Backdrop>;
}
