import { chunkAad, readMetadata, unseal } from './crypto.ts';
import type { OfflineStore } from './store.ts';
import type { OfflineManifest } from './types.ts';

export function byteRange(value: string | null, size: number): { start: number; end: number; partial: boolean } | null {
  if (!Number.isSafeInteger(size) || size <= 0) return null;
  if (!value) return { start: 0, end: size - 1, partial: false };
  const match = /^bytes=(\d*)-(\d*)$/.exec(value); if (!match || !match[1] && !match[2]) return null;
  if (!match[1]) { const suffix = Number(match[2]); return Number.isSafeInteger(suffix) && suffix > 0 ? { start: Math.max(0, size - suffix), end: size - 1, partial: true } : null; }
  const start = Number(match[1]); const end = match[2] ? Number(match[2]) : size - 1;
  return Number.isSafeInteger(start) && Number.isSafeInteger(end) && start >= 0 && start < size && end >= start ? { start, end: Math.min(end, size - 1), partial: true } : null;
}
export function validateManifest(value: OfflineManifest): boolean {
  return value && value.assets?.length > 0 && value.assets.length <= 2 && Number.isFinite(value.durationSeconds) && value.durationSeconds > 0 && Number.isFinite(Date.parse(value.expiresAt))
    && new Set(value.assets.map(asset => asset.id)).size === value.assets.length
    && value.assets.every(asset => ['media', 'subtitle'].includes(asset.id) && asset.bytes > 0 && asset.bytes <= 32 * 1024 ** 3 && Number.isSafeInteger(asset.bytes)
      && asset.chunkBytes === 2 * 1024 * 1024 && asset.chunks === Math.ceil(asset.bytes / asset.chunkBytes) && /^[a-f0-9-]{3,100}$/.test(asset.fingerprint)
      && /^(?:video\/[a-z0-9.+-]+|audio\/[a-z0-9.+-]+|text\/vtt; charset=utf-8)$/.test(asset.contentType));
}
export async function offlineResponse(request: Request, store: OfflineStore, key: CryptoKey, vaultId: string, entryId: string, assetId: string, signal: AbortSignal, release: () => void): Promise<Response> {
  let handedOff = false;
  try {
    const row = await store.entry(entryId);
    if (!row || row.vaultId !== vaultId || row.state !== 'ready') return new Response('Cihaz kopyası hazır değil.', { status: 404 });
    const metadata = await readMetadata(key, vaultId, entryId, row.metadata); const manifest = metadata.manifest;
    if (!manifest || !validateManifest(manifest)) return new Response('Cihaz kaydı doğrulanamadı.', { status: 422 });
    if (Date.parse(manifest.expiresAt) <= Date.now() || signal.aborted) return new Response('Çevrimdışı erişim yenilenmeli.', { status: 410 });
    const asset = manifest.assets.find(item => item.id === assetId); if (!asset) return new Response(null, { status: 404 });
    const range = byteRange(request.headers.get('Range'), asset.bytes);
    if (!range) return new Response(null, { status: 416, headers: { 'Content-Range': `bytes */${asset.bytes}`, 'Content-Length': '0' } });
    const headers = new Headers({ 'Content-Type': asset.contentType, 'Content-Length': String(range.end - range.start + 1), 'Accept-Ranges': 'bytes', 'Cache-Control': 'no-store', 'X-Home-Offline': '1' });
    if (range.partial) headers.set('Content-Range', `bytes ${range.start}-${range.end}/${asset.bytes}`);
    if (request.method === 'HEAD') return new Response(null, { status: range.partial ? 206 : 200, headers });
    const chunkOffset = manifest.assets.slice(0, manifest.assets.indexOf(asset)).reduce((sum, item) => sum + item.chunks, 0); let position = range.start;
    const body = new ReadableStream<Uint8Array>({
      async pull(controller) {
        try {
          if (signal.aborted || Date.parse(manifest.expiresAt) <= Date.now()) throw new Error('Arşiv kilitlendi veya erişim süresi doldu.');
          const current = await store.entry(entryId); if (!current || current.vaultId !== vaultId || current.state !== 'ready') throw new Error('Cihaz kopyası kaldırıldı.');
          const index = Math.floor(position / asset.chunkBytes); const chunk = await store.chunk(entryId, chunkOffset + index);
          if (!chunk) throw new Error('Cihaz parçası eksik.');
          const decoded = await unseal(key, chunk.sealed, chunkAad(vaultId, entryId, asset.fingerprint, chunkOffset + index));
          if (decoded.byteLength !== Math.min(asset.chunkBytes, asset.bytes - index * asset.chunkBytes) || signal.aborted) throw new Error('Cihaz parçası doğrulanamadı veya arşiv kilitlendi.');
          const start = position - index * asset.chunkBytes; const length = Math.min(decoded.byteLength - start, range.end - position + 1);
          controller.enqueue(new Uint8Array(decoded, start, length)); position += length;
          if (position > range.end) { controller.close(); release(); }
        } catch (error) { controller.error(error); release(); }
      },
      cancel() { release(); },
    });
    handedOff = true; return new Response(body, { status: range.partial ? 206 : 200, headers });
  } catch { return new Response('Şifreli cihaz kopyası açılamadı.', { status: 403 }); }
  finally { if (!handedOff) release(); }
}
