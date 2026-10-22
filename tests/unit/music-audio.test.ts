import { test, beforeEach } from 'node:test';
import assert from 'node:assert/strict';
import { setImmediate } from 'node:timers/promises';
import { GaplessEngine, replayGain, type MusicAudioTransport, type MusicAudioState } from '../../web/src/audio/GaplessEngine.ts';
import type { MediaItem, MusicPcmInfo } from '../../web/src/api.ts';

// Deterministic audio clock and PCM fixtures, without a browser or network connection.
class Buffer {
  channels: Float32Array[]; duration: number;
  constructor(channels: number, frames: number, rate: number) { this.channels = Array.from({ length: channels }, () => new Float32Array(frames)); this.duration = frames / rate; }
  getChannelData(index: number) { return this.channels[index]; }
}
class Gain { gain = { value: 1, setTargetAtTime() {} }; connect() {} disconnect() {} }
class Source {
  buffer!: Buffer; playbackRate = { value: 1 }; onended: (() => void) | null = null; when = -1; stopped = false;
  connect() {} disconnect() {} start(when: number) { this.when = when; } stop() { this.stopped = true; }
}
class Context {
  static latest: Context; currentTime = 0; state = 'suspended'; destination = {}; sources: Source[] = [];
  constructor() { Context.latest = this; }
  createGain() { return new Gain(); } createBuffer(channels: number, frames: number, rate: number) { return new Buffer(channels, frames, rate); }
  createBufferSource() { const value = new Source(); this.sources.push(value); return value; }
  async resume() { this.state = 'running'; } async suspend() { this.state = 'suspended'; } async close() { this.state = 'closed'; }
}
let tick: () => void;
beforeEach(() => {
  Object.assign(globalThis, { AudioContext: Context, window: { setInterval: (callback: () => void) => { tick = callback; return 1; }, clearInterval() {} } });
});
const media = (id: string, duration: number) => ({ id, durationSeconds: duration, kind: 'audio', positionSeconds: 0, isWatched: false }) as MediaItem;
const info = (id: string, seconds: number): MusicPcmInfo => ({ mediaId: id, fingerprint: 'fixture', frames: seconds * 48000, sampleRate: 48000, channels: 2, trackGainDb: null, albumGainDb: null, trackPeak: null, albumPeak: null, loudnessLufs: null, title: id, artist: 'Isolated fixture', albumId: null });
const drain = async () => { for (let i = 0; i < 8; i++) await setImmediate(); };
function setup(items: MediaItem[], overrides: Partial<MusicAudioTransport> = {}) {
  const states: MusicAudioState[] = []; const progress: unknown[][] = []; const tracks: number[] = [];
  const transport: MusicAudioTransport = { prepare: async id => info(id, items.find(item => item.id === id)!.durationSeconds!), read: async (item, start, frames) => {
    const data = new ArrayBuffer(frames * 8); new Float32Array(data).fill(item.mediaId === 'one' ? .1 : .2); return data;
  }, ...overrides };
  const engine = new GaplessEngine({ state: state => states.push(state), progress: (...args) => progress.push(args), track: index => tracks.push(index), external() {} }, transport);
  return { engine, states, progress, tracks };
}
test('scheduled PCM chunks meet exactly at both chunk and album track boundaries', async () => {
  const items = [media('one', 13.5), media('two', 3.25)]; const { engine, tracks, progress } = setup(items);
  try {
    engine.load(items, 0, 0); await drain(); const context = Context.latest; const sources = context.sources;
    assert.equal(sources.length, 4); assert.equal(sources.reduce((sum, source) => sum + source.buffer.duration, 0), 16.75);
    for (let i = 1; i < sources.length; i++) assert.ok(Math.abs(sources[i].when - sources[i - 1].when - sources[i - 1].buffer.duration) < 1e-10);
    context.currentTime = .5; tick(); context.currentTime = 13.75; tick();
    assert.deepEqual(tracks, [0, 1]); assert.ok(progress.some(value => value[0] === 'one' && value[1] === 13.5));
  } finally { engine.dispose(); }
});
test('a seek fences a late PCM response and stops the old scheduled nodes', async () => {
  const items = [media('one', 10), media('two', 8)]; let resolve!: (data: ArrayBuffer) => void; let oldSignal!: AbortSignal;
  const { engine } = setup(items, { read: async (item, start, frames, signal) => {
    if (item.mediaId === 'one') { oldSignal = signal; return new Promise<ArrayBuffer>(done => { resolve = done; }); }
    const data = new ArrayBuffer(frames * 8); new Float32Array(data).fill(.2); return data;
  } });
  try {
    engine.load(items, 0, 0); await drain(); engine.load(items, 1, 2); assert.ok(oldSignal.aborted);
    resolve(new ArrayBuffer(6 * 48000 * 8)); await drain(); tick(); await drain();
    assert.ok(Context.latest.sources.length > 0); assert.ok(Context.latest.sources.every(source => Math.abs(source.buffer.channels[0][0] - .2) < 1e-6));
    assert.equal(Context.latest.sources.reduce((sum, source) => sum + source.buffer.duration, 0), 6);
  } finally { engine.dispose(); }
});
test('repeat-one remains bounded and pause keeps the same audio-clock schedule', async () => {
  const items = [media('one', 2)]; const { engine, states } = setup(items);
  try {
    engine.setOrder(false, 'one'); engine.load(items, 0, 0); await drain();
    const context = Context.latest; assert.ok(context.sources.length >= 2 && context.sources.length <= 13);
    context.currentTime = .5; tick(); const when = context.sources.map(source => source.when);
    engine.toggle(); await drain(); assert.equal(context.state, 'suspended'); assert.equal(states.at(-1)!.playing, false);
    engine.toggle(); await drain(); assert.equal(context.state, 'running'); assert.deepEqual(context.sources.slice(0, when.length).map(source => source.when), when); assert.ok(context.sources.every(source => !source.stopped));
  } finally { engine.dispose(); }
});
test('ended music can restart, and malformed PCM is rejected before scheduling', async () => {
  const items = [media('one', .5)]; const { engine, states } = setup(items);
  try {
    engine.load(items, 0, 0); await drain(); Context.latest.currentTime = .2; tick(); Context.latest.currentTime = .7; tick();
    assert.equal(states.at(-1)!.position, .5); assert.equal(states.at(-1)!.playing, false);
    engine.toggle(); await drain(); assert.equal(Context.latest.sources.length, 2); assert.equal(Context.latest.sources[1].buffer.duration, .5);
  } finally { engine.dispose(); }
  const broken = setup(items, { read: async () => new ArrayBuffer(4) });
  try { broken.engine.load(items, 0, 0); await drain(); assert.match(broken.states.at(-1)!.error, /eksik/); assert.equal(Context.latest.sources.length, 0); }
  finally { broken.engine.dispose(); }
});
test('ReplayGain honors album gain, peak headroom, measured fallback and off', () => {
  const song = { ...info('one', 1), trackGainDb: -6, albumGainDb: -3, trackPeak: .8, albumPeak: 1.2, loudnessLufs: -20 };
  assert.ok(Math.abs(replayGain(song, { replayGain: 'track', preampDb: 0, preventClipping: true }) - .5011872336) < 1e-9);
  assert.ok(Math.abs(replayGain(song, { replayGain: 'album', preampDb: 10, preventClipping: true }) - 1 / 1.2) < 1e-9);
  assert.equal(replayGain(song, { replayGain: 'off', preampDb: 10, preventClipping: true }), 1);
  assert.equal(replayGain({ ...song, trackGainDb: null, trackPeak: null }, { replayGain: 'track', preampDb: 0, preventClipping: true }), 1);
});
test('PCM never prepares before admission and a revoked lease stops scheduled buffers', async () => {
  const items = [media('one', 30)]; let admit!: () => void; let prepared = 0; let closed = 0;
  const { engine, states } = setup(items, {
    begin: async () => new Promise<void>(resolve => { admit = resolve; }),
    prepare: async id => { prepared++; return info(id, 30); },
    heartbeat: async () => { throw Object.assign(new Error('Oynatma kapatıldı.'), { status: 410 }); },
    close: () => { closed++; },
  });
  try {
    engine.load(items, 0, 0); await drain(); assert.equal(prepared, 0); assert.equal(Context.latest.sources.length, 0);
    admit(); await drain(); assert.ok(Context.latest.sources.length > 0);
    engine.heartbeat(); await drain();
    assert.equal(states.at(-1)!.playing, false); assert.match(states.at(-1)!.error, /kapatıldı/);
    assert.ok(Context.latest.sources.every(source => source.stopped)); assert.equal(Context.latest.state, 'suspended'); assert.ok(closed >= 1);
  } finally { engine.dispose(); }
});
test('an old heartbeat cannot stop a new seek and disposal closes the transport', async () => {
  const items = [media('one', 40)]; let reject!: (error: Error) => void; let closed = 0;
  const { engine, states } = setup(items, { heartbeat: async () => new Promise<void>((_, fail) => { reject = fail; }), close: () => { closed++; } });
  engine.load(items, 0, 0); await drain(); engine.heartbeat(); await drain();
  engine.seek(20); await drain(); reject(Object.assign(new Error('Eski oturum kapatıldı.'), { status: 410 })); await drain();
  assert.equal(states.at(-1)!.error, ''); assert.ok(Context.latest.sources.some(source => !source.stopped));
  engine.dispose(); assert.equal(closed, 1);
});
test('adding the next track keeps the exact current sample clock and existing lease', async () => {
  const items=[media('one',8),media('two',4)];let admitted=0;const {engine,states}=setup(items,{begin:async()=>{admitted++;}});
  try{
    engine.load(items.slice(0,1),0,0);await drain();const context=Context.latest;context.currentTime=2;tick();const before=[...context.sources],position=states.at(-1)!.position;
    assert.equal(engine.appendQueue(items),true);await drain();assert.equal(admitted,1);assert.ok(before.every(source=>!source.stopped));assert.equal(states.at(-1)!.position,position);
    const last=before.at(-1)!;assert.equal(context.sources[before.length].when,last.when+last.buffer.duration);context.currentTime=8.5;tick();assert.equal(states.at(-1)!.index,1);
    assert.equal(engine.appendQueue([items[1],items[0]]),false);assert.equal(admitted,1);
  }finally{engine.dispose();}
});
test('appending while paused does not resume playback or spend another lease', async () => {
  const items=[media('one',8),media('two',4)];let admitted=0;const {engine}=setup(items,{begin:async()=>{admitted++;}});
  try{engine.load(items.slice(0,1),0,0);await drain();Context.latest.currentTime=1;tick();engine.toggle();await drain();const count=Context.latest.sources.length;engine.appendQueue(items);await drain();assert.equal(Context.latest.state,'suspended');assert.equal(Context.latest.sources.length,count);assert.equal(admitted,1);engine.toggle();await drain();assert.equal(admitted,1);assert.ok(Context.latest.sources.length>count);}finally{engine.dispose();}
});
test('appending after the queue ended waits for Play and opens a fresh lease for the new track',async()=>{
  const items=[media('one',1),media('two',2)];const opened:string[]=[];const {engine,states}=setup(items,{begin:async id=>{opened.push(id);}});
  try{engine.load(items.slice(0,1),0,0);await drain();Context.latest.currentTime=.3;tick();Context.latest.currentTime=1.3;tick();assert.equal(states.at(-1)!.playing,false);engine.appendQueue(items);await drain();assert.deepEqual(opened,['one']);engine.toggle();await drain();assert.deepEqual(opened,['one','two']);}finally{engine.dispose();}
});
