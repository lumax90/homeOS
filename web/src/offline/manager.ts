import { ApiError, request, type MediaItem, type Session } from '../api';
import { appUrl } from '../base';
import { chunkAad, createVault, newOfflineId, openVault, readMetadata, seal, sealMetadata, sha256 } from './crypto.ts';
import { OfflineStore } from './store.ts';
import { acknowledgeProgress } from './progress.ts';
import type { OfflineDeviceStatus, OfflineEntry, OfflineExport, OfflineInput, OfflineManifest, OfflineMetadata, OfflineProgressResult, OfflineStorageUsage, OfflineView, VaultHeader } from './types.ts';

export interface OfflineManagerState { vaults: VaultHeader[]; vault: VaultHeader | null; unlocked: boolean; items: OfflineView[]; usage: OfflineStorageUsage; online: boolean | null; device: OfflineDeviceStatus | null; busy: boolean; message: string }
const delay = (ms: number, signal: AbortSignal) => new Promise<void>((resolve, reject) => {
  const timer = setTimeout(done, ms); function done() { signal.removeEventListener('abort', aborted); resolve(); }
  function aborted() { clearTimeout(timer); reject(new DOMException('İptal', 'AbortError')); } if (signal.aborted) aborted(); else signal.addEventListener('abort', aborted, { once: true });
});
const emptyProgress = () => ({ position: 0, sequence: 0, acknowledged: 0, eventId: newOfflineId(), baseStamp: '', pending: false, conflict: null });
export class OfflineManager {
  readonly store: OfflineStore;
  private key: CryptoKey | null = null; private epoch = 0; private abort = new AbortController(); private pumping = false; private wakeRequested = false; private refreshing = false; private syncing = false;
  private listeners = new Set<() => void>(); private accesses = new Map<MessagePort, string>();
  private locks = new Map<string, Promise<unknown>>();
  private broadcast: BroadcastChannel | null = null;
  private state: OfflineManagerState = { vaults: [], vault: null, unlocked: false, items: [], usage: { quota: 8 * 1024 ** 3, reserved: 0, stored: 0, count: 0 }, online: null, device: null, busy: false, message: '' };
  constructor(store = new OfflineStore(appUrl('/'))) {
    this.store = store;
    if (typeof window !== 'undefined') {
      if ('BroadcastChannel' in window) { this.broadcast = new BroadcastChannel('home-offline-' + appUrl('/')); this.broadcast.onmessage = event => { if (event.data?.type === 'lock' && event.data.vaultId === this.state.vault?.id) void this.lock(false); if (event.data?.type === 'remove') { this.closeAccess(event.data.id); void this.refresh(); } }; }
      window.addEventListener('home-offline-lock', () => { void this.lock(); });
      window.addEventListener('home-session-state', event => { const value = (event as CustomEvent<Session>).detail; if (!value.authenticated || this.state.vault && this.state.vault.userId !== value.userId) void this.lock(); });
      window.addEventListener('pagehide', () => { void this.lock(); });
      window.addEventListener('online', () => { void this.maintain(); });
      window.addEventListener('home-offline-changed', () => { void this.maintain(); });
      window.setInterval(() => { if (this.key) void this.maintain(); }, 20000);
      navigator.serviceWorker?.addEventListener('message', event => { void this.access(event); });
    }
  }
  snapshot = () => this.state;
  subscribe = (callback: () => void) => { this.listeners.add(callback); return () => { this.listeners.delete(callback); }; };
  private emit(changes: Partial<OfflineManagerState> = {}) { this.state = { ...this.state, ...changes }; for (const listener of this.listeners) listener(); }
  private async exclusive<T>(name: string, operation: () => Promise<T>): Promise<T> {
    if (typeof navigator !== 'undefined' && 'locks' in navigator) return navigator.locks.request('home-offline-' + appUrl('/') + ':' + name, operation);
    const next = (this.locks.get(name) ?? Promise.resolve()).then(operation); this.locks.set(name, next.catch(() => {})); return next;
  }
  async refresh() {
    const epoch = this.epoch; const key = this.key; const vault = this.state.vault;
    const [vaults, usage] = await Promise.all([this.store.vaults(), this.store.usage()]);
    const items: OfflineView[] = [];
    if (key && vault) for (const row of await this.store.entries(vault.id)) {
      try { items.push({ row, metadata: await readMetadata(key, vault.id, row.id, row.metadata) }); }
      catch { this.emit({ message: 'Bir cihaz kaydı çözülemedi; arşiv bozulmuş olabilir.' }); }
    }
    if (epoch === this.epoch) this.emit({ vaults, usage, items, unlocked: !!this.key });
  }
  async probe(): Promise<OfflineDeviceStatus | null> {
    try {
      const device = await request<OfflineDeviceStatus>('/offline/device', { signal: AbortSignal.timeout(4000) }); this.emit({ online: true, device }); return device;
    } catch (error) {
      if (error instanceof ApiError && error.status < 500) { this.emit({ online: true, device: null }); throw error; }
      this.emit({ online: false, device: null }); return null;
    }
  }
  async initialize() {
    await this.refresh(); const device = await this.probe().catch(() => null);
    if (device && !device.policy.enabled) for (const vault of this.state.vaults.filter(value => this.sameIdentity(value, device))) for (const row of await this.store.entries(vault.id)) await this.store.remove(row.id);
    await this.refresh();
  }
  private sameIdentity(vault: VaultHeader, device: OfflineDeviceStatus) { return vault.serverId === device.serverId && vault.userId === device.userId && vault.deviceId === device.deviceId; }
  async create(password: string) {
    this.emit({ busy: true, message: '' });
    try {
      const device = await this.probe(); if (!device?.policy.enabled) throw new Error('Cihaz arşivi oluşturmak için Home hesabınla çevrimiçi olmalısın.');
      if ((await this.store.vaults()).some(vault => this.sameIdentity(vault, device))) throw new Error('Bu hesap/cihaz için arşiv zaten var; cihaz arşivi parolanla kilidini aç.');
      const created = await createVault(device, password); await this.store.addVault(created.header);
      await this.lock(); this.key = created.key; this.emit({ vault: created.header, unlocked: true }); await this.refresh();
    } finally { this.emit({ busy: false }); }
  }
  async unlock(id: string, password: string) {
    this.emit({ busy: true, message: '' });
    try {
      const vault = await this.store.vault(id); if (!vault) throw new Error('Cihaz arşivi bulunamadı.');
      const device = await this.probe();
      if (device && !this.sameIdentity(vault, device)) throw new Error('Bu arşivin Home hesabıyla giriş yap.');
      if (device && !device.policy.enabled) { for (const row of await this.store.entries(vault.id)) await this.store.remove(row.id); throw new Error('Bu hesapta çevrimdışı erişim kapatılmış.'); }
      const key = await openVault(vault, password); await this.lock(); this.key = key; this.emit({ vault, unlocked: true });
      if ('locks' in navigator) await navigator.locks.request('home-offline-' + appUrl('/') + ':transfer', { ifAvailable: true }, lock => lock ? this.store.pause(vault.id) : Promise.resolve());
      else await this.store.pause(vault.id);
      await this.refresh(); if (device) await this.validate(device); void this.maintain();
    } finally { this.emit({ busy: false }); }
  }
  async lock(announce = true) {
    const vault = this.state.vault; this.epoch++; this.key = null; this.abort.abort(); this.abort = new AbortController();
    if (vault && announce) this.broadcast?.postMessage({ type: 'lock', vaultId: vault.id });
    for (const port of this.accesses.keys()) { try { port.postMessage({ revoked: true }); port.close(); } catch { } } this.accesses.clear();
    this.emit({ unlocked: false, items: [], vault: null });
    if (vault) await this.store.pause(vault.id).catch(() => {});
  }
  async forget(id: string) { if (this.state.vault?.id === id) await this.lock(); await this.store.removeVault(id); await this.refresh(); }
  private requireKey() { if (!this.key || !this.state.vault) throw new Error('Önce cihaz arşivinin kilidini aç.'); return { key: this.key, vault: this.state.vault, epoch: this.epoch }; }
  private async view(id: string): Promise<OfflineView> {
    const { key, vault, epoch } = this.requireKey(); const row = await this.store.entry(id);
    if (!row || row.vaultId !== vault.id) throw new Error('Bu kopya açık arşive ait değil.');
    const metadata = await readMetadata(key, vault.id, id, row.metadata); if (epoch !== this.epoch) throw new DOMException('Arşiv kilitlendi.', 'AbortError'); return { row, metadata };
  }
  async queue(media: MediaItem, input: Omit<OfflineInput, 'clientId'>) {
    const { key, vault, epoch } = this.requireKey(); const device = await this.probe();
    if (!device || !this.sameIdentity(vault, device) || !device.policy.enabled) throw new Error('Cihaza indirme için kendi Home hesabınla bağlantı gerekli.');
    const id = newOfflineId(); const command: OfflineInput = { ...input, clientId: id, expectedMatchRevision: media.matchRevision };
    const metadata: OfflineMetadata = { input: command, media: { id: media.id, title: media.title, kind: media.kind, durationSeconds: media.durationSeconds, sizeBytes: media.sizeBytes }, manifest: null, progress: emptyProgress() };
    const bitrate = media.kind === 'audio' ? 192000 : input.profile === 'compatible-1080' ? 6000000 : 3000000;
    const reserved = input.profile === 'original' ? media.sizeBytes + 4 * 1024 ** 2 : Math.ceil((media.durationSeconds ?? 0) * bitrate / 8 * 1.3) + 16 * 1024 ** 2;
    let capacity = Infinity;
    if (navigator.storage?.estimate) { const estimate = await navigator.storage.estimate(); if (estimate.quota !== undefined && estimate.usage !== undefined) capacity = Math.max(0, estimate.quota - estimate.usage - 32 * 1024 ** 2) + this.state.usage.stored; }
    const encrypted = await sealMetadata(key, vault.id, id, metadata); if (epoch !== this.epoch) throw new Error('Arşiv değişti.');
    await this.store.reserve({ id, vaultId: vault.id, grantId: null, reservedBytes: reserved, storedBytes: 0, nextChunk: 0, state: 'queued', revision: 1, metadata: encrypted, createdAt: Date.now(), message: 'Cihaza indirme sırada.' }, capacity);
    await this.refresh(); void this.pump();
  }
  async pause(id: string) {
    const { row } = await this.view(id); await this.store.change(id, row.revision, { state: 'paused', message: 'Aktarım duraklatıldı.' }); this.abort.abort(); this.abort = new AbortController(); await this.refresh();
  }
  async resume(id: string) {
    let { row } = await this.view(id);
    if (row.grantId) {
      const job = await request<OfflineExport>(`/offline/exports/${row.grantId}`, { signal: AbortSignal.timeout(10000) });
      if (job.status === 'failed' || job.requiresPreparation) await request(`/offline/exports/${row.grantId}/retry`, { method: 'POST', body: JSON.stringify({ revision: job.revision }), signal: AbortSignal.timeout(10000) });
      else if (job.status === 'ready' && Date.parse(job.expiresAt) <= Date.now()) await request(`/offline/exports/${row.grantId}/renew`, { method: 'POST', body: JSON.stringify({ revision: job.revision }), signal: AbortSignal.timeout(10000) });
    }
    row = await this.store.entry(id) ?? row; await this.store.change(id, row.revision, { state: 'queued', retryAt: 0, message: 'Aktarım devam edecek.' }); await this.refresh(); void this.pump();
  }
  async remove(id: string) {
    const { row } = await this.view(id); this.closeAccess(id); await this.store.remove(id); this.broadcast?.postMessage({ type: 'remove', id }); await this.refresh();
    if (row.grantId) {
      try { const job = await request<OfflineExport>(`/offline/exports/${row.grantId}`, { signal: AbortSignal.timeout(5000) }); await request(`/offline/exports/${row.grantId}/cancel`, { method: 'POST', body: JSON.stringify({ revision: job.revision }), signal: AbortSignal.timeout(5000) }); } catch { }
    }
  }
  private async pump() {
    if (this.pumping) { this.wakeRequested = true; return; }
    if (!this.key || this.state.online !== true) return; this.pumping = true;
    try {
      await this.exclusive('transfer', async () => {
        while (this.key && this.state.online === true) {
          const vault = this.state.vault!; const entries = await this.store.entries(vault.id);
          const row = entries.filter(item => ['queued', 'preparing', 'downloading'].includes(item.state) && (item.retryAt ?? 0) <= Date.now()).sort((a, b) => a.createdAt - b.createdAt)[0]; if (!row) break;
          const epoch = this.epoch;
          try { await this.download(row.id, this.abort.signal); }
          catch (error) {
            if (epoch !== this.epoch) break;
            const latest = await this.store.entry(row.id);
            const transient = error instanceof TypeError || error instanceof DOMException && error.name === 'TimeoutError' || error instanceof ApiError && (error.status >= 500 || error.status === 429);
            if (latest && latest.state !== 'paused') await this.store.change(row.id, latest.revision, { state: transient ? 'queued' : 'paused', retryAt: transient ? Date.now() + 30000 : 0, message: error instanceof Error ? error.message : 'Aktarım kesildi.' }).catch(() => {});
            if (!(error instanceof ApiError)) this.emit({ message: 'Aktarım durdu. Bağlantı veya cihaz alanını kontrol edip devam edebilirsin.' });
          }
          await this.refresh();
        }
      });
    } finally { this.pumping = false; if (this.wakeRequested) { this.wakeRequested = false; void this.pump(); } }
  }
  private async download(id: string, signal: AbortSignal) {
    const { key, vault, epoch } = this.requireKey(); let { row, metadata } = await this.view(id);
    row = await this.store.change(id, row.revision, { state: 'preparing', message: 'Sunucu kopyası bekleniyor.' });
    if (!row.grantId) {
      const device = this.state.device;
      if (!device || !this.sameIdentity(vault, device) || epoch !== this.epoch) throw new Error('Home hesabı değişti.');
      const job = await request<OfflineExport>(`/offline/media/${metadata.media.id}`, { method: 'POST', body: JSON.stringify({ ...metadata.input, expectedUserId: vault.userId, expectedSessionId: device.sessionId }), signal: AbortSignal.any([signal, AbortSignal.timeout(20000)]) });
      if (epoch !== this.epoch) throw new Error('Arşiv kilitlendi.');
      row = await this.store.change(id, row.revision, { grantId: job.id });
    }
    while (true) {
      signal.throwIfAborted(); if (epoch !== this.epoch) throw new Error('Arşiv kilitlendi.');
      const job = await request<OfflineExport>(`/offline/exports/${row.grantId}`, { signal: AbortSignal.any([signal, AbortSignal.timeout(15000)]) });
      if (job.status === 'ready') {
        if (job.requiresPreparation) { await request(`/offline/exports/${row.grantId}/retry`, { method: 'POST', body: JSON.stringify({ revision: job.revision }), signal }); continue; }
        if (Date.parse(job.expiresAt) <= Date.now()) { await request(`/offline/exports/${row.grantId}/renew`, { method: 'POST', body: JSON.stringify({ revision: job.revision }), signal }); continue; }
        break;
      }
      if (!['queued', 'running'].includes(job.status)) throw new Error(job.message || 'Sunucu kopyası hazırlanamadı.');
      this.emit({ message: `Sunucu hazırlığı · ${job.progress}%` }); await delay(2500, signal);
    }
    let manifest = await request<OfflineManifest>(`/offline/exports/${row.grantId}/manifest`, { signal: AbortSignal.any([signal, AbortSignal.timeout(15000)]) });
    const sameAssets = metadata.manifest && JSON.stringify(metadata.manifest.assets) === JSON.stringify(manifest.assets);
    if (metadata.manifest && !sameAssets && row.nextChunk > 0) { await this.store.resetChunks(id, row.revision, 'preparing'); row = (await this.store.entry(id))!; }
    if (!sameAssets) metadata = { ...metadata, manifest, progress: { ...emptyProgress(), position: manifest.isWatched ? 0 : manifest.positionSeconds, baseStamp: manifest.progressStamp } };
    else metadata = { ...metadata, manifest };
    const chunks = manifest.assets.reduce((sum, item) => sum + item.chunks, 0); const reserved = manifest.assets.reduce((sum, item) => sum + item.bytes, 0) + chunks * 28 + 16384;
    const encrypted = await sealMetadata(key, vault.id, id, metadata); signal.throwIfAborted();
    row = await this.store.change(id, row.revision, { metadata: encrypted, state: 'downloading', reservedBytes: reserved, message: 'Şifreli parçalar cihaza yazılıyor.' });
    let refreshed = 0;
    for (let index = row.nextChunk; index < chunks; index++) {
      signal.throwIfAborted(); if (epoch !== this.epoch) throw new Error('Arşiv kilitlendi.');
      let relative = index; const asset = manifest.assets.find(item => { if (relative < item.chunks) return true; relative -= item.chunks; return false; })!;
      const response = await fetch(appUrl(`/api/offline/exports/${row.grantId}/chunks/${asset.id}/${relative}?fingerprint=${encodeURIComponent(asset.fingerprint)}`), { credentials: 'same-origin', signal: AbortSignal.any([signal, AbortSignal.timeout(20000)]) });
      if (!response.ok) { const error = await response.json().catch(() => null); throw new ApiError(error?.message ?? 'Cihaz parçası alınamadı.', response.status); }
      const expected = Math.min(asset.chunkBytes, asset.bytes - relative * asset.chunkBytes); const reader = response.body?.getReader(); if (!reader) throw new Error('Aktarım gövdesi yok.');
      const bytes = new Uint8Array(expected); let offset = 0;
      try { while (true) { const part = await reader.read(); if (part.done) break; if (offset + part.value.byteLength > expected) throw new Error('Aktarım parçası sınırı aşıldı.'); bytes.set(part.value, offset); offset += part.value.byteLength; } } finally { await reader.cancel().catch(() => {}); }
      if (offset !== expected || response.headers.get('X-Home-Fingerprint') !== asset.fingerprint || await sha256(bytes.buffer) !== response.headers.get('X-Home-Sha256')) throw new Error('Aktarım parçasının bütünlüğü doğrulanamadı.');
      const sealed = await seal(key, bytes, chunkAad(vault.id, id, asset.fingerprint, index)); signal.throwIfAborted();
      if (epoch !== this.epoch || !await this.store.commitChunk(id, row.revision, { entryId: id, index, sealed, bytes: sealed.data.byteLength + sealed.iv.byteLength })) throw new Error('Aktarım niyeti değişti.');
      if (Date.now() - refreshed > 750) { await this.refresh(); refreshed = Date.now(); }
    }
    await this.store.complete(id, row.revision, chunks); this.emit({ message: 'Cihaz kopyası çevrimdışı hazır.' });
  }
  private async validate(device: OfflineDeviceStatus) {
    const { key, vault, epoch } = this.requireKey();
    if (!this.sameIdentity(vault, device)) { await this.lock(); return; }
    if (!device.policy.enabled) { for (const row of await this.store.entries(vault.id)) { this.closeAccess(row.id); await this.store.remove(row.id); } await this.lock(); throw new Error('Bu hesabın çevrimdışı erişimi kapatıldı.'); }
    for (const item of await this.store.entries(vault.id)) {
      if (epoch !== this.epoch) return; if (!item.grantId) continue;
      let server = device.exports.find(job => job.id === item.grantId);
      if (!server) {
        try { server = await request<OfflineExport>(`/offline/exports/${item.grantId}`, { signal: AbortSignal.timeout(5000) }); }
        catch (error) { if (!(error instanceof ApiError) || ![403, 404, 410].includes(error.status)) throw error; }
      }
      if (!server || ['revoked', 'cancelled'].includes(server.status)) { this.closeAccess(item.id); await this.store.remove(item.id); continue; }
      if (item.state !== 'ready' || server.status !== 'ready') continue;
      if (Date.parse(server.expiresAt) - Date.now() < 24 * 60 * 60 * 1000) {
        const renewed = await request<OfflineExport>(`/offline/exports/${server.id}/renew`, { method: 'POST', body: JSON.stringify({ revision: server.revision }), signal: AbortSignal.timeout(5000) });
        await this.exclusive(vault.id, async () => {
          const latest = await this.store.entry(item.id); if (!latest || epoch !== this.epoch) return;
          const metadata = await readMetadata(key, vault.id, item.id, latest.metadata); if (metadata.manifest) metadata.manifest.expiresAt = renewed.expiresAt;
          await this.store.change(item.id, latest.revision, { metadata: await sealMetadata(key, vault.id, item.id, metadata) });
        });
      }
    }
  }
  async maintain() {
    if (this.refreshing || !this.key) return; this.refreshing = true;
    try { const device = await this.probe(); if (device && this.key) { await this.validate(device); await this.sync(); void this.pump(); } await this.refresh(); }
    catch (error) { if (error instanceof ApiError && [401, 403].includes(error.status)) await this.lock(); else this.emit({ message: (error as Error).message }); }
    finally { this.refreshing = false; }
  }
  async saveProgress(id: string, position: number) {
    const { key, vault } = this.requireKey();
    await this.exclusive(vault.id, async () => {
      const { row, metadata } = await this.view(id); if (!metadata.manifest || row.state !== 'ready' || !Number.isFinite(position)) return;
      metadata.progress = { ...metadata.progress, position: Math.max(0, Math.min(position, metadata.manifest.durationSeconds)), sequence: metadata.progress.sequence + 1, eventId: newOfflineId(), pending: true };
      await this.store.change(id, row.revision, { metadata: await sealMetadata(key, vault.id, id, metadata) });
    });
    await this.refresh();
  }
  async sync() {
    if (this.syncing || !this.key || !this.state.vault || this.state.online !== true) return; this.syncing = true;
    const { key, vault, epoch } = this.requireKey();
    try {
      for (const row of await this.store.entries(vault.id)) {
        if (epoch !== this.epoch) return;
        const metadata = await readMetadata(key, vault.id, row.id, row.metadata); const sent = metadata.progress;
        if (!row.grantId || !sent.pending || sent.conflict || !metadata.manifest) continue;
        const result = await request<OfflineProgressResult>(`/offline/exports/${row.grantId}/progress`, { method: 'POST', signal: AbortSignal.timeout(8000), body: JSON.stringify({ eventId: sent.eventId, sequence: sent.sequence, positionSeconds: sent.position, expectedStamp: sent.baseStamp, expectedUserId: vault.userId }) });
        await this.exclusive(vault.id, async () => {
          if (epoch !== this.epoch) return; const latest = await this.view(row.id); const progress = latest.metadata.progress;
          latest.metadata.progress = acknowledgeProgress(progress, sent, result);
          await this.store.change(row.id, latest.row.revision, { metadata: await sealMetadata(key, vault.id, row.id, latest.metadata) });
        });
      }
    } finally { this.syncing = false; }
  }
  async resolve(id: string, choice: 'local' | 'server') {
    const { key, vault } = this.requireKey();
    await this.exclusive(vault.id, async () => {
      const { row, metadata } = await this.view(id); const conflict = metadata.progress.conflict; if (!conflict) return;
      metadata.progress = { ...metadata.progress, baseStamp: conflict.stamp, position: choice === 'server' ? conflict.positionSeconds : metadata.progress.position,
        sequence: Math.max(metadata.progress.sequence, conflict.sequence) + 1, eventId: newOfflineId(), pending: choice === 'local', conflict: null };
      await this.store.change(id, row.revision, { metadata: await sealMetadata(key, vault.id, id, metadata) });
    });
    await this.refresh(); await this.sync();
  }
  private closeAccess(id: string) { for (const [port, entry] of this.accesses) if (entry === id) { port.postMessage({ revoked: true }); port.close(); this.accesses.delete(port); } }
  private async access(event: MessageEvent) {
    if (event.data?.type !== 'home.offline.access' || !event.ports[0]) return;
    const worker = event.source as ServiceWorker | null; if (!worker || worker.scriptURL !== new URL(appUrl('/sw.js'), location.href).href) return;
    const port = event.ports[0];
    try {
      const { key, vault, epoch } = this.requireKey(); if (event.data.vaultId !== vault.id) throw new Error('Arşiv kilitli.');
      const view = await this.view(event.data.entryId);
      if (view.row.state !== 'ready' || !view.metadata.manifest || Date.parse(view.metadata.manifest.expiresAt) <= Date.now() || epoch !== this.epoch) throw new Error('Çevrimdışı kopya yeniden doğrulanmalı.');
      this.accesses.set(port, view.row.id); port.onmessage = () => { this.accesses.delete(port); port.close(); }; port.start(); port.postMessage({ ok: true, key });
    } catch { port.postMessage({ ok: false }); port.close(); }
  }
}

let shared: OfflineManager | null = null;
export function offlineManager(): OfflineManager {
  if (!('indexedDB' in globalThis) || !crypto.subtle) throw new Error('Cihaz arşivi için HTTPS, Web Crypto ve IndexedDB desteği gerekli.');
  return shared ??= new OfflineManager();
}
