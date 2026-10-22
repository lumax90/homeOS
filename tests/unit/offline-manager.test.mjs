import { test } from 'node:test';
import assert from 'node:assert/strict';
import { build } from 'vite';
import { fileURLToPath } from 'node:url';
import { createHash } from 'node:crypto';
import { IDBFactory } from 'fake-indexeddb';
import { OfflineStore } from '../../web/src/offline/store.ts';

// Bundle the production controller for Node; no browser, DOM renderer or internet endpoint is used.
const output = await build({ configFile: false, publicDir: false, envDir: false, logLevel: 'silent', build: { write: false, minify: false, target: 'es2022', lib: { entry: fileURLToPath(new URL('../../web/src/offline/manager.ts', import.meta.url)), formats: ['es'] } } });
const code = (Array.isArray(output) ? output[0] : output).output.find(item => item.type === 'chunk').code;
Object.assign(globalThis, { document: { querySelector: () => null }, location: { href: 'https://home.test/', origin: 'https://home.test' } });
const { OfflineManager } = await import('data:text/javascript;base64,' + Buffer.from(code).toString('base64'));
const CHUNK = 2 * 1024 * 1024;
function setup() {
  const window = new EventTarget(); window.setInterval = () => 1; Object.assign(globalThis, { window });
  Object.defineProperty(globalThis, 'navigator', { value: { storage: { estimate: async () => ({ quota: 16 * 1024 ** 3, usage: 0 }) } }, configurable: true });
  const bytes = new Uint8Array(CHUNK + 64).fill(73); const calls = []; const device = { serverId: 'instance', userId: '1', deviceId: 'device', sessionId: 'session', displayName: 'Isolated fixture', policy: { enabled: true }, exports: [] };
  let blocked = false; let released = false; let arrived;
  const secondChunk = new Promise(resolve => { arrived = resolve; });
  const manifest = { grantId: 'grant', mediaId: 'media', title: 'PRIVATE TITLE', kind: 'video', libraryName: 'Fixture', durationSeconds: 120, sourceFingerprint: 'abc-def', matchRevision: 1, expiresAt: new Date(Date.now() + 7 * 86400000).toISOString(),
    assets: [{ id: 'media', contentType: 'video/mp4', bytes: bytes.length, fingerprint: 'abc-def', chunkBytes: CHUNK, chunks: 2 }], progressStamp: '0'.repeat(64), positionSeconds: 0, isWatched: false };
  const job = { id: 'grant', userId: '1', deviceId: 'device', status: 'ready', revision: 2, expiresAt: manifest.expiresAt, manifest, requiresPreparation: false };
  globalThis.fetch = async (url, options = {}) => {
    const path = new URL(url, 'https://home.test').pathname; calls.push(path);
    if (path === '/api/offline/device') return Response.json(device);
    if (path === '/api/offline/media/media') {
      const input = JSON.parse(options.body); assert.equal(input.expectedUserId, device.userId); assert.equal(input.expectedSessionId, device.sessionId);
      device.exports = [job]; return Response.json(job);
    }
    if (path === '/api/offline/exports/grant') return Response.json(job);
    if (path.endsWith('/manifest')) return Response.json(manifest);
    if (path.includes('/chunks/media/')) {
      const index = Number(path.split('/').at(-1));
      if (index === 1 && blocked && !released) { arrived(); await new Promise((_, reject) => { if (options.signal.aborted) reject(options.signal.reason); else options.signal.addEventListener('abort', () => reject(options.signal.reason), { once: true }); }); }
      const data = bytes.slice(index * CHUNK, Math.min(bytes.length, (index + 1) * CHUNK));
      return new Response(data, { headers: { 'X-Home-Fingerprint': 'abc-def', 'X-Home-Sha256': createHash('sha256').update(data).digest('hex') } });
    }
    if (path.endsWith('/progress')) { const input = JSON.parse(options.body); return Response.json({ status: 'applied', stamp: '1'.repeat(64), sequence: input.sequence, positionSeconds: input.positionSeconds, durationSeconds: 120, isWatched: false }); }
    if (path.endsWith('/cancel')) return new Response(null, { status: 204 });
    throw new Error('Unexpected fixture request: ' + path);
  };
  const store = new OfflineStore('manager-' + Math.random(), new IDBFactory()); const manager = new OfflineManager(store);
  const item = { id: 'media', title: 'PRIVATE TITLE', kind: 'video', durationSeconds: 120, sizeBytes: bytes.length, matchRevision: 1 };
  const wait = predicate => new Promise((resolve, reject) => {
    const timeout = setTimeout(() => { stop(); reject(new Error('Controller state timeout: ' + JSON.stringify(manager.snapshot().items.map(item => ({ state: item.row.state, message: item.row.message }))))); }, 10000);
    const stop = manager.subscribe(() => { if (predicate(manager.snapshot())) { clearTimeout(timeout); stop(); resolve(); } });
    if (predicate(manager.snapshot())) { clearTimeout(timeout); stop(); resolve(); }
  });
  return { manager, store, item, device, calls, wait, secondChunk, set block(value) { blocked = value; }, set released(value) { released = value; } };
}
test('production controller queues, encrypts, completes and synchronizes its device copy', async () => {
  const f = setup(); await f.manager.create('controller-fixture-password'); await f.manager.queue(f.item, { profile: 'original' }); await f.wait(state => state.items[0]?.row.state === 'ready');
  const row = f.manager.snapshot().items[0].row; assert.equal(row.nextChunk, 2); assert.equal(f.calls.filter(path => path === '/api/offline/media/media').length, 1);
  assert.equal(new TextDecoder().decode(row.metadata.data).includes('PRIVATE TITLE'), false);
  await f.manager.saveProgress(row.id, 45); await f.manager.sync(); await f.manager.refresh(); assert.equal(f.manager.snapshot().items[0].metadata.progress.pending, false);
  await f.manager.lock(); assert.equal(f.manager.snapshot().unlocked, false); assert.equal(f.manager.snapshot().items.length, 0);
});
test('controller pause resumes the existing server grant and keeps the first committed chunk', async () => {
  const f = setup(); f.block = true; await f.manager.create('controller-fixture-password'); await f.manager.queue(f.item, { profile: 'original' }); await f.secondChunk;
  const row = (await f.store.entries(f.manager.snapshot().vault.id))[0]; await f.manager.pause(row.id); assert.equal((await f.store.entry(row.id)).nextChunk, 1);
  f.released = true; await f.manager.resume(row.id); await f.wait(state => state.items[0]?.row.state === 'ready');
  assert.equal(f.calls.filter(path => path === '/api/offline/media/media').length, 1); assert.equal(f.calls.filter(path => path.endsWith('/chunks/media/0')).length, 1); await f.manager.lock();
});
test('known server revocation removes local ciphertext before the vault can be used again', async () => {
  const f = setup(); await f.manager.create('controller-fixture-password'); await f.manager.queue(f.item, { profile: 'original' }); await f.wait(state => state.items[0]?.row.state === 'ready');
  const vault = f.manager.snapshot().vault; f.device.policy.enabled = false; await f.manager.maintain();
  assert.equal(f.manager.snapshot().unlocked, false); assert.equal((await f.store.entries(vault.id)).length, 0); assert.equal((await f.store.usage()).stored, 0);
});
