export interface OfflinePolicy { enabled: boolean; maximumItems: number; maximumBytes: number; leaseDays: number; revision: number }
export interface OfflineInput { clientId: string; profile: 'original' | 'compatible-720' | 'compatible-1080'; audioStream?: number | null; subtitleId?: string | null; burnSubtitle?: boolean; subtitleDelaySeconds?: number; expectedMatchRevision?: number }
export interface OfflineAsset { id: string; contentType: string; bytes: number; fingerprint: string; chunkBytes: number; chunks: number; language: string | null; title: string | null }
export interface OfflineManifest { grantId: string; mediaId: string; title: string; kind: string; libraryName: string; durationSeconds: number; sourceFingerprint: string; matchRevision: number; expiresAt: string; assets: OfflineAsset[]; progressStamp: string; positionSeconds: number; isWatched: boolean }
export interface OfflineExport { id: string; userId: string; deviceId: string; mediaId: string; sourceFingerprint: string; input: OfflineInput; status: string; revision: number; progress: number; message: string; expiresAt: string; reservedBytes: number; manifest: OfflineManifest | null; requiresPreparation: boolean }
export interface OfflineDeviceStatus { serverId: string; userId: string; deviceId: string; sessionId: string; displayName: string; policy: OfflinePolicy; exports: OfflineExport[] }
export interface OfflineProgressResult { status: 'applied' | 'conflict' | 'stale'; stamp: string; positionSeconds: number; durationSeconds: number; isWatched: boolean; sequence: number }
export interface Sealed { iv: ArrayBuffer; data: ArrayBuffer }
export interface VaultHeader { id: string; serverId: string; userId: string; deviceId: string; label: string; salt: ArrayBuffer; wrappedKey: Sealed; createdAt: number; version: 1 }
export interface LocalProgress { position: number; sequence: number; acknowledged: number; eventId: string; baseStamp: string; pending: boolean; conflict: OfflineProgressResult | null }
export interface OfflineMetadata { input: OfflineInput; media: { id: string; title: string; kind: string; durationSeconds: number | null; sizeBytes: number }; manifest: OfflineManifest | null; progress: LocalProgress }
export type OfflineEntryState = 'queued' | 'preparing' | 'downloading' | 'paused' | 'ready' | 'failed' | 'revoked';
export interface OfflineEntry { id: string; vaultId: string; grantId: string | null; reservedBytes: number; storedBytes: number; nextChunk: number; state: OfflineEntryState; revision: number; metadata: Sealed; createdAt: number; message: string; retryAt?: number }
export interface OfflineChunk { entryId: string; index: number; sealed: Sealed; bytes: number }
export interface OfflineView { row: OfflineEntry; metadata: OfflineMetadata }
export interface OfflineStorageUsage { quota: number; reserved: number; stored: number; count: number }
