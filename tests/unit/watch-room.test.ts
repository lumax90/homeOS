import { test } from 'node:test';
import assert from 'node:assert/strict';
import { WatchController, watchPosition } from '../../web/src/watch/WatchController.ts';
import type { WatchRoom, WatchTransport, WatchPlayer, WatchPlayerState, WatchPulse, WatchCommandRequest, WatchConnection } from '../../web/src/watch/types.ts';

const tick = () => new Promise<void>(resolve => queueMicrotask(resolve));
function deferred<T>() { let resolve!: (value: T) => void; const promise = new Promise<T>(accept => { resolve = accept; }); return { promise, resolve }; }
function fixture() {
  let time = 0; let revision = 0; const server = 1_790_000_000_000;
  const room: WatchRoom = { id: 'room', mode: 'together', ownerId: '1', ownerName: 'Isolated owner', mediaId: 'media', title: 'Isolated film', kind: 'video', fingerprint: 'a-b', matchRevision: 1,
    state: 'paused', positionSeconds: 10, durationSeconds: 120, anchorUnixMs: server, serverUnixMs: server, rate: 1, volume: .7, muted: false, revision: 1, syncRevision: 1, epoch: 1, guestControl: false, canControl: true, canManage: true, message: '',
    participants: [{ id: 'member', userId: '1', name: 'Owner', device: 'Fixture', role: 'player', connected: true, ready: false, state: 'preparing', positionSeconds: 10, playing: false, owner: true }], invitations: [] };
  const local: WatchPlayerState = { mediaId: 'media', playbackId: 'lease', position: 10, playing: false, ready: true, armed: true, buffering: false, error: false, rate: 1, volume: 1, muted: false };
  const calls = { play: 0, pause: 0, seeks: [] as number[], speeds: [] as number[], restarts: [] as number[], stopped: [] as string[], volumes: [] as number[], reads: 0, pulses: [] as WatchPulse[], commands: [] as WatchCommandRequest[], leaves: [] as number[] };
  const view = () => structuredClone({ ...room, serverUnixMs: server + time });
  const connection = (clientRevision: number, role: 'player' | 'controller' = 'player'): WatchConnection => ({ room: view(), memberId: 'member', clientId: 'client', clientRevision, role });
  const transport: WatchTransport = {
    create: async input => connection(input.clientRevision), join: async (_, _client, clientRevision, role) => connection(clientRevision, role),
    leave: async (_, clientRevision) => { calls.leaves.push(clientRevision); }, snapshot: async () => view(),
    pulse: async (_, _member, input) => { calls.pulses.push(input); return view(); },
    command: async (_, input) => { calls.commands.push(input); room.revision++; return { appliedRevision: room.revision, duplicate: false, room: view() }; },
  };
  const player: WatchPlayer = {
    read: () => { calls.reads++; return { ...local }; },
    pause: () => { calls.pause++; local.playing = false; }, play: async () => { calls.play++; local.playing = true; return true; },
    seek: position => { calls.seeks.push(position); local.position = position; }, speed: rate => { calls.speeds.push(rate); local.rate = rate; },
    volume: (volume, muted) => { calls.volumes.push(volume); local.volume = volume; local.muted = muted; },
    restart: async position => { calls.restarts.push(position); local.position = position; local.playing = false; local.playbackId = 'new-lease'; },
    stop: message => { calls.stopped.push(message); local.playing = false; },
  };
  const controller = new WatchController(transport, 'client', () => ++revision, () => time); controller.attach(player);
  return { controller, room, local, player, transport, calls, server, connection, advance: (milliseconds: number) => { time += milliseconds; }, view,
    join: async (role: 'player' | 'controller' = 'player') => { await controller.join('room', role); await tick(); } };
}
test('server-scheduled start uses a monotonic clock and never plays before the shared anchor', async t => {
  const f = fixture(); f.room.state = 'playing'; f.room.anchorUnixMs = f.server + 2000;
  await f.join(); assert.equal(f.calls.play, 0); f.advance(1500); f.controller.tick(); assert.equal(f.calls.play, 0);
  t.mock.method(Date, 'now', () => 1); f.advance(600); f.controller.tick(); await tick();
  assert.equal(f.calls.play, 1); assert.ok(Math.abs(f.controller.position() - 10.1) < .001);
  assert.equal(watchPosition(f.room, f.server - 999), 10); assert.equal(watchPosition(f.room, f.server + 500000), 120);
});
test('small drift changes rate gently and large drift seeks only once within the cooldown', async () => {
  const f = fixture(); f.room.state = 'playing'; f.local.position = 9.5; f.local.playing = true; await f.join();
  assert.equal(f.calls.seeks.length, 0); assert.equal(f.local.rate, 1.04);
  f.local.position = 6; f.advance(100); f.controller.tick(); await tick(); assert.equal(f.calls.seeks.length, 1);
  f.local.position = 6; f.advance(100); f.controller.tick(); assert.equal(f.calls.seeks.length, 1);
});
test('late poll response cannot undo a newer pause or seek command', async () => {
  const f = fixture(); const pending = deferred<WatchRoom>(); const old = f.view(); f.transport.pulse = async () => pending.promise;
  await f.join();
  f.transport.command = async (_, input) => { f.calls.commands.push(input); f.room.revision = 2; f.room.syncRevision = 2; f.room.positionSeconds = 45; return { room: f.view(), appliedRevision: 2, duplicate: false }; };
  await f.controller.command({ action: 'seek', positionSeconds: 45 }); assert.equal(f.local.position, 45);
  pending.resolve({ ...old, state: 'playing' }); await tick(); await tick();
  assert.equal(f.controller.snapshot().connection!.room.revision, 2); assert.equal(f.local.position, 45); assert.equal(f.calls.play, 0);
});
test('lost contact pauses the local renderer and fresh server state can resynchronize it', async () => {
  const f = fixture(); f.room.state = 'playing'; await f.join(); await tick(); assert.equal(f.local.playing, true);
  const pending = deferred<WatchRoom>(); f.transport.pulse = async () => pending.promise;
  f.advance(9000); f.controller.tick(); assert.equal(f.local.playing, false); assert.equal(f.controller.snapshot().connected, false);
  f.room.state = 'paused'; f.room.positionSeconds = 13; f.room.revision++; f.room.syncRevision++;
  pending.resolve(f.view()); await tick(); await tick();
  assert.equal(f.controller.snapshot().connected, true); assert.equal(f.local.position, 13); assert.equal(f.local.playing, false);
});
test('a join arriving after leave is discarded and the newer close revision is preserved', async () => {
  const f = fixture(); const pending = deferred<WatchConnection>(); f.transport.join = async () => pending.promise;
  const joining = f.controller.join('room', 'player'); await f.controller.leave();
  pending.resolve(f.connection(1)); await joining;
  assert.equal(f.controller.snapshot().connection, null); assert.equal(f.calls.leaves[0], 2); assert.equal(f.calls.pulses.length, 0);
});
test('server restart epoch rebuilds the renderer once and stays paused until a new play command', async () => {
  const f = fixture(); await f.join(); f.local.error = true; f.local.ready = false; f.local.buffering = true; f.local.preparing = false;
  f.room.epoch = 2; f.room.revision++; f.room.syncRevision++; f.room.positionSeconds = 20;
  await f.controller.pulse(); await tick(); f.controller.tick(); await tick();
  assert.deepEqual(f.calls.restarts, [20]); assert.equal(f.local.playing, false); assert.equal(f.calls.play, 0);
  await f.controller.pulse(); assert.deepEqual(f.calls.restarts, [20]);
});
test('controller-only membership neither reads nor starts a local playback or volume change', async () => {
  const f = fixture(); f.room.mode = 'remote'; await f.join('controller');
  await f.controller.command({ action: 'volume', volume: .4, muted: false }); f.advance(2500); f.controller.tick(); await tick();
  assert.equal(f.calls.reads, 0); assert.equal(f.calls.play, 0); assert.equal(f.calls.restarts.length, 0); assert.equal(f.calls.volumes.length, 0);
  assert.equal(f.calls.pulses.at(-1)!.playbackId, null);
});
test('a lost command response is retried with the exact idempotency key and expected revision', async () => {
  const f = fixture(); await f.join();
  f.transport.command = async (_, input) => {
    f.calls.commands.push(input);
    if (f.calls.commands.length === 1) { f.room.revision++; f.room.positionSeconds = 30; throw new TypeError('Isolated lost response'); }
    return { appliedRevision: f.room.revision, duplicate: true, room: f.view() };
  };
  await f.controller.command({ action: 'seek', positionSeconds: 30 });
  assert.equal(f.calls.commands.length, 2); assert.deepEqual(f.calls.commands[0], f.calls.commands[1]); assert.equal(f.local.position, 30);
});
test('autoplay denial remains unready and does not generate feedback control commands', async () => {
  const f = fixture(); f.local.armed = false; f.room.state = 'waiting'; await f.join();
  assert.equal(f.calls.pulses[0].ready, false); assert.equal(f.calls.pulses[0].state, 'gesture');
  f.local.armed = true; await f.controller.pulse(); assert.equal(f.calls.pulses.at(-1)!.ready, true);
  f.player.play = async () => { f.local.armed = false; return false; }; f.room.state = 'playing'; f.room.revision++;
  await f.controller.pulse(); await tick(); await f.controller.pulse();
  assert.equal(f.calls.pulses.at(-1)!.ready, false); assert.equal(f.calls.commands.length, 0);
});
test('a removed participant loses its local transport and stale state cannot revive it', async () => {
  const f = fixture(); await f.join(); f.room.participants = []; f.room.revision++;
  await f.controller.pulse(); assert.equal(f.controller.snapshot().connection, null); assert.equal(f.calls.stopped.length, 1);
  f.controller.tick(); assert.equal(f.calls.play, 0);
});
test('an intentionally closed or timed-out lease never auto-reopens in the same room epoch', async () => {
  const f = fixture(); await f.join();
  f.transport.pulse = async () => { throw Object.assign(new Error('Isolated lease closed by limits'), { status: 410 }); };
  f.player.stop = message => { f.calls.stopped.push(message); f.local.playing = false; f.local.playbackId = null; f.local.error = true; f.local.ready = false; };
  await f.controller.pulse(); f.controller.tick(); await tick();
  assert.equal(f.calls.restarts.length, 0); assert.equal(f.calls.stopped.length, 1); assert.equal(f.local.playbackId, null);
  assert.ok(f.controller.snapshot().connection); assert.match(f.controller.snapshot().message, /closed by limits/);
});
