import {test} from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import {selectPlaybackTransport} from '../../web/src/playbackTransport.ts';

test('Chromium native HLS hints do not bypass the supported MSE transport',()=>{
  for(const mode of ['remux','audio-transcode','video-transcode','live-hls']){
    assert.equal(selectPlaybackTransport(mode,true,'maybe'),'hls.js');
    assert.equal(selectPlaybackTransport(mode,true,'probably'),'hls.js');
    assert.equal(selectPlaybackTransport(mode,true,''),'hls.js');
  }
});
test('original files stay direct and native-only devices keep their HLS fallback',()=>{
  assert.equal(selectPlaybackTransport('direct',true,'maybe'),'direct');
  assert.equal(selectPlaybackTransport('direct',false,''),'direct');
  assert.equal(selectPlaybackTransport('video-transcode',false,'maybe'),'native-hls');
  assert.equal(selectPlaybackTransport('live-hls',false,'probably'),'native-hls');
  assert.equal(selectPlaybackTransport('video-transcode',false,''),'unsupported');
});
test('video and live controllers use the tested routing and retain the recovery owner',async()=>{
  const video=await readFile(new URL('../../web/src/components/usePlayback.ts',import.meta.url),'utf8');
   const live=await readFile(new URL('../../web/src/live/useLiveStream.ts',import.meta.url),'utf8');
  for(const code of [video,live]){
    assert.match(code,/selectPlaybackTransport\([^;]+isSupported\(\)[^;]+canPlayType\('application\/vnd\.apple\.mpegurl'\)\)/);
     assert.doesNotMatch(code,/if\s*\([^;{\n]*canPlayType\('application\/vnd\.apple\.mpegurl'\)/);
  }
  const nativeHandler=video.slice(video.indexOf('function mediaError()'),video.indexOf('const shared:'));
  assert.ok(nativeHandler.indexOf('if (hlsRef.current) return;')<nativeHandler.indexOf('setError('));
  assert.match(video,/!data\.fatal \|\| !alive\.current \|\| epoch !== generation\.current/);
  assert.match(video,/!recovered\.current/);
  assert.match(video,/hls\.recoverMediaError\(\)/);
});
