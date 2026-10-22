import { appUrl } from './base';
import type { Session } from './api';

export async function pushRegistration(): Promise<ServiceWorkerRegistration | undefined> {
  if (typeof navigator === 'undefined' || !('serviceWorker' in navigator) || !window.isSecureContext) return undefined;
  return navigator.serviceWorker.getRegistration(appUrl('/'));
}
export function pushWorkerMessage<T>(worker: ServiceWorker, message: unknown, timeout = 1800): Promise<T> {
  return new Promise((resolve, reject) => {
    const channel = new MessageChannel();
    const timer = setTimeout(() => { channel.port1.close(); reject(new Error('Home istemcisi güncellenmeli veya yeniden açılmalı.')); }, timeout);
    channel.port1.onmessage = event => { clearTimeout(timer); channel.port1.close(); resolve(event.data as T); };
    worker.postMessage(message, [channel.port2]);
  });
}
export async function syncLocalPush(session: Pick<Session, 'userId' | 'sessionId' | 'authenticated'>): Promise<void> {
  const registration = await pushRegistration(); if (!registration?.active) return;
  await pushWorkerMessage(registration.active, { type: 'home.push.sync', expected: { userId: session.authenticated ? session.userId : null, sessionId: session.authenticated ? session.sessionId : null } }).catch(() => {});
}
export async function clearLocalPush(session: Pick<Session, 'userId' | 'sessionId'> | null): Promise<void> {
  if (!session?.sessionId) return;
  const registration = await pushRegistration().catch(() => undefined); if (!registration?.active) return;
  await pushWorkerMessage(registration.active, { type: 'home.push.clear', expected: { userId: session.userId, sessionId: session.sessionId } }, 700).catch(() => {});
}

let localLock: Promise<unknown> = Promise.resolve();
export function withPushLock<T>(operation: () => Promise<T>): Promise<T> {
  if ('locks' in navigator) return navigator.locks.request('home-push-' + appUrl('/'), { signal: AbortSignal.timeout(15000) }, operation);
  const next = localLock.then(operation); localLock = next.catch(() => {}); return next;
}
export function pushKeyBytes(value: string): Uint8Array<ArrayBuffer> {
  const encoded = value.replace(/-/g, '+').replace(/_/g, '/');
  return Uint8Array.from(atob(encoded + '='.repeat((4 - encoded.length % 4) % 4)), char => char.charCodeAt(0));
}
export function pushKeyString(value: ArrayBuffer): string {
  return btoa(String.fromCharCode(...new Uint8Array(value))).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}
export function subscribeBrowserPush(registration: ServiceWorkerRegistration, publicKey: string): Promise<PushSubscription> {
  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => reject(new Error('Tarayıcının push servisine ulaşılamadı; yeniden dene.')), 30000);
    registration.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: pushKeyBytes(publicKey) })
      .then(value => { clearTimeout(timer); resolve(value); }, error => { clearTimeout(timer); reject(error); });
  });
}
