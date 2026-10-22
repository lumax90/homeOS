import { readFile, writeFile } from 'node:fs/promises';
import { DatabaseSync } from 'node:sqlite';

const base = process.env.HOME_BASE_URL ?? 'http://localhost:5080';
if (!process.env.HOME_USERNAME || !process.env.HOME_PASSWORD) throw new Error('Set HOME_USERNAME and HOME_PASSWORD for an existing Home account.');
const cookies = new Map(); let csrf = ''; let signedIn = false;
async function api(path, method = 'GET', data) {
  const headers = { Cookie: [...cookies].map(([key, value]) => `${key}=${value}`).join('; ') };
  if (method !== 'GET') headers['X-CSRF-TOKEN'] = csrf;
  if (data !== undefined) headers['Content-Type'] = 'application/json';
  const response = await fetch(`${base}/api${path}`, { method, headers, body: data === undefined ? undefined : JSON.stringify(data), signal: AbortSignal.timeout(45000) });
  for (const cookie of response.headers.getSetCookie()) { const pair = cookie.split(';', 1)[0]; const position = pair.indexOf('='); cookies.set(pair.slice(0, position), pair.slice(position + 1)); }
  if (!response.ok) throw new Error(`Home ${path}: HTTP ${response.status}`);
  return response.status === 204 ? null : response.json();
}
try {
  csrf = (await api('/session')).csrfToken;
  await api('/auth/login', 'POST', { displayName: process.env.HOME_USERNAME, password: process.env.HOME_PASSWORD }); signedIn = true;
  csrf = (await api('/session')).csrfToken;
  const [health, runtime, update, profile, connection, vad, wanted, jobs, downloads] = await Promise.all([
    api('/health'), api('/operations/runtime'), api('/operations/updates'), api('/subtitles/profile'), api('/subtitles/connection'), api('/subtitles/sync/capabilities'),
    api('/subtitles/wanted'), api('/subtitles/sync/jobs'), api('/downloads'),
  ]);
  if (!vad.available) throw new Error(`Published VAD unavailable: ${vad.error}`);
  if (![wanted, jobs, downloads].every(Array.isArray)) throw new Error('Subtitle list response is not a JSON array.');
  let database;
  if (process.env.HOME_VERIFY_DATABASE) {
    const db = new DatabaseSync(process.env.HOME_VERIFY_DATABASE, { readOnly: true });
    try {
      database = { schema: db.prepare('PRAGMA user_version').get().user_version,
        subtitleVersions: db.prepare('SELECT COUNT(*) AS count FROM managed_subtitles').get().count,
        mediaOverrides: db.prepare('SELECT COUNT(*) AS count FROM media_subtitle_overrides').get().count,
        syncJobs: db.prepare('SELECT COUNT(*) AS count FROM subtitle_sync_jobs').get().count,
        providerDownloadAttempts: db.prepare('SELECT COUNT(*) AS count FROM subtitle_download_attempts').get().count,
        downloads: db.prepare('SELECT COUNT(*) AS count FROM downloads').get().count,
        enabledSyntheticLibraries: db.prepare('SELECT COUNT(*) AS count FROM libraries WHERE is_demo=1 AND enabled=1').get().count };
    } finally { db.close(); }
  }
  const upgrade = JSON.parse(await readFile('artifacts/update-live-0.18.0.json', 'utf8').then(value => value.replace(/^\uFEFF/, '')).catch(() => 'null'));
  const result = { verifiedAt: new Date().toISOString(), version: health.version, processId: runtime.processId, statePath: runtime.statePath, rollback: update.rollback, upgrade,
    connection: { configured: connection.configured, authenticated: connection.authenticated }, profile, vad, wantedJobs: wanted.length, syncJobs: jobs.length,
    downloads: downloads.length, database, mediaOrSubtitleDownloadsInitiatedByVerification: 0, mediaAnalysisJobsInitiatedByVerification: 0 };
  await writeFile(process.env.HOME_VERIFY_OUTPUT ?? 'artifacts/live-runtime-v018.json', JSON.stringify(result, null, 2) + '\n');
  console.log(JSON.stringify({ version: health.version, schema: database?.schema, vad: vad.available, model: vad.model, providerConnected: connection.authenticated, downloads: downloads.length, syncJobs: jobs.length }, null, 2));
} finally { if (signedIn) await api('/auth/logout', 'POST').catch(() => {}); }
