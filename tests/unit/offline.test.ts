import { test } from 'node:test';
import assert from 'node:assert/strict';
import { IDBFactory, IDBObjectStore } from 'fake-indexeddb';
import { createVault, openVault, seal, unseal, sealMetadata, readMetadata, chunkAad, newOfflineId } from '../../web/src/offline/crypto.ts';
import { OfflineStore } from '../../web/src/offline/store.ts';
import { byteRange, offlineResponse } from '../../web/src/offline/range.ts';
import { acknowledgeProgress } from '../../web/src/offline/progress.ts';
import type { OfflineEntry, OfflineManifest, OfflineMetadata } from '../../web/src/offline/types.ts';

const CHUNK = 2 * 1024 * 1024;
const password = 'isolated-offline-passphrase';
const identity = (userId = '1') => ({ serverId: 'server-fixture', userId, deviceId: 'device-' + userId, displayName: 'Isolated ' + userId });
async function fixture(bytes = new Uint8Array([1, 2, 3, 4])) {
  const factory = new IDBFactory(); const store = new OfflineStore('fixture', factory); const vault = await createVault(identity(), password); await store.addVault(vault.header);
  const id = newOfflineId(); const manifest: OfflineManifest = { grantId: newOfflineId(), mediaId: 'media-fixture', title: 'PRIVATE TITLE', kind: 'video', libraryName: 'PRIVATE LIBRARY', durationSeconds: 120,
    sourceFingerprint: '123-abc', matchRevision: 1, expiresAt: new Date(Date.now() + 3600000).toISOString(), assets: [{ id: 'media', contentType: 'video/mp4', bytes: bytes.length, fingerprint: '123-abc', chunkBytes: CHUNK, chunks: Math.ceil(bytes.length / CHUNK), language: null, title: null }], progressStamp: 'stamp-zero', positionSeconds: 0, isWatched: false };
  const metadata: OfflineMetadata = { input: { clientId: id, profile: 'original' }, media: { id: manifest.mediaId, title: manifest.title, kind: 'video', durationSeconds: 120, sizeBytes: bytes.length }, manifest,
    progress: { position: 0, sequence: 0, acknowledged: 0, eventId: newOfflineId(), baseStamp: 'stamp-zero', pending: false, conflict: null } };
  const row: OfflineEntry = { id, vaultId: vault.header.id, grantId: manifest.grantId, reservedBytes: bytes.length + manifest.assets[0].chunks * 28 + 16000, storedBytes: 0, nextChunk: 0, state: 'queued', revision: 1, metadata: await sealMetadata(vault.key, vault.header.id, id, metadata), createdAt: Date.now(), message: '' };
  await store.reserve(row); const active = await store.change(id, 1, { state: 'downloading' });
  async function chunk(index: number) { const data = bytes.slice(index * CHUNK, Math.min(bytes.length, (index + 1) * CHUNK)); const sealed = await seal(vault.key, data, chunkAad(vault.header.id, id, manifest.assets[0].fingerprint, index)); return { entryId: id, index, sealed, bytes: sealed.data.byteLength + 12 }; }
  async function complete() { for (let i = 0; i < manifest.assets[0].chunks; i++) assert.equal(await store.commitChunk(id, active.revision, await chunk(i)), true); await store.complete(id, active.revision, manifest.assets[0].chunks); }
  return { factory, store, vault, id, manifest, metadata, active, bytes, chunk, complete };
}
test('vault key and metadata require the passphrase and the exact account/entry context', async () => {
  const f = await fixture(); const reopened = await openVault(f.vault.header, password);
  assert.equal((await readMetadata(reopened, f.vault.header.id, f.id, f.active.metadata)).media.title, 'PRIVATE TITLE');
  await assert.rejects(openVault(f.vault.header, 'incorrect-passphrase'));
  await assert.rejects(openVault({ ...f.vault.header, userId: 'other' }, password));
  await assert.rejects(readMetadata(reopened, f.vault.header.id, newOfflineId(), f.active.metadata));
  await assert.rejects(crypto.subtle.exportKey('raw', reopened));
  assert.equal(new TextDecoder().decode(f.active.metadata.data).includes('PRIVATE TITLE'), false);
});
test('quota reservations are atomic across vaults and a failed reservation leaves no entry', async () => {
  const store = new OfflineStore('quota', new IDBFactory()); const one = await createVault(identity('1'), password); const two = await createVault(identity('2'), password);
  await store.addVault(one.header); await store.addVault(two.header); await store.setQuota(2048);
  const entry = (vaultId: string): OfflineEntry => ({ id: newOfflineId(), vaultId, grantId: null, reservedBytes: 1400, storedBytes: 0, nextChunk: 0, state: 'queued', revision: 1, metadata: { iv: new ArrayBuffer(12), data: new ArrayBuffer(16) }, createdAt: Date.now(), message: '' });
  const results = await Promise.allSettled([store.reserve(entry(one.header.id)), store.reserve(entry(two.header.id))]);
  assert.equal(results.filter(value => value.status === 'fulfilled').length, 1); assert.equal((await store.usage()).count, 1); assert.equal((await store.usage()).reserved, 1400);
  await assert.rejects(store.setQuota(1024)); assert.equal((await store.usage()).quota, 2048);
});
test('pause and removal fence late chunks and a resumed transfer retains its committed prefix', async () => {
  const f = await fixture(new Uint8Array(CHUNK + 16)); assert.equal(await f.store.commitChunk(f.id, f.active.revision, await f.chunk(0)), true);
  await f.store.pause(f.vault.header.id); const paused = (await f.store.entry(f.id))!; assert.equal(paused.nextChunk, 1); assert.equal(paused.state, 'queued');
  assert.equal(await f.store.commitChunk(f.id, f.active.revision, await f.chunk(1)), false); assert.equal(await f.store.chunk(f.id, 1), undefined);
  const resumed = await f.store.change(f.id, paused.revision, { state: 'downloading' }); assert.equal(await f.store.commitChunk(f.id, resumed.revision, await f.chunk(1)), true);
  await f.store.complete(f.id, resumed.revision, 2); assert.equal((await f.store.entry(f.id))!.state, 'ready');
  await f.store.remove(f.id); assert.equal(await f.store.commitChunk(f.id, resumed.revision, await f.chunk(1)), false); assert.equal((await f.store.usage()).stored, 0);
});
test('a quota failure after the chunk write rolls back both the blob and its progress counter', async t => {
  const f = await fixture(); const original = IDBObjectStore.prototype.put;
  const mocked = t.mock.method(IDBObjectStore.prototype, 'put', function (this: IDBObjectStore, value: OfflineEntry, ...args: unknown[]) {
    if (this.name === 'entries' && value.nextChunk === 1) throw new DOMException('Isolated quota fault', 'QuotaExceededError');
    return original.apply(this, [value, ...args] as Parameters<typeof original>);
  });
  await assert.rejects(f.store.commitChunk(f.id, f.active.revision, await f.chunk(0))); mocked.mock.restore();
  assert.equal(await f.store.chunk(f.id, 0), undefined); assert.equal((await f.store.entry(f.id))!.nextChunk, 0); assert.equal((await f.store.entry(f.id))!.storedBytes, 0);
});
test('encrypted local HTTP responses implement exact cross-chunk ranges, suffixes, HEAD and 416', async () => {
  const bytes = new Uint8Array(CHUNK + 128); for (let i = 0; i < bytes.length; i++) bytes[i] = i % 251;
  const f = await fixture(bytes); await f.complete();
  const response = (range?: string, method = 'GET') => offlineResponse(new Request('https://home.test/api/offline/local/fixture', { method, headers: range ? { Range: range } : {} }), f.store, f.vault.key, f.vault.header.id, f.id, 'media', new AbortController().signal, () => {});
  const partial = await response(`bytes=${CHUNK - 4}-${CHUNK + 8}`); assert.equal(partial.status, 206); assert.deepEqual(new Uint8Array(await partial.arrayBuffer()), bytes.slice(CHUNK - 4, CHUNK + 9));
  const suffix = await response('bytes=-17'); assert.deepEqual(new Uint8Array(await suffix.arrayBuffer()), bytes.slice(-17));
  const head = await response(undefined, 'HEAD'); assert.equal(head.headers.get('Content-Length'), String(bytes.length)); assert.equal((await head.arrayBuffer()).byteLength, 0);
  const outside = await response(`bytes=${bytes.length}-`); assert.equal(outside.status, 416); assert.equal(outside.headers.get('Content-Range'), `bytes */${bytes.length}`);
  assert.equal((await response('bytes=0-1,4-5')).status, 416); assert.equal(byteRange('bytes=-0', 100), null);
});
test('wrong vault/key and expired leases cannot serve a stored file', async () => {
  const f = await fixture(); await f.complete(); const other = await createVault(identity('2'), password); let released = 0;
  const serve = (key: CryptoKey, vault: string) => offlineResponse(new Request('https://home.test/local'), f.store, key, vault, f.id, 'media', new AbortController().signal, () => released++);
  assert.equal((await serve(f.vault.key, other.header.id)).status, 404); assert.equal((await serve(other.key, f.vault.header.id)).status, 403);
  f.metadata.manifest!.expiresAt = new Date(Date.now() - 1).toISOString(); const row = (await f.store.entry(f.id))!;
  await f.store.change(f.id, row.revision, { metadata: await sealMetadata(f.vault.key, f.vault.header.id, f.id, f.metadata) });
  assert.equal((await serve(f.vault.key, f.vault.header.id)).status, 410); assert.equal(released, 3);
});
test('locking during a response aborts subsequent encrypted chunks', async () => {
  const f = await fixture(new Uint8Array(CHUNK * 3)); await f.complete(); const cancellation = new AbortController();
  const response = await offlineResponse(new Request('https://home.test/local'), f.store, f.vault.key, f.vault.header.id, f.id, 'media', cancellation.signal, () => {});
  const reader = response.body!.getReader(); assert.equal((await reader.read()).value!.length, CHUNK); cancellation.abort(); await assert.rejects(reader.read());
});
test('GCM rejects altered ciphertext and reuse under a different asset fingerprint', async () => {
  const f = await fixture(); const part = await f.chunk(0); const changed = part.sealed.data.slice(0); new Uint8Array(changed)[0] ^= 1;
  await assert.rejects(unseal(f.vault.key, { ...part.sealed, data: changed }, chunkAad(f.vault.header.id, f.id, '123-abc', 0)));
  await assert.rejects(unseal(f.vault.key, part.sealed, chunkAad(f.vault.header.id, f.id, '123-def', 0)));
});
test('late progress acknowledgements retain newer local positions and conflicts preserve both choices', () => {
  const current = { position: 45, sequence: 3, acknowledged: 1, eventId: 'event-3', baseStamp: 'base', pending: true, conflict: null };
  const sent = { ...current, position: 30, sequence: 2, eventId: 'event-2' };
  const applied = acknowledgeProgress(current, sent, { status: 'applied', stamp: 'after-2', positionSeconds: 30, durationSeconds: 120, isWatched: false, sequence: 2 });
  assert.equal(applied.position, 45); assert.equal(applied.sequence, 3); assert.equal(applied.pending, true); assert.equal(applied.baseStamp, 'after-2');
  const old = acknowledgeProgress(applied, { ...sent, sequence: 1 }, { status: 'applied', stamp: 'old', positionSeconds: 10, durationSeconds: 120, isWatched: false, sequence: 1 }); assert.equal(old, applied);
  const conflict = acknowledgeProgress(current, sent, { status: 'conflict', stamp: 'other-device', positionSeconds: 80, durationSeconds: 120, isWatched: false, sequence: 1 });
  assert.equal(conflict.position, 45); assert.equal(conflict.conflict!.positionSeconds, 80); assert.equal(conflict.pending, true);
});
