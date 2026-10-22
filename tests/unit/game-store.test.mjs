import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import { runInNewContext } from 'node:vm';

test('five real reviewed web titles have pinned upstream sources, licenses, scripts and actual artwork', async () => {
  const root = new URL('../../server/HomeServer.Api/Gaming/Store/', import.meta.url); const catalog = JSON.parse(await readFile(new URL('catalog.json', root), 'utf8'));
  assert.equal(catalog.format, 1); assert.deepEqual(catalog.games.map(game => game.id), ['2048', 'a-dark-room', 'hexgl', 'hextris', 'oh-hi']);
  for (const game of catalog.games) {
    assert.match(game.commit, /^[a-f0-9]{40}$/); assert.equal(game.archiveUrl, `https://codeload.github.com/${game.repository}/zip/${game.commit}`);
    assert.ok(game.sourceFiles.some(file => file.path === 'index.html')); assert.ok(game.sourceFiles.some(file => file.path === game.licensePath));
    for (const script of game.scripts) assert.ok(game.sourceFiles.some(file => file.path === script), game.id + ': ' + script);
    for (const file of game.sourceFiles) { assert.match(file.gitBlobSha1, /^[a-f0-9]{40}$/); assert.ok(file.size >= 0); assert.doesNotMatch(file.path, /\.(?:exe|dll|bat|cmd|ttf|otf|woff2?)$/i); }
    const image = await readFile(new URL(`artwork/${game.id}.png`, root)); assert.deepEqual([...image.subarray(0, 4)], [137, 80, 78, 71]); assert.equal(createHash('sha256').update(image).digest('hex'), game.artworkSha256);
    assert.ok((await readFile(new URL(`artwork/${game.id}-LICENSE.txt`, root), 'utf8')).length > 500);
  }
});
test('store actions do not imply installation and browsing never executes a download', async () => {
  const ui = await readFile(new URL('../../web/src/gaming/Games.tsx', import.meta.url), 'utf8');
  assert.match(ui, /game\.installed === false/); assert.match(ui, /games\.filter\(game => game\.inStore\)/); assert.match(ui, /\/install`\)/); assert.match(ui, /onPress=\{\(\) => install\.mutate\(\)\}/);
  const service = await readFile(new URL('../../server/HomeServer.Api/Gaming/GameInstaller.cs', import.meta.url), 'utf8');
  assert.match(service, /QueueProductJob\("game-install"/); assert.match(service, /Directory\.Move\(build, target\)/); assert.match(service, /GitBlobSha1/); assert.doesNotMatch(service, /FileMode\.Create,/);
  const source = await readFile(new URL('../../server/HomeServer.Api/Gaming/GamePackageSource.cs', import.meta.url), 'utf8');
  assert.match(source, /AllowAutoRedirect = false/); assert.match(source, /UseCookies = false/); assert.match(source, /codeload\.github\.com/); assert.match(source, /RemoteMediaHttp\.Allowed\(address, false\)/);
});
test('opaque web-game compatibility storage uses only supplied profile data and serializes it through the nonce-bound bridge', async () => {
  const source = await readFile(new URL('../../server/HomeServer.Api/Gaming/Runtime/home-web.js', import.meta.url), 'utf8');
  const listeners = new Map(), messages = [], pending = new Map(); let next = 0;
  const parent = { postMessage(message) { messages.push(message); } };
  const document = { documentElement: { classList: { toggle() {} } }, body: { appendChild() {} }, querySelectorAll: () => [], addEventListener(type, callback) { listeners.set('document:' + type, callback); } };
  const context = { document, parent, location: { hash: '#isolated-nonce' }, performance: { now: () => 100 }, Date: class extends Date {}, TextEncoder, Object, Proxy, Map, KeyboardEvent: class {}, MouseEvent: class {}, console,
    fetch: async path => { assert.equal(path, 'home-config.json'); return { ok: true, json: async () => ({ id: 'fixture', adapter: '2048', input: 'digital', scripts: [] }) }; },
    requestAnimationFrame: callback => { pending.set(++next, callback); return next; }, cancelAnimationFrame: id => pending.delete(id), setTimeout: callback => { pending.set(++next, callback); return next; }, clearTimeout: id => pending.delete(id),
    addEventListener(type, callback) { listeners.set(type, callback); }, navigator: {} };
  context.window = context; runInNewContext(source, context);
  assert.equal(context.localStorage.getItem('unknown'), null); assert.equal(context.localStorage.unknown, undefined);
  const message = { protocol: 'home-game-v1', nonce: 'isolated-nonce', type: 'init', files: { 'web-profile.json': JSON.stringify({ storage: { score: '42' }, cookies: {} }) } };
  listeners.get('message')({ source: {}, origin: 'https://home.invalid', data: message }); assert.equal(context.homeGame.phase, 'waiting');
  listeners.get('message')({ source: parent, origin: 'https://home.invalid', data: message }); await new Promise(resolve => setImmediate(resolve)); assert.equal(context.localStorage.score, '42');
  context.localStorage.score = '84'; context.document.cookie = 'game=private';
  listeners.get('message')({ source: parent, origin: 'https://home.invalid', data: { ...message, type: 'flush' } });
  const saved = messages.find(message => message.type === 'save'); assert.equal(saved.nonce, 'isolated-nonce'); const profile = JSON.parse(saved.files['web-profile.json']); assert.equal(profile.storage.score, '84'); assert.equal(profile.cookies.game, 'private');
  assert.ok(messages.some(message => message.type === 'flushed')); assert.doesNotMatch(source, /allow-same-origin/);
});
test('the curated source links and description do not claim active player counts or a publisher integration', async () => {
  const ui = await readFile(new URL('../../web/src/gaming/Games.tsx', import.meta.url), 'utf8');
  assert.match(ui, /Beş tarayıcı klasiği/); assert.doesNotMatch(ui, /players online|aktif oyuncu|downloads today/i);
});
