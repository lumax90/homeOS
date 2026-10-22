import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { filterGames, hasGameCheckpoint, type Game } from '../../web/src/gaming/model.ts';

const rom: Game = { id: 'rom-' + 'a'.repeat(48), title: 'Isolated ROM fixture', publisher: '', sourceUrl: '', sourceRepository: '', sourceCommit: '', version: 'gba-' + 'b'.repeat(64), engine: 'RetroArch / mGBA', license: 'user-supplied', kind: 'native', aspect: '16/9', genres: [], sizeBytes: 512, installed: true, inStore: false, runtime: 'native', playable: false, system: 'gba', romSha256: 'b'.repeat(64), state: { gameId: 'rom-' + 'a'.repeat(48), inLibrary: true, favorite: false, lastPlayedAt: null, playedSeconds: 0, saveVersion: null, saveRevision: 0, hasSave: false } };

test('real supplied ROM metadata is distinct from installed browser games and cannot imply resumable native gameplay', () => {
  assert.equal(rom.kind, 'native'); assert.equal(rom.inStore, false); assert.deepEqual(filterGames([rom], 'ROM', 'library'), [rom]); assert.deepEqual(filterGames([rom], '', 'favorites'), []);
  assert.equal(hasGameCheckpoint(rom), false); assert.equal(hasGameCheckpoint({ ...rom, state: { ...rom.state, hasSave: true, saveVersion: rom.version } }), false);
});
test('native entry has an honest placeholder icon rather than invented artwork, publisher or gameplay screenshot', async () => {
  const ui = await readFile(new URL('../../web/src/gaming/Games.tsx', import.meta.url), 'utf8');
  assert.match(ui, /game\.kind === 'native' \? <div className="native-rom-art"/); assert.match(ui, /game\.playable !== false && <span className="game-card-play"/); assert.match(ui, /Yayın kurulumu bekleniyor/);
  assert.match(ui, /useState<GameFilter>\('library'\)/);
});
test('direct native play route is gated before any web session is launched', async () => {
  const source = await readFile(new URL('../../web/src/gaming/GamePlayer.tsx', import.meta.url), 'utf8');
  const route = source.slice(source.indexOf('export function GamePlayer('));
  assert.ok(route.indexOf("game.data.kind === 'native'") < route.indexOf('return <WebGamePlayer'));
  assert.doesNotMatch(route, /mutate|iframe|sessions/);
  const endpoint = await readFile(new URL('../../server/HomeServer.Api/Gaming/GameCatalog.cs', import.meta.url), 'utf8');
  assert.match(endpoint, /Playable: actor\.IsAdmin && profile\?\.Enabled == true && profile\.InputReady/); assert.match(endpoint, /StartsWith\("rom-"/);
  assert.match(route, /game\.data\.playable \? <NativeGamePlayer/);
});
test('held-input gate cannot be inferred from staged assets or bypassed by the configuration tool', async () => {
  const profile = await readFile(new URL('../../server/HomeServer.Api/Gaming/NativeRuntimeProfile.cs', import.meta.url), 'utf8');
  const configure = await readFile(new URL('../../tools/configure-native-runtime.mjs', import.meta.url), 'utf8');
  assert.match(profile, /bool ContinuousInputVerified = false/);
  assert.match(profile, /ContinuousInputVerified && !RejectedRemotePadSha256\.Equals/);
  assert.match(profile, /if \(!InputReady\) throw new DomainException\(InputBlockReason, 409\)/);
  assert.doesNotMatch(configure, /continuousInputVerified\s*:\s*true/);
});
test('ROM selection never uploads until an explicit authorized submit and retains user/session CSRF request handling', async () => {
  const source = await readFile(new URL('../../web/src/gaming/NativeGames.tsx', import.meta.url), 'utf8');
  assert.match(source, /if \(!file \|\| !authorized\)/); assert.match(source, /body: file, signal: controller.current.signal/); assert.match(source, /onSubmit=\{submit\}/); assert.match(source, /controller.current\?\.abort\(\)/);
  assert.doesNotMatch(source, /https:\/\/.*\.(gba|zip)|window\.open|location\.href\s*=/);
});
test('native prototype reuses reviewed upstream candidates and never uses new encoder or global desktop launch code', async () => {
  const runtime = await readFile(new URL('../../server/HomeServer.Api/Gaming/NativeGamingRuntime.cs', import.meta.url), 'utf8');
  assert.match(runtime, /2026\.914\.233613/); assert.match(runtime, /cd9d03cbf9a42b394f7b72a733a2f39cb5f0edd8/); assert.match(runtime, /false, false, components/);
  assert.match(runtime, /capture-isolation-not-configured/); assert.doesNotMatch(runtime, /Process\.Start|SendInput|VideoEncoder|framebuffer/);
  const importer = await readFile(new URL('../../server/HomeServer.Api/Gaming/NativeRomLibrary.cs', import.meta.url), 'utf8');
  assert.doesNotMatch(importer, /HttpClient|Process\.Start|FileMode\.Create,/); assert.match(importer, /SemaphoreSlim _upload = new\(1, 1\)/); assert.match(importer, /MaximumBytes/); assert.match(importer, /VerifiedFilePublication.Promote/);
});
