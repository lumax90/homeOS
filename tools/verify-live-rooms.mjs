import { writeFile } from 'node:fs/promises';
import { DatabaseSync } from 'node:sqlite';

const base = process.env.HOME_BASE_URL ?? 'http://localhost:5080';
const expected = process.env.HOME_EXPECTED_VERSION ?? '0.23.1';
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
  const [health, runtime, updates, rooms, playback, offline, downloads, vad] = await Promise.all([
    api('/health'), api('/operations/runtime'), api('/operations/updates'), api('/watch/rooms'), api('/account/playback?all=true'), api('/offline/device'), api('/downloads'), api('/subtitles/sync/capabilities'),
  ]);
  if (health.version !== expected || ![rooms, playback.active, offline.exports, downloads].every(Array.isArray) || !vad.available) throw new Error('Published room/runtime contract was not returned.');
  const indexResponse = await fetch(`${base}/`, { signal: AbortSignal.timeout(15000) }); if (!indexResponse.ok) throw new Error('Client shell is unavailable.');
  const index = await indexResponse.text(); const script = /<script[^>]+src="([^"]+)"/.exec(index)?.[1]; if (!script) throw new Error('Built client entry is unavailable.');
  const scriptUrl = new URL(script, base + '/'); if (scriptUrl.origin !== new URL(base).origin) throw new Error('Unexpected client entry origin.');
  const entryResponse = await fetch(scriptUrl, { signal: AbortSignal.timeout(15000) }); if (!entryResponse.ok) throw new Error('Built client entry cannot be read.');
  const entry = await entryResponse.text(); if (!entry.includes('home-rooms-changed') || !entry.includes('/watch/rooms')) throw new Error('Room controller was not included in the client.');
  const workerResponse = await fetch(`${base}/sw.js`, { signal: AbortSignal.timeout(15000) }); if (!workerResponse.ok) throw new Error('PWA worker is unavailable.');
  const worker = await workerResponse.text(); if (!worker.includes('home.offline.access')) throw new Error('Offline PWA worker was not retained.');
  let database;
  if (process.env.HOME_VERIFY_DATABASE) {
    const db = new DatabaseSync(process.env.HOME_VERIFY_DATABASE, { readOnly: true });
    try {
      database = { schema: db.prepare('PRAGMA user_version').get().user_version,
        rooms: db.prepare('SELECT COUNT(*) AS count FROM watch_rooms').get().count,
        roomCommands: db.prepare('SELECT COUNT(*) AS count FROM watch_room_commands').get().count,
        roomClients: db.prepare('SELECT COUNT(*) AS count FROM watch_room_clients').get().count,
        offlineExports: db.prepare('SELECT COUNT(*) AS count FROM offline_exports').get().count,
        downloads: db.prepare('SELECT COUNT(*) AS count FROM downloads').get().count,
        enabledSyntheticLibraries: db.prepare('SELECT COUNT(*) AS count FROM libraries WHERE is_demo=1 AND enabled=1').get().count,
        foreignKeyViolations: db.prepare('PRAGMA foreign_key_check').all().length,
      };
      if (database.schema !== 27 || database.foreignKeyViolations !== 0) throw new Error('Room migration is incomplete.');
    } finally { db.close(); }
  }
  const result = { verifiedAt: new Date().toISOString(), version: health.version, processId: runtime.processId, statePath: runtime.statePath, rollback: updates.rollback,
    roomsVisibleToOwner: rooms.length, clientBuilt: true, offlineWorkerRetained: true, activePlayback: playback.active.length, downloads: downloads.length, vadAvailable: vad.available, database,
    roomsOrPlaybackStartedByVerification: 0, internetMediaDownloadsInitiatedByVerification: 0 };
  await writeFile(process.env.HOME_VERIFY_OUTPUT ?? 'artifacts/live-runtime-v0231.json', JSON.stringify(result, null, 2) + '\n');
  console.log(JSON.stringify({ version: health.version, schema: database?.schema, rooms: rooms.length, activePlayback: result.activePlayback, downloads: result.downloads, clientBuilt: true }, null, 2));
} finally { if (signedIn) await api('/auth/logout', 'POST').catch(() => {}); }
