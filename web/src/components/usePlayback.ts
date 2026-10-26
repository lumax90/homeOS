import { useCallback, useEffect, useRef, useState } from 'react';
import type Hls from 'hls.js';
import workerUrl from 'hls.js/dist/hls.worker.js?url';
import { ApiError, mutate, request, type MediaItem, type MediaTracks, type PlaybackSession } from '../api';
import { appUrl } from '../base';
import { browserPlayback } from '../playbackIdentity';
import { useSession } from './SessionContext';
import type { WatchPlayer } from '../watch/types';
import { selectPlaybackTransport } from '../playbackTransport';
import { browserCapabilityEnvironment, playbackCapabilities } from '../playbackCapabilities';
import { movieHlsConfig } from '../movieHlsConfig';

export interface PlaybackOptions { audioStream: number | null; maxHeight: number | null; maxBitrate: number | null; forceCompatible: boolean; forceRemux: boolean; subtitleId: string | null; burnSubtitle: boolean; subtitleDelaySeconds: number }
const defaultOptions: PlaybackOptions = { audioStream: null, maxHeight: null, maxBitrate: null, forceCompatible: false, forceRemux: false, subtitleId: null, burnSubtitle: false, subtitleDelaySeconds: 0 };

function clientCapabilities(video: HTMLVideoElement, hls: typeof Hls) {
  const codecs = (values: Record<string, string>) => Object.entries(values).filter(([, mime]) => video.canPlayType(mime)).map(([codec]) => codec);
  return {
    containers: codecs({ mp4: 'video/mp4', m4v: 'video/mp4', webm: 'video/webm', mkv: 'video/x-matroska', mov: 'video/quicktime', avi: 'video/x-msvideo',
      mp3: 'audio/mpeg', m4a: 'audio/mp4', flac: 'audio/flac', ogg: 'audio/ogg', opus: 'audio/ogg', wav: 'audio/wav' }),
    videoCodecs: codecs({ h264: 'video/mp4; codecs="avc1.42E01E"', hevc: 'video/mp4; codecs="hvc1.1.6.L93.B0"', vp9: 'video/webm; codecs="vp09.00.10.08"', av1: 'video/mp4; codecs="av01.0.04M.08"' }),
    audioCodecs: codecs({ aac: 'audio/mp4; codecs="mp4a.40.2"', mp3: 'audio/mpeg', opus: 'audio/ogg; codecs="opus"', vorbis: 'audio/ogg; codecs="vorbis"', flac: 'audio/flac', pcm_s16le: 'audio/wav', ac3: 'audio/mp4; codecs="ac-3"', eac3: 'audio/mp4; codecs="ec-3"' }),
    hls: !!video.canPlayType('application/vnd.apple.mpegurl') || hls.isSupported(), maxAudioChannels: 2,
  };
}

export function usePlayback(item: MediaItem, playbackKey = 0, coordination: { managed: boolean; position?: number; binding?: { roomId: string; memberId: string; clientRevision: number; epoch: number } } = { managed: false }) {
  const account = useSession();
  const owner = useRef(account.userId);
  const login = useRef(account.sessionId);
  const coordinated = useRef(coordination); coordinated.current = coordination;
  const armed = useRef(false);
  const video = useRef<HTMLVideoElement>(null);
  const sessionRef = useRef<PlaybackSession | null>(null);
  const optionsRef = useRef<PlaybackOptions>(defaultOptions);
  const hlsRef = useRef<Hls | null>(null);
  const pending = useRef<AbortController | null>(null);
  const generation = useRef(0);
  const revision = useRef(0);
  const clientId = useRef(browserPlayback.clientId);
  const alive = useRef(true);
  const loaded = useRef(false);
  const attaching = useRef(false);
  const autoPlay = useRef(true);
  const recovered = useRef(false);
  const lastReconnect = useRef(0);
  const pulseNow = useRef(() => {});
  const positionRef = useRef(item.isWatched ? 0 : item.positionSeconds);
  const lengthRef = useRef(item.durationSeconds ?? 0);
  const lastSaved = useRef(0);
  const [session, setSession] = useState<PlaybackSession | null>(null);
  const [options, setOptions] = useState(defaultOptions);
  const [position, setPosition] = useState(positionRef.current);
  const [length, setLength] = useState(lengthRef.current);
  const [playing, setPlaying] = useState(false);
  const [busy, setBusy] = useState(true);
  const [error, setError] = useState('');
  const [notice, setNotice] = useState('');
  const [saveError, setSaveError] = useState(false);

  const save = useCallback((keepalive = false) => {
    if (!loaded.current) return;
    lastSaved.current = Date.now();
    void mutate(`/media/${item.id}/progress`, { positionSeconds: positionRef.current, durationSeconds: lengthRef.current || null,
      clientId: clientId.current, sequence: browserPlayback.nextProgress(), expectedUserId: owner.current, expectedMatchRevision: item.matchRevision }, 'PUT', keepalive)
      .then(() => { if (alive.current) setSaveError(false); }).catch(() => { if (alive.current) setSaveError(true); });
  }, [item.id, item.matchRevision]);

  const start = useCallback(async (seconds: number, changes: Partial<PlaybackOptions> = {}, resume = loaded.current ? !(video.current?.paused ?? true) : autoPlay.current) => {
    const element = video.current;
    if (!element) return;
    const watch = coordinated.current.binding;
    const epoch = ++generation.current;
    const command = revision.current = browserPlayback.nextRevision();
    pending.current?.abort();
    const controller = new AbortController();
    pending.current = controller;
    save();
    loaded.current = false; attaching.current = false;
    element.pause();
    const next = { ...optionsRef.current, ...changes };
    optionsRef.current = next; setOptions(next);
    setBusy(true); setError(''); setNotice('');
    autoPlay.current = resume;
    try {
      const { default: HlsPlayer } = await import('hls.js');
      if (controller.signal.aborted) return;
      const tracks = await request<MediaTracks>(`/media/${item.id}/tracks?allowUnprobed=true`, { signal: controller.signal });
      const profile = tracks.streams.length ? await playbackCapabilities(item.relativePath.split('.').at(-1)?.toLowerCase() ?? '', tracks, next,
        browserCapabilityEnvironment(element, HlsPlayer.isSupported()), controller.signal) : clientCapabilities(element, HlsPlayer);
      const plan = await request<PlaybackSession>('/playback', { method: 'POST', signal: controller.signal,
        body: JSON.stringify({ mediaId: item.id, startSeconds: seconds, client: profile,
          replaceSessionId: sessionRef.current?.id ?? null, clientId: clientId.current, clientRevision: command, expectedUserId: owner.current, expectedSessionId: login.current, watch: watch ?? null, ...next }) });
      if (controller.signal.aborted || !alive.current || epoch !== generation.current) {
        if (plan.id) void mutate(`/playback/${plan.id}`, undefined, 'DELETE', true).catch(() => {});
        return;
      }
      hlsRef.current?.destroy(); hlsRef.current = null;
      sessionRef.current = plan; setSession(plan);
      lengthRef.current = plan.durationSeconds || lengthRef.current; setLength(lengthRef.current);
      positionRef.current = seconds; setPosition(seconds);
      attaching.current = true; recovered.current = false;
      const transport = selectPlaybackTransport(plan.mode, HlsPlayer.isSupported(), element.canPlayType('application/vnd.apple.mpegurl'), plan.transport);
      if (transport === 'direct' || transport === 'native-hls') {
        element.src = appUrl(plan.url); element.load();
      } else if (transport === 'hls.js') {
        const hls = new HlsPlayer(movieHlsConfig(plan.requestedStartSeconds - plan.startSeconds,appUrl(workerUrl)));
        hlsRef.current = hls;
        hls.on(HlsPlayer.Events.ERROR, (_event, data) => {
          if (!data.fatal || !alive.current || epoch !== generation.current) return;
          if (data.type === HlsPlayer.ErrorTypes.MEDIA_ERROR && !recovered.current) {
            recovered.current = true; hls.recoverMediaError(); return;
          }
          if (data.type === HlsPlayer.ErrorTypes.NETWORK_ERROR && data.response?.code === 410 && Date.now() - lastReconnect.current > 30_000) {
            lastReconnect.current = Date.now(); void start(positionRef.current, {}, autoPlay.current); return;
          }
          if (data.type === HlsPlayer.ErrorTypes.MEDIA_ERROR && !next.forceCompatible) {
            void start(positionRef.current, { forceCompatible: true }, autoPlay.current); return;
          }
          setBusy(false);
          setError('Oynatma akışı kesildi. Kaldığın yerden yeniden deneyebilirsin.');
          if (plan.id) void request<PlaybackSession>(`/playback/${plan.id}`).then(state => { if (state.error && alive.current && epoch === generation.current) setError(state.error); }).catch(() => {});
        });
        hls.loadSource(appUrl(plan.url)); hls.attachMedia(element);
      } else throw new Error('Bu cihaz HLS oynatamıyor.');
    } catch (err) {
      if (!controller.signal.aborted && alive.current && epoch === generation.current) {
        setBusy(false); setError((err as Error).message);
      }
    } finally { if (pending.current === controller) pending.current = null; }
  }, [item.id, item.relativePath, save]);

  useEffect(() => {
    alive.current = true;
    const element = video.current;
    loaded.current = false;
    positionRef.current = coordinated.current.managed ? coordinated.current.position ?? 0 : item.isWatched ? 0 : item.positionSeconds;
    lengthRef.current = item.durationSeconds ?? 0;
    setPosition(positionRef.current); setLength(lengthRef.current);
    optionsRef.current = { ...optionsRef.current, audioStream: null, forceCompatible: false, forceRemux: false, subtitleId: null, burnSubtitle: false, subtitleDelaySeconds: 0 };
    void start(positionRef.current, {}, !coordinated.current.managed);
    const pulse = () => {
      const id = sessionRef.current?.id;
      const epoch = generation.current;
      const current = () => alive.current && !pending.current && epoch === generation.current && sessionRef.current?.id === id;
      const stop = (message: string) => {
        if (!current()) return;
        save(); loaded.current = false; attaching.current = false;
        video.current?.pause(); hlsRef.current?.destroy(); hlsRef.current = null;
        video.current?.removeAttribute('src'); video.current?.load();
        setBusy(false); setPlaying(false); setError(message);
      };
      if (id && !pending.current) void mutate<PlaybackSession>(`/playback/${id}/heartbeat`, { positionSeconds: positionRef.current, paused: video.current?.paused ?? true })
        .then(state => { if (state.error) stop(state.error); else if (current()) { sessionRef.current = state; setSession(state); } })
        .catch(error => {
          if (!current()) return;
          if (error instanceof ApiError && [401, 403, 404, 409, 410].includes(error.status)) stop(error.message);
          else setNotice('Sunucu bağlantısı bekleniyor.');
        });
    };
    const visibility = () => { if (document.hidden) save(true); else pulse(); };
    document.addEventListener('visibilitychange', visibility); window.addEventListener('home-playback-changed', pulse);
    pulseNow.current = pulse;
    const heartbeat = window.setInterval(pulse, 5_000);
    return () => {
      const closingRevision = revision.current;
      save(true); alive.current = false; ++generation.current; pending.current?.abort();
      pulseNow.current = () => {}; window.clearInterval(heartbeat); document.removeEventListener('visibilitychange', visibility); window.removeEventListener('home-playback-changed', pulse);
      hlsRef.current?.destroy(); hlsRef.current = null;
      element?.pause(); element?.removeAttribute('src'); element?.load();
      void mutate(`/playback/clients/${clientId.current}/close`, { revision: closingRevision }, 'POST', true).catch(() => {});
    };
  }, [start, save, playbackKey]);

  function metadataLoaded() {
    const element = video.current, plan = sessionRef.current;
    if (!element || !plan || !attaching.current) return;
    if (!lengthRef.current && Number.isFinite(element.duration)) { lengthRef.current = element.duration; setLength(element.duration); }
    if (!loaded.current) {
      const relative = Math.max(0, plan.requestedStartSeconds - plan.startSeconds);
      element.currentTime = relative;
      loaded.current = true; setBusy(false);
      if (autoPlay.current) void element.play().catch(() => { armed.current = false; if (alive.current) setNotice('Başlamak için oynat düğmesine dokun.'); });
    }
  }
  function timeUpdated() {
    if (!loaded.current || !video.current) return;
    const current = (sessionRef.current?.startSeconds ?? 0) + video.current.currentTime;
    positionRef.current = current; setPosition(current);
    if (Date.now() - lastSaved.current > 10_000) save();
  }
  function seek(seconds: number) {
    if (!video.current || !Number.isFinite(seconds)) return;
    const desired = Math.max(0, Math.min(seconds, (lengthRef.current || seconds + .01) - .01));
    const relative = desired - (sessionRef.current?.startSeconds ?? 0);
    const ranges = video.current.buffered;
    const inRange = sessionRef.current?.mode === 'direct' || desired >= (sessionRef.current?.telemetry?.windowStartSeconds ?? 0)
      && Array.from({ length: ranges.length }, (_, i) => i).some(i => relative >= ranges.start(i) && relative <= ranges.end(i));
    if (loaded.current && relative >= 0 && inRange) {
      video.current.currentTime = relative; positionRef.current = desired; setPosition(desired); save();
    } else void start(desired);
  }
  function toggle() {
    const element = video.current;
    if (!element) return;
    if (element.ended) { void start(0, {}, true); return; }
    if (error) { void start(positionRef.current, {}, true); return; }
    if (element.paused) { autoPlay.current = true; void element.play().catch(() => setNotice('Oynatma başlatılamadı. Yeniden dene.')); }
    else { autoPlay.current = false; element.pause(); }
  }
  function mediaError() {
    if (!attaching.current) return;
    // hls.js owns its media error recovery. A native event must not race that
    // bounded recovery and leave a recovered player displaying a stale error.
    if (hlsRef.current) return;
    if (video.current?.error?.code === 2) { setBusy(false); setError('Oynatma akışı kesildi. Kaldığın yerden yeniden deneyebilirsin.'); return; }
    if (sessionRef.current?.mode === 'direct' && !optionsRef.current.forceRemux && !optionsRef.current.forceCompatible) {
      void start(positionRef.current, { forceRemux: true }, !coordinated.current.managed);
    } else if (!optionsRef.current.forceCompatible) {
      void start(positionRef.current, { forceCompatible: true }, !coordinated.current.managed);
    } else { setBusy(false); setError('Seçilen akış cihazda çözülemedi. Farklı ses/kalite seçip yeniden dene.'); }
  }
  const shared: WatchPlayer = {
    read: () => ({ mediaId: item.id, playbackId: sessionRef.current?.id ?? null, position: positionRef.current, playing: !(video.current?.paused ?? true),
      ready: loaded.current && !pending.current && !video.current?.seeking && (video.current?.readyState ?? 0) >= 3, armed: armed.current,
      buffering: !!pending.current || !loaded.current || (video.current?.readyState ?? 0) < 3, error: !!error,
      preparing: !!pending.current || attaching.current && !loaded.current && !error,
      rate: video.current?.defaultPlaybackRate ?? 1, volume: video.current?.volume ?? 1, muted: video.current?.muted ?? false }),
    pause: () => { autoPlay.current = false; video.current?.pause(); },
    play: async () => {
      if (!video.current || !loaded.current) return false;
      try { autoPlay.current = true; await video.current.play(); armed.current = true; return true; }
      catch (error) { if ((error as Error).name !== 'AbortError') armed.current = false; if (alive.current) setNotice('Bu cihazda başlamak için oynatmaya izin ver.'); return false; }
    },
    seek,
    speed: rate => { if (video.current && Math.abs(video.current.playbackRate - rate) > .001) video.current.playbackRate = rate; },
    volume: (volume, muted) => { if (video.current) { video.current.volume = volume; video.current.muted = muted; } },
    restart: seconds => start(seconds, {}, false),
    stop: message => {
      save(true); ++generation.current; pending.current?.abort(); loaded.current = false; attaching.current = false;
      video.current?.pause(); hlsRef.current?.destroy(); hlsRef.current = null; video.current?.removeAttribute('src'); video.current?.load();
      const id = sessionRef.current?.id; sessionRef.current = null; setSession(null); setPlaying(false); setBusy(false); setError(message);
      if (id) void mutate(`/playback/${id}`, undefined, 'DELETE', true).catch(() => {});
    },
  };
  return {
    video, session, options, position, length, playing, busy, error, notice, saveError, save, seek, toggle, setNotice,
    shared,
    requestPlay: async () => {
      autoPlay.current = true;
      if (pending.current) return; // A seek/quality replacement will play when its new source is ready.
      if (video.current?.ended) { await start(0, {}, true); return; }
      if (error || !loaded.current) { await start(positionRef.current, {}, true); return; }
      try { await video.current?.play(); armed.current = true; }
      catch { setNotice('Başlamak için oynat düğmesine dokun.'); }
    },
    requestPause: () => { autoPlay.current = false; video.current?.pause(); },
    timeline: () => ({ offset: sessionRef.current?.startSeconds ?? 0, position: positionRef.current,
      duration: lengthRef.current, ready: loaded.current, pending: !!pending.current }),
    arm: async () => { const success = await shared.play(); if (coordinated.current.managed) shared.pause(); return success; },
    retry: () => start(positionRef.current, {}, !coordinated.current.managed),
    restart: () => start(0, {}, !coordinated.current.managed),
    configure: (changes: Partial<PlaybackOptions>) => start(positionRef.current, changes),
    events: { onLoadedMetadata: metadataLoaded, onCanPlay: () => { metadataLoaded(); setBusy(false); }, onWaiting: () => { if (loaded.current) setBusy(true); }, onPlaying: () => setBusy(false), onTimeUpdate: timeUpdated, onError: mediaError,
      onPlay: () => { armed.current = true; autoPlay.current = true; setPlaying(true); setNotice(''); pulseNow.current(); }, onPause: () => { if (loaded.current) autoPlay.current = false; setPlaying(false); save(); pulseNow.current(); }, onEnded: () => {
        setPlaying(false); timeUpdated(); save(); if (coordinated.current.managed) return; const id = sessionRef.current?.id; sessionRef.current = null;
        if (id) void mutate(`/playback/${id}`, undefined, 'DELETE', true).catch(() => {});
      } },
  };
}
