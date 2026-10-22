import {test} from 'node:test';
import assert from 'node:assert/strict';
import {defaultMusicExperience,radioGroups,type RadioFavorite} from '../../web/src/music/experience.ts';
import {selectedVideo,stockVideos} from '../../web/src/music/sceneSources.ts';

const favorites:RadioFavorite[]=[{station:{id:'one',name:'Evening Jazz',country:'TR',tags:['jazz','ambient'],language:'turkish',url:'https://fixture.invalid/one',codec:'MP3',bitrate:128,hls:false,homepage:null},category:'Living room'},{station:{id:'two',name:'Night Jazz',country:'GB',tags:['jazz'],language:'english',url:'https://fixture.invalid/two',codec:'AAC',bitrate:64,hls:false,homepage:null},category:'Desk'},{station:{id:'three',name:'Quiet Blue',country:'TR',tags:['ambient'],language:'turkish',url:'https://fixture.invalid/three',codec:'MP3',bitrate:128,hls:false,homepage:null},category:'Living room'}];
test('custom radio categories retain their names and combine with actual genre, country and search filters',()=>{
  assert.deepEqual(radioGroups(favorites,'').map(g=>[g.name,g.items.length]),[['Living room',2],['Desk',1]]);
  assert.deepEqual(radioGroups(favorites,'jazz','','TR').flatMap(g=>g.items.map(row=>row.station.id)),['one']);
  assert.deepEqual(radioGroups(favorites,'','quiet','TR').map(g=>g.name),['Living room']);assert.deepEqual(radioGroups(favorites,'','missing'),[]);assert.equal(favorites.length,3);
});
test('online metadata covers and visual stock are opt-in independently of browsing radio metadata',()=>{
  assert.equal(defaultMusicExperience.onlineMetadata,false);assert.equal(defaultMusicExperience.onlineCovers,false);assert.equal(defaultMusicExperience.onlineScenes,false);assert.equal(defaultMusicExperience.radioDirectory,true);
});
test('legacy scene selection keeps its original clip and stale clip ids cannot select another scene or arbitrary URL',()=>{
  assert.equal(selectedVideo('fireplace')?.id,'fireplace-original');assert.equal(selectedVideo('rain','rain-night')?.id,'rain-night');assert.equal(selectedVideo('rain','aquarium-boka')?.id,'rain-window-10');assert.equal(selectedVideo('ocean','https://arbitrary.invalid/movie')?.id,'ocean-dive-06');assert.equal(selectedVideo('vinyl'),undefined);assert.equal(new Set(stockVideos.map(video=>video.id)).size,stockVideos.length);
});
