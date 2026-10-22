import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createPushController } from '../../web/push-controller.mjs';

const id = char => char.repeat(32);
const now = Date.now();
const binding = (char = 'a', user = '1') => ({ bindingId: id(char), channelId: id('c'), sessionId: id(char === 'a' ? 'd' : 'e'), userId: user, expiresAt: new Date(now + 100000).toISOString() });
const message = (value, char = 'f') => ({ v: 1, bindingId: value.bindingId, deliveryId: id(char), test: false, expiresAt: now + 60000, title: 'PRIVATE CONTENT', url: 'https://evil.example/' });
function fixture(current = binding()) {
  let state = { binding: null, seen: [] }; let remote = { userId: current?.userId ?? null, sessionId: current?.sessionId ?? null, current }; let offline = false;
  const shown = []; const closed = []; let opened = 0; let fetcher;
  const controller = createPushController({ load: async () => structuredClone(state), save: async value => { state = structuredClone(value); },
    fetchState: async () => { if (fetcher) return fetcher(); if (offline) throw new Error('offline'); return remote; },
    show: async value => shown.push(value), closeExcept: async value => closed.push(value), open: async () => { opened++; }, now: () => now });
  return { controller, shown, closed, get state() { return state; }, get opened() { return opened; }, set remote(value) { remote = value; }, set offline(value) { offline = value; }, set fetcher(value) { fetcher = value; } };
}
test('a confirmed account receives only the generic envelope, with bounded duplicate suppression', async () => {
  const current = binding(); const f = fixture(current); await f.controller.sync({ userId: current.userId, sessionId: current.sessionId });
  assert.equal(await f.controller.push(message(current)), true); assert.equal(await f.controller.push(message(current)), false);
  assert.deepEqual(f.shown, [{ bindingId: current.bindingId, deliveryId: id('f'), test: false }]);
  assert.equal(await f.controller.click({ homePush: true, bindingId: current.bindingId, url: 'https://evil.example' }), true); assert.equal(f.opened, 1);
});
test('logout clears notifications and a queued old-account push cannot show or open the next profile', async () => {
  const first = binding(); const next = binding('b', '2'); const f = fixture(first); await f.controller.sync(first); await f.controller.push(message(first));
  await f.controller.clear(first); f.remote = { userId: next.userId, sessionId: next.sessionId, current: next }; await f.controller.sync(next);
  assert.equal(await f.controller.push(message(first)), false); assert.equal(await f.controller.click({ homePush: true, bindingId: first.bindingId }), false);
  assert.equal(f.opened, 0); assert.equal(f.state.binding.bindingId, next.bindingId); assert.ok(f.closed.includes(null));
  await f.controller.clear(first); assert.equal(f.state.binding.bindingId, next.bindingId);
});
test('a stale in-flight session response cannot restore the old binding after clear', async () => {
  const current = binding(); const f = fixture(current); await f.controller.sync(current);
  let complete; f.fetcher = () => new Promise(resolve => { complete = resolve; });
  const pending = f.controller.sync(current); await f.controller.clear(current);
  complete({ userId: current.userId, sessionId: current.sessionId, current }); await pending;
  assert.equal(f.state.binding, null);
});
test('a short-lived generic push works offline only for the cached binding', async () => {
  const current = binding(); const f = fixture(current); await f.controller.sync(current); f.offline = true;
  assert.equal(await f.controller.push(message(current)), true);
  assert.equal(await f.controller.push(message(binding('b', '2'))), false);
  await f.controller.clear(current); assert.equal(await f.controller.push({ ...message(current, '1'), test: true }), false);
});
test('expired, malformed and far-future payloads never display', async () => {
  const current = binding(); const f = fixture(current); await f.controller.sync(current);
  for (const payload of [null, {}, { ...message(current), expiresAt: now - 1 }, { ...message(current), expiresAt: now + 3600000 }, { ...message(current), bindingId: '../bad' }, { ...message(current), v: 2 }]) assert.equal(await f.controller.push(payload), false);
  assert.equal(f.shown.length, 0);
});
test('a server-side revocation clears a previously cached session on the next push', async () => {
  const current = binding(); const f = fixture(current); await f.controller.sync(current); f.remote = { userId: null, sessionId: null, current: null };
  assert.equal(await f.controller.push(message(current)), false); assert.equal(f.state.binding, null); assert.ok(f.closed.includes(null));
});
test('new-profile state is not replaced by a late message from an old tab', async () => {
  const current = binding('b', '2'); const f = fixture(current); await f.controller.sync(current);
  await f.controller.sync(binding()); assert.equal(f.state.binding.bindingId, current.bindingId);
});
test('notification clicks recheck the current server account before opening Home', async () => {
  const old = binding(); const next = binding('b', '2'); const f = fixture(old); await f.controller.sync(old);
  f.remote = { userId: next.userId, sessionId: next.sessionId, current: next };
  assert.equal(await f.controller.click({ homePush: true, bindingId: old.bindingId }), false); assert.equal(f.opened, 0);
});
