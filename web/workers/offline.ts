import { OfflineStore } from '../src/offline/store.ts';
import { offlineResponse } from '../src/offline/range.ts';

const worker = self as unknown as ServiceWorkerGlobalScope;
const base = new URL(worker.registration.scope).pathname.replace(/\/$/, '');
const prefix = base + '/api/offline/local/';
const store = new OfflineStore(base + '/');
worker.addEventListener('message', event => {
  if (event.data?.type !== 'home.offline.ping') return;
  const client = event.source as Client | null;
  if (!client?.url) return; const url = new URL(client.url);
  if (url.origin === worker.location.origin && (url.pathname === base || url.pathname.startsWith(base + '/'))) event.ports[0]?.postMessage({ version: 1 });
});
worker.addEventListener('fetch', event => {
  const url = new URL(event.request.url);
  if (url.origin !== worker.location.origin || !url.pathname.startsWith(prefix)) return;
  event.respondWith(serve(event));
});
async function serve(event: FetchEvent): Promise<Response> {
  if (!['GET', 'HEAD'].includes(event.request.method)) return new Response(null, { status: 405 });
  const path = new URL(event.request.url).pathname.slice(prefix.length).split('/');
  if (path.length !== 3 || !/^[a-f0-9]{32}$/.test(path[0]) || !/^[a-f0-9]{32}$/.test(path[1]) || !['media', 'subtitle'].includes(path[2]) || !event.clientId) return new Response(null, { status: 404 });
  const client = await worker.clients.get(event.clientId); if (!client) return new Response(null, { status: 403 });
  const channel = new MessageChannel(); const cancellation = new AbortController(); let closed = false;
  const release = () => { if (closed) return; closed = true; channel.port1.postMessage({ done: true }); channel.port1.close(); event.request.signal.removeEventListener('abort', abort); };
  const abort = () => { cancellation.abort(); release(); };
  event.request.signal.addEventListener('abort', abort, { once: true });
  try {
    const key = await new Promise<CryptoKey>((resolve, reject) => {
      const timeout = setTimeout(() => reject(new Error('Cihaz arşivi açık değil.')), 4000);
      channel.port1.onmessage = message => {
        if (message.data?.revoked) { cancellation.abort(); return; }
        clearTimeout(timeout);
        const candidate = message.data?.key as CryptoKey | undefined;
        if (message.data?.ok && candidate?.type === 'secret' && candidate.algorithm.name === 'AES-GCM' && !candidate.extractable) resolve(candidate);
        else reject(new Error('Cihaz arşivi kilitli.'));
      };
      client.postMessage({ type: 'home.offline.access', vaultId: path[0], entryId: path[1] }, [channel.port2]);
    });
    return await offlineResponse(event.request, store, key, path[0], path[1], path[2], cancellation.signal, release);
  } catch { release(); return new Response('Cihaz arşivinin kilidini aç.', { status: 423 }); }
}
