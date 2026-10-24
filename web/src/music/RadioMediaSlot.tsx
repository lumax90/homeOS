import {useLayoutEffect,useRef,type RefObject} from 'react';

/** The portal host is constant: fullscreen/scene changes move the same media element, never recreate it. */
export function RadioMediaSlot({host,parking}:{host:HTMLDivElement;parking:RefObject<HTMLDivElement|null>}){
  const target=useRef<HTMLDivElement>(null);
  useLayoutEffect(()=>{
    const parent=target.current!;parent.appendChild(host);
    return()=>{if(host.parentElement===parent&&parking.current)parking.current.appendChild(host);};
  },[host,parking]);
  return <div ref={target} className="music-stock-scene radio-media-slot"/>;
}
