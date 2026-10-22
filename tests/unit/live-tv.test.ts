import {test} from 'node:test';
import assert from 'node:assert/strict';
import {liveWindow,programmeProgress} from '../../web/src/live/types.ts';

test('guide progress uses actual programme times and clamps future and ended programmes',()=>{
  const row={startsAt:'2026-10-22T12:00:00Z',endsAt:'2026-10-22T13:00:00Z'};assert.equal(programmeProgress(row,Date.parse('2026-10-22T12:30:00Z')),.5);assert.equal(programmeProgress(row,Date.parse('2026-10-22T11:30:00Z')),0);assert.equal(programmeProgress(row,Date.parse('2026-10-22T14:00:00Z')),1);assert.equal(programmeProgress({...row,endsAt:row.startsAt}),null);assert.equal(programmeProgress(null),null);
});
test('live sliding windows never expose discarded history or invalid seek positions',()=>{
  assert.deepEqual(liveWindow(600,1800,300),{start:600,end:1800,position:600,behind:1200,ready:true});assert.equal(liveWindow(600,1800,2000).position,1800);assert.equal(liveWindow(600,1800,NaN).behind,1200);assert.equal(liveWindow(0,Infinity,1).ready,false);assert.equal(liveWindow(100,90,95).ready,false);
});
