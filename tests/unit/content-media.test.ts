import {test}from'node:test';
import assert from'node:assert/strict';
import {youtubeTrailerUrl,creditsInRow}from'../../web/src/contentMedia.ts';

test('only video keys produce click-loaded privacy-enhanced non-autoplay embeds',()=>{
  const url=new URL(youtubeTrailerUrl('Original001','en')!);
  assert.equal(url.origin,'https://www.youtube-nocookie.com');assert.equal(url.searchParams.get('autoplay'),'0');
  assert.equal(url.searchParams.get('controls'),'1');assert.equal(url.searchParams.get('playsinline'),'1');
  assert.equal(youtubeTrailerUrl('../anything','en'),null);assert.equal(youtubeTrailerUrl('https://example.invalid','tr'),null);
});
test('the collapsed cast uses a single responsive row, with a bounded large-screen count',()=>{
  assert.equal(creditsInRow(300),2);assert.equal(creditsInRow(720),4);assert.equal(creditsInRow(1250),8);
});
