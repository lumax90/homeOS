import { createContext, useContext, useEffect, useRef, useState, type ReactNode } from 'react';
import { Button } from '@heroui/react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { Cast, Pause, Play, Square } from 'lucide-react';
import { ApiError, clock, mutate, request, randomId, type MediaItem } from '../api';
import './receivers.css';

export interface Receiver { id:string; playbackId:string; url:string; contentType:string; name:string; mode:string; kind:'cast'|'airplay'|'dlna'; revision:number; mediaId:string; positionSeconds:number; durationSeconds:number; targetId:string|null; streamStartSeconds:number }
interface Status { id:string; revision:number; closed:boolean; positionSeconds:number; durationSeconds:number; paused:boolean; volume:number|null; state:string; observedAt:string|null; message:string|null }
interface RemotePlayer { currentTime:number; duration:number; isPaused:boolean; volumeLevel:number; isConnected:boolean; isMediaLoaded:boolean; mediaInfo:{contentId:string}|null; playerState:string }
interface RemoteController { playOrPause:()=>void; seek:()=>void; setVolumeLevel:()=>void; stop:()=>void }
interface CastSession { loadMedia:(input:unknown)=>Promise<void>; getCastDevice:()=>{friendlyName:string} }
interface CastContext { setOptions:(value:unknown)=>void; requestSession:()=>Promise<void>; getCurrentSession:()=>CastSession|null }
interface CastGlobals { cast?:{framework:{CastContext:{getInstance:()=>CastContext};RemotePlayer:new()=>RemotePlayer;RemotePlayerController:new(player:RemotePlayer)=>RemoteController}}; chrome?:{cast?:{AutoJoinPolicy:{ORIGIN_SCOPED:string};media:{DEFAULT_MEDIA_RECEIVER_APP_ID:string;MediaInfo:new(url:string,mime:string)=>{metadata:unknown;duration:number};GenericMediaMetadata:new()=>{title:string};LoadRequest:new(info:unknown)=>{currentTime:number;autoplay:boolean}}}}; __onGCastApiAvailable?:(available:boolean)=>void }
const castWindow=window as unknown as CastGlobals;
let castPromise:Promise<void>|null=null;
export function loadCast():Promise<void> {
  if(castWindow.cast?.framework)return Promise.resolve();
  if(castPromise)return castPromise;
  castPromise=new Promise<void>((resolve,reject)=>{
    const script=document.createElement('script');let settled=false;
    const finish=(error?:Error)=>{if(settled)return;settled=true;clearTimeout(timer);if(error){script.remove();reject(error);}else resolve();};
    const timer=setTimeout(()=>finish(new Error('Cast göndericisi zamanında yüklenemedi.')),12000);
    castWindow.__onGCastApiAvailable=available=>finish(available?undefined:new Error('Bu tarayıcıda Google Cast kullanılamıyor.'));
    script.src='https://www.gstatic.com/cv/js/sender/v1/cast_sender.js?loadCastFramework=1';script.onerror=()=>finish(new Error('Cast göndericisi yüklenemedi.'));document.head.append(script);
  }).catch(error=>{castPromise=null;throw error;});
  return castPromise;
}
function configureCast(){const sdk=castWindow.chrome!.cast!;const context=castWindow.cast!.framework.CastContext.getInstance();context.setOptions({receiverApplicationId:sdk.media.DEFAULT_MEDIA_RECEIVER_APP_ID,autoJoinPolicy:sdk.AutoJoinPolicy.ORIGIN_SCOPED});return context;}
const commandId=randomId;
export const hasAirplay='webkitShowPlaybackTargetPicker' in HTMLVideoElement.prototype;
type AirplayVideo=HTMLVideoElement&{webkitShowPlaybackTargetPicker?:()=>void};
interface PlaybackContext { receiver:Receiver|null; start:(item:MediaItem,kind:Receiver['kind'],target:string,position:number,subtitleId?:string)=>Promise<void>; attach:(receiver:Receiver)=>void }
const Context=createContext<PlaybackContext|null>(null);
export function useReceiverPlayback(){const value=useContext(Context);if(!value)throw new Error('Alıcı oynatma bağlamı bulunamadı.');return value;}

export function ReceiverPlaybackProvider({children}:{children:ReactNode}) {
  const client=useQueryClient();const [receiver,setReceiver]=useState<Receiver|null>(null);const [status,setStatus]=useState<Status|null>(null);const [error,setError]=useState('');
  const [seek,setSeek]=useState<number|null>(null);const [volume,setVolume]=useState<number|null>(null);
  const active=useRef<Receiver|null>(null);const last=useRef<Status|null>(null);const remote=useRef<RemotePlayer|null>(null);const controller=useRef<RemoteController|null>(null);const airplay=useRef<AirplayVideo>(null);
  const mounted=useRef(true);const generation=useRef(0);const starting=useRef<AbortController|null>(null);const busy=useRef(false);
  const ownsCast=(value:Receiver)=>remote.current?.mediaInfo?.contentId===value.url;
  const clear=()=>{active.current=null;last.current=null;remote.current=null;controller.current=null;if(mounted.current){setReceiver(null);setStatus(null);setSeek(null);setVolume(null);}void client.invalidateQueries({queryKey:['receiver-sessions']});};
  const update=(value:Status)=>{if(active.current?.id!==value.id||!mounted.current)return;last.current=value;active.current={...active.current,revision:value.revision};setStatus(value);if(value.closed)clear();};
  const release=async(value:Receiver,keepalive=false)=>{
    const revision=active.current?.id===value.id?last.current?.revision??value.revision:value.revision;
    return mutate<Status>(`/receivers/${value.id}/control`,{action:'stop',commandId:commandId(),expectedRevision:revision},'POST',keepalive);
  };
  const haltSender=(value:Receiver)=>{
    if(value.kind==='cast'&&ownsCast(value))controller.current?.stop();
    if(value.kind==='airplay'&&airplay.current){airplay.current.pause();airplay.current.removeAttribute('src');airplay.current.load();}
  };
  useEffect(()=>{
    mounted.current=true;
    const stop=()=>{generation.current++;starting.current?.abort();const value=active.current;if(value){haltSender(value);void release(value,true).catch(()=>{});clear();}};
    window.addEventListener('home-receiver-stop',stop);window.addEventListener('pagehide',stop);
    return()=>{mounted.current=false;stop();window.removeEventListener('home-receiver-stop',stop);window.removeEventListener('pagehide',stop);};
  },[]);
  const attach=(value:Receiver)=>{
    if(active.current)throw new Error('Önce açık alıcı oturumunu bitir.');
    if(value.kind!=='dlna')throw new Error('Bu alıcıyı onu başlatan tarayıcı göndericisinden yönet.');
    active.current=value;last.current=null;setReceiver(value);setStatus(null);setError('');
  };
  const start=async(item:MediaItem,kind:Receiver['kind'],target:string,position:number,subtitleId?:string)=>{
    if(active.current||starting.current)throw new Error('Önce açık alıcı oturumunu bitir.');
    const pending=new AbortController();starting.current=pending;const version=++generation.current;let grant:Receiver|null=null;
    try {
      let cast:CastSession|null=null;
      if(kind==='cast'){await loadCast();if(pending.signal.aborted)throw new DOMException('İptal edildi.','AbortError');const context=configureCast();await context.requestSession();cast=context.getCurrentSession();if(!cast)throw new Error('Cast alıcısı seçilmedi.');}
      if(pending.signal.aborted)throw new DOMException('İptal edildi.','AbortError');
      grant=await request<Receiver>('/receivers/start',{method:'POST',body:JSON.stringify({mediaId:item.id,kind,targetId:kind==='dlna'?target:null,name:cast?.getCastDevice().friendlyName,positionSeconds:position,subtitleId:subtitleId||null}),signal:pending.signal});
      if(cast){const sdk=castWindow.chrome!.cast!.media;const info=new sdk.MediaInfo(grant.url,grant.contentType);const metadata=new sdk.GenericMediaMetadata();metadata.title=item.title;info.metadata=metadata;info.duration=Math.max(0,grant.durationSeconds-grant.streamStartSeconds);const input=new sdk.LoadRequest(info);input.currentTime=Math.max(0,position-grant.streamStartSeconds);input.autoplay=true;await cast.loadMedia(input);remote.current=new castWindow.cast!.framework.RemotePlayer();controller.current=new castWindow.cast!.framework.RemotePlayerController(remote.current);}
      if(!mounted.current||generation.current!==version||pending.signal.aborted)throw new DOMException('İptal edildi.','AbortError');
      active.current=grant;last.current=null;setReceiver(grant);setStatus(null);setSeek(null);setVolume(null);setError('');void client.invalidateQueries({queryKey:['receiver-sessions']});
    } catch(cause){if(grant){haltSender(grant);void release(grant,true).catch(()=>{});}throw cause;}
    finally{if(starting.current===pending)starting.current=null;}
  };
  useEffect(()=>{
    if(!receiver)return;let stopped=false;let timer:ReturnType<typeof setTimeout>|undefined;
    const poll=async()=>{
      if(stopped)return;
      try {
        if(!busy.current){
          let value:Status;
          if(receiver.kind==='dlna')value=await request<Status>(`/receivers/${receiver.id}/status`);
          else {
            const cast=remote.current;const video=airplay.current;
            if(receiver.kind==='cast'&&cast?.isConnected&&cast.isMediaLoaded&&!ownsCast(receiver)){await release(receiver);clear();return;}
            if(receiver.kind==='cast'&&cast?.playerState==='IDLE'&&!cast.isMediaLoaded){await release(receiver);clear();return;}
            if(receiver.kind==='cast'&&(!cast?.isConnected||!cast.isMediaLoaded)||receiver.kind==='airplay'&&(!video||video.readyState<1)){
              value=await request<Status>(`/receivers/${receiver.id}/status`);
            } else {
              const seconds=receiver.kind==='cast'?cast!.currentTime:video!.currentTime;
              const paused=receiver.kind==='cast'?cast!.isPaused:video!.paused;
              value=await mutate<Status>(`/receivers/${receiver.id}/control`,{action:'heartbeat',positionSeconds:Math.max(0,Math.min(receiver.durationSeconds||86400,seconds+receiver.streamStartSeconds)),paused,volume:receiver.kind==='cast'?cast!.volumeLevel:video!.volume});
            }
          }
          if(!stopped){update(value);setError(value.message??'');}
        }
      } catch(cause){if(!stopped){setError((cause as Error).message);if(cause instanceof ApiError&&[401,403,404,410].includes(cause.status)){haltSender(receiver);clear();}}}
      finally{if(!stopped&&active.current?.id===receiver.id)timer=setTimeout(()=>{void poll();},5000);}
    };
    void poll();return()=>{stopped=true;if(timer)clearTimeout(timer);};
  },[receiver?.id]);
  const command=useMutation({mutationFn:async(input:{action:string;value?:number})=>{
    const value=active.current;if(!value||busy.current)return;busy.current=true;
    try {
      if(value.kind==='dlna'||input.action==='stop'){
        if(input.action==='stop')haltSender(value);
        const response=await mutate<Status>(`/receivers/${value.id}/control`,{action:input.action,commandId:commandId(),expectedRevision:last.current?.revision??value.revision,positionSeconds:input.action==='seek'?input.value:undefined,volume:input.action==='volume'?input.value:undefined});
        update(response);
      } else if(value.kind==='cast') {
        const cast=remote.current;const control=controller.current;if(!cast?.isConnected||!control||!ownsCast(value))throw new Error('Cast alıcısı bu Home akışına bağlı değil.');
        if(input.action==='play'&&cast.isPaused||input.action==='pause'&&!cast.isPaused)control.playOrPause();
        if(input.action==='seek'){if(input.value!<value.streamStartSeconds)throw new Error('Daha erken bir konum için medyayı yeniden gönder.');cast.currentTime=input.value!-value.streamStartSeconds;control.seek();}
        if(input.action==='volume'){cast.volumeLevel=input.value!;control.setVolumeLevel();}
      } else {
        const video=airplay.current;if(!video)throw new Error('AirPlay akışı hazır değil.');
        if(input.action==='play')await video.play();if(input.action==='pause')video.pause();
        if(input.action==='seek')video.currentTime=Math.max(0,input.value!-value.streamStartSeconds);if(input.action==='volume')video.volume=input.value!;
      }
      if(mounted.current)setError('');
    } catch(cause){const state=await request<Status>(`/receivers/${value.id}/status`).catch(()=>null);if(state)update(state);throw cause;}
    finally{busy.current=false;}
  },onSuccess:()=>{setSeek(null);setVolume(null);},onError:cause=>setError(cause.message)});
  const position=status?.positionSeconds??receiver?.positionSeconds??0;const length=status?.durationSeconds??receiver?.durationSeconds??0;
  return <Context.Provider value={{receiver,start,attach}}>{children}{receiver&&<aside className="receiver-dock" aria-label="Harici alıcı kumandası">
    <details><summary><Cast size={16}/><strong>{receiver.name}</strong><span>{clock(position)} / {clock(length)}</span></summary><div className="home-form">
      <p className="field-hint">{receiver.kind==='dlna'?'Cihazdan okunan durum':receiver.kind==='cast'?'Google Cast':'AirPlay'} · {{playing:'Oynuyor',paused:'Duraklatıldı',buffering:'Hazırlanıyor',connecting:'Bağlanıyor',unknown:'Durum bekleniyor'}[status?.state??'connecting']??status?.state}</p>
      <div className="content-actions"><Button variant="ghost" aria-label="Alıcıda oynat" isDisabled={command.isPending} onPress={()=>command.mutate({action:'play'})}><Play size={16}/></Button><Button variant="ghost" aria-label="Alıcıda duraklat" isDisabled={command.isPending} onPress={()=>command.mutate({action:'pause'})}><Pause size={16}/></Button><Button variant="ghost" isDisabled={command.isPending} onPress={()=>command.mutate({action:'stop'})}><Square size={16}/> Bitir</Button></div>
      <label className="field"><span>Alıcı konumu · {clock(seek??position)}</span><input type="range" min={receiver.streamStartSeconds} max={Math.max(receiver.streamStartSeconds,length)} step={1} value={seek??position} disabled={command.isPending||!length} onChange={event=>setSeek(Number(event.target.value))} onPointerUp={event=>command.mutate({action:'seek',value:Number(event.currentTarget.value)})} onKeyUp={event=>{if(['ArrowLeft','ArrowRight','Home','End'].includes(event.key))command.mutate({action:'seek',value:Number(event.currentTarget.value)});}}/></label>
      <label className="field"><span>Ses seviyesi</span><input type="range" min={0} max={1} step={.05} value={volume??status?.volume??1} disabled={command.isPending||receiver.kind==='dlna'&&status?.volume==null} onChange={event=>setVolume(Number(event.target.value))} onPointerUp={event=>command.mutate({action:'volume',value:Number(event.currentTarget.value)})} onKeyUp={event=>{if(['ArrowLeft','ArrowRight','Home','End'].includes(event.key))command.mutate({action:'volume',value:Number(event.currentTarget.value)});}}/></label>
    </div></details>
    {receiver.kind==='airplay'&&<><video ref={airplay} src={receiver.url} controls playsInline onLoadedMetadata={()=>{if(airplay.current)airplay.current.currentTime=Math.max(0,receiver.positionSeconds-receiver.streamStartSeconds);}} onEnded={()=>command.mutate({action:'stop'})}/><Button variant="ghost" onPress={()=>airplay.current?.webkitShowPlaybackTargetPicker?.()}>AirPlay hedefini seç</Button></>}
    {error&&<p className="form-error" role="status">{error}</p>}
  </aside>}</Context.Provider>;
}
