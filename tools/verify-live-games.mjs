import { readFile, writeFile } from 'node:fs/promises';
import { DatabaseSync } from 'node:sqlite';
import { join } from 'node:path';

// Read-only catalog/package/state verification. Never starts a game, emulator, transfer or provider request.
const base = process.env.HOME_BASE_URL ?? 'http://127.0.0.1:5080';
const expectedVersion = process.env.HOME_VERIFY_VERSION ?? '0.32.0';
const tag = process.env.HOME_VERIFY_TAG ?? 'v032';
if (!/^v\d+$/.test(tag)) throw new Error('Use a bounded evidence tag.');
if (!process.env.HOME_USERNAME || !process.env.HOME_PASSWORD) throw new Error('Set HOME_USERNAME and HOME_PASSWORD.');
const migration = JSON.parse(await readFile('artifacts/gaming-migration-v032.json', 'utf8'));
const cookies = new Map(); let csrf = '', signedIn = false;
function headers() { return { Cookie: [...cookies].map(([name, value]) => name + '=' + value).join('; ') }; }
async function api(path, method = 'GET', body) {
  const requestHeaders = headers(); if (method !== 'GET') requestHeaders['X-CSRF-TOKEN'] = csrf;
  if (body !== undefined) requestHeaders['Content-Type'] = 'application/json';
  const response = await fetch(base + '/api' + path, { method, headers: requestHeaders, body: body === undefined ? undefined : JSON.stringify(body), signal: AbortSignal.timeout(15000) });
  for (const cookie of response.headers.getSetCookie()) { const pair = cookie.split(';', 1)[0]; const index = pair.indexOf('='); cookies.set(pair.slice(0, index), pair.slice(index + 1)); }
  if (!response.ok) throw new Error(`${path}: HTTP ${response.status}`);
  return response.status === 204 ? null : response.json();
}
function capture(database) {
  const db = new DatabaseSync(database, { readOnly: true });
  try {
    return {
      schema: db.prepare('PRAGMA user_version').get().user_version,
      foreignKeys: db.prepare('PRAGMA foreign_key_check').all().length,
      language: db.prepare("SELECT value FROM settings WHERE key='metadata.language'").get()?.value,
      media: db.prepare('SELECT id,library_id,relative_path,size_bytes FROM media ORDER BY id').all(),
      requests: db.prepare('SELECT id,content_id,requested_by FROM content_requests ORDER BY id').all(),
      downloads: db.prepare('SELECT id,info_hash,owner_id,archived,desired_state FROM downloads ORDER BY id').all(),
      personal: db.prepare('SELECT user_id,content_id,favorite,rating FROM personal_content ORDER BY user_id,content_id').all(),
      collections: db.prepare('SELECT id,user_id,name,shared FROM collections ORDER BY id').all(),
      gameStates: db.prepare('SELECT COUNT(*) AS count FROM game_user_state').get().count
    };
  } finally { db.close(); }
}
try {
  csrf = (await api('/session')).csrfToken;
  await api('/auth/login', 'POST', { displayName: process.env.HOME_USERNAME, password: process.env.HOME_PASSWORD }); signedIn = true;
  const session = await api('/session'); csrf = session.csrfToken;
  const health = await api('/health'); if (health.version !== expectedVersion) throw new Error('Unexpected runtime version.');
  const runtime = await api('/operations/runtime'); const before = capture(join(migration.isolatedDirectory, 'home.db')); const current = capture(join(runtime.statePath, 'home.db'));
  const preserved = ['media', 'requests', 'downloads', 'personal', 'collections'].every(key => before[key].every(row => current[key].some(value => JSON.stringify(value) === JSON.stringify(row))));
  if (!preserved || current.schema !== 40 || current.foreignKeys || current.language !== before.language) throw new Error('Existing data or settings were not preserved.');
  const catalog = await api('/games');
  const storeExpected = process.env.HOME_VERIFY_STORE === '1';
  const expectedIds = storeExpected ? ['2048', 'a-dark-room', 'hexgl', 'hextris', 'oh-hi', 'tiny-crate', 'wrap-it-up'] : ['tiny-crate', 'wrap-it-up'];
  if (catalog.serverRunnerAvailable || catalog.items.map(game => game.id).sort().join(',') !== expectedIds.sort().join(',')) throw new Error('Unexpected game catalog or unsupported server runner claim.');
  if (storeExpected && catalog.items.filter(game => game.inStore).length !== 5) throw new Error('Expected five reviewed store entries.');
  const artwork = [];
  for (const game of catalog.items) {
    const response = await fetch(base + `/api/games/${game.id}/artwork/cover.png`, { headers: headers(), signal: AbortSignal.timeout(15000) });
    if (!response.ok || !response.headers.get('content-type')?.startsWith('image/png')) throw new Error('Game cover was not served.');
    const bytes = new Uint8Array(await response.arrayBuffer()); if (bytes[0] !== 137 || bytes[1] !== 80) throw new Error('Game artwork is not a PNG.'); artwork.push({ id: game.id, bytes: bytes.length });
  }
  const html = await (await fetch(base + '/games', { cache: 'no-store' })).text(); const entry = /<script[^>]+src="([^"]+)"/.exec(html)?.[1]; if (!entry) throw new Error('Game route did not serve the Home shell.');
  const bundle = await (await fetch(new URL(entry, base))).text();
  if (!bundle.includes('Games-') || !bundle.includes('GamePlayer-') || !bundle.includes('home-page-v2')) throw new Error('The game pages or navigation contract are not in the active client.');
  const result = { verifiedAt: new Date().toISOString(), version: health.version, schema: current.schema, foreignKeyViolations: current.foreignKeys, existingDataPreserved: preserved, metadataLanguage: current.language,
    counts: { media: current.media.length, requests: current.requests.length, downloads: current.downloads.length, personal: current.personal.length, gameStates: current.gameStates },
    games: catalog.items.map(game => ({ id: game.id, title: game.title, version: game.version, bytes: game.sizeBytes, sourceCommit: game.sourceCommit, installed: game.installed, inStore: game.inStore })), artwork, clientEntry: entry,
    downloadStates: current.downloads.map(row => ({ id: row.id, desiredState: row.desired_state, archived: row.archived })), gameplayCommandsIssued: 0, acquisitionCommandsIssued: 0, browserDeviceAcceptance: 'User-owned; no automated browser suite or device/gameplay acceptance claimed.' };
  await writeFile(`artifacts/live-games-${tag}.json`, JSON.stringify(result, null, 2) + '\n'); console.log(JSON.stringify(result, null, 2));
} finally { if (signedIn) await api('/auth/logout', 'POST').catch(() => {}); }
