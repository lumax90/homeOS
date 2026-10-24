import {test} from 'node:test';
import assert from 'node:assert/strict';
import {RadioSceneIntent} from '../../web/src/music/radioSceneIntent.ts';
import {stockVideos} from '../../web/src/music/sceneSources.ts';

test('pausing cancels late scene work while retaining the selected scene for resume',()=>{
  const intent=new RadioSceneIntent();const ticket=intent.select('fireplace-original');intent.pause();
  assert.equal(intent.current(ticket),false);assert.equal(intent.scene,'fireplace-original');
  const resumed=intent.play();assert.equal(intent.current(resumed),true);assert.equal(intent.current(ticket),false);
});
test('switching scenes or leaving visual mode prevents stale preparation from taking over playback',()=>{
  const intent=new RadioSceneIntent();const old=intent.select('fireplace-original');const next=intent.select('rain-night');
  assert.equal(intent.current(old),false);assert.equal(intent.current(next),true);intent.select(null);assert.equal(intent.current(next),false);
});
test('an incidental play request does not invalidate an in-progress selected scene',()=>{
  const intent=new RadioSceneIntent();const scene=intent.select('fireplace-original');assert.equal(intent.play(),scene);assert.equal(intent.current(scene),true);
});
test('the shared scene catalog retains its curated URLs and individual attribution',()=>{
  assert.equal(stockVideos.length,10);assert.equal(new Set(stockVideos.map(item=>item.id)).size,10);
  for(const item of stockVideos){assert.equal(new URL(item.url).hostname,'upload.wikimedia.org');assert.ok(item.license&&item.author&&item.source);}
  assert.equal(stockVideos.find(item=>item.id==='rain-window-07')!.source,'https://commons.wikimedia.org/wiki/?curid=33649027');
});
