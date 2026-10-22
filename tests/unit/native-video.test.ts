import {test} from 'node:test';
import assert from 'node:assert/strict';
import {lowDelayReceiver,videoBufferSample} from '../../web/src/gaming/nativeVideo.ts';

test('minimum browser jitter target is feature-gated and does not pretend an unsupported or denied control works',()=>{
  const receiver={jitterBufferTarget:200};assert.equal(lowDelayReceiver(receiver as unknown as RTCRtpReceiver),true);assert.equal(receiver.jitterBufferTarget,0);
  assert.equal(lowDelayReceiver({} as RTCRtpReceiver),false);
  const denied=Object.defineProperty({},'jitterBufferTarget',{set(){throw new RangeError('unsupported');}});assert.equal(lowDelayReceiver(denied as RTCRtpReceiver),false);
});
test('displayed buffer/decode statistics are real counter deltas, not fabricated total input latency',()=>{
  const sample=videoBufferSample({delay:1.2,emitted:120,decode:.12,frames:120},{delay:1,emitted:100,decode:.1,frames:100});assert.ok(Math.abs(sample.bufferMs!-10)<.00001);assert.ok(Math.abs(sample.decodeMs!-1)<.00001);
  assert.deepEqual(videoBufferSample({delay:0,emitted:0,decode:0,frames:0}),{bufferMs:null,decodeMs:null});
});
