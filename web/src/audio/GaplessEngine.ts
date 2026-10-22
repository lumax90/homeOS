import type { MediaItem, MusicPcmInfo, MusicPlaybackPreferences } from '../api';

export interface MusicAudioState { index: number; position: number; duration: number; playing: boolean; buffering: boolean; error: string; notice: string; info: MusicPcmInfo | null }
interface Segment { index: number; occurrence: number; info: MusicPcmInfo; start: number; end: number; offset: number; final: boolean; completed: boolean; node: AudioBufferSourceNode; gain: GainNode }
interface Callbacks { state: (state: MusicAudioState) => void; track: (index: number) => void; progress: (id: string, seconds: number, duration: number, keepalive?: boolean) => void; external: (index: number) => void }
export interface MusicAudioTransport {
  begin?: (id: string, signal: AbortSignal) => Promise<void>;
  prepare: (id: string, signal: AbortSignal) => Promise<MusicPcmInfo>;
  read: (info: MusicPcmInfo, start: number, frames: number, signal: AbortSignal) => Promise<ArrayBuffer>;
  heartbeat?: (id: string, seconds: number, paused: boolean, signal: AbortSignal) => Promise<void>;
  close?: () => void;
}
const defaultPreferences: MusicPlaybackPreferences = { replayGain: 'track', preampDb: 0, preventClipping: true };

export function replayGain(info: MusicPcmInfo, preferences: MusicPlaybackPreferences) {
  if (preferences.replayGain === 'off') return 1;
  const measured = info.loudnessLufs === null ? 0 : -18 - info.loudnessLufs;
  const db = (preferences.replayGain === 'album' ? info.albumGainDb ?? info.trackGainDb ?? measured : info.trackGainDb ?? measured) + preferences.preampDb;
  let gain = 10 ** (Math.max(-45, Math.min(30, db)) / 20);
  if (preferences.preventClipping) {
    const peak = preferences.replayGain === 'album' ? info.albumPeak ?? info.trackPeak : info.trackPeak;
    gain = Math.min(gain, peak && peak > 0 ? 1 / peak : 1);
  }
  return gain;
}

/** One audio clock schedules decoded sample ranges across track boundaries. No crossfade or silence trimming. */
export class GaplessEngine {
  private readonly context = new AudioContext({ sampleRate: 48000, latencyHint: 'playback' });
  private readonly master = this.context.createGain();
  private readonly timer: number;
  private queue: MediaItem[] = [];
  private segments: Segment[] = [];
  private prepared = new Map<string, Promise<MusicPcmInfo>>();
  private controller = new AbortController();
  private opening: Promise<void> = Promise.resolve();
  private lastHeartbeat = 0;
  private pulsePending = false;
  private epoch = 0;
  private occurrence = 0;
  private activeOccurrence = -1;
  private planned = -1;
  private frame = 0;
  private nextTime = 0;
  private externalIndex = -1;
  private wantPlay = true;
  private pumping = false;
  private closed = false;
  private speed = 1;
  private shuffle = false;
  private repeat = 'off';
  private preferences = defaultPreferences;
  private lastSaved = 0;
  private readonly callbacks: Callbacks;
  private readonly transport: MusicAudioTransport;
  private state: MusicAudioState = { index: 0, position: 0, duration: 0, playing: false, buffering: true, error: '', notice: '', info: null };
  constructor(callbacks: Callbacks, transport: MusicAudioTransport) {
    this.callbacks = callbacks; this.transport = transport;
    this.master.connect(this.context.destination);
    this.timer = window.setInterval(() => this.tick(), 150);
  }
  load(queue: MediaItem[], index: number, seconds: number, autoplay = true) {
    this.save(); this.clear(); this.queue = queue; this.planned = index; this.frame = Math.max(0, Math.floor(seconds * 48000));
    this.nextTime = this.context.currentTime + .1; this.wantPlay = autoplay; this.externalIndex = -1; this.activeOccurrence = -1; this.occurrence++;
    this.state = { index, position: seconds, duration: queue[index]?.durationSeconds ?? 0, playing: false, buffering: true, error: '', notice: '', info: null };
    const epoch = this.epoch; this.lastHeartbeat = Date.now();
    this.opening = this.transport.begin?.(queue[index].id, this.controller.signal) ?? Promise.resolve();
    void this.opening.catch(error => { if (!this.closed && epoch === this.epoch && !this.controller.signal.aborted) this.fail(error); });
    this.emit(); if (autoplay) this.resume(); void this.fill();
  }
  /** Appending to the listening queue must not restart the current audio clock or its lease. */
  appendQueue(queue: MediaItem[]) {
    if (queue.length < this.queue.length || this.queue.some((item,index) => item.id !== queue[index]?.id || item.modifiedAt !== queue[index]?.modifiedAt || item.sizeBytes !== queue[index]?.sizeBytes)) return false;
    const previousLength=this.queue.length;this.queue=queue;
    if (queue.length>previousLength && this.planned<0 && this.externalIndex<0 && (this.wantPlay || this.segments.some(segment=>segment.end>this.context.currentTime))) {
      this.planned=previousLength;this.frame=0;this.nextTime=Math.max(this.nextTime,this.context.currentTime+.04);void this.fill();
    }
    return true;
  }
  private clear() {
    this.epoch++; this.controller.abort(); this.controller = new AbortController(); this.prepared.clear();
    for (const segment of this.segments) { segment.node.onended = null; try { segment.node.stop(); } catch { /* Already ended. */ } segment.node.disconnect(); segment.gain.disconnect(); }
    this.segments = [];
  }
  private prepare(index: number): Promise<MusicPcmInfo> {
    const id = this.queue[index].id;
    if (!this.prepared.has(id)) {
      const epoch = this.epoch;
      const promise = this.transport.prepare(id, this.controller.signal).catch(error => { if (this.epoch === epoch) this.prepared.delete(id); throw error; });
      this.prepared.set(id, promise);
      // Retain only the current and next descriptors; decoded buffers have a separate bounded horizon.
      if (this.prepared.size > 3) this.prepared.delete(this.prepared.keys().next().value!);
    }
    return this.prepared.get(id)!;
  }
  private nextIndex(index: number) {
    if (this.repeat === 'one') return index;
    if (this.shuffle && this.queue.length > 1) { const offset = 1 + Math.floor(Math.random() * (this.queue.length - 1)); return (index + offset) % this.queue.length; }
    if (index + 1 < this.queue.length) return index + 1;
    return this.repeat === 'all' ? 0 : -1;
  }
  private async fill() {
    if (this.pumping || this.closed || this.state.error || !this.wantPlay || this.planned < 0) return;
    this.pumping = true; const epoch = this.epoch;
    try {
      await this.opening; if (epoch !== this.epoch || this.closed) return;
      while (!this.closed && epoch === this.epoch && this.wantPlay && this.planned >= 0 && this.nextTime - this.context.currentTime < 24) {
        const index = this.planned;
        if (this.queue[index].kind !== 'audio') { this.externalIndex = index; this.planned = -1; break; }
        const info = await this.prepare(index); if (epoch !== this.epoch) return;
        if (info.sampleRate !== 48000 || info.channels !== 2 || !Number.isSafeInteger(info.frames) || info.frames <= 0) throw new Error('Ses örnekleme bilgisi desteklenmiyor.');
        this.frame = Math.min(this.frame, info.frames - 1);
        // Prefetch a deterministic adjacent track; shuffle is chosen only at the actual queue boundary.
        const adjacent = !this.shuffle ? (this.repeat === 'one' ? index : index + 1 < this.queue.length ? index + 1 : this.repeat === 'all' ? 0 : -1) : -1;
        if (adjacent >= 0 && this.queue[adjacent].kind === 'audio') void this.prepare(adjacent).catch(() => {});
        const frames = Math.min(48000 * 6, info.frames - this.frame);
        const data = await this.transport.read(info, this.frame, frames, this.controller.signal); if (epoch !== this.epoch || this.closed) return;
        if (data.byteLength !== frames * 8) throw new Error('Ses aralığı eksik geldi.');
        const buffer = this.context.createBuffer(2, frames, 48000); const values = new DataView(data); const left = buffer.getChannelData(0); const right = buffer.getChannelData(1);
        for (let i = 0; i < frames; i++) { left[i] = values.getFloat32(i * 8, true); right[i] = values.getFloat32(i * 8 + 4, true); }
        const when = Math.max(this.nextTime, this.context.currentTime + .04);
        if (when > this.nextTime + .005 && this.segments.length && this.context.state === 'running') this.state.notice = 'Ağ beklemesi oldu; ses yeniden tamponlandı.';
        const node = this.context.createBufferSource(); node.buffer = buffer; node.playbackRate.value = this.speed;
        const gain = this.context.createGain(); gain.gain.value = replayGain(info, this.preferences); node.connect(gain); gain.connect(this.master);
        const final = this.frame + frames === info.frames;
        this.segments.push({ index, occurrence: this.occurrence, info, start: when, end: when + buffer.duration / this.speed, offset: this.frame / 48000, final, completed: false, node, gain });
        node.onended = () => { node.disconnect(); gain.disconnect(); }; node.start(when);
        this.nextTime = when + buffer.duration / this.speed; this.frame += frames;
        if (final) { this.planned = this.nextIndex(index); this.frame = 0; this.occurrence++; }
      }
    } catch (error) {
      if (!this.closed && epoch === this.epoch && !this.controller.signal.aborted) {
        this.fail(error as Error);
      }
    } finally { this.pumping = false; }
  }
  private tick() {
    if (this.closed) return;
    const time = this.context.currentTime;
    for (const segment of this.segments) {
      if (segment.start <= time && segment.end > time) {
        if (this.activeOccurrence !== segment.occurrence) { this.activeOccurrence = segment.occurrence; this.state.index = segment.index; this.state.info = segment.info; this.callbacks.track(segment.index); }
        this.state.position = Math.min(segment.info.frames / 48000, segment.offset + (time - segment.start) * this.speed); this.state.duration = segment.info.frames / 48000;
      }
      if (segment.end <= time && segment.final && !segment.completed) {
        segment.completed = true; this.callbacks.progress(segment.info.mediaId, segment.info.frames / 48000, segment.info.frames / 48000);
        if (segment.occurrence === this.activeOccurrence) this.state.position = segment.info.frames / 48000;
      }
    }
    const available = this.segments.some(segment => segment.end > time);
    this.state.playing = this.wantPlay && this.context.state === 'running' && available;
    this.state.buffering = this.wantPlay && !this.state.error && !available && (this.planned >= 0 || this.pumping);
    if (!available && this.planned < 0 && !this.pumping) {
      this.wantPlay = false;
      this.transport.close?.();
      if (this.externalIndex >= 0) { const index = this.externalIndex; this.externalIndex = -1; this.callbacks.external(index); }
    }
    this.segments = this.segments.filter(segment => segment.end > time - 1);
    if (this.state.playing && Date.now() - this.lastSaved > 10000) this.save();
    this.emit(); void this.fill(); this.heartbeat(false);
  }
  private fail(error: Error) {
    this.save(); this.clear(); this.transport.close?.(); this.state.error = error.message; this.state.buffering = false; this.state.playing = false; this.wantPlay = false; void this.context.suspend(); this.emit();
  }
  heartbeat(force = true) {
    if (this.closed || this.state.error || this.pulsePending || !this.transport.heartbeat || !this.queue.length || !force && Date.now() - this.lastHeartbeat < 20000) return;
    const epoch = this.epoch; this.lastHeartbeat = Date.now(); this.pulsePending = true;
    void this.transport.heartbeat(this.state.info?.mediaId ?? this.queue[this.state.index].id, this.state.position, !this.wantPlay || this.context.state !== 'running', this.controller.signal)
      .catch((error: Error & { status?: number }) => {
        if (this.closed || epoch !== this.epoch || this.controller.signal.aborted) return;
        if (error.status && [401, 403, 404, 409, 410].includes(error.status)) this.fail(error);
        else { this.state.notice = 'Sunucu bağlantısı bekleniyor.'; this.emit(); }
      }).finally(() => { this.pulsePending = false; });
  }
  private emit() { if (!this.closed) this.callbacks.state({ ...this.state }); }
  private resume() {
    void this.context.resume().then(() => { if (!this.closed) { this.state.notice = ''; this.tick(); } }).catch(() => { this.state.notice = 'Başlamak için oynat düğmesine dokun.'; this.emit(); });
  }
  toggle() {
    if (this.state.error) { this.load(this.queue, this.state.index, this.state.position, true); return; }
    if (!this.wantPlay && this.planned < 0 && this.state.duration > 0 && this.state.position >= this.state.duration - .01) { const next=this.nextIndex(this.state.index);this.load(this.queue,next<0?this.state.index:next,0,true); return; }
    if (this.context.state !== 'running' || !this.wantPlay) { this.wantPlay = true; this.resume(); void this.fill(); }
    else { this.wantPlay = false; this.save(); void this.context.suspend().then(() => this.tick()); }
  }
  seek(seconds: number) { if (Number.isFinite(seconds)) this.load(this.queue, this.state.index, Math.max(0, Math.min(seconds, Math.max(0, this.state.duration - .01))), this.wantPlay); }
  setOrder(shuffle: boolean, repeat: string) {
    if (this.shuffle === shuffle && this.repeat === repeat) return;
    this.shuffle = shuffle; this.repeat = repeat;
    if (this.queue.length) this.load(this.queue, this.state.index, this.state.position, this.wantPlay);
  }
  setSpeed(value: number) { if (value < .5 || value > 2 || !Number.isFinite(value)) return; this.speed = value; if (this.queue.length) this.load(this.queue, this.state.index, this.state.position, this.wantPlay); }
  setVolume(value: number) { this.master.gain.setTargetAtTime(Math.max(0, Math.min(1, value)), this.context.currentTime, .02); }
  setPreferences(preferences: MusicPlaybackPreferences) {
    this.preferences = preferences;
    for (const segment of this.segments) segment.gain.gain.setTargetAtTime(replayGain(segment.info, preferences), this.context.currentTime, .04);
  }
  save(keepalive = false) {
    if (!this.state.info) return; this.lastSaved = Date.now(); this.callbacks.progress(this.state.info.mediaId, this.state.position, this.state.duration, keepalive);
  }
  dispose() { if (this.closed) return; this.save(true); this.closed = true; this.clear(); this.transport.close?.(); window.clearInterval(this.timer); this.master.disconnect(); void this.context.close(); }
}
