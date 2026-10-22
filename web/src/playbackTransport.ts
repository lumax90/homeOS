export type PlaybackTransport = 'direct' | 'hls.js' | 'native-hls' | 'unsupported';

/** A non-empty canPlayType result is only a hint, not verified native HLS support. */
export function selectPlaybackTransport(mode: string, mseHls: boolean, nativeHls: string): PlaybackTransport {
  if (mode === 'direct') return 'direct';
  if (mseHls) return 'hls.js';
  if (nativeHls) return 'native-hls';
  return 'unsupported';
}
