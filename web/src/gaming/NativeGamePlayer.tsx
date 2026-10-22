import { useEffect, useRef, useState } from 'react';
import { Button } from '@heroui/react';
import { ArrowLeft, CircleAlert, Gamepad2, LoaderCircle, Maximize, Pause, Play, ShieldCheck, Volume2, VolumeX } from 'lucide-react';
import { useQuery,useQueryClient } from '@tanstack/react-query';
import { appUrl, mutate,request,randomId } from '../api';
import { runtimeText, translate as t, useUiLanguage } from '../i18n';
import { useController, useControllerCommands } from '../controller/react';
import { gamepadsNeutral, type Game } from './model';
import './native-player.css';
import { nativeWhepUrl } from './nativeTransport';
import {NativeInputSender} from './nativeInput';
import {lowDelayReceiver,videoBufferSample,playoutDetails,type NativeVideoCounters,type NativePlayoutCounters} from './nativeVideo';
import {nativeDisplayPreferences} from './nativePreferences';
import {useSession} from '../components/SessionContext';
import {systemName} from './libraryModel';
import {nativePadsSample,samePad,samePadAction,nativeKeyboardSystem,emptyPointer,videoPointer,retroKey,type NativePadSample,type NativePointer} from './nativePad';
import {NativePlayerOptions} from './NativePlayerOptions';
import {resolveGameDisplay,type GameSettingsView} from './gameSettingsModel';
import {NativeTouchPad} from './NativeTouchPad';
import {withTouch} from './nativeTouch';

interface Launch {id:string;url:string;version:string;hasSave:boolean;inputEpoch:number;discs?:string[];discIndex?:number;stateSupported?:boolean}
interface Receipt {paused:boolean;hasSave:boolean;inputEpoch:number;discIndex?:number}
interface Reader { close(): void }
interface ReaderOptions { url: string; token: string; onError: (error: string) => void; onTrack: (event: RTCTrackEvent) => void }
declare global { interface Window { HomeNativeWebRTCReader?: new(options: ReaderOptions) => Reader } }
let loadingReader: Promise<void> | undefined;
function loadReader() {
  if (window.HomeNativeWebRTCReader) return Promise.resolve();
  return loadingReader ??= new Promise<void>((resolve,reject)=>{const script=document.createElement('script');script.src=appUrl('/native/home-reader.js');script.onload=()=>resolve();script.onerror=()=>{loadingReader=undefined;reject(new Error(t('Yerel yayın okuyucusu yüklenemedi.')));};document.head.appendChild(script);});
}
export function NativeGamePlayer({ game, onExit }: { game: Game; onExit: () => void }) {
  const session=useSession();const [display,setDisplay]=useState(()=>nativeDisplayPreferences(session.userId??''));const [integerSize,setIntegerSize]=useState<{width:number;height:number}|null>(null);
  const settingView=useQuery({queryKey:['games','settings',game.id],queryFn:()=>request<GameSettingsView>(`/games/${game.id}/settings`),staleTime:0});
  useUiLanguage();const cache=useQueryClient(),controller=useController();const video=useRef<HTMLVideoElement>(null),root=useRef<HTMLElement>(null),menu=useRef<HTMLDivElement>(null);
  const [ready,setReady]=useState(false),[paused,setPaused]=useState(true),[error,setError]=useState(''),[busy,setBusy]=useState(false),[saved,setSaved]=useState(false),[waiting,setWaiting]=useState(false),[muted,setMuted]=useState(true);
  const [discs,setDiscs]=useState<string[]>([]),[discIndex,setDiscIndex]=useState(0);const fit=useRef(display.fit);fit.current=display.fit;
  const stream=useRef<MediaStream|null>(null);
  const input=useRef<NativeInputSender<NativePadSample>|null>(null),pointer=useRef<NativePointer>({...emptyPointer}),keyboard=useRef(new Set<number>());
  const padSlots=useRef(new Map<number,number>());
  const [touchControls,setTouchControls]=useState(()=>navigator.maxTouchPoints>0),touchButtons=useRef(0);
  const inputSocket=useRef<WebSocket|null>(null),inputSocketReady=useRef(false);
  const receiver=useRef<RTCRtpReceiver|null>(null);
  const [videoDelay,setVideoDelay]=useState<{bufferMs:number|null;decodeMs:number|null}>({bufferMs:null,decodeMs:null});
  const [playout,setPlayout]=useState<{minimumMs:number|null;targetMs:number|null}>({minimumMs:null,targetMs:null});
  const active=useRef<Launch|null>(null),alive=useRef(false),pausedRef=useRef(true),epoch=useRef(0),sequence=useRef(0),keys=useRef(new Set<string>()),busyRef=useRef(false),ended=useRef(false),resuming=useRef(false);
  useEffect(()=>{const update=()=>setDisplay(resolveGameDisplay(nativeDisplayPreferences(session.userId??''),settingView.data?.coreDisplay,settingView.data?.gameDisplay));update();window.addEventListener('home-native-display',update);return()=>window.removeEventListener('home-native-display',update);},[session.userId,settingView.data]);
  useEffect(()=>{if(video.current)video.current.volume=display.volume/100;},[display.volume]);
  useEffect(()=>{const element=video.current,parent=element?.parentElement;if(!element||!parent)return;const update=()=>{if(display.fit!=='integer'){setIntegerSize(null);return;}const width=element.videoWidth||240,height=element.videoHeight||160,scale=Math.floor(Math.min(parent.clientWidth/width,parent.clientHeight/height));setIntegerSize(scale>=1?{width:width*scale,height:height*scale}:null);};const observer=new ResizeObserver(update);observer.observe(parent);element.addEventListener('loadedmetadata',update);update();return()=>{observer.disconnect();element.removeEventListener('loadedmetadata',update);};},[display.fit,ready]);
  pausedRef.current=paused;busyRef.current=busy;
  const command=async(action:string)=>{const launch=active.current;if(!launch)return;const result=await mutate<Receipt>(`/games/native/sessions/${launch.id}/command`,{action});if(result.inputEpoch>=epoch.current){epoch.current=result.inputEpoch;if(alive.current){if(action!=='heartbeat'||result.paused)setPaused(result.paused);setSaved(result.hasSave);setDiscIndex(result.discIndex??0);}}return result;};
  const pause=()=>{resuming.current=false;keys.current.clear();keyboard.current.clear();pointer.current={...emptyPointer};setWaiting(false);if(pausedRef.current)return;pausedRef.current=true;setPaused(true);void command('pause').catch(reason=>{if(alive.current)setError((reason as Error).message);});};
  const playVideo=(withSound:boolean)=>{const element=video.current;if(!element)return;element.muted=!withSound;setMuted(!withSound);void element.play().catch(()=>{if(alive.current){element.muted=true;setMuted(true);}});};
  useControllerCommands(menu,action=>{if(action==='back'||action==='menu'){if(ready&&!error&&!busy)void resume();return true;}return false;});
  useEffect(()=>{if(!paused&&ready)return controller?.acquireGameplay();},[controller,paused,ready]);
  useEffect(()=>{
    alive.current=true;ended.current=false;let disposed=false,reader:Reader|undefined,heartbeat=0;
    const stop=()=>{reader?.close();if(active.current&&!ended.current){ended.current=true;void mutate(`/games/native/sessions/${active.current.id}/command`,{action:'end'},'POST',true).catch(()=>{});}};
    queueMicrotask(()=>{if(disposed)return;if(typeof RTCPeerConnection==='undefined'){setError(t('Oyun yayını için WebRTC destekli bir tarayıcı gerekir.'));return;}const clientId=randomId(),ticket=Array.from(crypto.getRandomValues(new Uint8Array(32)),value=>value.toString(16).padStart(2,'0')).join('');
      void mutate<Launch>(`/games/${game.id}/native-sessions`,{clientId,ticket}).then(async launch=>{
         active.current=launch;epoch.current=launch.inputEpoch;if(!disposed){setSaved(launch.hasSave);setDiscs(launch.discs??[]);setDiscIndex(launch.discIndex??0);}if(disposed){stop();return;}await loadReader();if(disposed){stop();return;}
         const inputUrl=new URL(appUrl(`/api/games/native/sessions/${launch.id}/input-stream`),window.location.href);inputUrl.protocol=inputUrl.protocol==='https:'?'wss:':'ws:';
         const socket=new WebSocket(inputUrl);inputSocket.current=socket;socket.onopen=()=>socket.send(JSON.stringify({ticket}));socket.onmessage=event=>{if(event.data==='ok')inputSocketReady.current=true;};socket.onerror=()=>{inputSocketReady.current=false;};socket.onclose=()=>{inputSocketReady.current=false;};
        reader=new window.HomeNativeWebRTCReader!({url:nativeWhepUrl(appUrl(launch.url),window.location.href),token:ticket,onError:reason=>{if(!disposed){pause();setError(reason);}},onTrack:event=>{if(disposed||!video.current)return;lowDelayReceiver(event.receiver);const media=stream.current??=new MediaStream();for(const track of media.getTracks())if(track.readyState==='ended')media.removeTrack(track);if(!media.getTracks().some(track=>track.id===event.track.id))media.addTrack(event.track);video.current.srcObject=media;if(event.track.kind==='video'){receiver.current=event.receiver;setReady(true);setError('');}void video.current.play().catch(()=>{});}});
         heartbeat=window.setInterval(()=>{if(inputSocketReady.current&&inputSocket.current?.readyState===WebSocket.OPEN)inputSocket.current.send('ping');void command('heartbeat').catch(reason=>{if(!disposed){setPaused(true);setError((reason as Error).message);}});},5000);
      }).catch(reason=>{if(!disposed)setError((reason as Error).message);});});
    const sampleInput=()=>{let pads:ArrayLike<Gamepad|null>=[];try{pads=navigator.getGamepads?.()??[];}catch{}input.current?.stage(withTouch(nativePadsSample(keys.current,keyboard.current,pads,pointer.current,game.system??'gba',padSlots.current),touchButtons.current));};
    const keydown=(event:KeyboardEvent)=>{if(event.key==='Escape'&&(!nativeKeyboardSystem(game.system??'')||event.shiftKey||pausedRef.current)){event.preventDefault();pause();return;}const key=event.key.length===1?event.key.toLowerCase():event.key;if(!pausedRef.current){event.preventDefault();keys.current.add(key);const code=retroKey(event);if(code!==undefined)keyboard.current.add(code);sampleInput();}};
    const keyup=(event:KeyboardEvent)=>{keys.current.delete(event.key.length===1?event.key.toLowerCase():event.key);const code=retroKey(event);if(code!==undefined)keyboard.current.delete(code);if(!pausedRef.current){event.preventDefault();sampleInput();}};const hidden=()=>{if(document.hidden)pause();};
    const point=(event:PointerEvent)=>{if(pausedRef.current||!video.current)return;event.preventDefault();const previous=pointer.current;pointer.current={...videoPointer(video.current,event.clientX,event.clientY,event.type!=='pointercancel'&&(event.buttons&1)!==0,fit.current==='fill'),mouseButtons:event.type==='pointercancel'?0:event.buttons&7,motionX:(previous.motionX??0)+(event.type==='pointermove'?Math.round(Math.max(-2048,Math.min(2048,event.movementX))):0),motionY:(previous.motionY??0)+(event.type==='pointermove'?Math.round(Math.max(-2048,Math.min(2048,event.movementY))):0),wheel:previous.wheel??0};sampleInput();};
    const wheel=(event:WheelEvent)=>{if(pausedRef.current)return;event.preventDefault();pointer.current={...pointer.current,wheel:(pointer.current.wheel??0)+Math.sign(event.deltaY)};sampleInput();};
    const element=video.current;const down=(event:PointerEvent)=>{if(!pausedRef.current)element?.setPointerCapture(event.pointerId);point(event);};element?.addEventListener('pointerdown',down);element?.addEventListener('pointermove',point);element?.addEventListener('pointerup',point);element?.addEventListener('pointercancel',point);element?.addEventListener('wheel',wheel,{passive:false});
    window.addEventListener('keydown',keydown);window.addEventListener('keyup',keyup);window.addEventListener('blur',pause);document.addEventListener('visibilitychange',hidden);window.addEventListener('pagehide',stop);window.addEventListener('home-watch-stop',stop);
    return()=>{disposed=true;alive.current=false;resuming.current=false;clearInterval(heartbeat);inputSocket.current?.close();inputSocket.current=null;inputSocketReady.current=false;stop();stream.current?.getTracks().forEach(track=>track.stop());stream.current=null;active.current=null;window.removeEventListener('keydown',keydown);window.removeEventListener('keyup',keyup);window.removeEventListener('blur',pause);document.removeEventListener('visibilitychange',hidden);window.removeEventListener('pagehide',stop);window.removeEventListener('home-watch-stop',stop);element?.removeEventListener('pointerdown',down);element?.removeEventListener('pointermove',point);element?.removeEventListener('pointerup',point);element?.removeEventListener('pointercancel',point);element?.removeEventListener('wheel',wheel);void cache.invalidateQueries({queryKey:['games']});};
  },[game.id,cache]);
  useEffect(()=>{
    if(!ready||paused||!active.current)return;let frame=0,chordHeld=true;
    const launch=active.current,period=epoch.current;
    const sender=new NativeInputSender<NativePadSample>(sample=>{const value={sequence:++sequence.current,...sample,inputEpoch:period};const socket=inputSocket.current;if(inputSocketReady.current&&socket?.readyState===WebSocket.OPEN&&socket.bufferedAmount<8192){socket.send(JSON.stringify(value));return Promise.resolve();}return mutate(`/games/native/sessions/${launch.id}/input`,value);},reason=>{if(alive.current&&!pausedRef.current){pause();setError((reason as Error).message);}},undefined,()=>inputSocketReady.current?0:20,samePad,samePadAction);input.current=sender;
    const sample=(time:number)=>{
      let pads:ArrayLike<Gamepad|null>=[];try{pads=navigator.getGamepads?.()??[];}catch{}
      const chord=Array.from(pads).some(pad=>pad?.connected&&pad.buttons[8]?.pressed&&pad.buttons[9]?.pressed);if(!chord)chordHeld=false;else if(!chordHeld){pause();return;}
       if(!pausedRef.current)sender.stage(withTouch(nativePadsSample(keys.current,keyboard.current,pads,pointer.current,game.system??'gba',padSlots.current),touchButtons.current));
      frame=requestAnimationFrame(sample);
    };frame=requestAnimationFrame(sample);return()=>{cancelAnimationFrame(frame);sender.dispose();if(input.current===sender)input.current=null;};
  },[ready,paused]);
  useEffect(()=>{
    if(!ready)return;let previous:NativeVideoCounters|undefined,previousPlayout:NativePlayoutCounters|undefined,pending=false,closed=false;
    const timer=setInterval(()=>{if(pending||!receiver.current)return;pending=true;void receiver.current.getStats().then(report=>{if(closed)return;for(const item of report.values())if(item.type==='inbound-rtp'&&(item.kind==='video'||item.mediaType==='video')){const current={delay:item.jitterBufferDelay??0,emitted:item.jitterBufferEmittedCount??0,decode:item.totalDecodeTime??0,frames:item.framesDecoded??0};setVideoDelay(videoBufferSample(current,previous));previous=current;const detail={emitted:current.emitted,minimum:item.jitterBufferMinimumDelay,target:item.jitterBufferTargetDelay};setPlayout(playoutDetails(detail,previousPlayout));previousPlayout=detail;}}).catch(()=>{}).finally(()=>{pending=false;});},1000);
    return()=>{closed=true;clearInterval(timer);};
  },[ready]);
  async function resume(){if(!ready||busyRef.current||resuming.current)return;playVideo(true);resuming.current=true;setWaiting(true);while(alive.current&&resuming.current){let neutral=true;try{neutral=gamepadsNeutral(navigator.getGamepads?.()??[]);}catch{}if(neutral)break;await new Promise<void>(resolve=>requestAnimationFrame(()=>resolve()));}if(!alive.current||!resuming.current)return;keys.current.clear();setWaiting(false);busyRef.current=true;setBusy(true);try{await command('resume');setError('');}catch(reason){setPaused(true);setError((reason as Error).message);void command('pause').catch(()=>{});}finally{resuming.current=false;busyRef.current=false;setBusy(false);}}
  async function exit(discard=false){if(busyRef.current)return;resuming.current=false;busyRef.current=true;setBusy(true);try{await command(discard?'discard':'end');ended.current=true;onExit();}catch(reason){setError((reason as Error).message);busyRef.current=false;setBusy(false);}}
  const controls=nativeKeyboardSystem(game.system??'')?t('Klavye ve fare doğrudan oyuna gider. Home menüsü: Shift + Escape veya Start + Select.'):['gba','gb','gbc'].includes(game.system??'gba')?t('Yön tuşları, J / K, Enter ve Shift. Gamepad ile Home menüsü: Start + Select.'):t('Yön tuşları / WASD, J K U I, Q E R T, Enter ve Shift. Menü: Escape veya Start + Select.');
  return <section ref={root} className={'game-player'+(touchControls?' native-has-touch':'')} aria-label="Sunucuda oyun oynatıcısı"><header className="game-player-header"><Button variant="ghost" onPress={()=>void exit()} aria-label="Kaydet ve oyunlara dön"><ArrowLeft size={20}/></Button><div><strong>{game.title}</strong><small>{game.systemLabel??systemName(game.system)} · {game.coreName??'RetroArch'}</small></div><span className="game-save-status"><ShieldCheck size={14}/>{saved?'Sunucuda kayıtlı':'Profil kaydı'}</span><Button isIconOnly variant="ghost" aria-label="Dokunmatik kumandayı göster" aria-pressed={touchControls} onPress={()=>setTouchControls(value=>!value)}><Gamepad2 size={19}/></Button><Button isIconOnly variant="ghost" aria-label="Oyun menüsünü aç" onPress={pause}><Pause size={19}/></Button>{document.fullscreenEnabled&&<Button isIconOnly variant="ghost" aria-label="Tam ekran" onPress={()=>void root.current?.requestFullscreen().catch(()=>{})}><Maximize size={18}/></Button>}</header>
    <div className="game-stage native-game-stage" data-controller-exclude><video ref={video} className="native-game-video" style={{objectFit:display.fit==='fill'?'fill':'contain',imageRendering:display.smooth?'auto':'pixelated',...(integerSize??{})}} autoPlay muted={muted} playsInline onContextMenu={event=>event.preventDefault()} onClick={()=>playVideo(true)}/><Button className="native-game-sound" isIconOnly variant="ghost" aria-label={muted?'Oyun sesini aç':'Oyun sesini kapat'} onPress={()=>playVideo(muted)}>{muted?<VolumeX size={19}/>:<Volume2 size={19}/>}</Button></div>
    {touchControls&&ready&&!paused&&!error&&<NativeTouchPad system={game.system??'gba'} onChange={buttons=>{touchButtons.current=buttons;}}/>}
    {!window.isSecureContext&&paused&&ready&&<p className="native-http-capability">{t('IP üzerinden oynarken klavye veya dokunmatik kumandayı kullanabilirsin. Tarayıcı oyun kolu erişimi için güvenilir HTTPS isteyebilir.')}</p>}
    <details className="native-game-stats"><summary>Yayın ölçümü</summary><p>Tarayıcı video tamponu: {videoDelay.bufferMs===null?'—':`${videoDelay.bufferMs.toFixed(0)} ms`}</p><p>Video çözme: {videoDelay.decodeMs===null?'—':`${videoDelay.decodeMs.toFixed(1)} ms`}</p><p>Tarayıcı minimum tamponu: {playout.minimumMs===null?'—':`${playout.minimumMs.toFixed(0)} ms`}</p><p>Tarayıcı hedef tamponu: {playout.targetMs===null?'—':`${playout.targetMs.toFixed(0)} ms`}</p><small>Bu değerler toplam tuş-görüntü gecikmesi değildir.</small></details>
    {(paused||!ready||error)&&<div className="game-pause-backdrop"><div ref={menu} className="game-pause-menu" role="dialog" aria-modal="true" aria-label="Oyun menüsü"><span className="game-pause-symbol">{error?<CircleAlert size={30}/>:ready?<Gamepad2 size={32}/>:<LoaderCircle size={28} className="spin"/>}</span><h1>{game.title}</h1>{error&&<p className="form-error" role="alert">{runtimeText(error)}</p>}{!ready&&!error&&<p>Oyun Home sunucusunda hazırlanıyor.</p>}{ready&&!error&&<><p>{controls}</p>{['3ds','nds'].includes(game.system??'')&&<p>Alt ekrana fareyle veya dokunarak erişebilirsin.</p>}{discs.length>1&&<label className="field">Oyun diski<select aria-label="Oyun diski" value={discIndex} disabled={busy} onChange={event=>{setBusy(true);void command('disc:'+event.target.value).catch(reason=>setError((reason as Error).message)).finally(()=>setBusy(false));}}>{discs.map((name,index)=><option key={index} value={index}>{name}</option>)}</select></label>}<Button className="play-button" isDisabled={busy||waiting} onPress={()=>void resume()}><Play size={17}/>{waiting?'Tuşları bırak…':'Oyuna dön'}</Button>{active.current&&<NativePlayerOptions game={game}/>}</>}<Button className="secondary-button" isDisabled={busy} onPress={()=>void exit()}>{busy?<LoaderCircle className="spin" size={16}/>:<ArrowLeft size={16}/>} Kaydet ve çık</Button>{error&&<Button variant="ghost" isDisabled={busy} onPress={()=>void exit(true)}>Kaydetmeden çık</Button>}</div></div>}
  </section>;
}
