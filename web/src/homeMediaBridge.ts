import { REGISTERED_MEDIA } from '@videojs/media';

/** Video.js controls see the whole Home timeline, not only the current HLS window. */
export interface HomeMediaState {
  offset: number; position: number; duration: number; ready: boolean; pending: boolean; error: string;
  subtitle: string; audio: number | null; quality: string;
}
export interface HomeTrackOption { id: string; label: string; language: string; kind?: string }
export interface HomeMediaPort {
  read(): HomeMediaState;
  play(): Promise<void>; pause(): void; seek(seconds: number): void; retry(): void;
  rate(value: number): void; volume(value: number, muted: boolean): void;
  subtitle(id: string): void; audio(index: number): void; quality(value: string): void;
  audioOptions(): HomeTrackOption[]; subtitleOptions(): HomeTrackOption[];
  chapters(): { startTime: number; endTime: number; text: string }[];
}

class OptionsList<T> extends EventTarget {
  items: T[] = [];
  get length() { return this.items.length; }
  [Symbol.iterator]() { return this.items[Symbol.iterator](); }
  getTrackById(id: string) { return this.items.find(item => (item as { id: string }).id === id) ?? null; }
  getRenditionById(id: string) { return this.getTrackById(id); }
  constructor() {
    super();
    return new Proxy(this, { get: (target, key, receiver) => {
      if (typeof key === 'string' && /^\d+$/.test(key)) return target.items[Number(key)];
      const value = Reflect.get(target, key, receiver);
      return typeof value === 'function' ? value.bind(target) : value;
    } });
  }
}

function ranges(values: [number, number][]): TimeRanges {
  return { length: values.length, start: index => {
    if (!values[index]) throw new RangeError('Time range index'); return values[index][0];
  }, end: index => { if (!values[index]) throw new RangeError('Time range index'); return values[index][1]; } };
}

export function createHomeMediaBridge(video: HTMLVideoElement, port: HomeMediaPort) {
  const audioTracks = new OptionsList<{ id: string; kind: string; label: string; language: string; enabled: boolean }>();
  const textTracks = new OptionsList<{ id: string; kind: string; label: string; language: string; mode: TextTrackMode; cues: unknown; addCue: () => void }>();
  const renditionValues = ['1080', '720', '480'];
  const videoRenditions = new OptionsList<{ id: string; width: number; height: number; bitrate: number; selected: boolean; active: boolean }>();
  Object.defineProperty(videoRenditions, 'selectedIndex', {
    get: () => renditionValues.indexOf(port.read().quality),
    set: (index: number) => { if (index === -1) port.quality('original'); else if (renditionValues[index]) port.quality(renditionValues[index]); }
  });
  const notify = (target: EventTarget, type: string) => target.dispatchEvent(new Event(type));
  let audioKey = '', subtitleKey = '', previousReady = false, previousError = '';

  function refresh() {
    const audios = port.audioOptions(); const captions = port.subtitleOptions();
    const nextAudioKey = JSON.stringify(audios);
    if (nextAudioKey !== audioKey) {
      audioKey = nextAudioKey;
      audioTracks.items = audios.map(option => ({ ...option, kind: 'main', get enabled() { return port.read().audio === Number(option.id); },
        set enabled(value: boolean) { if (value) port.audio(Number(option.id)); } }));
      notify(audioTracks, 'addtrack');
    }
    const nextSubtitleKey = JSON.stringify(captions);
    if (nextSubtitleKey !== subtitleKey) {
      subtitleKey = nextSubtitleKey;
      textTracks.items = captions.map(option => ({ ...option, kind: 'subtitles', get mode(): TextTrackMode { return port.read().subtitle === option.id ? 'showing' : 'disabled'; },
        set mode(value: TextTrackMode) { if (value === 'showing') port.subtitle(option.id); else if (value === 'disabled' && port.read().subtitle === option.id) port.subtitle(''); },
        cues: null, addCue() {} }));
      // Chapters are already in absolute seconds in Home's verified track inventory.
      textTracks.items.push({ id: 'home-chapters', kind: 'chapters', label: 'Chapters', language: '', mode: 'hidden',
        get cues() { return port.chapters(); }, addCue() {} });
      notify(textTracks, 'addtrack');
    }
    if (!videoRenditions.length) videoRenditions.items = renditionValues.map(id => ({ id, width: Number(id) * 16 / 9,
      height: Number(id), bitrate: Number(id) >= 1080 ? 8_000_000 : Number(id) >= 720 ? 4_000_000 : 1_500_000,
      get selected() { return port.read().quality === id; }, set selected(value: boolean) { if (value) port.quality(id); },
      get active() { return port.read().quality === id; } }));
    notify(audioTracks, 'change'); notify(textTracks, 'change'); notify(videoRenditions, 'change');
    const state = port.read();
    notify(video, 'durationchange'); notify(video, 'progress');
    if (state.ready && !previousReady) notify(video, 'loadedmetadata');
    if (state.error && state.error !== previousError) notify(video, 'error');
    previousReady = state.ready; previousError = state.error;
  }
  const shifted = (source: TimeRanges) => {
    const state = port.read(); if (!state.ready) return ranges([]);
    return ranges(Array.from({ length: source.length }, (_, i) => [Math.max(0, source.start(i) + state.offset),
      Math.min(state.duration, source.end(i) + state.offset)] as [number, number]).filter(([start, end]) => end > start));
  };
  const media = new Proxy(video, {
    get(target, key) {
      if (key === REGISTERED_MEDIA) return target;
      const state = port.read();
      switch (key) {
        case 'currentTime': return state.ready ? Math.min(state.duration, Math.max(0, state.offset + target.currentTime)) : state.position;
        case 'duration': return state.duration;
        case 'seeking': return state.pending || target.seeking;
        case 'readyState': return state.ready ? target.readyState : 0;
        case 'seekable': return ranges(state.duration > 0 && state.ready ? [[0, state.duration]] : []);
        case 'buffered': return shifted(target.buffered);
        case 'played': return shifted(target.played);
        case 'audioTracks': return audioTracks;
        case 'textTracks': return textTracks;
        case 'videoRenditions': return videoRenditions;
        case 'error': return state.error ? { code: target.error?.code ?? 3, message: state.error } : null;
        case 'play': return () => port.play();
        case 'pause': return () => port.pause();
        case 'load': return () => port.retry();
      }
      const value = Reflect.get(target, key, target);
      return typeof value === 'function' && key !== 'constructor' ? value.bind(target) : value;
    },
    set(target, key, value) {
      switch (key) {
        case 'currentTime':
          if (Number.isFinite(value)) port.seek(Math.max(0, Math.min(value, port.read().duration)));
          // A denied room seek (or a no-op seek) creates no native seek event.
          // Release Video.js's optimistic seek using the authoritative time.
          if (!port.read().pending && !target.seeking) queueMicrotask(() => notify(target, 'seeked'));
          return true;
        case 'playbackRate': if (Number.isFinite(value) && value >= .2 && value <= 4) port.rate(value); return true;
        case 'volume': if (Number.isFinite(value)) port.volume(Math.max(0, Math.min(1, value)), target.muted); return true;
        case 'muted': port.volume(target.volume, !!value); return true;
      }
      return Reflect.set(target, key, value, target);
    },
    has(target, key) { return ['audioTracks', 'videoRenditions', 'textTracks'].includes(String(key)) || Reflect.has(target, key); }
  });
  refresh();
  return { media, refresh };
}
