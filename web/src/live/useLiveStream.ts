import {useEffect,useRef,useState,type RefObject} from 'react';
import type Hls from 'hls.js';
import workerUrl from 'hls.js/dist/hls.worker.js?url';
import {ApiError,appUrl,mutate,request} from '../api';
import {browserPlayback} from '../playbackIdentity';
import {selectPlaybackTransport} from '../playbackTransport';
import {browserCapabilityEnvironment,playbackCapabilities} from '../playbackCapabilities';
import {LiveAttemptOwner,LiveRecoveryPolicy,liveTarget,terminalLiveStatus} from './liveRecovery';
import type {LivePlaybackStatus,LiveStreamPlan,LiveWatch} from './types';

export function useLiveStream(channelId:string,video:RefObject<HTMLVideoElement|null>){
  const[attempt,setAttempt]=useState(0),[error,setError]=useState(''),[notice,setNotice]=useState(''),[busy,setBusy]=useState(true);
  const[bufferSeconds,setBufferSeconds]=useState(12),[plan,setPlan]=useState<LiveStreamPlan|null>(null),revoked=useRef(false);
  const controls=useRef({play:async()=>{},pause:()=>{},goLive:()=>{}});
  useEffect(()=>{
    if(revoked.current)return;
    const element=video.current!,clientId=browserPlayback.clientId,policy=new LiveRecoveryPolicy();
    let alive=true,revision=0,mediaEpoch=0,hls:Hls|null=null,connection:AbortController|null=null,heartbeat:AbortController|null=null;
    let retryTimer:ReturnType<typeof setTimeout>|undefined,connecting=false,wanted=true,internalTransition=false,needsReconnect=false,compatible=false,recovered=false,positioned=false;
    let lastProgress=performance.now(),lastPosition=0,lastBufferIncrease=0,serverWarning=false,completed=false;
    const owner=new LiveAttemptOwner(id=>{void mutate(`/live/sessions/${id}`,undefined,'DELETE',true).catch(()=>{});});
    setBusy(true);setError('');setNotice('');setBufferSeconds(12);setPlan(null);
    const stopMedia=()=>{mediaEpoch=0;internalTransition=true;hls?.destroy();hls=null;element.pause();element.removeAttribute('src');element.load();};
    const closeClient=()=>{if(revision)void mutate(`/live/clients/${clientId}/close`,{revision},'POST',true).catch(()=>{});};
    function finish(message:string){if(!alive)return;alive=false;clearTimeout(retryTimer);connection?.abort();heartbeat?.abort();owner.stop();stopMedia();closeClient();setBusy(false);setError(message);}
    function recover(reason:Error,forceCompatible=false){
      if(!alive||retryTimer)return;
      if(reason instanceof ApiError&&terminalLiveStatus(reason.status)){finish(reason.message);return;}
      compatible ||= forceCompatible;needsReconnect=true;
      if(!wanted){setBusy(false);setNotice('Yayına devam ettiğinde bağlantı yenilenecek.');return;}
      const delay=policy.next(performance.now());
      if(delay===null){finish('Canlı kaynak birkaç denemeden sonra toparlanamadı. Yeniden deneyebilirsin.');return;}
      setBusy(true);setNotice('Yayına yeniden bağlanılıyor…');
      retryTimer=setTimeout(()=>{retryTimer=undefined;if(alive&&wanted)void connect();},delay);
    }
    async function play(){
      wanted=true;if(!alive)return;
      if(needsReconnect){recover(Error('Canlı bağlantı yenileniyor.'));return;}
      try{await element.play();}catch{if(alive){if(!hls&&element.error&&owner.current(mediaEpoch))recover(Error('Canlı yayın bağlantısı kesildi.'),[3,4].includes(element.error.code)&&!compatible);else{setBusy(false);setNotice('Başlamak için oynat düğmesine dokun.');}}}
    }
    function pause(){wanted=false;clearTimeout(retryTimer);retryTimer=undefined;element.pause();setBusy(false);}
    function goLive(){if(!element.seekable.length)return;const target=liveTarget(element.seekable.start(0),element.seekable.end(element.seekable.length-1),policy.bufferSeconds);if(target!==null)element.currentTime=target;}
    controls.current={play,pause,goLive};

    async function connect(){
      if(!alive||!wanted||connecting)return;
      connecting=true;needsReconnect=false;completed=false;recovered=false;positioned=false;const epoch=owner.begin();
      connection?.abort();const abort=new AbortController();connection=abort;
      const deadline=setTimeout(()=>abort.abort(),70000);revision=browserPlayback.nextRevision();
      stopMedia();setBusy(true);setError('');
      try{
        const prepared=await request<LiveWatch>(`/live/channels/${channelId}/play`,{method:'POST',body:JSON.stringify({clientId,clientRevision:revision,prepareOnly:true}),signal:abort.signal});
        if(!owner.accept(epoch,prepared.id))return;
        const{default:HlsPlayer}=await import('hls.js');if(!owner.current(epoch))return;
        if(!prepared.tracks)throw Error('Canlı yayın izleri hazırlanamadı.');
        const client=await playbackCapabilities('',prepared.tracks,{audioStream:null,maxHeight:null,maxBitrate:null},browserCapabilityEnvironment(element,HlsPlayer.isSupported()),abort.signal);
        const launch=await request<LiveWatch>(`/live/sessions/${prepared.id}/start`,{method:'POST',body:JSON.stringify({client,forceCompatible:compatible}),signal:abort.signal});
        if(!owner.current(epoch))return;
        setPlan(launch.plan??null);const url=appUrl(launch.url);
        const transport=selectPlaybackTransport('live-hls',HlsPlayer.isSupported(),element.canPlayType('application/vnd.apple.mpegurl'),launch.plan?.transport);
        mediaEpoch=epoch;
        lastProgress=performance.now();lastPosition=0;
        if(transport==='native-hls'){element.src=url;element.load();if(wanted)await play();}
        else if(transport==='hls.js'){
          const player=new HlsPlayer({workerPath:appUrl(workerUrl),enableWorker:true,lowLatencyMode:false,maxBufferLength:30,maxMaxBufferLength:60,backBufferLength:20,maxBufferSize:32*1024*1024,
            liveSyncDuration:policy.bufferSeconds,liveMaxLatencyDuration:Infinity,maxLiveSyncPlaybackRate:1,
            fragLoadPolicy:{default:{maxTimeToFirstByteMs:10000,maxLoadTimeMs:30000,timeoutRetry:{maxNumRetry:2,retryDelayMs:500,maxRetryDelayMs:2000},errorRetry:{maxNumRetry:3,retryDelayMs:500,maxRetryDelayMs:3000}}}});
          hls=player;
          player.on(HlsPlayer.Events.MANIFEST_PARSED,()=>{if(owner.current(epoch)&&wanted)void play();});
          player.on(HlsPlayer.Events.ERROR,(_,data)=>{
            if(!owner.current(epoch)||!data.fatal)return;
            if(data.response?.code&&[401,403,409,410].includes(data.response.code)){finish('Canlı oynatma izni sona erdi.');return;}
            if(data.type===HlsPlayer.ErrorTypes.MEDIA_ERROR&&!recovered){recovered=true;internalTransition=true;player.recoverMediaError();return;}
            recover(Error('Canlı yayın bağlantısı kesildi.'),data.type===HlsPlayer.ErrorTypes.MEDIA_ERROR&&!compatible);
          });
          player.loadSource(url);player.attachMedia(element);
        }else throw Error('Bu cihaz HLS oynatamıyor.');
      }catch(reason){
        if(owner.current(epoch)){owner.discard(epoch);recover(reason instanceof Error?reason:Error('Canlı bağlantı kurulamadı.'));}
      }finally{clearTimeout(deadline);if(connection===abort)connection=null;connecting=false;if(!wanted)internalTransition=false;else if(owner.current(epoch)&&!element.paused&&element.readyState>=3)playing();}
    }
    const playing=()=>{if(!alive||connecting)return;if(!wanted){element.pause();return;}internalTransition=false;positioned=true;lastProgress=performance.now();lastPosition=element.currentTime;policy.playing(lastProgress);setBusy(false);setNotice('');};
    const waiting=()=>{
      if(!alive||!wanted||internalTransition)return;setBusy(true);
      if(performance.now()-lastBufferIncrease>10000){lastBufferIncrease=performance.now();policy.stalled();setBufferSeconds(policy.bufferSeconds);if(hls)hls.config.liveSyncDuration=policy.bufferSeconds;}
    };
    const paused=()=>{if(alive&&!internalTransition&&!retryTimer&&!element.error&&!element.ended)wanted=false;};
    const started=()=>{if(alive&&!internalTransition){wanted=true;if(needsReconnect)recover(Error('Canlı bağlantı yenileniyor.'));}};
    const nativeReady=()=>{if(alive&&!hls&&!positioned&&element.seekable.length){goLive();positioned=true;}if(alive&&hls&&recovered&&internalTransition&&wanted)void play();};
    const mediaError=()=>{if(alive&&!hls&&element.error&&owner.current(mediaEpoch))recover(Error('Canlı yayın bağlantısı kesildi.'),[3,4].includes(element.error.code)&&!compatible);};
    const ended=()=>{if(!alive||connecting)return;if(completed){setBusy(false);setNotice('Yayın sona erdi.');}else recover(Error('Canlı yayın bağlantısı sona erdi.'));};
    for(const[event,handler]of [['playing',playing],['waiting',waiting],['pause',paused],['play',started],['canplay',nativeReady],['loadedmetadata',nativeReady],['error',mediaError],['ended',ended]] as const)element.addEventListener(event,handler);
    const pulse=async()=>{
      if(!alive||!owner.id||connecting||heartbeat)return;const id=owner.id,abort=new AbortController();heartbeat=abort;const timeout=setTimeout(()=>abort.abort(),10000);
      try{
        const status=await request<LivePlaybackStatus>(`/live/sessions/${id}/heartbeat`,{method:'POST',signal:abort.signal});
        if(!alive||id!==owner.id)return;completed=status.state==='completed';
        if(status.state==='failed'||status.state==='interrupted'){
          if(!status.recoverable)finish(status.error??'Canlı yayın durdu.');else recover(Error(status.error??'Canlı yayın bağlantısı kesildi.'));
        }else if(serverWarning){serverWarning=false;setNotice('');}
      }catch(reason){
        if(!alive||id!==owner.id)return;
        if(reason instanceof ApiError&&terminalLiveStatus(reason.status))finish(reason.message);
        else{serverWarning=true;setNotice('Sunucu bağlantısı bekleniyor.');}
      }finally{clearTimeout(timeout);if(heartbeat===abort)heartbeat=null;}
    };
    const timer=setInterval(()=>{void pulse();},10000);
    const watchdog=setInterval(()=>{
      if(!alive||!wanted||connecting||retryTimer||element.paused)return;
      if(Math.abs(element.currentTime-lastPosition)>.1){lastPosition=element.currentTime;lastProgress=performance.now();}
      else if(performance.now()-lastProgress>20000){lastProgress=performance.now();recover(Error('Canlı yayın ilerlemiyor.'));}
    },1000);
    const authStop=()=>{revoked.current=true;finish('Canlı oynatma izni sona erdi.');};
    const pageHide=()=>finish('Canlı yayın bağlantısı kesildi; kanalı yeniden açabilirsin.');
    window.addEventListener('home-watch-stop',authStop);window.addEventListener('pagehide',pageHide);
    void connect();
    return()=>{
      alive=false;clearTimeout(retryTimer);clearInterval(timer);clearInterval(watchdog);connection?.abort();heartbeat?.abort();owner.stop();
      for(const[event,handler]of [['playing',playing],['waiting',waiting],['pause',paused],['play',started],['canplay',nativeReady],['loadedmetadata',nativeReady],['error',mediaError],['ended',ended]] as const)element.removeEventListener(event,handler);
      stopMedia();closeClient();window.removeEventListener('home-watch-stop',authStop);window.removeEventListener('pagehide',pageHide);
    };
  },[channelId,attempt,video]);
  return{busy,error,notice,plan,bufferSeconds,setNotice,play:()=>controls.current.play(),pause:()=>controls.current.pause(),goLive:()=>controls.current.goLive(),retry:()=>{if(!revoked.current)setAttempt(value=>value+1);}};
}
