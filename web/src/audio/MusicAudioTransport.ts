import { ApiError, appUrl, mutate, request, type MusicPcmInfo, type PlaybackSession } from '../api';
import type { MusicAudioTransport } from './GaplessEngine';

export function createMusicAudioTransport(clientId: string, nextRevision: () => number): MusicAudioTransport {
  let playbackId: string | null = null; let revision = 0; let closed = false;
  const lease = () => { if (!playbackId || closed) throw new ApiError('Müzik oynatma oturumu yenilenmeli.', 410); return playbackId; };
  const prepare = (id: string, signal: AbortSignal) => request<MusicPcmInfo>(`/music/media/${id}/prepare?playbackId=${lease()}`, { method: 'POST', signal });
  async function read(info: MusicPcmInfo, start: number, frames: number, signal: AbortSignal, retry = true): Promise<ArrayBuffer> {
    const id = lease();
    const response = await fetch(appUrl(`/api/music/media/${info.mediaId}/pcm?${new URLSearchParams({ playbackId: id, fingerprint: info.fingerprint, start: String(start), frames: String(frames) })}`), { credentials: 'same-origin', signal });
    if (response.status === 410 && retry) {
      // Renew only a cached descriptor within the same lease. A revoked lease cannot be recreated by a GET retry.
      const renewed = await prepare(info.mediaId, signal);
      if (renewed.fingerprint !== info.fingerprint || renewed.frames !== info.frames) throw new Error('Ses dosyası değişti. Parçayı yeniden aç.');
      return read(renewed, start, frames, signal, false);
    }
    if (!response.ok) {
      if (response.status === 401) window.dispatchEvent(new Event('home-session-expired'));
      const error = await response.json().catch(() => null); throw new ApiError(error?.message || 'Ses aralığı sunucudan alınamadı.', response.status);
    }
    return response.arrayBuffer();
  }
  return {
    async begin(mediaId, signal) {
      closed = false;
      const previous = playbackId; playbackId = null;
      const current = revision = nextRevision();
      try {
        const result = await request<PlaybackSession>('/music/playback', { method: 'POST', signal, body: JSON.stringify({ mediaId, clientId, clientRevision: current }) });
        if (closed || current !== revision || signal.aborted) {
          if (result.id) void mutate(`/playback/${result.id}`, undefined, 'DELETE', true).catch(() => {});
          throw new DOMException('Eski müzik komutu iptal edildi.', 'AbortError');
        }
        playbackId = result.id;
      } catch (error) { if (previous) void mutate(`/playback/${previous}`, undefined, 'DELETE', true).catch(() => {}); throw error; }
    },
    prepare, read,
    async heartbeat(mediaId, positionSeconds, paused, signal) {
      const id = playbackId; if (!id || closed) return;
      await request(`/playback/${id}/heartbeat`, { method: 'POST', signal, body: JSON.stringify({ mediaId, positionSeconds, paused }) });
    },
    close() {
      if (closed) return; closed = true; playbackId = null;
      if (revision) void mutate(`/playback/clients/${clientId}/close`, { revision }, 'POST', true).catch(() => {});
    }
  };
}
