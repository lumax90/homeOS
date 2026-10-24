import {useEffect,useRef,type RefObject} from 'react';
import type {NativeDisplayPreferences} from './nativePreferences';

interface ViewerWindow extends Window{homeSelkies?:{receiver:RTCRtpReceiver|null;error:string|null;release:()=>void}}
interface Props{url:string;frame:RefObject<HTMLIFrameElement|null>;display:NativeDisplayPreferences;muted:boolean;size:{width:number;height:number}|null;onReady:(video:HTMLVideoElement,receiver:RTCRtpReceiver)=>void;onError:(message:string)=>void}

/** Released Selkies viewer, with Home's controls and device-owned input outside the frame. */
export function SelkiesViewer(props:Props){
  const latest=useRef(props);latest.current=props;
  const refresh=useRef(()=>{});
  useEffect(()=>{
    const frame=props.frame.current!;let stop=()=>{};
    const loaded=()=>{
      stop();const child=frame.contentWindow as ViewerWindow|null,doc=frame.contentDocument;if(!child||!doc)return;
      let closed=false,attached:HTMLVideoElement|null=null,lastReceiver:RTCRtpReceiver|null=null;
      const timeout=setTimeout(()=>{if(!closed&&!lastReceiver)latest.current.onError('Selkies görüntü bağlantısı kurulamadı.');},45000);
      const changed=()=>{
        if(closed)return;const state=child.homeSelkies;
        if(state?.error){clearTimeout(timeout);latest.current.onError(state.error);return;}
        const element=doc.querySelector('video');if(!element)return;
        if(element!==attached){attached?.removeEventListener('playing',changed);attached?.removeEventListener('volumechange',changed);attached=element;element.addEventListener('playing',changed);element.addEventListener('volumechange',changed);}
        const volume=latest.current.display.volume/100;if(element.volume!==volume)element.volume=volume;if(element.muted!==latest.current.muted)element.muted=latest.current.muted;
        doc.documentElement.style.setProperty('--home-fit',latest.current.display.fit==='fill'?'fill':'contain');
        doc.documentElement.style.setProperty('--home-pixels',latest.current.display.smooth?'auto':'pixelated');
        if(state?.receiver&&element.readyState>=2&&lastReceiver!==state.receiver){clearTimeout(timeout);lastReceiver=state.receiver;latest.current.onReady(element,state.receiver);}
      };
      refresh.current=changed;
      const observer=new MutationObserver(changed);observer.observe(doc.body,{childList:true,subtree:true});
      child.addEventListener('home-selkies-track',changed);child.addEventListener('home-selkies-error',changed);changed();
      stop=()=>{closed=true;clearTimeout(timeout);observer.disconnect();attached?.removeEventListener('playing',changed);attached?.removeEventListener('volumechange',changed);child.removeEventListener('home-selkies-track',changed);child.removeEventListener('home-selkies-error',changed);child.homeSelkies?.release();refresh.current=()=>{};};
    };
    frame.addEventListener('load',loaded);
    // The listener is installed before navigating, including cache-fast local loads.
    frame.src=props.url;
    return()=>{frame.removeEventListener('load',loaded);stop();frame.src='about:blank';};
  },[props.url,props.frame]);
  useEffect(()=>refresh.current(),[props.display.fit,props.display.smooth,props.display.volume,props.muted]);
  return <iframe ref={props.frame} title="Selkies oyun yayını" className="native-game-video native-selkies-viewer" style={props.size??undefined} tabIndex={-1} allow="autoplay; fullscreen" aria-hidden="true"/>;
}
