import {nativeInputMask} from './nativeInput.ts';
export interface NativePointer{x:number;y:number;pressed:boolean;mouseButtons?:number;motionX?:number;motionY?:number;wheel?:number;offscreen?:boolean}
export interface NativePadSample{buttons:number;axes:number[];pressure:number[];pointer:NativePointer;keyCodes:number[];ports?:Array<{buttons:number;axes:number[];pressure:number[]}>}
export const emptyPointer:NativePointer={x:0,y:0,pressed:false};
export const retroKeyCodes:Readonly<Record<string,number>>={Enter:13,Tab:9,Backspace:8,Space:32,Delete:127,ArrowUp:273,ArrowDown:274,ArrowRight:275,ArrowLeft:276,Insert:277,Home:278,End:279,PageUp:280,PageDown:281,ShiftLeft:304,ShiftRight:303,ControlLeft:306,ControlRight:305,AltLeft:308,AltRight:307,F1:282,F2:283,F3:284,F4:285,F5:286,F6:287,F7:288,F8:289,F9:290,F10:291,F11:292,F12:293};
const symbols:Readonly<Record<string,number>>={Escape:27,Minus:45,Equal:61,BracketLeft:91,BracketRight:93,Backslash:92,Semicolon:59,Quote:39,Backquote:96,Comma:44,Period:46,Slash:47,NumpadDecimal:266,NumpadDivide:267,NumpadMultiply:268,NumpadSubtract:269,NumpadAdd:270,NumpadEnter:271,NumpadEqual:272,CapsLock:301,NumLock:300};
const analogSystems=new Set(['3ds','n64','ps1','ps2','psp','dreamcast','gamecube','wii','wiiu','xbox']);
export function nativeKeyboardSystem(system:string){return /^(dos|commodore_|apple_ii|msx|zx|atari_st|pc_|pcxt|sharp_x68000|bbcmicro|cpc|sam_coupe|thomson_moto|mac68k|rp6502|uxn)/.test(system);}
export function retroKey(event:Pick<KeyboardEvent,'key'|'code'>):number|undefined{
  if(/^Key[A-Z]$/.test(event.code))return event.code.charCodeAt(3)+32;
  if(/^Digit[0-9]$/.test(event.code))return event.code.charCodeAt(5);
  if(/^Numpad[0-9]$/.test(event.code))return 256+Number(event.code.slice(6));
  const known=retroKeyCodes[event.code]??symbols[event.code];if(known!==undefined)return known;
  if(event.key.length===1&&event.key.charCodeAt(0)>=32&&event.key.charCodeAt(0)<=126)return event.key.toLowerCase().charCodeAt(0);
  return retroKeyCodes[event.code]??retroKeyCodes[event.key];
}
export function nativePadSample(keys:ReadonlySet<string>,keyboard:ReadonlySet<number>,pads:ArrayLike<Gamepad|null>,pointer:NativePointer,system:string):NativePadSample{
  const full=!['gba','gb','gbc'].includes(system),pad=Array.from(pads).find(pad=>pad?.connected&&pad.mapping==='standard');
  const axis=(value:number)=>!Number.isFinite(value)||Math.abs(value)<.12?0:Math.round(Math.max(-1,Math.min(1,value))*32767);
  const axes=Array.from({length:4},(_,i)=>axis(pad?.axes[i]??0));
  const computer=nativeKeyboardSystem(system);if(full&&!computer){if(keys.has('a'))axes[0]=-32767;if(keys.has('d'))axes[0]=32767;if(keys.has('w'))axes[1]=-32767;if(keys.has('s'))axes[1]=32767;}
  const pressure=Array.from({length:16},()=>0);for(const [button,id]of [[0,full?0:8],[1,full?8:0],[2,1],[3,9],[4,10],[5,11],[6,12],[7,13],[8,2],[9,3],[10,14],[11,15],[12,4],[13,5],[14,6],[15,7]])pressure[id]=Math.round(Math.max(0,Math.min(1,pad?.buttons[button]?.value??0))*32767);
  return {buttons:nativeInputMask(computer?new Set():keys,pads,full,analogSystems.has(system)||computer),axes,pressure,pointer:{...pointer},keyCodes:[...keyboard].sort((a,b)=>a-b)};
}
export function samePadAction(a:NativePadSample,b:NativePadSample){return a.buttons===b.buttons&&a.pointer.pressed===b.pointer.pressed&&a.pointer.mouseButtons===b.pointer.mouseButtons&&a.keyCodes.length===b.keyCodes.length&&a.keyCodes.every((v,i)=>v===b.keyCodes[i])&&(a.ports?.length??0)===(b.ports?.length??0)&&(a.ports??[]).every((port,i)=>port.buttons===b.ports![i].buttons);}
export function samePad(a:NativePadSample|undefined,b:NativePadSample){return !!a&&samePadAction(a,b)&&a.axes.every((v,i)=>v===b.axes[i])&&a.pressure.every((v,i)=>v===b.pressure[i])&&a.pointer.x===b.pointer.x&&a.pointer.y===b.pointer.y&&a.pointer.motionX===b.pointer.motionX&&a.pointer.motionY===b.pointer.motionY&&a.pointer.wheel===b.pointer.wheel&&a.pointer.offscreen===b.pointer.offscreen&&(a.ports??[]).every((port,i)=>port.axes.every((v,j)=>v===b.ports![i].axes[j])&&port.pressure.every((v,j)=>v===b.ports![i].pressure[j]));}
export function nativePadsSample(keys:ReadonlySet<string>,keyboard:ReadonlySet<number>,pads:ArrayLike<Gamepad|null>,pointer:NativePointer,system:string,slots:Map<number,number>):NativePadSample{
  const devices=Array.from(pads);for(const [index,pad]of devices.entries())if(pad?.connected&&!slots.has(index)&&slots.size<4)slots.set(index,slots.size);
  const padAt=(port:number)=>devices[[...slots].find(([,slot])=>slot===port)?.[0]??-1]??null;
  const main=nativePadSample(keys,keyboard,[padAt(0)],pointer,system);
  main.ports=Array.from({length:3},(_,i)=>{const sample=nativePadSample(new Set(),new Set(),[padAt(i+1)],emptyPointer,system);return {buttons:sample.buttons,axes:sample.axes,pressure:sample.pressure};});return main;
}
/** Map coordinates against the actual displayed video rectangle, including letterboxing/integer scale. */
export function videoPointer(video:Pick<HTMLVideoElement,'videoWidth'|'videoHeight'|'getBoundingClientRect'>,clientX:number,clientY:number,pressed:boolean,fill=false):NativePointer{
  const rect=video.getBoundingClientRect(),vw=video.videoWidth||400,vh=video.videoHeight||480;
  const scale=Math.min(rect.width/vw,rect.height/vh),width=fill?rect.width:vw*scale,height=fill?rect.height:vh*scale;
  if(width<=0||height<=0)return {...emptyPointer};
  const x=(clientX-rect.left-(rect.width-width)/2)/width,y=(clientY-rect.top-(rect.height-height)/2)/height;
  const inside=x>=0&&x<=1&&y>=0&&y<=1;return {x:Math.round((Math.max(0,Math.min(1,x))*2-1)*32767),y:Math.round((Math.max(0,Math.min(1,y))*2-1)*32767),pressed:pressed&&inside,...(!inside?{offscreen:true}:{})};
}
