import {test} from 'node:test';
import assert from 'node:assert/strict';
import {createHomeMediaBridge, type HomeMediaPort, type HomeMediaState} from '../../web/src/homeMediaBridge.ts';
import {parsePageRoute, pagePath, detailsPath} from '../../web/src/pageRoutes.ts';
import {createStore, combine} from '@videojs/store';
import {timeFeature, playbackFeature, qualityFeature, audioTrackFeature, textTrackFeature} from '@videojs/core/dom';
import {getRegisteredMedia} from '@videojs/media';

function fixture() {
  const events: unknown[][]=[];
  const state:HomeMediaState={offset:500,position:502,duration:9326,ready:true,pending:false,error:'',subtitle:'',audio:1,quality:'auto'};
  const native=Object.assign(new EventTarget(),{currentTime:2,duration:8,readyState:4,seeking:false,paused:true,ended:false,loop:false,src:'blob:isolated',currentSrc:'blob:isolated',preload:'metadata',crossOrigin:null,volume:.8,muted:false,defaultMuted:false,playbackRate:1,defaultPlaybackRate:1,
    videoWidth:1920,videoHeight:804,error:null,buffered:{length:1,start:()=>0,end:()=>8},played:{length:1,start:()=>0,end:()=>2},seekable:{length:1,start:()=>0,end:()=>8},
    play:async()=>{events.push(['native-play']);},pause:()=>{events.push(['native-pause']);},load:()=>{events.push(['native-load']);},canPlayType:()=> 'probably'});
  let canControl=true;
  const port:HomeMediaPort={read:()=>state,play:async()=>{if(canControl)events.push(['play']);},pause:()=>{if(canControl)events.push(['pause']);},seek:seconds=>{if(canControl){events.push(['seek',seconds]);state.position=seconds;}},retry:()=>events.push(['retry']),rate:value=>{if(canControl){events.push(['rate',value]);native.playbackRate=value;}},volume:(value,muted)=>{if(canControl){events.push(['volume',value,muted]);native.volume=value;native.muted=muted;}},
    subtitle:id=>{events.push(['subtitle',id]);state.subtitle=id;},audio:index=>{events.push(['audio',index]);state.audio=index;},quality:value=>{events.push(['quality',value]);state.quality=value==='original'?'auto':value;},
    audioOptions:()=>[{id:'1',label:'English · AAC 5.1',language:'en'},{id:'3',label:'Türkçe · AC3',language:'tr'}],
    subtitleOptions:()=>[{id:'vtt-en',label:'English',language:'en'},{id:'pgs-tr',label:'Türkçe · PGS',language:'tr'}],
    chapters:()=>[{startTime:0,endTime:500,text:'Opening'},{startTime:500,endTime:9326,text:'Chapter 2'}]};
  const bridge=createHomeMediaBridge(native as unknown as HTMLVideoElement,port);
  return{native,port,bridge,state,events,set canControl(value:boolean){canControl=value;}};
}
test('Video.js gets the full timeline and buffer offsets, while seeks go to Home',()=>{
  const f=fixture();const media=f.bridge.media;
  assert.equal(media.currentTime,502);assert.equal(media.duration,9326);
  assert.equal(getRegisteredMedia(media),f.native);
  assert.equal(media.buffered.start(0),500);assert.equal(media.buffered.end(0),508);
  assert.equal(media.seekable.start(0),0);assert.equal(media.seekable.end(0),9326);
  media.currentTime=8000;
  assert.deepEqual(f.events,[['seek',8000]]);assert.equal(f.native.currentTime,2);
  f.state.pending=true;f.state.ready=false;
  assert.equal(media.currentTime,8000);assert.equal(media.buffered.length,0);assert.equal(media.readyState,0);
  f.state.offset=8000;f.state.pending=false;f.state.ready=true;f.native.currentTime=3;
  assert.equal(media.currentTime,8003);assert.equal(media.duration,9326);
});
test('audio and subtitle menus select real Home stream/track identities',()=>{
  const f=fixture();const media=f.bridge.media as unknown as {audioTracks:ArrayLike<{id:string;enabled:boolean}>;textTracks:ArrayLike<{id:string;mode:string}>};
  assert.equal(media.audioTracks[0].id,'1');assert.equal(media.audioTracks[1].id,'3');
  media.audioTracks[1].enabled=true;assert.equal(f.state.audio,3);
  assert.equal(media.audioTracks[0].enabled,false);
  media.textTracks[1].mode='showing';assert.equal(f.state.subtitle,'pgs-tr');
  media.textTracks[1].mode='disabled';assert.equal(f.state.subtitle,'');
  assert.deepEqual(f.events,[['audio',3],['subtitle','pgs-tr'],['subtitle','']]);
});
test('quality menus request server profiles; Auto restores original/automatic compatibility',()=>{
  const f=fixture();const list=(f.bridge.media as unknown as {videoRenditions:{selectedIndex:number;[index:number]:{id:string;selected:boolean}}}).videoRenditions;
  assert.equal(list.selectedIndex,-1);list.selectedIndex=1;
  assert.equal(f.state.quality,'720');assert.equal(list[1].selected,true);
  list.selectedIndex=-1;assert.equal(f.state.quality,'auto');
  list.selectedIndex=99;assert.deepEqual(f.events,[['quality','720'],['quality','original']]);
});
test('normal controls, hotkeys and denied room actions cannot bypass the Home command port',async()=>{
  const f=fixture();const media=f.bridge.media;
  await media.play();media.pause();media.playbackRate=1.5;media.volume=.4;media.muted=true;media.load();
  assert.deepEqual(f.events,[['play'],['pause'],['rate',1.5],['volume',.4,false],['volume',.4,true],['retry']]);
  f.events.length=0;f.canControl=false;
  await media.play();media.pause();media.currentTime=2000;media.playbackRate=2;media.volume=1;
  assert.deepEqual(f.events,[]);assert.equal(f.native.currentTime,2);
  let settled=false;media.addEventListener('seeked',()=>{settled=true;},{once:true});
  media.currentTime=3000;await Promise.resolve();assert.equal(settled,true);
});
test('bridged track events remain subscribable and cannot forward invalid times/rates',()=>{
  const f=fixture();let changed=0;
  const list=(f.bridge.media as unknown as {videoRenditions:EventTarget}).videoRenditions;
  list.addEventListener('change',()=>changed++);f.bridge.refresh();assert.equal(changed,1);
  f.bridge.media.currentTime=NaN;f.bridge.media.playbackRate=Infinity;
  assert.deepEqual(f.events,[]);
});
test('deep links round trip under root and a reverse-proxy base path',()=>{
  const id='11ee851cbd6a40ae8c63f09131e365e8';
  for(const path of ['/title/tmdb/movie/438631',`/watch/${id}`,`/media/${id}`,'/library']){
    const route=parsePageRoute(path);assert.notEqual(route.kind,'not-found');
    if(route.kind!=='not-found')assert.equal(pagePath(route),path);
    assert.deepEqual(parsePageRoute('/home'+path,'/home'),route);
  }
  assert.equal(detailsPath({id,kind:'video',content:{provider:'tmdb',kind:'movie',providerId:438631}}),'/title/tmdb/movie/438631');
  assert.equal(detailsPath({id,kind:'audio'}),`/media/${id}`);
  assert.deepEqual(parsePageRoute('/','', 'library'),{kind:'section',section:'library'});
});
test('invalid, external-scope and traversal paths do not become playback or content commands',()=>{
  for(const path of ['/watch/../home','/watch/not-an-id','/title/other/movie/12','/title/tmdb/movie/0','/title/tmdb/movie/1/extra','//elsewhere/watch/abc'])assert.equal(parsePageRoute(path).kind,'not-found');
  assert.equal(parsePageRoute('/homepage/library','/home').kind,'not-found');
});
test('the installed Video.js store reads Home duration, captions, audio and server quality choices',async()=>{
  // Minimal event targets for the DOM feature store, not a browser or renderer.
  const previousDocument=globalThis.document;const previousElement=globalThis.HTMLElement;const previousMedia=globalThis.HTMLMediaElement;
  Object.assign(globalThis,{document:Object.assign(new EventTarget(),{hidden:false}),HTMLElement:EventTarget,HTMLMediaElement:{HAVE_FUTURE_DATA:3}});
  const f=fixture();const store=createStore()(combine(timeFeature,playbackFeature,qualityFeature,audioTrackFeature,textTrackFeature));
  const detach=store.attach({media:f.bridge.media,container:null});
  try{
    assert.equal(store.state.duration,9326);assert.equal(store.state.currentTime,502);
    assert.equal(store.state.audioTrackList.length,2);assert.equal(store.state.videoRenditionList.length,3);
    assert.equal(store.state.textTrackList.filter(track=>track.kind==='subtitles').length,2);
    assert.equal(store.state.chaptersCues[1].startTime,500);
    store.selectAudioTrack('3');store.selectSubtitlesTrack('pgs-tr');store.selectVideoRendition('720');f.bridge.refresh();
    assert.equal(store.state.audioTrackList.find(track=>track.enabled)?.id,'3');
    assert.equal(store.state.subtitlesShowing,true);assert.equal(store.state.activeVideoRendition?.id,'720');
    await store.seek(8000);assert.ok(f.events.some(([action,value])=>action==='seek'&&value===8000));
  }finally{detach();store.destroy();Object.assign(globalThis,{document:previousDocument,HTMLElement:previousElement,HTMLMediaElement:previousMedia});}
});
