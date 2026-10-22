import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { filterGames, gamepadsNeutral, hasGameCheckpoint, readGameMessage, type Game } from '../../web/src/gaming/model.ts';
import { GameSaveQueue, type GameSavePulse } from '../../web/src/gaming/saveQueue.ts';
import { pagePath, parsePageRoute } from '../../web/src/pageRoutes.ts';
const game = (id = 'tiny-crate'): Game => ({ id, title: 'Isolated ' + id, publisher: 'Fixture', sourceUrl: '', sourceRepository: '', sourceCommit: '', version: 'one', engine: '', license: '', kind: 'web', aspect: '16/9', genres: ['puzzle'], sizeBytes: 4, state: { gameId: id, inLibrary: false, favorite: false, lastPlayedAt: null, playedSeconds: 0, saveVersion: null, saveRevision: 0, hasSave: false } });
test('one catalog filters real entries without inventing library favorites or incompatible resume states', () => {
  const first = game(); const second = game('wrap-it-up'); second.state.inLibrary = true; second.state.favorite = true;
  assert.deepEqual(filterGames([first, second], '', 'library'), [second]); assert.deepEqual(filterGames([first, second], 'WRAP', 'favorites'), [second]); assert.equal(filterGames([first], 'unknown', 'all').length, 0);
  second.state.hasSave = true; second.state.saveVersion = 'old'; assert.equal(hasGameCheckpoint(second), false); second.state.saveVersion = 'one'; assert.equal(hasGameCheckpoint(second), true);
});
test('addressable game detail and player routes retain the configured base path boundary', () => {
  for (const kind of ['game', 'game-play'] as const) { const route = { kind, id: 'tiny-crate' }; assert.deepEqual(parsePageRoute('/home-server' + pagePath(route), '/home-server'), route); }
  for (const path of ['/games/../home', '/games/a/anything', '/games/%2e%2e', '/games/UPPER/play']) assert.equal(parsePageRoute(path).kind, 'not-found');
  assert.deepEqual(parsePageRoute('/games'), { kind: 'section', section: 'games' }); assert.equal(parsePageRoute('/games', '/different').kind, 'not-found');
});
test('frame message handshake rejects foreign nonce, oversized and path-bearing saves', () => {
  const message = { protocol: 'home-game-v1', nonce: 'current', type: 'save', files: { 'home-progress.json': '{"map":1}' } };
  assert.ok(readGameMessage(message, 'current')); assert.equal(readGameMessage(message, 'other'), null);
  assert.equal(readGameMessage({ ...message, files: { '../home.db': '{}' } }, 'current'), null);
  assert.equal(readGameMessage({ ...message, files: { 'home-progress.json': 'x'.repeat(33000) } }, 'current'), null);
  assert.equal(readGameMessage({ ...message, type: 'loading', progress: NaN }, 'current'), null);
  assert.equal(readGameMessage({ ...message, type: 'execute' }, 'current'), null);
});
test('resume requires released buttons and centered sticks but disconnected pads are not blockers', () => {
  const pad = { connected: true, buttons: [{ pressed: false, value: 0 }], axes: [0, 0] } as Gamepad;
  assert.equal(gamepadsNeutral([null, pad]), true); pad.buttons[0].pressed = true; assert.equal(gamepadsNeutral([pad]), false);
  pad.buttons[0].pressed = false; pad.axes[0] = .6; assert.equal(gamepadsNeutral([pad]), false); pad.connected = false; assert.equal(gamepadsNeutral([pad]), true);
});
test('checkpoint writer serializes revisions and coalesces the latest frame while a write is pending', async () => {
  const calls: GameSavePulse[] = []; let release: (() => void) | undefined;
  const queue = new GameSaveQueue({ revision: 0, files: {}, version: 'one' }, async pulse => { calls.push(pulse); if (calls.length === 1) await new Promise<void>(resolve => { release = resolve; }); return { revision: calls.length, files: pulse.files ?? {}, version: 'one' }; });
  queue.stage({ 'home-progress.json': '{"map":1}' }); const pending = queue.flush(true); queue.stage({ 'home-progress.json': '{"map":2}' }); queue.stage({ 'home-progress.json': '{"map":3}' }); assert.equal(queue.flush(false), pending); release!(); await pending;
  assert.equal(calls.length, 2); assert.equal(calls[0].saveRevision, 0); assert.equal(calls[1].saveRevision, 1); assert.equal(calls[1].running, false); assert.equal(calls[1].files!['home-progress.json'], '{"map":3}'); assert.equal(queue.dirty, false);
});
test('failed checkpoint remains retryable without advancing the expected server revision', async () => {
  let fail = true; const calls: GameSavePulse[] = [];
  const queue = new GameSaveQueue({ revision: 4, files: {}, version: 'one' }, async pulse => { calls.push(pulse); if (fail) throw new Error('isolated connection failure'); return { revision: 5, files: pulse.files!, version: 'one' }; });
  queue.stage({ 'home-progress.json': '{}' }); await assert.rejects(queue.flush(false)); assert.equal(queue.dirty, true); fail = false; await queue.flush(false); assert.equal(calls[1].saveRevision, 4); assert.equal(queue.dirty, false); assert.equal(queue.hasSavedData, true);
});
test('page exit can dispatch the latest snapshot without waiting for an in-flight response', async () => {
  let release: (() => void) | undefined; let count = 0;
  const queue = new GameSaveQueue({ revision: 4, files: {}, version: 'one' }, async pulse => { if (++count === 1) await new Promise<void>(resolve => { release = resolve; }); return { revision: 5, files: pulse.files!, version: 'one' }; });
  queue.stage({ 'home-progress.json': '{"map":1}' }); const pending = queue.flush(true); queue.stage({ 'home-progress.json': '{"map":2}' });
  assert.deepEqual(queue.unloadPulse(), { running: false, saveRevision: 4, files: { 'home-progress.json': '{"map":2}' } });
  queue.stage({ 'home-progress.json': '{"map":1}' }); release!(); await pending;
});
test('a reverted frame received during flight is not lost just because it matched the previous save', async () => {
  let release: (() => void) | undefined; const initial = { 'home-progress.json': '{"map":0}' }; const calls: GameSavePulse[] = [];
  const queue = new GameSaveQueue({ revision: 0, files: initial, version: 'one' }, async pulse => { calls.push(pulse); if (calls.length === 1) await new Promise<void>(resolve => { release = resolve; }); return { revision: calls.length, files: pulse.files!, version: 'one' }; });
  queue.stage({ 'home-progress.json': '{"map":1}' }); const pending = queue.flush(true); queue.stage(initial); release!(); await pending; assert.deepEqual(calls[1].files, initial);
});
test('player uses only a sandboxed reviewed runtime, explicit input ownership and validated exit/save', async () => {
  const player = await readFile(new URL('../../web/src/gaming/GamePlayer.tsx', import.meta.url), 'utf8'); const bridge = await readFile(new URL('../../tools/games/home-game.js', import.meta.url), 'utf8');
  assert.match(player, /sandbox="allow-scripts"/); assert.doesNotMatch(player, /allow-same-origin/); assert.match(player, /event\.source !== frame\.current\?\.contentWindow/); assert.match(player, /event\.origin !== 'null'/);
  assert.match(player, /controller\.acquireGameplay\(\)/); assert.match(player, /gamepadsNeutral/); assert.match(player, /flushFrame\(\)/); assert.match(player, /queueMicrotask/);
  assert.match(player, /window\.addEventListener\('pagehide', end\)/); assert.match(player, /unloadPulse\(\)/); assert.match(player, /sessions\/\$\{started\.id\}\/end/);
  assert.match(bridge, /persistentPaths: \[\]/); assert.match(bridge, /event\.source !== parent/); assert.doesNotMatch(bridge, /localStorage|document\.cookie|fetch\(['"]\/api/);
});
