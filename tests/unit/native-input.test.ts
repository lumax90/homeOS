import {test} from 'node:test';
import assert from 'node:assert/strict';
import {NativeInputSender,nativeInputMask} from '../../web/src/gaming/nativeInput.ts';
import {readFile} from 'node:fs/promises';

function fakeClock(){
  let now=0,next=1;const tasks=new Map<number,{at:number;callback:()=>void}>();
  return {now:()=>now,later:(callback:()=>void,ms:number)=>{const id=next++;tasks.set(id,{at:now+ms,callback});return id;},cancel:(handle:unknown)=>{tasks.delete(handle as number);},advance:(ms:number)=>{now+=ms;for(const [id,task]of [...tasks])if(task.at<=now){tasks.delete(id);task.callback();}}};
}
async function settled(){for(let i=0;i<8;i++)await Promise.resolve();}
test('keyboard/gamepad mappings combine supported buttons without interpreting them as Home navigation',()=>{
  const pad={connected:true,mapping:'standard',buttons:Array.from({length:16},()=>({pressed:false})),axes:[0,0]} as unknown as Gamepad;
  (pad.buttons[0] as {pressed:boolean}).pressed=true;pad.axes[0]=1;
  assert.equal(nativeInputMask(new Set(['j','ArrowUp']),[pad]),(1<<0)|(1<<4)|(1<<8)|(1<<7));
  assert.equal(nativeInputMask(new Set(['Enter','Shift']),[]),(1<<3)|(1<<2));
});
test('full RetroPad adds X/Y/triggers/stick buttons without changing the GBA mapping',()=>{
  const pad={connected:true,mapping:'standard',buttons:Array.from({length:16},()=>({pressed:true})),axes:[0,0]} as unknown as Gamepad;
  assert.equal(nativeInputMask(new Set(),[pad],true),0xffff);
  assert.equal(nativeInputMask(new Set(['u','i','r','t','1','2']),[],true),(1<<1)|(1<<9)|(1<<12)|(1<<13)|(1<<14)|(1<<15));
  assert.equal(nativeInputMask(new Set(['u','i','e','r','1','2']),[]),0);
});
test('first press is immediate and a brief press-release while HTTP is pending is preserved',async()=>{
  const time=fakeClock(),calls:number[]=[];let release!:()=>void;
  const sender=new NativeInputSender(async buttons=>{calls.push(buttons);if(calls.length===1)await new Promise<void>(resolve=>{release=resolve;});},error=>{throw error;},time);
  sender.stage(1<<8);assert.deepEqual(calls,[1<<8]);sender.stage(0);release();await settled();
  time.advance(20);await settled();assert.deepEqual(calls,[1<<8,0]);sender.dispose();
});
test('unchanged held state sends a bounded keepalive instead of flooding or imposing 100ms edge delay',async()=>{
  const time=fakeClock(),calls:number[]=[];const sender=new NativeInputSender(async buttons=>{calls.push(buttons);},error=>{throw error;},time);
  sender.stage(1<<8);await settled();for(let i=0;i<9;i++){time.advance(20);sender.stage(1<<8);await settled();}assert.deepEqual(calls,[1<<8]);
  time.advance(20);sender.stage(1<<8);await settled();assert.deepEqual(calls,[1<<8,1<<8]);
  time.advance(20);sender.stage(0);await settled();assert.deepEqual(calls,[1<<8,1<<8,0]);sender.dispose();
});
test('dispose drops stale epoch edges and overflow fails neutral rather than retaining a stuck press',async()=>{
  const time=fakeClock(),calls:number[]=[],errors:unknown[]=[];let release!:()=>void;
  const sender=new NativeInputSender(async buttons=>{calls.push(buttons);await new Promise<void>(resolve=>{release=resolve;});},error=>errors.push(error),time);
  sender.stage(1<<8);for(let i=0;i<34;i++)sender.stage(i%2?1<<8:0);assert.equal(errors.length,1);release();await settled();time.advance(1000);assert.deepEqual(calls,[1<<8]);
});
test('expensive firewall policy inspection is outside the native relay loop and remains a fail-closed requirement',async()=>{
  const source=await readFile(new URL('../../server/HomeServer.Api/Gaming/NativeGameManager.cs',import.meta.url),'utf8');
  const loop=source.slice(source.indexOf('protected override async Task ExecuteAsync'));
  assert.doesNotMatch(loop,/NativeLocalInputGuard\.InspectPorts/);assert.match(loop,/MonitorInputProtectionAsync/);assert.match(loop,/IsRecentSafeResult/);
  const player=await readFile(new URL('../../web/src/gaming/NativeGamePlayer.tsx',import.meta.url),'utf8');assert.doesNotMatch(player,/time-last>=100/);assert.match(player,/sampleInput\(\)/);
});
