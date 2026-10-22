import {useEffect,useRef,useState} from 'react';
import {createPortal} from 'react-dom';
import {Button} from '@heroui/react';
import {Expand,LoaderCircle,Pause,Play,Radio,RotateCw,Volume2,X} from 'lucide-react';
import {appUrl,mutate} from '../api';
import {translate as t,runtimeText,useUiLanguage} from '../i18n';
import {browserPlayback} from '../playbackIdentity';
import {useControllerCommands} from '../controller/react';
import {selectPlaybackTransport} from '../playbackTransport';
import {MusicVisualMode} from './MusicVisualMode';
import type {RadioLaunch,RadioStation} from './experience';
import './music-experience.css';

export function RadioPlayer({station,onClose}:{station:RadioStation;onClose:()=>void}){
  useUiLanguage();const audio=useRef<HTMLAudioElement>(null),panel=useRef<HTMLDivElement>(null),lease=useRef<string|null>(null),revoked=useRef(false);
  const [playing,setPlaying]=useState(false),[error,setError]=useState(''),[buffering,setBuffering]=useState(true),[visual,setVisual]=useState(false),[volume,setVolume]=useState(.8),[wanted,setWanted]=useState(true),[attempt,setAttempt]=useState(0);
  const volumeRef=useRef(volume);volumeRef.current=volume;
  function toggle(){const element=audio.current;if(!element||revoked.current)return;if(playing||buffering){setWanted(false);setPlaying(false);setBuffering(false);}else if(lease.current&&element.src)void element.play().catch(()=>setError(t('Başlamak için oynat düğmesine dokun.')));else{setError('');setWanted(true);setAttempt(value=>value+1);}}
  useEffect(()=>{
    if(!wanted||revoked.current)return;let live=true,session:string|null=null,destroy:(()=>void)|undefined,blockedUntil=0;const element=audio.current!;element.volume=volumeRef.current;setBuffering(true);setError('');
    const end=()=>{element.pause();destroy?.();destroy=undefined;element.removeAttribute('src');element.load();if(session){const id=session;session=null;if(lease.current===id)lease.current=null;void mutate(`/music/radio/sessions/${id}`,undefined,'DELETE',true).catch(()=>{});}};
    const failed=(message:string)=>{if(!live)return;setError(message);setPlaying(false);setBuffering(false);end();};
    const play=()=>{void element.play().catch(()=>{if(live){blockedUntil=Date.now()+60000;setBuffering(false);setError(t('Başlamak için oynat düğmesine dokun.'));}});};
    const onPlaying=()=>{if(live){blockedUntil=0;setPlaying(true);setBuffering(false);setError('');}},onPause=()=>{if(live)setPlaying(false);},onWaiting=()=>{if(live&&session)setBuffering(true);},onError=()=>{if(live&&session)failed(t('Radyo bağlantısı kesildi. Kanalı yeniden açabilirsin.'));};
    element.addEventListener('playing',onPlaying);element.addEventListener('pause',onPause);element.addEventListener('waiting',onWaiting);element.addEventListener('error',onError);element.addEventListener('ended',onError);
    void mutate<RadioLaunch>('/music/radio/play',{stationId:station.id,clientId:browserPlayback.clientId,revision:browserPlayback.nextRevision()}).then(async launch=>{
      if(!live||revoked.current){void mutate(`/music/radio/sessions/${launch.id}`,undefined,'DELETE',true).catch(()=>{});return;}
      session=launch.id;lease.current=session;const source=appUrl(launch.url);
      if(!launch.station.hls){element.src=source;play();return;}
      const {default:Hls}=await import('hls.js');if(!live||!session)return;
      const transport=selectPlaybackTransport('live-hls',Hls.isSupported(),element.canPlayType('application/vnd.apple.mpegurl'));
      if(transport==='native-hls'){element.src=source;play();return;}
      if(transport!=='hls.js'){failed(t('Bu cihaz radyo yayın biçimini desteklemiyor.'));return;}
      const hls=new Hls({maxBufferLength:20,backBufferLength:0});destroy=()=>hls.destroy();hls.on(Hls.Events.MANIFEST_PARSED,play);hls.on(Hls.Events.ERROR,(_,data)=>{if(data.fatal)failed(t('Radyo bağlantısı kesildi. Kanalı yeniden açabilirsin.'));});hls.loadSource(source);hls.attachMedia(element);
    }).catch(reason=>failed((reason as Error).message));
    const stop=(revoke:boolean)=>{live=false;revoked.current=revoke;end();setBuffering(false);setPlaying(false);setWanted(false);},pageHide=()=>stop(false),authStop=()=>stop(true);window.addEventListener('pagehide',pageHide);window.addEventListener('home-watch-stop',authStop);
    let heartbeat=false;const timer=setInterval(()=>{if(!session||heartbeat)return;if(blockedUntil){if(Date.now()>=blockedUntil)end();return;}heartbeat=true;void mutate(`/music/radio/sessions/${session}/heartbeat`).catch(reason=>failed((reason as Error).message)).finally(()=>{heartbeat=false;});},20000);
    return()=>{live=false;clearInterval(timer);window.removeEventListener('pagehide',pageHide);window.removeEventListener('home-watch-stop',authStop);element.removeEventListener('playing',onPlaying);element.removeEventListener('pause',onPause);element.removeEventListener('waiting',onWaiting);element.removeEventListener('error',onError);element.removeEventListener('ended',onError);end();};
  },[station.id,wanted,attempt]);
  useEffect(()=>{const open=()=>setVisual(true);window.addEventListener('home-music-visual',open);return()=>window.removeEventListener('home-music-visual',open);},[]);
  useEffect(()=>{
    if(!('mediaSession'in navigator))return;navigator.mediaSession.metadata=new MediaMetadata({title:station.name,artist:'Home Radio'});navigator.mediaSession.playbackState=playing?'playing':'paused';
    const actions={play:()=>{if(revoked.current)return;if(audio.current?.src)void audio.current.play().catch(()=>{});else{setWanted(true);setAttempt(value=>value+1);}},pause:()=>{setWanted(false);setPlaying(false);setBuffering(false);}};
    for(const action of ['play','pause'] as const)try{navigator.mediaSession.setActionHandler(action,actions[action]);}catch{}
    return()=>{for(const action of ['play','pause'] as const)try{navigator.mediaSession.setActionHandler(action,null);}catch{}};
  },[station.name,playing]);
  useControllerCommands(panel,action=>{if(action==='playpause'){toggle();return true;}if(action==='back'){onClose();return true;}return false;});
  const changeVolume=(value:number)=>{setVolume(value);if(audio.current)audio.current.volume=value;};
  return createPortal(<><div ref={panel} data-controller-player-dock="radio" className="music-radio-dock music-dock" role="region" aria-label="Radyo oynatıcı"><audio ref={audio} preload="none"/><div className="music-radio-dock-art"><Radio size={24}/></div><div className="music-dock-title"><strong title={station.name}>{station.name}</strong><span>{error?runtimeText(error):t('CANLI RADYO')+' · '+station.tags.slice(0,2).join(' / ')}</span></div><Button isIconOnly data-controller-player-primary variant="primary" aria-label={playing||buffering?t('Duraklat'):t('Oynat')} onPress={toggle}>{buffering?<LoaderCircle className="spin" size={21}/>:playing?<Pause size={21}/>:<Play size={21}/>}</Button>{error&&<Button isIconOnly variant="ghost" aria-label="Radyoya yeniden bağlan" onPress={()=>{setWanted(true);setAttempt(value=>value+1);}}><RotateCw size={19}/></Button>}<div className="music-dock-volume"><Volume2 size={17}/><input type="range" aria-label="Radyo ses düzeyi" min="0" max="1" step=".05" value={volume} onChange={event=>changeVolume(Number(event.target.value))}/></div><Button variant="ghost" onPress={()=>setVisual(true)} aria-label="Görsel müzik modu"><Expand size={19}/><span className="music-dock-visual-label">Görsel mod</span></Button><Button isIconOnly variant="ghost" aria-label="Radyoyu kapat" onPress={onClose}><X size={19}/></Button></div>{visual&&<MusicVisualMode title={station.name} subtitle={t('CANLI RADYO')} playing={playing} onToggle={toggle} volume={volume} onVolume={changeVolume} onClose={()=>setVisual(false)}/>}</>,document.body);
}
