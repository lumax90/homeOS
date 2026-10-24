import {test} from 'node:test';
import assert from 'node:assert/strict';
import {playbackCapabilities, type CapabilityEnvironment} from '../../web/src/playbackCapabilities.ts';
import {playbackBuffer} from '../../web/src/playbackDiagnostics.ts';
import {selectPlaybackTransport} from '../../web/src/playbackTransport.ts';
import type {MediaTracks} from '../../web/src/api.ts';

const tracks = { fingerprint: 'isolated-source', bitRate: 40_000_000, streams: [
  {index: 0, kind: 'video', codec: 'hevc', codecString: 'hvc1.2.4.L153.B0', width: 3840, height: 2160, frameRate: 23.976, bitDepth: 10, colorTransfer: 'smpte2084'},
  {index: 1, kind: 'audio', codec: 'dts', channels: 6, sampleRate: 48000, isDefault: true},
] } as MediaTracks;
const options = {audioStream: null, maxHeight: null, maxBitrate: null};
const environment: CapabilityEnvironment = { canPlayType: mime => mime.includes('matroska') ? '' : 'probably', mseSupported: true, isTypeSupported: () => true };

test('source-specific HEVC plus incompatible audio offers video-copy rather than a codec-wide fallback', async () => {
  const client = await playbackCapabilities('mkv', tracks, options, environment);
  assert.deepEqual([client.capabilities.videoStream, client.capabilities.audioStream], [0, 1]);
  const format = client.capabilities.formats.find(profile => profile.container === 'mp4' && profile.transport === 'hls.js')!;
  assert.equal(format.original, false); assert.equal(format.videoCopy, true); assert.equal(format.compatible, true);
  assert.equal(client.capabilities.formats.find(profile => profile.container === 'ts' && profile.transport === 'hls.js')!.videoCopy, false);
});
test('MSE-specific rejection cannot be overwritten by a generic file-format hint', async () => {
  const client = await playbackCapabilities('mkv', tracks, options, {...environment, isTypeSupported: mime => !mime.includes('hvc1')});
  assert.equal(client.capabilities.formats.find(profile => profile.container === 'mp4' && profile.transport === 'hls.js')!.videoCopy, false);
  assert.equal(client.capabilities.formats.find(profile => profile.container === 'mp4' && profile.transport === 'native-hls')!.videoCopy, true);
  assert.equal(selectPlaybackTransport('audio-transcode', true, 'probably', 'native-hls'), 'native-hls');
});
test('MediaCapabilities tests actual 4K Main10 rate and accepts efficient audio-only adaptation', async () => {
  const seen: MediaDecodingConfiguration[] = [];
  const client = await playbackCapabilities('mkv', tracks, options, {...environment, decodingInfo: async configuration => {seen.push(configuration); return {supported: true};}});
  assert.ok(seen.some(config => config.video?.width === 3840 && config.video.height === 2160 && config.video.framerate === 23.976 && config.audio?.channels === '2'));
  assert.ok(client.capabilities.formats.some(profile => profile.videoCopy));
});
test('explicit decoding rejection prevents preservation while a missing API retains exact MIME support', async () => {
  const client = await playbackCapabilities('mkv', tracks, options, {...environment, decodingInfo: async config => ({supported: !config.video?.contentType.includes('hvc1')})});
  assert.equal(client.capabilities.formats.some(profile => profile.videoCopy), false);
  assert.ok(client.capabilities.formats.some(profile => profile.compatible));
  const fallback = await playbackCapabilities('mkv', tracks, options, {...environment, decodingInfo: async () => {throw new TypeError('older browser');}});
  assert.ok(fallback.capabilities.formats.some(profile => profile.videoCopy));
});
test('source-supported multichannel audio remains eligible and the selected track is bound', async () => {
  const audio = {...tracks.streams[1], codec: 'aac', codecString: 'mp4a.40.2', index: 2, isDefault: false};
  const source = {...tracks, streams: [...tracks.streams, audio]};
  const client = await playbackCapabilities('mp4', source, {...options, audioStream: 2}, environment);
  assert.equal(client.capabilities.audioStream, 2); assert.equal(client.maxAudioChannels, 6);
  assert.equal(client.capabilities.formats.find(format => format.transport === 'direct')!.original, true);
});
test('aborted capability probes cannot produce a stale playback command', async () => {
  const controller = new AbortController(); controller.abort();
  await assert.rejects(playbackCapabilities('mp4', tracks, options, environment, controller.signal), {name: 'AbortError'});
});
test('buffer measurement excludes disconnected future ranges', () => {
  assert.equal(playbackBuffer({currentTime: 4, buffered: {length: 2, start: i => [0, 30][i], end: i => [10, 40][i]}}), 6);
  assert.equal(playbackBuffer({currentTime: 20, buffered: {length: 2, start: i => [0, 30][i], end: i => [10, 40][i]}}), 0);
});
