import type { OfflineChunk, OfflineEntry, OfflineEntryState, OfflineStorageUsage, Sealed, VaultHeader } from './types.ts';

const DEFAULT_QUOTA = 8 * 1024 * 1024 * 1024;
const request = <T>(value: IDBRequest<T>): Promise<T> => new Promise((resolve, reject) => { value.onsuccess = () => resolve(value.result); value.onerror = () => reject(value.error); });
export class OfflineStore {
  private name: string; private factory: IDBFactory;
  constructor(scope: string, factory: IDBFactory = indexedDB) { this.name = 'home-offline-v1-' + encodeURIComponent(scope); this.factory = factory; }
  private async open(): Promise<IDBDatabase> {
    const pending = this.factory.open(this.name, 1);
    pending.onupgradeneeded = () => {
      const db = pending.result;
      const vaults = db.createObjectStore('vaults', { keyPath: 'id' }); vaults.createIndex('identity', ['serverId', 'userId', 'deviceId'], { unique: true });
      const entries = db.createObjectStore('entries', { keyPath: 'id' }); entries.createIndex('vault', 'vaultId');
      const chunks = db.createObjectStore('chunks', { keyPath: ['entryId', 'index'] }); chunks.createIndex('entry', 'entryId');
      db.createObjectStore('settings');
    };
    return request(pending);
  }
  private async transaction<T>(stores: string[], mode: IDBTransactionMode, action: (tx: IDBTransaction) => Promise<T>): Promise<T> {
    const db = await this.open(); const tx = db.transaction(stores, mode);
    const completion = new Promise<void>((resolve, reject) => { tx.oncomplete = () => resolve(); tx.onabort = () => reject(tx.error ?? new Error('Cihaz kaydı iptal edildi.')); tx.onerror = () => {}; });
    try { const result = await action(tx); await completion; return result; }
    catch (error) { try { tx.abort(); } catch { } await completion.catch(() => {}); throw error; }
    finally { db.close(); }
  }
  vaults(): Promise<VaultHeader[]> { return this.transaction(['vaults'], 'readonly', tx => request(tx.objectStore('vaults').getAll())); }
  vault(id: string): Promise<VaultHeader | undefined> { return this.transaction(['vaults'], 'readonly', tx => request(tx.objectStore('vaults').get(id))); }
  addVault(value: VaultHeader): Promise<void> { return this.transaction(['vaults'], 'readwrite', async tx => { await request(tx.objectStore('vaults').add(value)); }); }
  entries(vaultId: string): Promise<OfflineEntry[]> { return this.transaction(['entries'], 'readonly', tx => request(tx.objectStore('entries').index('vault').getAll(vaultId))); }
  entry(id: string): Promise<OfflineEntry | undefined> { return this.transaction(['entries'], 'readonly', tx => request(tx.objectStore('entries').get(id))); }
  chunk(id: string, index: number): Promise<OfflineChunk | undefined> { return this.transaction(['chunks'], 'readonly', tx => request(tx.objectStore('chunks').get([id, index]))); }
  usage(): Promise<OfflineStorageUsage> {
    return this.transaction(['settings', 'entries'], 'readonly', async tx => {
      const rows = await request<OfflineEntry[]>(tx.objectStore('entries').getAll()); const quota = await request<number | undefined>(tx.objectStore('settings').get('quota')) ?? DEFAULT_QUOTA;
      return { quota, reserved: rows.reduce((sum, value) => sum + value.reservedBytes, 0), stored: rows.reduce((sum, value) => sum + value.storedBytes, 0), count: rows.length };
    });
  }
  setQuota(bytes: number): Promise<void> {
    if (!Number.isSafeInteger(bytes) || bytes < 1024 || bytes > 64 * 1024 ** 3) throw new Error('Cihaz kotası geçersiz.');
    return this.transaction(['settings', 'entries'], 'readwrite', async tx => {
      const entries = await request<OfflineEntry[]>(tx.objectStore('entries').getAll());
      if (entries.reduce((sum, value) => sum + value.reservedBytes, 0) > bytes) throw new Error('Yeni kota mevcut cihaz kopyalarından küçük; önce dosya kaldır.');
      await request(tx.objectStore('settings').put(bytes, 'quota'));
    });
  }
  reserve(entry: OfflineEntry, capacity = Infinity): Promise<void> {
    if (!Number.isSafeInteger(entry.reservedBytes) || entry.reservedBytes <= 0 || entry.storedBytes !== 0 || entry.nextChunk !== 0 || !/^[a-f0-9]{32}$/.test(entry.id)) throw new Error('Cihaz kopyası kaydı geçersiz.');
    return this.transaction(['vaults', 'settings', 'entries'], 'readwrite', async tx => {
      if (!await request(tx.objectStore('vaults').get(entry.vaultId))) throw new Error('Cihaz arşivi kaldırılmış.');
      const rows = await request<OfflineEntry[]>(tx.objectStore('entries').getAll()); const quota = Math.min(await request<number | undefined>(tx.objectStore('settings').get('quota')) ?? DEFAULT_QUOTA, capacity);
      if (rows.length >= 200 || rows.filter(row => row.vaultId === entry.vaultId).length >= 100 || rows.reduce((sum, row) => sum + row.reservedBytes, 0) + entry.reservedBytes > quota) throw new Error('Cihaz kotası dolu. Dosya kaldır veya kotayı artır.');
      await request(tx.objectStore('entries').add(entry));
    });
  }
  change(id: string, revision: number, changes: Partial<Pick<OfflineEntry, 'grantId' | 'metadata' | 'state' | 'message' | 'reservedBytes' | 'retryAt'>>): Promise<OfflineEntry> {
    if (changes.reservedBytes !== undefined && (!Number.isSafeInteger(changes.reservedBytes) || changes.reservedBytes <= 0)) throw new Error('Cihaz kotası geçersiz.');
    return this.transaction(['entries', 'settings'], 'readwrite', async tx => {
      const object = tx.objectStore('entries'); const row = await request<OfflineEntry | undefined>(object.get(id));
      if (!row || row.revision !== revision) throw new Error('Cihaz kopyası arada değişti.');
      if (changes.reservedBytes !== undefined && changes.reservedBytes > row.reservedBytes) {
        const rows = await request<OfflineEntry[]>(object.getAll()); const quota = await request<number | undefined>(tx.objectStore('settings').get('quota')) ?? DEFAULT_QUOTA;
        if (rows.reduce((sum, value) => sum + value.reservedBytes, 0) - row.reservedBytes + changes.reservedBytes > quota) throw new Error('Hazırlanan dosya cihaz kotasını aşıyor.');
      }
      const next = { ...row, ...changes, revision: row.revision + 1 }; await request(object.put(next)); return next;
    });
  }
  commitChunk(id: string, revision: number, chunk: OfflineChunk): Promise<boolean> {
    return this.transaction(['entries', 'chunks'], 'readwrite', async tx => {
      const object = tx.objectStore('entries'); const row = await request<OfflineEntry | undefined>(object.get(id));
      if (!row || row.revision !== revision || row.state !== 'downloading' || row.nextChunk !== chunk.index) return false;
      if (chunk.entryId !== id || chunk.bytes !== chunk.sealed.data.byteLength + chunk.sealed.iv.byteLength || chunk.bytes > 2 * 1024 * 1024 + 28 || row.storedBytes + chunk.bytes > row.reservedBytes) throw new Error('Şifreli parça kotası veya sırası geçersiz.');
      await request(tx.objectStore('chunks').add(chunk)); await request(object.put({ ...row, storedBytes: row.storedBytes + chunk.bytes, nextChunk: row.nextChunk + 1 })); return true;
    });
  }
  complete(id: string, revision: number, totalChunks: number): Promise<void> {
    return this.transaction(['entries', 'chunks'], 'readwrite', async tx => {
      const rows = tx.objectStore('entries'); const value = await request<OfflineEntry | undefined>(rows.get(id));
      if (!value || value.state !== 'downloading' || value.revision !== revision || value.nextChunk !== totalChunks || await request(tx.objectStore('chunks').index('entry').count(id)) !== totalChunks) throw new Error('Cihaz kopyasının parçaları henüz tamamlanmadı.');
      await request(rows.put({ ...value, state: 'ready', message: 'Çevrimdışı hazır.', revision: value.revision + 1 }));
    });
  }
  pause(vaultId: string): Promise<void> {
    return this.transaction(['entries'], 'readwrite', async tx => {
      const object = tx.objectStore('entries'); const rows = await request<OfflineEntry[]>(object.index('vault').getAll(vaultId));
      for (const row of rows) if (['preparing', 'downloading'].includes(row.state)) await request(object.put({ ...row, state: 'queued', revision: row.revision + 1, message: 'Arşiv açıldığında aktarım devam edecek.' }));
    });
  }
  resetChunks(id: string, revision: number, state: OfflineEntryState = 'queued'): Promise<void> {
    return this.transaction(['entries', 'chunks'], 'readwrite', async tx => {
      const object = tx.objectStore('entries'); const row = await request<OfflineEntry | undefined>(object.get(id));
      if (!row || row.revision !== revision) throw new Error('Cihaz kopyası değişti.');
      for (const key of await request(tx.objectStore('chunks').index('entry').getAllKeys(id))) await request(tx.objectStore('chunks').delete(key));
      await request(object.put({ ...row, revision: row.revision + 1, nextChunk: 0, storedBytes: 0, state }));
    });
  }
  remove(id: string): Promise<void> {
    return this.transaction(['entries', 'chunks'], 'readwrite', async tx => {
      for (const key of await request(tx.objectStore('chunks').index('entry').getAllKeys(id))) await request(tx.objectStore('chunks').delete(key));
      await request(tx.objectStore('entries').delete(id));
    });
  }
  removeVault(id: string): Promise<void> {
    return this.transaction(['vaults', 'entries', 'chunks'], 'readwrite', async tx => {
      for (const row of await request<OfflineEntry[]>(tx.objectStore('entries').index('vault').getAll(id))) {
        for (const key of await request(tx.objectStore('chunks').index('entry').getAllKeys(row.id))) await request(tx.objectStore('chunks').delete(key));
        await request(tx.objectStore('entries').delete(row.id));
      }
      await request(tx.objectStore('vaults').delete(id));
    });
  }
}
