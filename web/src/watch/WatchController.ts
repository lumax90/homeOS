import type { WatchCommand, WatchCommandRequest, WatchConnection, WatchPlayer, WatchRoom, WatchTransport } from './types.ts';

export interface WatchState { connection: WatchConnection | null; busy: boolean; connected: boolean; message: string; drift: number }
const status = (error: unknown) => (error as { status?: number })?.status;
const retryable = (error: unknown) => status(error) === undefined || (status(error) ?? 0) >= 500;
const id = () => [...crypto.getRandomValues(new Uint8Array(16))].map(value => value.toString(16).padStart(2, '0')).join('');
export function watchPosition(room: WatchRoom, serverNow: number): number {
  return Math.max(0, Math.min(room.durationSeconds, room.positionSeconds + (room.state === 'playing' ? Math.max(0, serverNow - room.anchorUnixMs) / 1000 * room.rate : 0)));
}

// The same production state machine drives a rendering member and a stream-free remote controller.
// A monotonic local clock extrapolates server time; wall-clock changes on the client do not seek the video.
export class WatchController {
  private value: WatchState = { connection: null, busy: false, connected: false, message: '', drift: 0 };
  private listeners = new Set<() => void>(); private player: WatchPlayer | null = null;
  private generation = 0; private abort = new AbortController(); private sequence = 0;
  private receivedAt = 0; private serverAt = 0; private lastPulse = -Infinity; private pulsePending = false;
  private lastSeek = -Infinity; private lastSync = ''; private lastVolume = ''; private restartNeeded = false; private restarting = false; private playPending = false;
  private timer: ReturnType<typeof setInterval> | undefined; private commandPending = false;
  private transport: WatchTransport; readonly clientId: string; private nextRevision: () => number; private now: () => number;
  constructor(transport: WatchTransport, clientId: string, nextRevision: () => number, now: () => number = () => performance.now()) {
    this.transport = transport; this.clientId = clientId; this.nextRevision = nextRevision; this.now = now;
  }
  snapshot = () => this.value;
  subscribe = (listener: () => void) => { this.listeners.add(listener); return () => { this.listeners.delete(listener); }; };
  private emit(changes: Partial<WatchState>) { this.value = { ...this.value, ...changes }; for (const listener of this.listeners) listener(); }
  start() { if (!this.timer) this.timer = setInterval(() => this.tick(), 250); }
  stop() { clearInterval(this.timer); this.timer = undefined; void this.leave(); }
  attach(player: WatchPlayer) { this.player = player; this.lastSync = ''; this.apply(); return () => { if (this.player === player) this.player = null; }; }
  private begin() {
    this.generation++; this.abort.abort(); this.abort = new AbortController(); this.sequence = 0; this.lastPulse = -Infinity;
    this.lastSync = ''; this.lastVolume = ''; this.lastSeek = -Infinity; this.restartNeeded = false;
    this.emit({ connection: null, busy: true, message: '', connected: false }); return this.generation;
  }
  private accepted(connection: WatchConnection, started: number, generation: number) {
    if (generation !== this.generation) return;
    this.receivedAt = this.now(); this.serverAt = connection.room.serverUnixMs + Math.min(500, Math.max(0, this.receivedAt - started) / 2);
    this.emit({ connection, connected: true, busy: false }); this.apply(); void this.pulse();
  }
  async create(mode: 'together' | 'remote') {
    const current = this.player?.read(); if (!current?.playbackId || !current.ready) throw new Error('Önce bu cihazda oynatıcıyı hazırla.');
    const generation = this.begin(); const revision = this.nextRevision(); const started = this.now(); this.player?.pause();
    const input = { clientId: this.clientId, clientRevision: revision, playbackId: current.playbackId, mode, positionSeconds: current.position, playing: current.playing, rate: current.rate, volume: current.volume, muted: current.muted };
    try { this.accepted(await this.withRetry(() => this.transport.create(input, this.abort.signal)), started, generation); }
    catch (error) { if (generation === this.generation) { this.emit({ busy: false, message: (error as Error).message }); await this.transport.leave(this.clientId, revision).catch(() => {}); } throw error; }
  }
  async join(roomId: string, role: 'player' | 'controller') {
    const generation = this.begin(); const revision = this.nextRevision(); const started = this.now(); this.player?.pause();
    try { this.accepted(await this.withRetry(() => this.transport.join(roomId, this.clientId, revision, role, this.abort.signal)), started, generation); }
    catch (error) { if (generation === this.generation) { this.emit({ busy: false, message: (error as Error).message }); await this.transport.leave(this.clientId, revision).catch(() => {}); } throw error; }
  }
  async leave() {
    const needed = !!this.value.connection || this.value.busy;
    const revision = this.nextRevision(); this.generation++; this.abort.abort(); this.abort = new AbortController();
    if (this.value.connection?.role === 'player') { this.player?.pause(); this.player?.speed(1); }
    this.emit({ connection: null, busy: false, connected: false, message: '', drift: 0 });
    if (needed) await this.transport.leave(this.clientId, revision).catch(() => {});
  }
  private async withRetry<T>(operation: () => Promise<T>): Promise<T> {
    try { return await operation(); } catch (error) { if (!retryable(error) || this.abort.signal.aborted) throw error; return operation(); }
  }
  private adopt(room: WatchRoom, started: number, generation: number) {
    const connection = this.value.connection; if (!connection || generation !== this.generation || room.id !== connection.room.id) return;
    if (room.revision < connection.room.revision || room.revision === connection.room.revision && room.serverUnixMs < connection.room.serverUnixMs) return;
    if (room.state === 'closed' || !room.participants.some(member => member.id === connection.memberId)) { this.end(room.message || 'Oda katılımı sona erdi.'); return; }
    this.restartNeeded ||= room.epoch !== connection.room.epoch && room.mediaId === connection.room.mediaId;
    this.receivedAt = this.now(); this.serverAt = room.serverUnixMs + Math.min(500, Math.max(0, this.receivedAt - started) / 2);
    this.emit({ connection: { ...connection, room }, connected: true, message: '' }); this.apply();
  }
  private end(message: string) {
    if (this.value.connection?.role === 'player') this.player?.stop(message);
    this.generation++; this.abort.abort(); this.abort = new AbortController(); this.emit({ connection: null, busy: false, connected: false, message });
  }
  position() { const room = this.value.connection?.room; return room ? watchPosition(room, this.serverAt + Math.max(0, this.now() - this.receivedAt)) : 0; }
  tick() {
    if (!this.value.connection) return;
    this.apply(); if (this.now() - this.lastPulse >= 2000) void this.pulse();
  }
  private apply() {
    const connection = this.value.connection; const player = this.player;
    if (!connection || connection.role !== 'player' || !player) return;
    const room = connection.room; const now = this.now(); const local = player.read();
    if (now - this.receivedAt > 8000) { player.pause(); player.speed(room.rate); if (this.value.connected) this.emit({ connected: false, message: 'Oda bağlantısı bekleniyor; bu cihaz duraklatıldı.' }); return; }
    if (local.mediaId !== room.mediaId) { player.pause(); return; }
    const position = this.position(); const serverNow = this.serverAt + Math.max(0, now - this.receivedAt);
    const playing = room.state === 'playing' && serverNow >= room.anchorUnixMs && position < room.durationSeconds - .05;
    if (this.restartNeeded && !this.restarting && !local.preparing) {
      this.restartNeeded = false; this.restarting = true; const generation = this.generation;
      player.pause(); void player.restart(position).finally(() => { this.restarting = false; if (generation === this.generation) this.apply(); }); return;
    }
    if (!playing && local.playing) player.pause();
    if (room.mode === 'remote') {
      const desired = `${room.volume}:${room.muted}`; if (this.lastVolume !== desired) { player.volume(room.volume, room.muted); this.lastVolume = desired; }
    }
    if (!local.ready || local.error || this.restarting) return;
    const drift = position - local.position; const sync = `${room.epoch}:${room.syncRevision}`; const changed = this.lastSync !== sync;
    const threshold = playing ? 1.25 : .3;
    if (Math.abs(drift) > threshold && (changed || now - this.lastSeek > 3000)) { player.pause(); player.seek(position); this.lastSeek = now; }
    this.lastSync = sync;
    const correction = playing && Math.abs(drift) >= .12 && Math.abs(drift) <= 1.25 ? Math.sign(drift) * .04 : 0;
    player.speed(room.rate * (1 + correction));
    if (playing && local.armed && !local.playing && !this.playPending) {
      this.playPending = true; const generation = this.generation;
      void player.play().then(ok => { if (!ok && generation === this.generation) this.emit({ message: 'Bu cihazda oynatmaya izin vererek odaya katıl.' }); })
        .finally(() => { this.playPending = false; });
    }
    if (Math.abs(this.value.drift - drift) > .15) this.emit({ drift });
  }
  async pulse() {
    const connection = this.value.connection; if (!connection || this.pulsePending) return;
    this.pulsePending = true; this.lastPulse = this.now(); const generation = this.generation; const started = this.now(); const room = connection.room;
    const local = connection.role === 'player' ? this.player?.read() : null;
    const matching = local?.mediaId === room.mediaId;
    const ready = !!(matching && local?.ready && local.armed && !local.error && !this.restartNeeded && !this.restarting && (room.state === 'playing' || Math.abs(local.position - this.position()) <= .65));
    const input = { clientRevision: connection.clientRevision, sequence: ++this.sequence, epoch: room.epoch, syncRevision: room.syncRevision, revision: room.revision,
      playbackId: matching && !local?.preparing && !this.restartNeeded && !this.restarting ? local?.playbackId ?? null : null, positionSeconds: matching ? local?.position ?? 0 : 0, ready, playing: matching && local?.playing === true,
      state: local?.error ? 'failed' : !matching || !local?.playbackId ? 'preparing' : !local.armed ? 'gesture' : !ready ? 'buffering' : 'ready' };
    try { this.adopt(await this.transport.pulse(room.id, connection.memberId, input, this.abort.signal), started, generation); }
    catch (error) {
      if (generation !== this.generation) return;
      if ([401, 403, 404].includes(status(error) ?? 0)) this.end((error as Error).message);
      else if ([409, 410].includes(status(error) ?? 0)) {
        try {
          const snapshot = await this.transport.snapshot(room.id, connection.memberId, this.abort.signal); this.adopt(snapshot, started, generation);
          if (this.value.connection?.role === 'player' && generation === this.generation && snapshot.epoch === room.epoch) {
            // A closed/limited lease in the same server epoch is intentional. Only an actual restart
            // epoch rebuilds automatically; otherwise require the renderer's explicit retry.
            this.player?.stop((error as Error).message); this.emit({ message: (error as Error).message });
          }
        } catch (refreshError) { if ([401, 403, 404, 410].includes(status(refreshError) ?? 0)) this.end((refreshError as Error).message); }
      } else if (this.now() - this.receivedAt > 8000) this.apply();
    } finally { this.pulsePending = false; }
  }
  async command(value: WatchCommand) {
    const connection = this.value.connection;
    if (!connection) throw new Error('Önce bir odaya bağlan.');
    if (this.commandPending) throw new Error('Önceki kumanda komutunun yanıtı bekleniyor.');
    this.commandPending = true; const generation = this.generation; const started = this.now(); const signal = this.abort.signal;
    const input: WatchCommandRequest = { ...value, memberId: connection.memberId, clientRevision: connection.clientRevision, commandId: id(), expectedRevision: connection.room.revision };
    this.emit({ busy: true, message: '' });
    try { const result = await this.withRetry(() => this.transport.command(connection.room.id, input, signal)); this.adopt(result.room, started, generation); }
    catch (error) {
      if (generation === this.generation) {
        if (status(error) === 409) {
          try { this.adopt(await this.transport.snapshot(connection.room.id, connection.memberId, signal), started, generation); } catch { }
        }
        if ([401, 403, 404, 410].includes(status(error) ?? 0) && status(error) !== 403) this.end((error as Error).message);
        else this.emit({ message: (error as Error).message });
      }
      throw error;
    } finally { this.commandPending = false; if (generation === this.generation) this.emit({ busy: false }); }
  }
}
