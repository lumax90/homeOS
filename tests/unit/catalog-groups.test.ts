import {test} from 'node:test';
import assert from 'node:assert/strict';
import {catalogGroups,catalogCardKey,catalogTitle} from '../../web/src/catalogGroups.ts';
import {detailsPath}from'../../web/src/pageRoutes.ts';
import type {MediaItem} from '../../web/src/api.ts';

test('manual file-only films stay separate and visible beside a matched downloaded title',()=>{
  const downloaded={id:'original-download',title:'Original downloaded title',content:{id:'matched-original'}} as MediaItem;
  const one={id:'original-manual-one',title:'Original manually imported one',content:null} as MediaItem;
  const two={id:'original-manual-two',title:'Original manually imported two',content:null} as MediaItem;
  assert.deepEqual(catalogGroups([one,two,downloaded]).map(item=>item.id),[one.id,two.id,downloaded.id]);
});
test('known versions group without merging independent unmatched files or different episodes',()=>{
  const first={id:'original-episode-one',content:{id:'show-original',seasonNumber:1,episodeNumber:1}} as MediaItem;
  const version={...first,id:'original-episode-one-version'};
  const next={...first,id:'original-episode-two',content:{...first.content!,episodeNumber:2}};
  assert.deepEqual(catalogGroups([first,version,next]).map(item=>item.id),[first.id,next.id]);
});
test('server series groups retain a stable card key and open their episode page, not one episode',()=>{
  const first={id:'a'.repeat(32),kind:'video',title:'ORIGINAL show · S01E01',catalogGroup:{key:'local:original',kind:'show',title:'ORIGINAL show'}} as MediaItem;
  const newer={...first,id:'b'.repeat(32),title:'ORIGINAL show · S01E20'};
  assert.equal(catalogCardKey(first),catalogCardKey(newer));assert.equal(catalogTitle(first),'ORIGINAL show');
  assert.equal(detailsPath(first),`/series/${first.id}`);assert.equal(catalogGroups([first,newer]).length,1);
});
