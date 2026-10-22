import { writeFile } from 'node:fs/promises';
import { DatabaseSync } from 'node:sqlite';

const base = process.env.HOME_BASE_URL ?? 'http://localhost:5080';
const expected = process.env.HOME_EXPECTED_VERSION ?? '0.21.0';
if (!process.env.HOME_USERNAME || !process.env.HOME_PASSWORD) throw new Error('Set HOME_USERNAME and HOME_PASSWORD for an existing administrator.');
const cookies = new Map(); let csrf = ''; let signedIn = false;
async function api(path, method = 'GET', data) {
  const headers = { Cookie: [...cookies].map(([key, value]) => `${key}=${value}`).join('; ') };
  if (method !== 'GET') headers['X-CSRF-TOKEN'] = csrf;
  if (data !== undefined) headers['Content-Type'] = 'application/json';
  const response = await fetch(`${base}/api${path}`, { method, headers, body: data === undefined ? undefined : JSON.stringify(data), signal: AbortSignal.timeout(30000) });
  for (const cookie of response.headers.getSetCookie()) { const pair = cookie.split(';', 1)[0]; const position = pair.indexOf('='); cookies.set(pair.slice(0, position), pair.slice(position + 1)); }
  if (!response.ok) throw new Error(`Home ${path}: HTTP ${response.status}`);
  return response.status === 204 ? null : response.json();
}
try {
  csrf = (await api('/session')).csrfToken;
  await api('/auth/login', 'POST', { displayName: process.env.HOME_USERNAME, password: process.env.HOME_PASSWORD }); signedIn = true;
  csrf = (await api('/session')).csrfToken;
  const [health, runtime, updates, configuration, device, usage, downloads, vad] = await Promise.all([
    api('/health'), api('/operations/runtime'), api('/operations/updates'), api('/push/configuration'), api('/push/device'),
    api('/account/playback?all=true'), api('/downloads'), api('/subtitles/sync/capabilities'),
  ]);
  if (health.version !== expected) throw new Error(`Expected Home ${expected}, received ${health.version}.`);
  if (![device.devices, downloads, usage.active].every(Array.isArray) || !vad.available) throw new Error('Published capability contract was not returned.');
  const workerResponse = await fetch(`${base}/sw.js`, { signal: AbortSignal.timeout(10000) });
  if (!workerResponse.ok) throw new Error('Published service worker is missing.');
  const worker = await workerResponse.text();
  if (!worker.includes('createPushController') || worker.includes('__HOME_PUSH_CONTROLLER__') || !worker.includes('pushsubscriptionchange')) throw new Error('Push service worker was not built.');
  let database;
  if (process.env.HOME_VERIFY_DATABASE) {
    const db = new DatabaseSync(process.env.HOME_VERIFY_DATABASE, { readOnly: true });
    try {
      database = { schema: db.prepare('PRAGMA user_version').get().user_version,
        pushSubscriptions: db.prepare('SELECT COUNT(*) AS count FROM browser_push_subscriptions').get().count,
        pushChannels: db.prepare("SELECT COUNT(*) AS count FROM notification_channels WHERE kind='push' AND deleted=0").get().count,
        deliveryAttempts: db.prepare('SELECT COUNT(*) AS count FROM notification_delivery_attempts').get().count,
        downloads: db.prepare('SELECT COUNT(*) AS count FROM downloads').get().count,
        enabledSyntheticLibraries: db.prepare('SELECT COUNT(*) AS count FROM libraries WHERE is_demo=1 AND enabled=1').get().count,
        foreignKeyViolations: db.prepare('PRAGMA foreign_key_check').all().length,
      };
      if (database.schema < 25 || database.foreignKeyViolations !== 0) throw new Error('Push migration is incomplete.');
    } finally { db.close(); }
  }
  const result = { verifiedAt: new Date().toISOString(), version: health.version, processId: runtime.processId, statePath: runtime.statePath, rollback: updates.rollback,
    push: { enabled: configuration.enabled, keyConfigured: !!configuration.publicKey, keyRevision: configuration.keyRevision, subscriptionsForAccount: device.devices.length, workerBuilt: true },
    activePlayback: usage.active.length, downloads: downloads.length, vadAvailable: vad.available, database,
    pushRegistrationsOrMessagesInitiatedByVerification: 0, mediaDownloadsInitiatedByVerification: 0 };
  await writeFile(process.env.HOME_VERIFY_OUTPUT ?? 'artifacts/live-runtime-v021.json', JSON.stringify(result, null, 2) + '\n');
  console.log(JSON.stringify({ version: health.version, schema: database?.schema, push: result.push, deliveryAttempts: database?.deliveryAttempts, activePlayback: result.activePlayback, downloads: result.downloads }, null, 2));
} finally { if (signedIn) await api('/auth/logout', 'POST').catch(() => {}); }
