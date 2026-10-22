import type {NativePadSample} from './nativePad.ts';

/** Eight-way touch D-pad, in normalized coordinates; the middle is neutral. */
export function touchDirection(x:number,y:number):number {
  if(!Number.isFinite(x)||!Number.isFinite(y)||x<0||x>1||y<0||y>1)return 0;
  return (x<.32?1<<6:x>.68?1<<7:0)|(y<.32?1<<4:y>.68?1<<5:0);
}
export function touchMask(pointers:ReadonlyMap<number,number>):number {let mask=0;for(const buttons of pointers.values())mask|=buttons;return mask&0xffff;}
export function withTouch(sample:NativePadSample,buttons:number):NativePadSample {
  return buttons?{...sample,buttons:sample.buttons|buttons,pressure:sample.pressure.map((value,index)=>buttons&(1<<index)?32767:value)}:sample;
}
