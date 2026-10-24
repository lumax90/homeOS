// Home's scoped host adapter. The released Selkies client owns signalling, RTP,
// congestion control and playback; this facade exposes its receiver to Home's
// existing diagnostics and keeps all gameplay input in Home's input owner.
const peers=new Set();
const state={receiver:null,error:null,release:()=>{
  for(const peer of peers)peer.close();peers.clear();state.receiver=null;
  const prefix=location.pathname.replace(/\/(?:index\.html)?$/,'');
  document.cookie=`selkies_token=;Path=${prefix}/api/;Max-Age=0;SameSite=Strict`;
}};
window.homeSelkies=state;
window.addEventListener('pagehide',state.release);
// This viewer has a per-session URL. Home owns persistent device/profile
// preferences; upstream viewer caches stay in this frame instead of leaving
// a new set of origin-storage keys behind for every game session.
for(const name of ['localStorage','sessionStorage']){
  const values=new Map();
  const storage={get length(){return values.size;},key:index=>[...values.keys()][index]??null,getItem:key=>values.get(String(key))??null,setItem:(key,value)=>values.set(String(key),String(value)),removeItem:key=>values.delete(String(key)),clear:()=>values.clear()};
  try{Object.defineProperty(window,name,{value:storage,configurable:true});}catch{}
}
window.__SELKIES_STREAMING_MODE__='webrtc';
window.__SELKIES_DEFER_INITIALIZATION=true;
const announce=name=>window.dispatchEvent(new Event(name));
const fail=()=>{state.error='Selkies görüntü bağlantısı kurulamadı.';announce('home-selkies-error');};
if(typeof RTCPeerConnection!=='function')fail();
else{
  const original=window.RTCPeerConnection;
  window.RTCPeerConnection=new Proxy(original,{construct(target,args,newTarget){
    const peer=Reflect.construct(target,args,newTarget);
    peers.add(peer);
    peer.addEventListener('track',event=>{if(event.track.kind==='video'){state.error=null;state.receiver=event.receiver;announce('home-selkies-track');}});
    peer.addEventListener('connectionstatechange',()=>{if(peer.connectionState==='failed')fail();if(peer.connectionState==='closed')peers.delete(peer);});
    return peer;
  }});
  const style=document.createElement('style');
  style.textContent='html,body,#app,.video-container{width:100%!important;height:100%!important;margin:0!important;overflow:hidden!important}#playButton,.status-bar,.loading-text{display:none!important}.video-container video{inset:0!important;width:100%!important;height:100%!important;object-fit:var(--home-fit,contain)!important;image-rendering:var(--home-pixels,pixelated)!important}';
  document.head.appendChild(style);
  try{await import('./src/selkies-core.js');window.selkiesCoreInitialize();}catch{fail();}
}
