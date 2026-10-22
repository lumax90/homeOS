import { test } from 'node:test';
import assert from 'node:assert/strict';
import { lyricIndex, lyricWordActive } from '../../web/src/audio/lyricTiming.ts';
import type { LyricLine } from '../../web/src/api.ts';

const lines: LyricLine[] = [
  { seconds: null, endSeconds: null, text: 'Plain', words: null },
  { seconds: 1, endSeconds: 5, text: 'One', words: [{ seconds: 1, endSeconds: 2, text: 'One' }] },
  { seconds: 1, endSeconds: 5, text: 'Duet', words: null },
  { seconds: 5, endSeconds: null, text: 'Last', words: null },
];
test('lyric seeking resets the active line in either direction and retains simultaneous lines', () => {
  assert.equal(lyricIndex(lines, 0), -1);
  assert.equal(lyricIndex(lines, 6), 3);
  assert.equal(lyricIndex(lines, 2), 1);
  assert.equal(lyricIndex(lines, Number.NaN), -1);
  assert.equal(lyricIndex(lines, Number.POSITIVE_INFINITY), -1);
  assert.equal(lyricWordActive(1, 5, 2), true);
});
test('ELRC word end markers leave vocal gaps unhighlighted', () => {
  assert.equal(lyricWordActive(1, 2, .9), false);
  assert.equal(lyricWordActive(1, 2, 1), true);
  assert.equal(lyricWordActive(1, 2, 2), false);
  assert.equal(lyricWordActive(5, null, 6), true);
  assert.equal(lyricWordActive(5, null, Number.NaN), false);
});
