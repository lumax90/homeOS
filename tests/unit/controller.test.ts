import {test} from 'node:test';
import assert from 'node:assert/strict';
import {GamepadReader,defaultControllerPreferences as prefs,standardButtons,completeMapping,validPreferences,spatialTarget,remoteAction,type PadSample} from '../../web/src/controller/model.ts';
import {controllerPlayback} from '../../web/src/controller/playback.ts';

function pad(buttons:number[]=[],axes=[0,0],index=0,mapping='standard'):PadSample{return{index,id:'isolated-'+index,mapping,connected:true,buttons:Array.from({length:17},(_,i)=>({pressed:buttons.includes(i),value:buttons.includes(i)?1:0})),axes};}
test('connecting a held button or stick cannot activate a control until release and a fresh press',()=>{
  const reader=new GamepadReader();assert.deepEqual(reader.sample([pad([0])],0,prefs),[]);assert.deepEqual(reader.sample([pad([0])],1000,prefs),[]);
  assert.deepEqual(reader.sample([pad()],1001,prefs),[]);assert.equal(reader.sample([pad([0])],1002,prefs)[0].action,'confirm');
  assert.deepEqual(reader.sample([pad([0])],2000,prefs),[]);
});
test('directions repeat at bounded intervals, while confirm/back/menu never repeat',()=>{
  const reader=new GamepadReader();reader.sample([pad()],0,prefs);
  assert.equal(reader.sample([pad([15])],1,prefs)[0].action,'right');assert.deepEqual(reader.sample([pad([15])],350,prefs),[]);
  assert.equal(reader.sample([pad([15])],381,prefs)[0].repeat,true);assert.deepEqual(reader.sample([pad([15])],400,prefs),[]);
  reader.sample([pad()],600,prefs);assert.equal(reader.sample([pad([1])],601,prefs)[0].action,'back');assert.deepEqual(reader.sample([pad([1])],5000,prefs),[]);
  reader.sample([pad()],5001,prefs);assert.equal(reader.sample([pad([9])],5002,prefs)[0].action,'menu');assert.deepEqual(reader.sample([pad([9])],9000,prefs),[]);
});
test('analog deadzone, hysteresis and opposite directions do not produce jitter navigation',()=>{
  const reader=new GamepadReader();reader.sample([pad()],0,prefs);assert.deepEqual(reader.sample([pad([], [.12,-.15])],1,prefs),[]);
  assert.equal(reader.sample([pad([], [.5,.05])],2,prefs)[0].action,'right');assert.deepEqual(reader.sample([pad([], [.3,.05])],3,prefs),[]);
  assert.deepEqual(reader.sample([pad([12,13])],4,prefs),[]);
});
test('right stick scroll is neutral-gated, bounded and separate from focus navigation',()=>{
  const reader=new GamepadReader();assert.deepEqual(reader.sample([pad([], [0,0,0,.7])],0,prefs),[]);reader.sample([pad([], [0,0,0,0])],1,prefs);
  assert.equal(reader.sample([pad([], [0,0,0,.7])],2,prefs)[0].action,'scrolldown');assert.deepEqual(reader.sample([pad([], [0,0,0,.7])],3,prefs),[]);
  assert.equal(reader.sample([pad([], [0,0,0,.7])],382,prefs)[0].repeat,true);
});
test('pointer/keyboard takeover and background resume require neutral input before gamepad repeats',()=>{
  const reader=new GamepadReader();reader.sample([pad()],0,prefs);reader.sample([pad([15])],1,prefs);reader.suspend();
  assert.deepEqual(reader.sample([pad([15])],1000,prefs),[]);reader.sample([pad()],1001,prefs);assert.equal(reader.sample([pad([15])],1002,prefs)[0].action,'right');
});
test('disconnect and reused indices cannot replay a previous controller action',()=>{
  const reader=new GamepadReader();reader.sample([pad()],0,prefs);reader.sample([pad([0])],1,prefs);reader.sample([null],2,prefs);
  assert.deepEqual(reader.sample([pad([0])],3,prefs),[]);reader.sample([pad()],4,prefs);assert.equal(reader.sample([pad([0])],5,prefs)[0].action,'confirm');
});
test('nonstandard pads do nothing until a complete explicit mapping is supplied',()=>{
  const reader=new GamepadReader();reader.sample([pad([], [0,0],0,'')],0,prefs);assert.deepEqual(reader.sample([pad([0],[0,0],0,'')],1,prefs),[]);
  const configured={...prefs,mappings:{'isolated-0':standardButtons}};reader.sample([pad([], [0,0],0,'')],2,configured);
  assert.equal(reader.sample([pad([0],[0,0],0,'')],3,configured)[0].action,'confirm');
  assert.equal(completeMapping({confirm:0,back:1}),false);
});
test('binding capture consumes the button rather than navigating, and multiple pads keep independent state',()=>{
  const reader=new GamepadReader();reader.sample([pad(),pad([], [0,0],1)],0,prefs);let capture=-1;
  assert.deepEqual(reader.sample([pad([0]),pad([], [0,0],1)],1,prefs,(_,button)=>{capture=button;return true;}),[]);assert.equal(capture,0);
  assert.equal(reader.sample([pad([0]),pad([1],[0,0],1)],2,prefs)[0].index,1);
});
test('preferences bound deadzones, controller counts and button indices without enabling a malformed mapping',()=>{
  assert.equal(validPreferences({deadzone:0}).deadzone,.2);assert.equal(validPreferences({deadzone:Infinity}).deadzone,.35);
  assert.equal(validPreferences({enabled:false}).enabled,false);assert.equal(validPreferences({mappings:{bad:{up:99,confirm:-1,back:1}}}).mappings.bad.up,undefined);
});
test('spatial navigation keeps the visual column and refuses opposite or identical rectangles',()=>{
  const box=(x:number,y:number)=>({left:x,top:y,right:x+100,bottom:y+120});const origin=box(120,0);
  assert.equal(spatialTarget([box(0,150),box(120,150),box(240,150)],origin,'down'),1);
  assert.equal(spatialTarget([box(0,0),box(240,0)],origin,'right'),1);
  assert.equal(spatialTarget([origin,box(0,0)],origin,'right'),-1);
  assert.equal(spatialTarget([box(0,300),box(120,300)],box(0,150),'down',170),1);
});
test('nearby off-column controls beat distant aligned ones without drifting within the same row',()=>{
  const origin={left:100,top:100,right:200,bottom:180};
  assert.equal(spatialTarget([{left:210,top:200,right:250,bottom:245},{left:110,top:850,right:190,bottom:950}],origin,'down'),0);
  assert.equal(spatialTarget([{left:260,top:140,right:300,bottom:180},{left:100,top:240,right:200,bottom:320}],origin,'down'),1);
  assert.equal(spatialTarget([{left:110,top:240,right:190,bottom:320},{left:220,top:110,right:270,bottom:165}],origin,'right'),1);
});
test('R3 focuses the player once per press and keeps the existing button assignments',()=>{
  const reader=new GamepadReader();assert.equal(standardButtons.focusplayer,11);assert.equal(standardButtons.playpause,2);assert.equal(standardButtons.menu,9);assert.equal(standardButtons.next,5);
  assert.deepEqual(reader.sample([pad([11])],0,prefs),[]);reader.sample([pad()],1,prefs);assert.equal(reader.sample([pad([11])],2,prefs)[0].action,'focusplayer');assert.deepEqual(reader.sample([pad([11])],900,prefs),[]);
});
test('remote direction, media and platform Back keys are normalized without hijacking printable typing',()=>{
  assert.equal(remoteAction('ArrowLeft'),'left');assert.equal(remoteAction('Enter'),'confirm');assert.equal(remoteAction('Unidentified',10009),'back');assert.equal(remoteAction('Unidentified',461),'back');
  assert.equal(remoteAction('MediaPlayPause'),'playpause');assert.equal(remoteAction('a'),undefined);
  assert.equal(remoteAction('Unidentified',37),'left');assert.equal(remoteAction('Unidentified',13),'confirm');
});
test('controller media commands use the existing port, clamp absolute seek and honor room/pending permission',async()=>{
  const calls:unknown[]=[];const state={position:97,duration:100,ready:true,pending:false};const port={read:()=>state,play:async()=>{calls.push('play');},pause:()=>calls.push('pause'),seek:(value:number)=>calls.push(value)};
  controllerPlayback('seekforward',port,true,true,()=>{});assert.deepEqual(calls,[100]);state.position=2;controllerPlayback('seekback',port,true,true,()=>{});assert.deepEqual(calls,[100,0]);
  controllerPlayback('playpause',port,true,false,()=>{});state.pending=true;controllerPlayback('playpause',port,true,true,()=>{});assert.equal(calls.length,2);
  state.pending=false;controllerPlayback('playpause',port,true,true,()=>{});await Promise.resolve();assert.equal(calls.at(-1),'play');controllerPlayback('playpause',port,false,true,()=>{});assert.equal(calls.at(-1),'pause');
  state.duration=NaN;controllerPlayback('seekforward',port,true,true,()=>{});assert.equal(calls.at(-1),'pause');
});
