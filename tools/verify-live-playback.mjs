import { writeFile } from 'node:fs/promises';
import { DatabaseSync } from 'node:sqlite';

const base = process.env.HOME_BASE_URL ?? 'http://localhost:5080';
const expected = process.env.HOME_EXPECTED_VERSION ?? '0.19.0';
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
  const [health, runtime, updates, usage, devices, downloads, vad] = await Promise.all([
    api('/health'), api('/operations/runtime'), api('/operations/updates'), api('/account/playback?all=true'), api('/playback/devices'), api('/downloads'), api('/subtitles/sync/capabilities'),
  ]);
  if (health.version !== expected) throw new Error(`Expected Home ${expected}, received ${health.version}.`);
  if (![usage.active, usage.history, devices, downloads].every(Array.isArray)) throw new Error('Playback list contract was not returned.');
  if (!vad.available) throw new Error('The packaged local subtitle VAD is unavailable.');
  let database;
  if (process.env.HOME_VERIFY_DATABASE) {
    const db = new DatabaseSync(process.env.HOME_VERIFY_DATABASE, { readOnly: true });
    try {
      database = { schema: db.prepare('PRAGMA user_version').get().user_version,
        activePlayback: db.prepare("SELECT COUNT(*) AS count FROM playback_leases WHERE status<>'closed'").get().count,
        playbackHistory: db.prepare("SELECT COUNT(*) AS count FROM playback_leases WHERE status='closed'").get().count,
        userPlaybackOverrides: db.prepare('SELECT COUNT(*) AS count FROM user_playback_limits').get().count,
        devices: db.prepare('SELECT COUNT(*) AS count FROM playback_devices').get().count,
        missingSessionDevice: db.prepare('SELECT COUNT(*) AS count FROM user_sessions WHERE playback_device_id IS NULL').get().count,
        downloads: db.prepare('SELECT COUNT(*) AS count FROM downloads').get().count,
        enabledSyntheticLibraries: db.prepare('SELECT COUNT(*) AS count FROM libraries WHERE is_demo=1 AND enabled=1').get().count,
      };
      if (database.schema < 23 || database.missingSessionDevice !== 0) throw new Error('Playback migration was not completed.');
    } finally { db.close(); }
  }
  const result = { verifiedAt: new Date().toISOString(), version: health.version, processId: runtime.processId, statePath: runtime.statePath, rollback: updates.rollback,
    limits: { server: usage.server, owner: usage.user, currentDevice: usage.deviceMaximumStreams },
    activePlayback: usage.active.length, recentHistory: usage.history.length, registeredDevices: devices.length, downloads: downloads.length, vadAvailable: vad.available, database,
    playbackStartedByVerification: 0, mediaDownloadsInitiatedByVerification: 0, mediaAnalysisJobsInitiatedByVerification: 0 };
  await writeFile(process.env.HOME_VERIFY_OUTPUT ?? 'artifacts/live-runtime-v019.json', JSON.stringify(result, null, 2) + '\n');
  console.log(JSON.stringify({ version: health.version, schema: database?.schema, limits: result.limits, activePlayback: result.activePlayback, downloads: result.downloads, vadAvailable: vad.available }, null, 2));
} finally { if (signedIn) await api('/auth/logout', 'POST').catch(() => {}); }
