import {test} from 'node:test';
import assert from 'node:assert/strict';
import {LiveAttemptOwner,LiveRecoveryPolicy,liveTarget,terminalLiveStatus} from '../../web/src/live/liveRecovery.ts';

test('live startup and return-to-live leave a useful buffer without seeking outside the available window',()=>{
  assert.equal(liveTarget(100,160),148);assert.equal(liveTarget(100,106),100);assert.equal(liveTarget(100,160,24),136);
  assert.equal(liveTarget(0,Infinity),null);assert.equal(liveTarget(10,9),null);
});
test('repeated source failures exhaust a bounded backoff even if each attempt briefly plays',()=>{
  const policy=new LiveRecoveryPolicy();
  for(const [index,delay]of [500,1500,3000,6000].entries()){policy.playing(index*5000);assert.equal(policy.next(index*5000+1000),delay);}
  policy.playing(30000);assert.equal(policy.next(31000),null);
});
test('only sustained healthy playback resets retries and buffering has a finite ceiling',()=>{
  const policy=new LiveRecoveryPolicy();assert.equal(policy.next(0),500);policy.playing(1000);policy.stalled();policy.playing(5000);
  assert.equal(policy.next(61000),1500);policy.playing(62000);assert.equal(policy.next(123000),500);
  for(let index=0;index<20;index++)policy.stalled();assert.equal(policy.bufferSeconds,24);
});
test('authentication/source revocation is terminal while timeouts and backpressure can recover',()=>{
  for(const status of [400,401,403,404,409,410])assert.equal(terminalLiveStatus(status),true);
  for(const status of [undefined,408,429,500,502,503,504])assert.equal(terminalLiveStatus(status),false);
});
test('late preparation after a newer connection releases only the late lease',()=>{
  const released:string[]=[];const owner=new LiveAttemptOwner(id=>released.push(id));const first=owner.begin(),second=owner.begin();
  assert.equal(owner.accept(second,'new'),true);assert.equal(owner.accept(first,'old'),false);assert.equal(owner.id,'new');assert.deepEqual(released,['old']);
  owner.stop();owner.stop();assert.deepEqual(released,['old','new']);
});
test('closing during preparation cannot resurrect the stream when its response arrives',()=>{
  const released:string[]=[];const owner=new LiveAttemptOwner(id=>released.push(id));const attempt=owner.begin();owner.stop();
  assert.equal(owner.accept(attempt,'late'),false);assert.equal(owner.id,null);assert.deepEqual(released,['late']);
});
test('a failed old attempt cannot release the active replacement',()=>{
  const released:string[]=[];const owner=new LiveAttemptOwner(id=>released.push(id));const first=owner.begin();owner.accept(first,'one');
  const second=owner.begin();owner.accept(second,'two');owner.discard(first);assert.equal(owner.id,'two');assert.deepEqual(released,['one']);
});
test('discarding a failed attempt invalidates its pending player callbacks immediately',()=>{
  const released:string[]=[];const owner=new LiveAttemptOwner(id=>released.push(id));const attempt=owner.begin();owner.accept(attempt,'failed');
  owner.discard(attempt);assert.equal(owner.current(attempt),false);assert.equal(owner.id,null);assert.deepEqual(released,['failed']);
});
