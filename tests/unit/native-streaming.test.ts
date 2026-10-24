import {test} from 'node:test';
import assert from 'node:assert/strict';

const entries=new Map<string,string>();
Object.defineProperty(globalThis,'document',{value:{querySelector:()=>({content:'/home'})},configurable:true});
Object.defineProperty(globalThis,'localStorage',{value:{getItem:(key:string)=>entries.get(key)??null,setItem:(key:string,value:string)=>entries.set(key,value)},configurable:true});
const {nativeStreamingBackend,saveNativeStreamingBackend,parseStreamingBackend}=await import('../../web/src/gaming/nativeStreaming.ts');

test('streaming preference stays device-local, profile-private and base-path scoped',()=>{
  entries.clear();assert.equal(nativeStreamingBackend('one'),'standard');saveNativeStreamingBackend('one','selkies');
  assert.equal(nativeStreamingBackend('one'),'selkies');assert.equal(nativeStreamingBackend('two'),'standard');
  assert.equal(entries.get('home.native-streaming:/home:one'),'selkies');
  saveNativeStreamingBackend('one','standard');assert.equal(nativeStreamingBackend('one'),'standard');
});
test('corrupt or future preference values preserve the established standard backend',()=>{
  for(const value of [null,undefined,'auto','fast','https://example.invalid','SELKIES',{},true])assert.equal(parseStreamingBackend(value),'standard');
  assert.equal(parseStreamingBackend('selkies'),'selkies');entries.set('home.native-streaming:/home:corrupt','unknown');assert.equal(nativeStreamingBackend('corrupt'),'standard');
});
