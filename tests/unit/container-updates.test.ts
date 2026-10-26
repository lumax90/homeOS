import {test}from'node:test';
import assert from'node:assert/strict';
import {approvalFor,updatePhase}from'../../web/src/containerUpdates.ts';
test('approval names the exact selected image and never the moving stable channel',()=>{
  const digest='sha256:'+'a'.repeat(64);
  assert.deepEqual(approvalFor({version:'0.99.0',revision:'b'.repeat(40),digest,totalBytes:100,estimatedDownloadBytes:40}),{digest});
});
test('untrusted image references cannot become update approval payloads',()=>{
  assert.throws(()=>approvalFor({version:'0.99.0',revision:'b'.repeat(40),digest:'other/image:stable',totalBytes:100,estimatedDownloadBytes:null}));
});
test('idle updates ask for consent, and busy downloads do not claim installation',()=>{
  assert.match(updatePhase('available'),/onayı/);assert.match(updatePhase('waiting'),/kullanılmadığında/);assert.match(updatePhase('downloading'),/çalışmaya devam/);
});
