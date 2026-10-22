/** Request minimum playout buffering only when the browser supports the standard receiver control. */
export function lowDelayReceiver(receiver: RTCRtpReceiver): boolean {
  let applied=false;
  if('jitterBufferTarget' in receiver)try{(receiver as RTCRtpReceiver&{jitterBufferTarget:number}).jitterBufferTarget=0;applied=true;}catch{}
  // Earlier Chromium builds expose the same low-delay request in seconds under this property.
  if('playoutDelayHint' in receiver)try{(receiver as RTCRtpReceiver&{playoutDelayHint:number}).playoutDelayHint=0;applied=true;}catch{}
  return applied;
}
export interface NativeVideoCounters { delay:number; emitted:number; decode:number; frames:number }
export function videoBufferSample(current:NativeVideoCounters,previous?:NativeVideoCounters):{bufferMs:number|null;decodeMs:number|null} {
  const emitted=current.emitted-(previous?.emitted??0),frames=current.frames-(previous?.frames??0);
  const delay=current.delay-(previous?.delay??0),decode=current.decode-(previous?.decode??0);
  return {bufferMs:emitted>0&&delay>=0?delay/emitted*1000:null,decodeMs:frames>0&&decode>=0?decode/frames*1000:null};
}
export interface NativePlayoutCounters{emitted:number;minimum?:number;target?:number}
export function playoutDetails(current:NativePlayoutCounters,previous?:NativePlayoutCounters){
  const count=current.emitted-(previous?.emitted??0);
  const average=(name:'minimum'|'target')=>count>0&&typeof current[name]==='number'?Math.max(0,current[name]!-(previous?.[name]??0))/count*1000:null;
  return {minimumMs:average('minimum'),targetMs:average('target')};
}
