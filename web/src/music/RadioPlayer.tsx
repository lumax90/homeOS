import {useCallback,useEffect,useLayoutEffect,useRef,useState} from 'react';
import {createPortal} from 'react-dom';
import {Button} from '@heroui/react';
import {Expand,LoaderCircle,Pause,Play,Radio,RotateCw,Volume2,X} from 'lucide-react';
import {translate as t,runtimeText,useUiLanguage} from '../i18n';
import {useControllerCommands} from '../controller/react';
import {MusicVisualMode} from './MusicVisualMode';
import {RadioMediaSlot} from './RadioMediaSlot';
import {useRadioPlayback} from './useRadioPlayback';
import type {RadioStation} from './experience';
import type {StockVideo} from './sceneSources';
import './music-experience.css';

export function RadioPlayer({station,onClose}:{station:RadioStation;onClose:()=>void}){
  useUiLanguage();const media=useRef<HTMLVideoElement>(null),panel=useRef<HTMLDivElement>(null),parking=useRef<HTMLDivElement>(null);
  const[host]=useState(()=>{const value=document.createElement('div');value.className='radio-media-host';return value;});
  const[visual,setVisual]=useState(false),[volume,setVolume]=useState(.8),[scene,setScene]=useState<string|null>(null);
  const player=useRadioPlayback(station,media,visual?scene:null,volume);
  const chooseScene=useCallback((video:StockVideo|undefined)=>setScene(video?.id??null),[]);
  const toggle=()=>{if(player.playing||player.buffering)player.pause();else void player.play();};
  useLayoutEffect(()=>{parking.current?.appendChild(host);return()=>host.remove();},[host]);
  useEffect(()=>{const open=()=>setVisual(true);window.addEventListener('home-music-visual',open);return()=>window.removeEventListener('home-music-visual',open);},[]);
  const actionRef=useRef(player);actionRef.current=player;
  useEffect(()=>{
    if(!('mediaSession'in navigator))return;
    if(typeof MediaMetadata==='function')navigator.mediaSession.metadata=new MediaMetadata({title:station.name,artist:'Home Radio'});
    for(const[action,handler]of [['play',()=>void actionRef.current.play()],['pause',()=>actionRef.current.pause()]] as const)try{navigator.mediaSession.setActionHandler(action,handler);}catch{}
    return()=>{for(const action of ['play','pause'] as const)try{navigator.mediaSession.setActionHandler(action,null);}catch{}};
  },[station.name]);
  useEffect(()=>{if('mediaSession'in navigator)try{navigator.mediaSession.playbackState=player.playing?'playing':'paused';}catch{}},[player.playing]);
  useControllerCommands(panel,action=>{if(action==='playpause'){toggle();return true;}if(action==='back'){onClose();return true;}return false;});
  return createPortal(<>
    {createPortal(<video ref={media} preload="none" playsInline disablePictureInPicture aria-hidden="true"/>,host)}
    <div ref={panel} data-controller-player-dock="radio" className="music-radio-dock music-dock" role="region" aria-label="Radyo oynatıcı">
      <div ref={parking} className="radio-media-parking"/>
      <div className="music-radio-dock-art"><Radio size={24}/></div><div className="music-dock-title"><strong title={station.name}>{station.name}</strong><span>{player.error?runtimeText(player.error):t('CANLI RADYO')+' · '+station.tags.slice(0,2).join(' / ')}</span></div>
      <Button isIconOnly data-controller-player-primary variant="primary" aria-label={player.playing||player.buffering?t('Duraklat'):t('Oynat')} onPress={toggle}>{player.buffering?<LoaderCircle className="spin" size={21}/>:player.playing?<Pause size={21}/>:<Play size={21}/>}</Button>
      {player.error&&<Button isIconOnly variant="ghost" aria-label="Radyoya yeniden bağlan" onPress={player.retry}><RotateCw size={19}/></Button>}
      <div className="music-dock-volume"><Volume2 size={17}/><input type="range" aria-label="Radyo ses düzeyi" min="0" max="1" step=".05" value={volume} onChange={event=>setVolume(Number(event.target.value))}/></div>
      <Button variant="ghost" onPress={()=>setVisual(true)} aria-label="Görsel müzik modu"><Expand size={19}/><span className="music-dock-visual-label">Görsel mod</span></Button><Button isIconOnly variant="ghost" aria-label="Radyoyu kapat" onPress={onClose}><X size={19}/></Button>
    </div>
    {visual&&<MusicVisualMode title={station.name} subtitle={t('CANLI RADYO')} playing={player.playing} buffering={player.buffering} onToggle={toggle} volume={volume} onVolume={setVolume} onClose={()=>setVisual(false)} onScene={chooseScene}
      externalScene={{media:<RadioMediaSlot host={host} parking={parking}/>,ready:player.sceneReady,pending:player.scenePending,error:player.sceneError,poster:player.poster,onRetry:player.retryScene}}/>}
  </>,document.body);
}
