import {test} from 'node:test';
import assert from 'node:assert/strict';
import {nativePadSample,nativePadsSample,videoPointer,emptyPointer,samePad,retroKey} from '../../web/src/gaming/nativePad.ts';
const pad=(pressed=false)=>({connected:true,mapping:'standard',axes:[.4,-.4,0,0],buttons:Array.from({length:16},(_,i)=>({pressed:pressed&&i===0,value:i===6?.3:0}))} as unknown as Gamepad);
test('analog axes, partial trigger pressure and pointer coordinates are real independent inputs',()=>{
  const value=nativePadSample(new Set(),new Set([97]),[pad()],{x:123,y:-456,pressed:true},'3ds');assert.equal(value.axes[0],Math.round(.4*32767));assert.equal(value.pressure[12],Math.round(.3*32767));assert.equal(value.buttons,0);assert.deepEqual(value.keyCodes,[97]);assert.equal(value.pointer.y,-456);assert(samePad(value,{...value}));
});
test('letterbox mapping never clicks outside the video area and accounts for original aspect ratio',()=>{
  const video={videoWidth:400,videoHeight:480,getBoundingClientRect:()=>({left:100,top:50,width:800,height:480} as DOMRect)};assert.equal(videoPointer(video,100,100,true).pressed,false);assert.deepEqual(videoPointer(video,500,290,true),{x:0,y:0,pressed:true});assert.equal(videoPointer(video,300,530,true).y,32767);
});
test('controller disconnect releases its slot without promoting another player into it',()=>{
  const slots=new Map<number,number>();let value=nativePadsSample(new Set(),new Set(),[pad(true),pad(true)],emptyPointer,'ps1',slots);assert(value.buttons!==0);assert(value.ports![0].buttons!==0);value=nativePadsSample(new Set(),new Set(),[null,pad(true)],emptyPointer,'ps1',slots);assert.equal(value.buttons,0);assert(value.ports![0].buttons!==0);
});
test('WASD analog movement does not also press shoulder buttons and keyboard text stays in the game',()=>{
  const value=nativePadSample(new Set(['w']),new Set(),[],emptyPointer,'n64');assert.equal(value.axes[1],-32767);assert.equal(value.buttons,0);assert.equal(retroKey({key:'a',code:'KeyA'}),97);assert.equal(retroKey({key:'Shift',code:'ShiftLeft'}),304);
});
test('shift changes cannot leave symbol keys stuck and computer keys do not also become a RetroPad',()=>{
  assert.equal(retroKey({key:'!',code:'Digit1'}),49);assert.equal(retroKey({key:'1',code:'Digit1'}),49);
  const computer=nativePadSample(new Set(['Enter','ArrowUp','w']),new Set([13,273,119]),[],emptyPointer,'dos');assert.equal(computer.buttons,0);assert.equal(computer.axes[1],0);assert.deepEqual(computer.keyCodes,[13,119,273]);
  const controller=pad();(controller.axes as number[])[0]=1;assert.equal(nativePadSample(new Set(),new Set(),[controller],emptyPointer,'n64').buttons,0);
});
