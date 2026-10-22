import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { runInNewContext } from 'node:vm';
import { patchGodotSandbox } from '../../tools/games/godot-sandbox.mjs';
import { gamePackMissingDependencies } from '../../tools/games/game-pack.mjs';

async function hooks(context) {
  const source = patchGodotSandbox(await readFile(new URL('../../server/HomeServer.Api/GamePacks/tiny-crate/index.js', import.meta.url), 'utf8'));
  const callback = source.slice(source.indexOf('function _godot_js_pwa_cb('), source.indexOf('function _godot_js_pwa_update('));
  const update = source.slice(source.indexOf('function _godot_js_pwa_update('), source.indexOf('var GodotRTCDataChannel='));
  return runInNewContext(callback + update + '; ({callback:_godot_js_pwa_cb,update:_godot_js_pwa_update})', context);
}
test('Godot native startup PWA callback tolerates a present but throwing sandbox serviceWorker getter', async () => {
  const navigator = {}; let queried = 0;
  Object.defineProperty(navigator, 'serviceWorker', { get() { queried++; throw new DOMException('sandbox access denied', 'SecurityError'); } });
  const engine = await hooks({ navigator, GodotRuntime: { get_func() { throw new Error('Unavailable PWA must not invoke runtime hooks'); } }, GodotPWA: { hasUpdate: true } });
  assert.doesNotThrow(() => engine.callback(1)); assert.equal(engine.update(), 1); assert.equal(queried, 2);
});
test('Godot PWA hooks remain functional in a permitted normal browser context', async () => {
  let callbacks = 0, updates = 0;
  const registration = { waiting: { postMessage(message) { assert.equal(message, 'update'); updates++; } } };
  const engine = await hooks({ navigator: { serviceWorker: { getRegistration: () => Promise.resolve(registration) } }, GodotRuntime: { get_func: () => () => {} }, GodotPWA: { hasUpdate: true, updateState(callback, value) { assert.equal(value, registration); callbacks++; } } });
  engine.callback(1); assert.equal(engine.update(), 0); await new Promise(resolve => setImmediate(resolve)); assert.equal(callbacks, 1); assert.equal(updates, 1);
});
test('sandbox adapter is idempotent and refuses unknown upstream code rather than silently modifying it', async () => {
  const source = await readFile(new URL('../../server/HomeServer.Api/GamePacks/tiny-crate/index.js', import.meta.url), 'utf8'); const patched = patchGodotSandbox(source);
  assert.equal(patchGodotSandbox(patched), patched); assert.throws(() => patchGodotSandbox('unknown engine'), /adapter site changed/);
  assert.doesNotMatch(patched, /allow-same-origin/);
});
test('reviewed Home shell disables persistence/PWA and retains a redacted startup cause', async () => {
  const shell = await readFile(new URL('../../tools/games/home-game.js', import.meta.url), 'utf8');
  assert.match(shell, /persistentPaths: \[\], serviceWorker: ''/); assert.match(shell, /home\.startError =/); assert.match(shell, /\[game resource\]/);
});
test('game packaging preserves font resources and replaces all referenced font binary formats', async () => {
  const builder = await readFile(new URL('../../tools/build-browser-games.ps1', import.meta.url), 'utf8');
  assert.match(builder, /exclude_filter="media\/font\/\*\.ttf,media\/font\/\*\.otf/);
  assert.doesNotMatch(builder, /exclude_filter="media\/font\/\*,/);
  assert.ok(builder.includes('(?:ttf|otf|woff2?)'));
});
test('package dependency validation catches omitted font resources and allows Godot import/remap files', () => {
  const scene = { name: 'res://scene.tscn', bytes: Buffer.from('[ext_resource path="res://media/font/profile/OptionsItem.tres" type="DynamicFont" id=1]\n[ext_resource path="res://image.png" type="Texture" id=2]') };
  const image = { name: 'res://image.png.import', bytes: Buffer.alloc(0) };
  assert.deepEqual(gamePackMissingDependencies([scene, image]), [{ source: scene.name, dependency: 'res://media/font/profile/OptionsItem.tres' }]);
  assert.deepEqual(gamePackMissingDependencies([scene, image, { name: 'res://media/font/profile/OptionsItem.tres', bytes: Buffer.alloc(0) }]), []);
});
test('update guards enumerate actual game sessions instead of treating an empty REST array as one object', async () => {
  for (const file of ['prepare-preview-update.ps1', 'update-live.ps1']) {
    const source = await readFile(new URL('../../tools/' + file, import.meta.url), 'utf8');
    assert.doesNotMatch(source, /@\(Invoke-RestMethod[^\n]+games\/sessions\/active/);
    assert.match(source, /\$gameSessions = Invoke-RestMethod[^\n]+games\/sessions\/active/);
    assert.match(source, /@\(\$gameSessions \| Where-Object \{ \$_\.id \}\)\.Count/);
  }
});
