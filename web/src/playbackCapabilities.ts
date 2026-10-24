import type { MediaStreamInfo, MediaTracks } from './api.ts';

export interface FormatSupport { container: string; transport: 'direct' | 'hls.js' | 'native-hls'; original: boolean; videoCopy: boolean; audioCopy: boolean; compatible: boolean }
export interface CapabilityEnvironment {
  canPlayType(mime: string): string;
  mseSupported: boolean;
  isTypeSupported(mime: string): boolean;
  decodingInfo?: (configuration: MediaDecodingConfiguration) => Promise<Pick<MediaCapabilitiesDecodingInfo, 'supported'>>;
}
export interface PlaybackProfileOptions { audioStream: number | null; maxHeight: number | null; maxBitrate: number | null }
const fileMime: Record<string, string> = { mp4: 'video/mp4', m4v: 'video/mp4', mov: 'video/mp4', mkv: 'video/x-matroska', webm: 'video/webm', avi: 'video/x-msvideo',
  mp3: 'audio/mpeg', m4a: 'audio/mp4', flac: 'audio/flac', ogg: 'audio/ogg', opus: 'audio/ogg', wav: 'audio/wav', ts: 'video/mp2t' };
const audioIds: Record<string, string> = { aac: 'mp4a.40.2', mp3: 'mp4a.6B', ac3: 'ac-3', eac3: 'ec-3', opus: 'opus', flac: 'flac', vorbis: 'vorbis' };
const muxVideo = ['h264', 'hevc', 'vp9', 'av1'], muxAudio = ['aac', 'mp3', 'ac3', 'eac3'];

function identifier(stream: MediaStreamInfo | undefined): string | undefined {
  if (!stream) return undefined;
  if (stream.codecString && /^[a-zA-Z0-9.\-]{1,96}$/.test(stream.codecString)) return stream.codecString;
  if (stream.dolbyVision) return undefined;
  if (stream.kind === 'audio') {
    if (stream.codec === 'aac' && stream.profile && stream.profile !== 'LC') return stream.profile === 'HE-AAC' ? 'mp4a.40.5' : stream.profile === 'HE-AACv2' ? 'mp4a.40.29' : undefined;
    return audioIds[stream.codec];
  }
  if (stream.codec === 'h264' && stream.bitDepth !== 10) return 'avc1.64002A';
  if (stream.codec === 'vp9') return stream.bitDepth === 10 ? 'vp09.02.51.10' : 'vp09.00.51.08';
  return undefined;
}

/** Probe the actual source/selected tracks per delivery path. No user-agent or TV-brand list. */
export async function playbackCapabilities(container: string, tracks: MediaTracks, options: PlaybackProfileOptions, environment: CapabilityEnvironment, signal?: AbortSignal) {
  const video = tracks.streams.find(stream => stream.kind === 'video');
  const audios = tracks.streams.filter(stream => stream.kind === 'audio');
  const audio = options.audioStream === null ? audios.find(stream => stream.isDefault) ?? audios[0] : audios.find(stream => stream.index === options.audioStream);
  const videoId = identifier(video), audioId = identifier(audio);
  const formats: FormatSupport[] = [];
  const native = !!environment.canPlayType('application/vnd.apple.mpegurl');
  const pending = new Map<string, Promise<boolean>>();
  const supported = (format: string, transport: FormatSupport['transport'], copyVideo: boolean, copyAudio: boolean): Promise<boolean> => {
    if (signal?.aborted) return Promise.reject(signal.reason ?? new DOMException('Playback cancelled', 'AbortError'));
    const v = video ? copyVideo ? videoId : 'avc1.64002A' : undefined;
    const a = audio ? copyAudio ? audioId : 'mp4a.40.2' : undefined;
    const rawAudio = !video && transport === 'direct' && ['mp3', 'flac', 'ogg', 'opus', 'wav'].includes(format);
    if (video && !v || audio && !a && !rawAudio) return Promise.resolve(false);
    if (transport !== 'direct') {
      if (copyVideo && video?.dolbyVision) return Promise.resolve(false); // This muxer path has not verified dynamic-metadata preservation.
      if (video && copyVideo && !muxVideo.includes(video.codec) || audio && copyAudio && !muxAudio.includes(audio.codec)) return Promise.resolve(false);
      // hls.js transmuxes supported TS streams into MP4; HEVC/VP9/AV1 use the fMP4 path.
      if (format === 'ts' && video && copyVideo && video.codec !== 'h264' && (transport !== 'native-hls' || video.codec !== 'hevc')) return Promise.resolve(false);
    }
    const base = transport === 'direct' ? fileMime[format] : video ? 'video/mp4' : 'audio/mp4';
    if (!base) return Promise.resolve(false);
    const codes = [v, a].filter(Boolean);
    const mime = rawAudio ? base : `${base}; codecs="${codes.join(', ')}"`;
    let hint = false;
    try {
      hint = transport === 'hls.js' ? environment.mseSupported && environment.isTypeSupported(mime)
        : !!environment.canPlayType(mime) && (transport !== 'native-hls' || native && !!environment.canPlayType(`application/vnd.apple.mpegurl; codecs="${codes.join(', ')}"`));
    } catch { return Promise.resolve(false); }
    if (!hint && transport !== 'direct' || !environment.decodingInfo || transport === 'direct' && !['mp4', 'm4v', 'mov', 'webm', 'm4a'].includes(format)) return Promise.resolve(hint);
    const height = copyVideo ? video?.height ?? 1080 : Math.min(video?.height ?? 1080, options.maxHeight ?? 1080);
    const ratio = video?.width && video.height ? video.width / video.height : 16 / 9;
    const configuration: MediaDecodingConfiguration = {
      type: transport === 'hls.js' ? 'media-source' : 'file',
      ...(video && { video: { contentType: `${transport === 'direct' ? base : 'video/mp4'}; codecs="${v}"`, width: Math.max(2, Math.round(height * ratio / 2) * 2), height,
        bitrate: copyVideo ? video.bitRate ?? tracks.bitRate ?? 8_000_000 : options.maxBitrate ?? 8_000_000,
        framerate: copyVideo ? video.frameRate ?? 30 : Math.min(video.frameRate ?? 30, 60),
        ...(copyVideo && video.colorTransfer === 'smpte2084' ? { transferFunction: 'pq' as const } : copyVideo && video.colorTransfer === 'arib-std-b67' ? { transferFunction: 'hlg' as const } : {}) } }),
      ...(audio && { audio: { contentType: `${format === 'webm' && transport === 'direct' ? 'audio/webm' : 'audio/mp4'}; codecs="${a}"`, channels: String(copyAudio ? audio.channels ?? 2 : 2),
        samplerate: copyAudio ? audio.sampleRate ?? 48000 : 48000, bitrate: copyAudio ? audio.bitRate ?? 192000 : 192000 } }),
    };
    const key = JSON.stringify(configuration);
    if (!pending.has(key)) pending.set(key, new Promise<boolean>(resolve => {
      const timer = setTimeout(() => resolve(hint), 1500);
      // Older browsers may reject newer configuration fields; retain their format-specific hint.
      Promise.resolve().then(() => environment.decodingInfo!(configuration)).then(result => resolve(result.supported), () => resolve(hint)).finally(() => clearTimeout(timer));
    }));
    return pending.get(key)!;
  };
  if (fileMime[container]) formats.push({ container, transport: 'direct', original: await supported(container, 'direct', true, true), videoCopy: false, audioCopy: false, compatible: false });
  await Promise.all((['hls.js', 'native-hls'] as const).map(async transport => {
    if (transport === 'hls.js' && !environment.mseSupported || transport === 'native-hls' && !native) return;
    for (const format of ['mp4', 'ts']) {
      const [original, videoCopy, audioCopy, compatible] = await Promise.all([
        supported(format, transport, true, true), supported(format, transport, true, false), supported(format, transport, false, true), supported(format, transport, false, false),
      ]);
      formats.push({ container: format, transport, original, videoCopy, audioCopy, compatible });
    }
  }));
  if (signal?.aborted) throw signal.reason ?? new DOMException('Playback cancelled', 'AbortError');
  return {
    containers: formats.filter(format => format.transport === 'direct' && format.original).map(format => format.container),
    videoCodecs: [...new Set([...(video && formats.some(format => format.original || format.videoCopy) ? [video.codec] : []), ...(formats.some(format => format.compatible || format.audioCopy) ? ['h264'] : [])])],
    audioCodecs: [...new Set([...(audio && formats.some(format => format.original || format.audioCopy) ? [audio.codec] : []), ...(formats.some(format => format.compatible || format.videoCopy) ? ['aac'] : [])])],
    hls: formats.some(format => format.transport !== 'direct' && (format.original || format.videoCopy || format.audioCopy || format.compatible)),
    maxAudioChannels: Math.max(2, Math.min(16, audio?.channels ?? 2)),
    capabilities: { fingerprint: tracks.fingerprint, videoStream: video?.index ?? null, audioStream: audio?.index ?? null, formats },
  };
}

export function browserCapabilityEnvironment(video: HTMLVideoElement, mseSupported: boolean): CapabilityEnvironment {
  return { canPlayType: mime => video.canPlayType(mime), mseSupported,
    isTypeSupported: mime => typeof MediaSource !== 'undefined' && MediaSource.isTypeSupported(mime),
    decodingInfo: navigator.mediaCapabilities?.decodingInfo?.bind(navigator.mediaCapabilities) };
}
