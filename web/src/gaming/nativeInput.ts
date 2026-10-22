export const nativeKeys: Readonly<Record<string,number>> = {ArrowUp:4,ArrowDown:5,ArrowLeft:6,ArrowRight:7,j:0,k:8,Enter:3,Shift:2,q:10,w:11};
export const retroPadKeys:Readonly<Record<string,number>>={ArrowUp:4,ArrowDown:5,ArrowLeft:6,ArrowRight:7,j:0,k:8,u:1,i:9,Enter:3,Shift:2,q:10,e:11,r:12,t:13,'1':14,'2':15};
export function nativeInputMask(keys:ReadonlySet<string>,pads:ArrayLike<Gamepad|null>,fullPad=false,analogOnly=false):number {
  const mapping=fullPad?retroPadKeys:nativeKeys;let mask=0;for(const key of keys){const bit=mapping[key];if(bit!==undefined)mask|=1<<bit;}
  const pad=Array.from(pads).find(pad=>pad?.connected&&pad.mapping==='standard');if(!pad)return mask;
  for(const [button,bit] of [[0,fullPad?0:8],[1,fullPad?8:0],[8,2],[9,3],[12,4],[13,5],[14,6],[15,7],[4,10],[5,11]])if(pad.buttons[button]?.pressed)mask|=1<<bit;
  if(fullPad)for(const [button,bit] of [[2,1],[3,9],[6,12],[7,13],[10,14],[11,15]])if(pad.buttons[button]?.pressed)mask|=1<<bit;
  if(!analogOnly){if(pad.axes[0]<-.5)mask|=1<<6;if(pad.axes[0]>.5)mask|=1<<7;if(pad.axes[1]<-.5)mask|=1<<4;if(pad.axes[1]>.5)mask|=1<<5;}return mask;
}
interface Clock { now():number; later(callback:()=>void,ms:number):unknown; cancel(handle:unknown):void }
const clock:Clock={now:()=>performance.now(),later:(callback,ms)=>setTimeout(callback,ms),cancel:handle=>clearTimeout(handle as ReturnType<typeof setTimeout>)};
/** Bounded, serialized edge transport; samples are coalesced only when unchanged, never across press/release. */
export class NativeInputSender<T=number> {
  private edges:T[]=[];private desired:T|undefined;private pending=false;private closed=false;private lastSent=-Infinity;private timer:unknown;
  private send:(buttons:T)=>Promise<unknown>;private fail:(error:unknown)=>void;private time:Clock;private interval:()=>number;private equal:(a:T|undefined,b:T)=>boolean;private coalesce:(a:T,b:T)=>boolean;
  constructor(send:(buttons:T)=>Promise<unknown>,fail:(error:unknown)=>void,time:Clock=clock,interval:()=>number=()=>20,equal:(a:T|undefined,b:T)=>boolean=(a,b)=>a===b,coalesce:(a:T,b:T)=>boolean=()=>false){this.send=send;this.fail=fail;this.time=time;this.interval=interval;this.equal=equal;this.coalesce=coalesce;}
  stage(buttons:T){
    if(this.closed)return;
    if(!this.equal(this.desired,buttons)){
      if(this.edges.length&&this.coalesce(this.edges[this.edges.length-1],buttons)){this.desired=buttons;this.edges[this.edges.length-1]=buttons;this.pump();return;}
      if(this.edges.length>=32){this.dispose();this.fail(new Error('Oyun girdisi kuyruğu doldu; tuşları bırakıp tekrar dene.'));return;}
      this.desired=buttons;this.edges.push(buttons);
    }else if(!this.pending&&!this.edges.length&&this.time.now()-this.lastSent>=200)this.edges.push(buttons);
    this.pump();
  }
  private pump(){
    if(this.closed||this.pending||this.timer!==undefined||!this.edges.length)return;
    const wait=this.interval()-(this.time.now()-this.lastSent);
    if(wait>0){this.timer=this.time.later(()=>{this.timer=undefined;this.pump();},wait);return;}
    const buttons=this.edges.shift()!;this.pending=true;this.lastSent=this.time.now();
    void this.send(buttons).catch(error=>{if(!this.closed){this.dispose();this.fail(error);}}).finally(()=>{this.pending=false;this.pump();});
  }
  dispose(){this.closed=true;this.edges=[];if(this.timer!==undefined)this.time.cancel(this.timer);this.timer=undefined;}
}
