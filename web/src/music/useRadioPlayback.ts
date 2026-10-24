import {useEffect,useRef,useState,type RefObject} from 'react';
import type Hls from 'hls.js';
import {ApiError,appUrl,mutate,request} from '../api';
import {browserPlayback} from '../playbackIdentity';
import {selectPlaybackTransport} from '../playbackTransport';
import {RadioSceneIntent} from './radioSceneIntent';
import type {RadioLaunch,RadioStation} from './experience';

interface VisualLaunch{url:string;hls:boolean;sceneId:string|null;generation:string|null;posterUrl?:string|null}
export function useRadioPlayback(station:RadioStation,media:RefObject<HTMLVideoElement|null>,scene:string|null,volume:number){
  const[playing,setPlaying]=useState(false),[buffering,setBuffering]=useState(true),[error,setError]=useState('');
  const[scenePending,setScenePending]=useState(false),[sceneError,setSceneError]=useState(''),[sceneReady,setSceneReady]=useState(false),[poster,setPoster]=useState<string|null>(null);
  const selected=useRef(scene);selected.current=scene;const level=useRef(volume);level.current=volume;
  const actions=useRef({play:async()=>{},pause:()=>{},retry:()=>{},scene:(_scene:string|null)=>{}});
  useEffect(()=>{
    const element=media.current!;const intent=new RadioSceneIntent();intent.select(selected.current);
    let alive=true,revoked=false,lease:RadioLaunch|null=null,hls:Hls|null=null,starting=false,baseEpoch=0,mediaEpoch=0,visualRevision=0;
    let baseRequest:AbortController|null=null,sceneRequest:AbortController|null=null,pulse=false,switching=false,activeScene:string|null=null;
    const stopMedia=()=>{++mediaEpoch;switching=true;hls?.destroy();hls=null;element.pause();element.removeAttribute('src');element.load();setSceneReady(false);};
    const end=()=>{++baseEpoch;baseRequest?.abort();sceneRequest?.abort();stopMedia();const old=lease;lease=null;if(old)void mutate(`/music/radio/sessions/${old.id}`,undefined,'DELETE',true).catch(()=>{});};
    const ensurePlay=async()=>{if(!alive||!intent.playing||revoked)return;try{await element.play();}catch{if(alive){setBuffering(false);setError('Başlamak için oynat düğmesine dokun.');}}};
    async function attach(url:string,isHls:boolean,sceneId:string|null){
      stopMedia();const epoch=mediaEpoch;activeScene=sceneId;element.volume=level.current;setBuffering(true);
      if(!isHls){element.src=appUrl(url);switching=false;await ensurePlay();return;}
      const{default:Player}=await import('hls.js');if(!alive||epoch!==mediaEpoch)return;
      const transport=selectPlaybackTransport('live-hls',Player.isSupported(),element.canPlayType('application/vnd.apple.mpegurl'));
      if(transport==='native-hls'){element.src=appUrl(url);switching=false;await ensurePlay();return;}
      if(transport!=='hls.js')throw Error('Bu cihaz radyo yayın biçimini desteklemiyor.');
      const player=new Player({lowLatencyMode:false,maxBufferLength:20,backBufferLength:5,liveSyncDurationCount:3});hls=player;
      player.on(Player.Events.MANIFEST_PARSED,()=>{if(alive&&epoch===mediaEpoch){switching=false;void ensurePlay();}});
      player.on(Player.Events.ERROR,(_,data)=>{if(alive&&epoch===mediaEpoch&&data.fatal)void failed('Radyo bağlantısı kesildi. Kanalı yeniden açabilirsin.');});
      player.loadSource(appUrl(url));player.attachMedia(element);
    }
    async function audioOnly(){
      const current=lease;if(!current||!alive)return;
      const requestRevision=++visualRevision;
      void request(`/music/radio/sessions/${current.id}/visual`,{method:'POST',body:JSON.stringify({sceneId:null,revision:requestRevision})}).catch(()=>{});
      await attach(current.url,current.station.hls,null);
    }
    async function failed(message:string){
      if(!alive||!hls&&!element.getAttribute('src'))return;
      if(activeScene&&lease){setSceneError(message);setSceneReady(false);await audioOnly().catch(()=>{setError(message);end();});}
      else{setError(message);setPlaying(false);setBuffering(false);end();}
    }
    async function applyScene(){
      const current=lease,ticket=intent.ticket(),target=intent.scene;if(!current||!intent.playing||!alive)return;
      sceneRequest?.abort();const abort=new AbortController();sceneRequest=abort;
      const valid=()=>alive&&lease?.id===current.id&&intent.current(ticket)&&!abort.signal.aborted;
      if(!target){setScenePending(false);setSceneError('');setPoster(null);if(activeScene||switching)await audioOnly();return;}
      setScenePending(true);setSceneError('');setSceneReady(false);setPoster(null);
      const timeout=setTimeout(()=>abort.abort(),250000);
      try{
        // Radio keeps its original source while the selected video is fetched/normalized once.
        const prepared=await request<{id:string;posterUrl:string}>(`/music/radio/sessions/${current.id}/scenes/${target}/prepare`,{method:'POST',signal:abort.signal});
        if(!valid())return;setPoster(appUrl(prepared.posterUrl));
        stopMedia();setBuffering(true);
        const launch=await request<VisualLaunch>(`/music/radio/sessions/${current.id}/visual`,{method:'POST',body:JSON.stringify({sceneId:target,revision:++visualRevision}),signal:abort.signal});
        if(!valid())return;await attach(launch.url,launch.hls,target);if(valid())setScenePending(false);
      }catch(reason){
        if(valid()||alive&&lease?.id===current.id&&intent.current(ticket)){
          setScenePending(false);setSceneError((reason as Error).name==='AbortError'?'Görsel hazırlığı zaman aşımına uğradı. Radyo açık kalır.':(reason as Error).message);
          if(switching||activeScene)await audioOnly().catch(()=>{});
        }
      }finally{clearTimeout(timeout);if(sceneRequest===abort)sceneRequest=null;}
    }
    async function start(){
      if(!alive||revoked||starting||!intent.playing)return;starting=true;const epoch=++baseEpoch;const abort=new AbortController();baseRequest=abort;setError('');setBuffering(true);
      try{
        const launch=await request<RadioLaunch>('/music/radio/play',{method:'POST',body:JSON.stringify({stationId:station.id,clientId:browserPlayback.clientId,revision:browserPlayback.nextRevision()}),signal:abort.signal});
        if(!alive||epoch!==baseEpoch||!intent.playing){void mutate(`/music/radio/sessions/${launch.id}`,undefined,'DELETE',true).catch(()=>{});return;}
        lease=launch;visualRevision=0;await attach(launch.url,launch.station.hls,null);if(intent.scene)void applyScene();
      }catch(reason){if(alive&&epoch===baseEpoch){setError((reason as Error).message);setBuffering(false);}}
      finally{starting=false;if(baseRequest===abort)baseRequest=null;if(alive&&intent.playing&&!lease&&epoch!==baseEpoch)void start();}
    }
    const play=async()=>{if(revoked)return;intent.play();if(!lease){await start();return;}if(!element.src){await attach(lease.url,lease.station.hls,null);if(intent.scene)void applyScene();}else await ensurePlay();};
    const pause=()=>{intent.pause();end();setPlaying(false);setBuffering(false);setScenePending(false);};
    const choose=(value:string|null)=>{
      const ticket=intent.select(value);sceneRequest?.abort();if(!value)setPoster(null);
      if(lease&&intent.playing){
        if(switching||!element.getAttribute('src'))void audioOnly().then(()=>{if(intent.current(ticket))void applyScene();}).catch(()=>{});
        else void applyScene();
      }
    };
    actions.current={play,pause,retry:()=>{if(revoked)return;end();intent.play();void start();},scene:choose};
    const onPlaying=()=>{if(!alive)return;if(!intent.playing){element.pause();return;}switching=false;setPlaying(true);setBuffering(false);setError('');setSceneReady(!!activeScene&&activeScene===intent.scene&&element.videoWidth>0);};
    const onPause=()=>{if(alive)setPlaying(false);};
    const onWaiting=()=>{if(alive&&intent.playing&&!switching)setBuffering(true);};
    const onError=()=>{if(alive&&lease&&!hls&&element.error)void failed('Radyo bağlantısı kesildi. Kanalı yeniden açabilirsin.');};
    const onEnded=()=>{if(alive&&lease)void failed('Radyo bağlantısı kesildi. Kanalı yeniden açabilirsin.');};
    element.addEventListener('playing',onPlaying);element.addEventListener('pause',onPause);element.addEventListener('waiting',onWaiting);element.addEventListener('error',onError);element.addEventListener('ended',onEnded);
    const shutdown=(revoke:boolean)=>{revoked=revoke;intent.pause();end();setPlaying(false);setBuffering(false);setScenePending(false);};
    const pageHide=()=>shutdown(false),authStop=()=>shutdown(true);window.addEventListener('pagehide',pageHide);window.addEventListener('home-watch-stop',authStop);
    const timer=setInterval(()=>{
      if(!alive||!lease||pulse)return;const id=lease.id;pulse=true;
      void mutate(`/music/radio/sessions/${id}/heartbeat`).catch(reason=>{if(alive&&lease?.id===id&&reason instanceof ApiError&&[401,403,404,409,410].includes(reason.status)){setError(reason.message);shutdown(true);}}).finally(()=>{pulse=false;});
    },15000);
    void start();
    return()=>{alive=false;intent.pause();end();clearInterval(timer);window.removeEventListener('pagehide',pageHide);window.removeEventListener('home-watch-stop',authStop);element.removeEventListener('playing',onPlaying);element.removeEventListener('pause',onPause);element.removeEventListener('waiting',onWaiting);element.removeEventListener('error',onError);element.removeEventListener('ended',onEnded);};
  },[station.id,media]);
  useEffect(()=>{actions.current.scene(scene);},[scene]);
  useEffect(()=>{if(media.current)media.current.volume=volume;},[volume,media]);
  return{playing,buffering,error,scenePending,sceneError,sceneReady,poster,play:()=>actions.current.play(),pause:()=>actions.current.pause(),retry:()=>actions.current.retry(),retryScene:()=>actions.current.scene(selected.current)};
}
