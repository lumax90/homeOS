import {test} from 'node:test';
import assert from 'node:assert/strict';
import Hls from 'hls.js';
import {movieHlsConfig} from '../../web/src/movieHlsConfig.ts';

test('the installed hls.js reproduces the missing explicit live-sync configuration failure',()=>{
  assert.throws(()=>new Hls({lowLatencyMode:false,liveMaxLatencyDurationCount:Infinity}),/Illegal hls\.js config.*liveMaxLatencyDurationCount/);
});
test('the actual movie configuration passes the installed library constructor and retains the file buffer policy',()=>{
  const player=new Hls(movieHlsConfig(42,'/home/assets/original-hls.worker.js'));
  try{
    assert.equal(player.config.startPosition,42);assert.equal(player.config.workerPath,'/home/assets/original-hls.worker.js');
    assert.equal(player.config.liveSyncDurationCount,3);assert.equal(player.config.liveMaxLatencyDurationCount,Infinity);
    assert.equal(player.config.liveMaxUnchangedPlaylistRefresh,Infinity);assert.equal(player.config.lowLatencyMode,false);
    assert.equal(player.config.maxBufferLength,30);assert.equal(player.config.maxMaxBufferLength,60);assert.equal(player.config.maxBufferSize,32*1024*1024);
    assert.equal(player.config.maxLiveSyncPlaybackRate,1);
  }finally{player.destroy();}
});
test('a keyframe offset before zero is clamped without changing movie latency units',()=>{
  const config=movieHlsConfig(-2,'/assets/original-hls.worker.js');assert.equal(config.startPosition,0);
  assert.equal(config.liveSyncDuration,undefined);assert.equal(config.liveMaxLatencyDuration,undefined);
});
