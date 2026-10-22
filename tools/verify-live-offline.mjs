import { writeFile } from 'node:fs/promises';
import { DatabaseSync } from 'node:sqlite';

const base = process.env.HOME_BASE_URL ?? 'http://localhost:5080';
const expected = process.env.HOME_EXPECTED_VERSION ?? '0.22.0';
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
  const [health, runtime, updates, offline, playback, downloads, vad] = await Promise.all([
    api('/health'), api('/operations/runtime'), api('/operations/updates'), api('/offline/device'), api('/account/playback?all=true'), api('/downloads'), api('/subtitles/sync/capabilities'),
  ]);
  if (health.version !== expected || ![offline.exports, playback.active, downloads].every(Array.isArray) || !vad.available) throw new Error('Published runtime contract was not returned.');
  const response = await fetch(`${base}/sw.js`, { signal: AbortSignal.timeout(15000) });
  if (!response.ok) throw new Error('Published service worker is missing.');
  const worker = await response.text();
  if (!worker.includes('home.offline.access') || !worker.includes('home-offline-v1-') || worker.includes('fake-indexeddb')) throw new Error('Offline worker build is not valid.');
  let database;
  if (process.env.HOME_VERIFY_DATABASE) {
    const db = new DatabaseSync(process.env.HOME_VERIFY_DATABASE, { readOnly: true });
    try {
      database = { schema: db.prepare('PRAGMA user_version').get().user_version,
        offlineExports: db.prepare('SELECT COUNT(*) AS count FROM offline_exports').get().count,
        progressReceipts: db.prepare('SELECT COUNT(*) AS count FROM offline_progress_receipts').get().count,
        policyOverrides: db.prepare('SELECT COUNT(*) AS count FROM user_offline_policies').get().count,
        downloads: db.prepare('SELECT COUNT(*) AS count FROM downloads').get().count,
        enabledSyntheticLibraries: db.prepare('SELECT COUNT(*) AS count FROM libraries WHERE is_demo=1 AND enabled=1').get().count,
        foreignKeyViolations: db.prepare('PRAGMA foreign_key_check').all().length,
      };
      if (database.schema < 26 || database.foreignKeyViolations !== 0) throw new Error('Offline migration is incomplete.');
    } finally { db.close(); }
  }
  const result = { verifiedAt: new Date().toISOString(), version: health.version, processId: runtime.processId, statePath: runtime.statePath, rollback: updates.rollback,
    offline: { policy: offline.policy, deviceExports: offline.exports.length, workerBuilt: true }, activePlayback: playback.active.length, downloads: downloads.length, vadAvailable: vad.available, database,
    offlineExportsOrDeviceDownloadsInitiatedByVerification: 0, internetMediaDownloadsInitiatedByVerification: 0 };
  await writeFile(process.env.HOME_VERIFY_OUTPUT ?? 'artifacts/live-runtime-v022.json', JSON.stringify(result, null, 2) + '\n');
  console.log(JSON.stringify({ version: health.version, schema: database?.schema, offline: result.offline, activePlayback: result.activePlayback, downloads: result.downloads }, null, 2));
} finally { if (signedIn) await api('/auth/logout', 'POST').catch(() => {}); }
