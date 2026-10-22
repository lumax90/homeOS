import {test} from 'node:test';
import assert from 'node:assert/strict';
import {randomId} from '../../web/src/identity.ts';
import {touchDirection,touchMask,withTouch} from '../../web/src/gaming/nativeTouch.ts';
import {nativePadsSample,emptyPointer} from '../../web/src/gaming/nativePad.ts';
import {nativeWhepUrl} from '../../web/src/gaming/nativeTransport.ts';

test('LAN command identities need no secure-context randomUUID and retain 128 random bits',()=>{
  const descriptor=Object.getOwnPropertyDescriptor(globalThis,'crypto'),original=globalThis.crypto;
  Object.defineProperty(globalThis,'crypto',{configurable:true,value:{getRandomValues:original.getRandomValues.bind(original)}});
  try{const ids=Array.from({length:1000},()=>randomId());assert.equal(new Set(ids).size,1000);for(const id of ids)assert.match(id,/^[a-f0-9]{32}$/);}finally{Object.defineProperty(globalThis,'crypto',descriptor!);}
});
test('IP-address WHEP keeps same origin and base path for a receive-only HTTP client',()=>{
  const path='/home/game-native/'+'a'.repeat(32)+'/whep';assert.equal(nativeWhepUrl(path,'http://192.168.1.30:5080/home/games'),'http://192.168.1.30:5080'+path);assert.throws(()=>nativeWhepUrl('http://127.0.0.1:5080'+path,'http://192.168.1.30:5080/home/games'));
});
test('multi-touch direction and face buttons survive independent finger releases',()=>{
  const pointers=new Map([[11,touchDirection(.1,.1)],[12,1<<8]]);assert.equal(touchMask(pointers),(1<<4)|(1<<6)|(1<<8));pointers.delete(11);assert.equal(touchMask(pointers),1<<8);pointers.clear();assert.equal(touchMask(pointers),0);for(const [x,y]of [[.5,.5],[-.1,.2],[NaN,.5],[.7,1.2]])assert.equal(touchDirection(x,y),0);
});
test('touch release cannot release an independently held keyboard button',()=>{
  const physical=nativePadsSample(new Set(['ArrowRight']),new Set(),[],emptyPointer,'gba',new Map());const mixed=withTouch(physical,1<<8);assert.equal(mixed.buttons,(1<<7)|(1<<8));assert.equal(mixed.pressure[8],32767);assert.equal(withTouch(physical,0).buttons,1<<7);assert.equal(physical.pressure[8],0);
});
