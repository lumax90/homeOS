import {useEffect,useRef,useState,type RefObject} from 'react';
import {appUrl,mutate,request} from '../api';
import {browserPlayback} from '../playbackIdentity';
import {selectPlaybackTransport} from '../playbackTransport';
import type {LiveWatch} from './types';

/** One native media element, one leased stream, and the shared HLS transport/recovery owner. */
export function useLiveStream(channelId:string,video:RefObject<HTMLVideoElement|null>){
  const[attempt,setAttempt]=useState(0),[error,setError]=useState(''),[notice,setNotice]=useState(''),[busy,setBusy]=useState(true);const revoked=useRef(false);
  useEffect(()=>{
    if(revoked.current)return;const element=video.current!;let alive=true,id:string|null=null,destroy:(()=>void)|undefined,pulse=false,usingHls=false;const abort=new AbortController(),revision=browserPlayback.nextRevision(),clientId=browserPlayback.clientId;setBusy(true);setError('');setNotice('');
    const deadline=setTimeout(()=>abort.abort(),55000);
    function stopMedia(){destroy?.();destroy=undefined;element.pause();element.removeAttribute('src');element.load();}
    function close(){void mutate(`/live/clients/${clientId}/close`,{revision},'POST',true).catch(()=>{});}
    function failed(reason:Error){if(!alive)return;alive=false;id=null;abort.abort();setError(reason.name==='AbortError'?'Kanal zamanında başlayamadı. Yeniden dene.':reason.message);setBusy(false);stopMedia();close();}
    const play=()=>{void element.play().catch(()=>{if(alive)setNotice('Başlamak için oynat düğmesine dokun.');});};
    const ready=()=>{if(alive){setBusy(false);setNotice('');}},waiting=()=>{if(alive&&!element.paused)setBusy(true);},mediaError=()=>{if(alive&&id&&!usingHls)failed(Error('Canlı yayın bağlantısı kesildi; kanalı yeniden açabilirsin.'));};
    element.addEventListener('playing',ready);element.addEventListener('waiting',waiting);element.addEventListener('error',mediaError);
    void request<LiveWatch>(`/live/channels/${channelId}/play`,{method:'POST',body:JSON.stringify({clientId,clientRevision:revision}),signal:abort.signal}).then(async launch=>{
      clearTimeout(deadline);if(!alive){void mutate(`/live/sessions/${launch.id}`,undefined,'DELETE',true).catch(()=>{});return;}id=launch.id;
      const {default:Hls}=await import('hls.js');if(!alive)return;const url=appUrl(launch.url),transport=selectPlaybackTransport('live-hls',Hls.isSupported(),element.canPlayType('application/vnd.apple.mpegurl'));
      if(transport==='native-hls'){element.src=url;setBusy(false);play();return;}if(transport!=='hls.js'){failed(Error('Bu cihaz HLS oynatamıyor.'));return;}
      const hls=new Hls({maxBufferLength:30,backBufferLength:30});usingHls=true;destroy=()=>hls.destroy();hls.on(Hls.Events.MANIFEST_PARSED,()=>{if(alive){setBusy(false);play();}});hls.on(Hls.Events.ERROR,(_,data)=>{if(data.fatal)failed(Error('Canlı yayın bağlantısı kesildi; kanalı yeniden açabilirsin.'));});hls.loadSource(url);hls.attachMedia(element);
    }).catch(failed).finally(()=>clearTimeout(deadline));
    const heartbeat=setInterval(()=>{if(!alive||!id||pulse)return;pulse=true;void mutate(`/live/sessions/${id}/heartbeat`).catch(failed).finally(()=>{pulse=false;});},15000);
    const authStop=()=>{revoked.current=true;alive=false;abort.abort();stopMedia();close();setBusy(false);},pageHide=()=>{alive=false;abort.abort();stopMedia();close();setBusy(false);setError('Canlı yayın bağlantısı kesildi; kanalı yeniden açabilirsin.');};window.addEventListener('home-watch-stop',authStop);window.addEventListener('pagehide',pageHide);
    return()=>{alive=false;clearTimeout(deadline);clearInterval(heartbeat);abort.abort();element.removeEventListener('playing',ready);element.removeEventListener('waiting',waiting);element.removeEventListener('error',mediaError);window.removeEventListener('home-watch-stop',authStop);window.removeEventListener('pagehide',pageHide);stopMedia();close();};
  },[channelId,attempt,video]);
  return{busy,error,notice,setNotice,retry:()=>{if(!revoked.current)setAttempt(value=>value+1);}};
}
