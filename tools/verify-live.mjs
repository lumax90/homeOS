import { readFile, writeFile } from 'node:fs/promises';
import { DatabaseSync } from 'node:sqlite';

const base = process.env.HOME_BASE_URL ?? 'http://localhost:5080';
const expected = process.env.HOME_EXPECTED_VERSION ?? JSON.parse(await readFile(new URL('../package.json', import.meta.url))).version;
const schema = Number(process.env.HOME_EXPECTED_SCHEMA ?? /LatestSchema = (\d+)/.exec(await readFile(new URL('../server/HomeServer.Api/Infrastructure/MediaStore.Operations.cs', import.meta.url), 'utf8'))?.[1]);
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
async function asset(path) {
  const response = await fetch(path, { signal: AbortSignal.timeout(15000) });
  if (!response.ok) throw new Error('Published client asset is unavailable.'); return response.text();
}
try {
  csrf = (await api('/session')).csrfToken;
  await api('/auth/login', 'POST', { displayName: process.env.HOME_USERNAME, password: process.env.HOME_PASSWORD }); signedIn = true;
  const session = await api('/session'); csrf = session.csrfToken;
  const [health, runtime, updates, rooms, playback, offline, downloads, budget, history, vad] = await Promise.all([
    api('/health'), api('/operations/runtime'), api('/operations/updates'), api('/watch/rooms'), api('/account/playback?all=true'), api('/offline/device'), api('/downloads'),
    api('/account/download-usage'), api('/account/download-history'), api('/subtitles/sync/capabilities'),
  ]);
  if (health.version !== expected || budget.userId !== session.userId || ![rooms, playback.active, offline.exports, downloads, history.items].every(Array.isArray) || !vad.available) throw new Error('Published runtime contract was not returned.');
  const index = await asset(`${base}/`); const script = /<script[^>]+src="([^"]+)"/.exec(index)?.[1]; if (!script) throw new Error('Built client entry is unavailable.');
  const scriptUrl = new URL(script, base + '/'); if (scriptUrl.origin !== new URL(base).origin) throw new Error('Unexpected client entry origin.');
  const entry = await asset(scriptUrl); if (!entry.includes('X-Home-Session') || !entry.includes('/download-usage') || !entry.includes('home-rooms-changed') || !entry.includes('/episode-orders') || !entry.includes('/episode-boundaries') || !entry.includes('/music/list-imports') || !entry.includes('/music/library/artists')) throw new Error('Current account/music/catalog client is not published.');
  const worker = await asset(`${base}/sw.js`); if (!worker.includes('home.offline.access')) throw new Error('Offline PWA worker is unavailable.');
  let database;
  if (process.env.HOME_VERIFY_DATABASE) {
    const db = new DatabaseSync(process.env.HOME_VERIFY_DATABASE, { readOnly: true });
    try {
      const count = sql => db.prepare(sql).get().count;
      database = { schema: db.prepare('PRAGMA user_version').get().user_version, rooms: count('SELECT COUNT(*) AS count FROM watch_rooms'),
        offlineExports: count('SELECT COUNT(*) AS count FROM offline_exports'), downloads: count('SELECT COUNT(*) AS count FROM downloads'),
        budgetOverrides: count('SELECT COUNT(*) AS count FROM user_download_budgets'), activeBudgetRuns: count("SELECT COUNT(*) AS count FROM download_budget_runs WHERE phase<>'closed'"),
        budgetPeriods: count('SELECT COUNT(*) AS count FROM download_budget_periods'),
        episodeRefreshPlans: count('SELECT COUNT(*) AS count FROM episode_refresh_plans'), episodeOrders: count('SELECT COUNT(*) AS count FROM catalog_episode_orders'),
        episodeBoundaryJobs: count('SELECT COUNT(*) AS count FROM episode_boundary_jobs'), retiredEpisodes: count('SELECT COUNT(*) AS count FROM episodes WHERE retired=1'),
        musicListJobs: count('SELECT COUNT(*) AS count FROM music_list_jobs'), musicVideoLinks: count('SELECT COUNT(*) AS count FROM music_video_links'),
        enabledSyntheticLibraries: count('SELECT COUNT(*) AS count FROM libraries WHERE is_demo=1 AND enabled=1'), foreignKeyViolations: db.prepare('PRAGMA foreign_key_check').all().length };
      if (database.schema !== schema || database.foreignKeyViolations !== 0) throw new Error('Published database migration is incomplete.');
    } finally { db.close(); }
  }
  const result = { verifiedAt: new Date().toISOString(), version: health.version, processId: runtime.processId, statePath: runtime.statePath, rollback: updates.rollback,
    rooms: rooms.length, activePlayback: playback.active.length, downloads: downloads.length, budget: { policy: budget.policy, period: budget.period, acquisitions: budget.acquisitions, committedBytes: budget.committedBytes, reservedBytes: budget.reservedBytes, activeTransfers: budget.activeTransfers },
    clientBuilt: true, offlineWorkerRetained: true, vadAvailable: vad.available, database, mediaActionsInitiatedByVerification: 0 };
  await writeFile(process.env.HOME_VERIFY_OUTPUT ?? `artifacts/live-runtime-${expected}.json`, JSON.stringify(result, null, 2) + '\n');
  console.log(JSON.stringify({ version: result.version, schema: database?.schema, downloads: result.downloads, budget: result.budget, activePlayback: result.activePlayback }, null, 2));
} finally { if (signedIn) await api('/auth/logout', 'POST').catch(() => {}); }
