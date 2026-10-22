import type { OfflineMetadata, Sealed, VaultHeader } from './types.ts';

const text = new TextEncoder();
export const newOfflineId = () => [...crypto.getRandomValues(new Uint8Array(16))].map(value => value.toString(16).padStart(2, '0')).join('');
const vaultAad = (header: Pick<VaultHeader, 'id' | 'serverId' | 'userId' | 'deviceId'>) => `home.offline.v1:${header.id}:${header.serverId}:${header.userId}:${header.deviceId}`;
export const metadataAad = (vault: string, entry: string) => `home.offline.meta.v1:${vault}:${entry}`;
export const chunkAad = (vault: string, entry: string, fingerprint: string, index: number) => `home.offline.chunk.v1:${vault}:${entry}:${fingerprint}:${index}`;
export async function seal(key: CryptoKey, bytes: ArrayBuffer | Uint8Array<ArrayBuffer>, aad: string): Promise<Sealed> {
  const iv = crypto.getRandomValues(new Uint8Array(12));
  const data = await crypto.subtle.encrypt({ name: 'AES-GCM', iv, additionalData: text.encode(aad), tagLength: 128 }, key, bytes);
  return { iv: iv.buffer, data };
}
export async function unseal(key: CryptoKey, value: Sealed, aad: string): Promise<ArrayBuffer> {
  if (value.iv.byteLength !== 12 || value.data.byteLength < 16) throw new Error('Çevrimdışı veri başlığı geçersiz.');
  return crypto.subtle.decrypt({ name: 'AES-GCM', iv: value.iv, additionalData: text.encode(aad), tagLength: 128 }, key, value.data);
}
async function wrappingKey(password: string, salt: ArrayBuffer): Promise<CryptoKey> {
  if (password.length < 8 || password.length > 256 || salt.byteLength !== 16) throw new Error('Cihaz arşivi parolası 8–256 karakter olmalı.');
  const material = await crypto.subtle.importKey('raw', text.encode(password), 'PBKDF2', false, ['deriveKey']);
  return crypto.subtle.deriveKey({ name: 'PBKDF2', salt, iterations: 600000, hash: 'SHA-256' }, material, { name: 'AES-GCM', length: 256 }, false, ['encrypt', 'decrypt']);
}
export async function createVault(identity: { serverId: string; userId: string; deviceId: string; displayName: string }, password: string): Promise<{ header: VaultHeader; key: CryptoKey }> {
  const salt = crypto.getRandomValues(new Uint8Array(16)); const raw = crypto.getRandomValues(new Uint8Array(32));
  const fields = { id: newOfflineId(), serverId: identity.serverId, userId: identity.userId, deviceId: identity.deviceId };
  try {
    const wrapper = await wrappingKey(password, salt.buffer); const wrappedKey = await seal(wrapper, raw, vaultAad(fields));
    const key = await crypto.subtle.importKey('raw', raw, { name: 'AES-GCM' }, false, ['encrypt', 'decrypt']);
    return { header: { ...fields, label: identity.displayName, salt: salt.buffer, wrappedKey, createdAt: Date.now(), version: 1 }, key };
  } finally { raw.fill(0); }
}
export async function openVault(header: VaultHeader, password: string): Promise<CryptoKey> {
  if (header.version !== 1) throw new Error('Bu cihaz arşivinin sürümü desteklenmiyor.');
  try {
    const wrapper = await wrappingKey(password, header.salt); const raw = await unseal(wrapper, header.wrappedKey, vaultAad(header));
    try { if (raw.byteLength !== 32) throw new Error(); return await crypto.subtle.importKey('raw', raw, { name: 'AES-GCM' }, false, ['encrypt', 'decrypt']); }
    finally { new Uint8Array(raw).fill(0); }
  } catch { throw new Error('Cihaz arşivi parolası doğru değil veya arşiv anahtarı bozulmuş.'); }
}
export function sealMetadata(key: CryptoKey, vault: string, entry: string, metadata: OfflineMetadata): Promise<Sealed> {
  return seal(key, text.encode(JSON.stringify(metadata)), metadataAad(vault, entry));
}
export async function readMetadata(key: CryptoKey, vault: string, entry: string, metadata: Sealed): Promise<OfflineMetadata> {
  if (metadata.data.byteLength > 128 * 1024) throw new Error('Çevrimdışı kayıt sınırı aşıldı.');
  return JSON.parse(new TextDecoder().decode(await unseal(key, metadata, metadataAad(vault, entry)))) as OfflineMetadata;
}
export async function sha256(bytes: ArrayBuffer): Promise<string> {
  return [...new Uint8Array(await crypto.subtle.digest('SHA-256', bytes))].map(value => value.toString(16).padStart(2, '0')).join('');
}
