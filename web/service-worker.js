const BASE = new URL(self.registration.scope).pathname.replace(/\/$/, '');
const CACHE = 'home-shell-' + encodeURIComponent(BASE) + '-__HOME_BUILD__';
const SHELL = __HOME_SHELL__.map(path => BASE + path);
__HOME_PUSH_CONTROLLER__

const PUSH_DB = 'home-push-' + encodeURIComponent(BASE);
const PUSH_TAG = 'home-push-' + encodeURIComponent(BASE) + '-';
async function pushStore(value, key = 'binding') {
  const db = await new Promise((resolve, reject) => {
    const request = indexedDB.open(PUSH_DB, 1);
    request.onupgradeneeded = () => request.result.createObjectStore('state');
    request.onsuccess = () => resolve(request.result); request.onerror = () => reject(request.error);
  });
  try {
    return await new Promise((resolve, reject) => {
      const tx = db.transaction('state', value === undefined ? 'readonly' : 'readwrite');
      const operation = value === undefined ? tx.objectStore('state').get(key) : tx.objectStore('state').put(value, key);
      let result; operation.onsuccess = () => { result = operation.result; };
      tx.oncomplete = () => resolve(value === undefined ? result ?? { binding: null, seen: [] } : undefined);
      tx.onerror = () => reject(tx.error); tx.onabort = () => reject(tx.error);
    });
  } finally { db.close(); }
}
async function pushState() {
  const response = await fetch(BASE + '/api/push/device', { credentials: 'same-origin', cache: 'no-store', signal: AbortSignal.timeout(2500) });
  if (response.status === 401) return { userId: null, sessionId: null, current: null };
  if (!response.ok) throw new Error('Push state unavailable');
  return response.json();
}
const push = createPushController({
  load: () => pushStore(), save: value => pushStore(value), fetchState: pushState,
  show: async message => self.registration.showNotification('Home', {
    body: (await pushStore(undefined, 'language')) === 'tr'
      ? message.test ? 'Push bildirimi denemesi cihazına ulaştı.' : 'Yeni bir bildirimin var. Görmek için Home’u aç.'
      : message.test ? 'The push notification test reached your device.' : 'You have a new notification. Open Home to view it.',
    icon: BASE + '/home-192.png', badge: BASE + '/home-192.png', tag: PUSH_TAG + message.deliveryId,
    data: { homePush: true, bindingId: message.bindingId, deliveryId: message.deliveryId },
  }),
  closeExcept: async bindingId => {
    for (const notification of await self.registration.getNotifications())
      if (notification.tag.startsWith(PUSH_TAG) && notification.data?.bindingId !== bindingId) notification.close();
  },
  open: async () => {
    const windows = await self.clients.matchAll({ type: 'window', includeUncontrolled: true });
    const existing = windows.find(client => { const url = new URL(client.url); return url.origin === self.location.origin && (url.pathname === BASE || url.pathname.startsWith(BASE + '/')); });
    if (existing) { existing.postMessage({ type: 'home.notification.open' }); await existing.focus(); }
    else await self.clients.openWindow(BASE + '/#notifications');
  },
});
self.addEventListener('message', event => {
  const source = event.source?.url ? new URL(event.source.url) : null;
  if (!source || source.origin !== self.location.origin || source.pathname !== BASE && !source.pathname.startsWith(BASE + '/')) return;
  const reply = value => event.ports[0]?.postMessage(value);
  if (event.data?.type === 'home.language' && ['en', 'tr'].includes(event.data.language)) { event.waitUntil(pushStore(event.data.language, 'language')); return; }
  if (event.data?.type === 'home.push.ping') { reply({ version: 1 }); return; }
  if (event.data?.type === 'home.activate') { event.waitUntil(self.skipWaiting()); return; }
  if (!['home.push.sync', 'home.push.clear'].includes(event.data?.type)) return;
  event.waitUntil((event.data.type === 'home.push.sync' ? push.sync(event.data.expected) : push.clear(event.data.expected))
    .then(() => reply({ ok: true })).catch(() => reply({ ok: false })));
});
self.addEventListener('push', event => {
  let message; try { message = event.data?.json(); } catch { return; }
  event.waitUntil(push.push(message).catch(() => {}));
});
self.addEventListener('notificationclick', event => {
  event.notification.close(); event.waitUntil(push.click(event.notification.data).catch(() => {}));
});
self.addEventListener('pushsubscriptionchange', event => {
  const renew = async () => {
    const previous = (await pushStore()).binding; if (!previous) return;
    const state = await pushState();
    if (!state.current || state.current.bindingId !== previous.bindingId) { await push.sync(); return; }
    const response = await fetch(BASE + '/api/session', { credentials: 'same-origin', cache: 'no-store', signal: AbortSignal.timeout(3000) });
    if (!response.ok) return;
    const session = await response.json(); if (session.userId !== previous.userId || session.sessionId !== previous.sessionId || !session.authenticated) { await push.sync(); return; }
    if (!event.newSubscription) {
      await push.clear(previous);
      await fetch(BASE + '/api/push/devices/' + state.current.channelId + '?revision=' + state.current.revision, {
        method: 'DELETE', credentials: 'same-origin', signal: AbortSignal.timeout(5000), headers: { 'X-CSRF-TOKEN': session.csrfToken },
      });
      return;
    }
    const key = event.newSubscription.options.applicationServerKey; if (!key) return;
    const applicationServerKey = btoa(String.fromCharCode(...new Uint8Array(key))).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
    if (applicationServerKey !== state.configuration.publicKey) { await push.clear(previous); return; }
    const renewed = await fetch(BASE + '/api/push/device', {
      method: 'POST', credentials: 'same-origin', signal: AbortSignal.timeout(5000), headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': session.csrfToken },
      body: JSON.stringify({ ...event.newSubscription.toJSON(), name: state.current.name, events: state.current.events,
        expectedUserId: session.userId, expectedSessionId: session.sessionId, expectedRevision: state.current.revision,
        keyRevision: state.configuration.keyRevision, applicationServerKey }),
    });
    if (renewed.ok) await push.sync({ userId: session.userId, sessionId: session.sessionId });
  };
  event.waitUntil(('locks' in self.navigator ? self.navigator.locks.request('home-push-' + BASE + '/', { signal: AbortSignal.timeout(15000) }, renew) : renew()).catch(() => {}));
});

self.addEventListener('install', event => {
  event.waitUntil(caches.open(CACHE).then(cache => cache.addAll(SHELL.map(url => new Request(url, { cache: 'reload', credentials: 'omit' })))));
});
self.addEventListener('activate', event => {
  event.waitUntil(caches.keys().then(keys => Promise.all(keys.filter(key => key !== CACHE && (key.startsWith('home-shell-' + encodeURIComponent(BASE) + '-') || !BASE && /^home-shell-[a-f0-9]{16}$/.test(key))).map(key => caches.delete(key)))).then(() => self.clients.claim()));
});
self.addEventListener('fetch', event => {
  const url = new URL(event.request.url);
  if (event.request.method !== 'GET' || url.origin !== self.location.origin || url.pathname.startsWith(BASE + '/api')) return;
  if (event.request.mode === 'navigate') {
    event.respondWith(fetch(event.request).catch(async () => (await caches.open(CACHE)).match(BASE + '/')
      .then(async response => response || new Response((await pushStore(undefined, 'language')) === 'tr' ? 'Home sunucusuna bağlanılamıyor.' : 'Cannot connect to your Home server.', { status: 503, headers: { 'Content-Type': 'text/plain; charset=utf-8' } }))));
  } else if (SHELL.includes(url.pathname)) {
    event.respondWith(caches.open(CACHE).then(async cache => (await cache.match(url.pathname)) || fetch(event.request)));
  }
});
