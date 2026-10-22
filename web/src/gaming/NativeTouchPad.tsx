import {useEffect,useRef,useState,type PointerEvent} from 'react';
import {ChevronDown,ChevronLeft,ChevronRight,ChevronUp} from 'lucide-react';
import {translate as t,useUiLanguage} from '../i18n';
import {touchDirection,touchMask} from './nativeTouch';

export function NativeTouchPad({system,onChange}:{system:string;onChange:(buttons:number)=>void}) {
  useUiLanguage();const pointers=useRef(new Map<number,number>()),change=useRef(onChange),[mask,setMask]=useState(0);change.current=onChange;
  function update(id:number,buttons:number){if(buttons)pointers.current.set(id,buttons);else pointers.current.delete(id);const value=touchMask(pointers.current);setMask(value);change.current(value);}
  useEffect(()=>{const clear=()=>{pointers.current.clear();setMask(0);change.current(0);};window.addEventListener('blur',clear);document.addEventListener('visibilitychange',clear);return()=>{clear();window.removeEventListener('blur',clear);document.removeEventListener('visibilitychange',clear);};},[]);
  function down(event:PointerEvent<HTMLElement>,buttons:number){event.preventDefault();event.currentTarget.setPointerCapture(event.pointerId);update(event.pointerId,buttons);}
  function release(event:PointerEvent<HTMLElement>){event.preventDefault();update(event.pointerId,0);}
  function direction(event:PointerEvent<HTMLDivElement>){const rect=event.currentTarget.getBoundingClientRect();return touchDirection((event.clientX-rect.left)/rect.width,(event.clientY-rect.top)/rect.height);}
  const simple=['gba','gb','gbc'].includes(system),ps=system==='ps1';
  const button=(label:string,id:number,className='')=><button key={id} type="button" className={'native-touch-button '+className} aria-label={label} aria-pressed={!!(mask&(1<<id))} onPointerDown={event=>down(event,1<<id)} onPointerUp={release} onPointerCancel={release} onLostPointerCapture={release} onContextMenu={event=>event.preventDefault()}>{label}</button>;
  return <div className="native-touch-pad" data-controller-exclude aria-label="Dokunmatik oyun kumandası">
    <div className="native-touch-shoulders">{button('L',10)}{!simple&&button('L2',12)}{!simple&&button('R2',13)}{button('R',11)}</div>
    <div className="native-touch-main"><div className="native-touch-directions" aria-label="Yönler" onPointerDown={event=>down(event,direction(event))} onPointerMove={event=>{if(event.currentTarget.hasPointerCapture(event.pointerId))update(event.pointerId,direction(event));}} onPointerUp={release} onPointerCancel={release} onLostPointerCapture={release} onContextMenu={event=>event.preventDefault()}><ChevronUp data-held={!!(mask&(1<<4))}/><ChevronLeft data-held={!!(mask&(1<<6))}/><i/><ChevronRight data-held={!!(mask&(1<<7))}/><ChevronDown data-held={!!(mask&(1<<5))}/></div>
      <div className={'native-touch-actions'+(simple?' is-simple':'')}>{!simple&&button(ps?'△':'X',9,'touch-top')}{!simple&&button(ps?'□':'Y',1,'touch-left')}{button(ps?'○':'A',8,'touch-right')}{button(ps?'×':'B',0,'touch-bottom')}</div>
    </div><div className="native-touch-system">{button(t('Select'),2)}{button(t('Start'),3)}</div>
  </div>;
}
