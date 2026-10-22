import {test} from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import {runInNewContext} from 'node:vm';
import {nativeWhepUrl} from '../../web/src/gaming/nativeTransport.ts';

const id='a'.repeat(32),ticket='b'.repeat(64),reader='11111111-1111-4111-8111-111111111111';
test('relative Home WHEP path reproduces the upstream URL-base error; its absolute adapter retains origin and base path',()=>{
  const path=`/game-native/${id}/whep`;
  assert.throws(()=>new URL(`${path}/${reader}`,path),TypeError);
  for(const base of ['', '/home']){
    const absolute=nativeWhepUrl(base+path,`http://localhost:5080${base}/games/rom-fixture/play`);
    assert.equal(absolute,`http://localhost:5080${base}${path}`);
    assert.equal(new URL(base+path+'/'+reader,absolute).href,absolute+'/'+reader);
  }
});
test('native WHEP endpoint cannot expose the ticket to another origin or arbitrary route',()=>{
  for(const path of [`https://foreign.invalid/game-native/${id}/whep`,`//foreign.invalid/game-native/${id}/whep`,'/api/operations',`/game-native/${id}/whep?ticket=${ticket}`,`/game-native/${id}/whep#fragment`])assert.throws(()=>nativeWhepUrl(path,'http://localhost:5080/games'));
});

// Runs the actual pinned SDK with isolated peer/HTTP fixtures, not a browser or real gameplay test.
async function harness(location:string){
  const code=await readFile(new URL('../../web/public/native/home-reader.js',import.meta.url),'utf8');
  const calls:Array<{url:string;init:RequestInit}> = [],peers:Peer[]=[];let ready!:()=>void;
  const negotiated=new Promise<void>(resolve=>{ready=resolve;});
  class Peer{
    transceivers:string[]=[];connectionState='new';onicecandidate?: (event:unknown)=>void;onconnectionstatechange?:()=>void;
    constructor(){peers.push(this);}
    addTransceiver(kind:string){this.transceivers.push(kind);}
    createDataChannel(){}
    async createOffer(){return {sdp:'v=0\r\na=ice-ufrag:fixture\r\na=ice-pwd:fixture-password\r\nm=video 9 UDP/TLS/RTP/SAVPF 96\r\na=rtpmap:96 H264/90000\r\nm=audio 9 UDP/TLS/RTP/SAVPF 111\r\na=rtpmap:111 opus/48000/2\r\na=fmtp:111 minptime=10\r\n'};}
    async setLocalDescription(){}
    async setRemoteDescription(){if(this.transceivers.length===1)throw new Error('isolated unsupported codec');ready();}
    close(){}
  }
  const errors:string[]=[];const fetcher=async(url:string,init:RequestInit)=>{
    calls.push({url,init});if(init.method==='POST')return new Response('isolated SDP answer',{status:201,headers:{Location:location}});return new Response(null,{status:204});
  };
  const window={location:{origin:'http://localhost:5080'},fetch:fetcher,setTimeout:()=>1};
  const context={window,URL,Headers,RTCPeerConnection:Peer,RTCSessionDescription:class{value:unknown;constructor(value:unknown){this.value=value;}},clearTimeout:()=>{}};
  runInNewContext(code,context);
  const constructor=(window as typeof window & {HomeNativeWebRTCReader:new(conf:unknown)=>{close():void}}).HomeNativeWebRTCReader;
  const instance=new constructor({url:`http://localhost:5080/home/game-native/${id}/whep`,token:ticket,onError:(error:string)=>errors.push(error)});
  return {calls,peers,errors,negotiated,instance,window,fetcher};
}
test('actual upstream reader uses absolute Location resolution and scoped bearer auth for OPTIONS POST PATCH DELETE',async()=>{
  const fixture=await harness(`/home/game-native/${id}/whep/${reader}`);await fixture.negotiated;
  const peer=fixture.peers.find(peer=>peer.transceivers.length===2)!;assert.ok(peer);
  peer.onicecandidate?.({candidate:{sdpMLineIndex:0,candidate:'candidate:1 1 UDP 1 127.0.0.1 5000 typ host'}});
  for(let i=0;i<12;i++)await new Promise<void>(resolve=>queueMicrotask(resolve));
  peer.connectionState='failed';peer.onconnectionstatechange?.();
  for(let i=0;i<12;i++)await new Promise<void>(resolve=>queueMicrotask(resolve));
  assert.deepEqual(fixture.calls.map(call=>call.init.method),['OPTIONS','POST','PATCH','DELETE']);
  for(const call of fixture.calls){assert.equal(new Headers(call.init.headers).get('Authorization'),'Bearer '+ticket);assert.equal(call.init.credentials,'same-origin');assert.equal(call.init.redirect,'error');assert.ok(call.url.startsWith(`http://localhost:5080/home/game-native/${id}/whep`));assert.ok(!call.url.includes(ticket));}
  assert.equal(new Headers(fixture.calls[2].init.headers).get('If-Match'),'*');assert.equal(fixture.window.fetch,fixture.fetcher);
  assert.ok(!fixture.errors.some(error=>error.includes('Invalid base URL')));fixture.instance.close();
});
test('actual SDK adapter rejects foreign WHEP Location before any credentialed resource request',async()=>{
  const fixture=await harness(`https://foreign.invalid/reader/${reader}`);
  for(let i=0;i<50;i++)await new Promise<void>(resolve=>queueMicrotask(resolve));
  assert.deepEqual(fixture.calls.map(call=>call.init.method),['OPTIONS','POST']);
  assert.ok(fixture.errors.some(error=>error.includes('left its Home session')));fixture.instance.close();
});
