import { readFile, writeFile } from 'node:fs/promises';
import { DatabaseSync } from 'node:sqlite';
import { setTimeout as wait } from 'node:timers/promises';

const base = process.env.HOME_BASE_URL ?? 'http://localhost:5080';
const username = process.env.HOME_USERNAME;
const password = process.env.HOME_PASSWORD;
if (!username || !password) throw new Error('Set HOME_USERNAME and HOME_PASSWORD for the existing Home account.');
const version = JSON.parse(await readFile(new URL('../package.json', import.meta.url))).version;
const output = process.env.HOME_VERIFY_OUTPUT ?? `artifacts/live-music-${version}.json`;
const cookies = new Map(); let csrf = '';
async function api(path, method = 'GET', data, attempt = 0) {
  const headers = { Cookie: [...cookies].map(([key, value]) => `${key}=${value}`).join('; ') };
  if (method !== 'GET') headers['X-CSRF-TOKEN'] = csrf;
  if (data !== undefined) headers['Content-Type'] = 'application/json';
  const response = await fetch(`${base}/api${path}`, { method, headers, body: data === undefined ? undefined : JSON.stringify(data), signal: AbortSignal.timeout(45000) });
  for (const value of response.headers.getSetCookie()) { const pair = value.split(';', 1)[0]; const index = pair.indexOf('='); cookies.set(pair.slice(0, index), pair.slice(index + 1)); }
  if (response.status === 429 && method === 'GET' && attempt < 2) {
    const seconds = Number(response.headers.get('Retry-After') ?? 10);
    await response.body?.cancel(); await wait(Math.min(60, Math.max(10, Number.isFinite(seconds) ? seconds : 10)) * 1000);
    return api(path, method, data, attempt + 1);
  }
  if (!response.ok) throw new Error(`Home ${path}: HTTP ${response.status}`);
  return response.status === 204 ? null : response.json();
}
let signedIn = false;
try {
  csrf = (await api('/session')).csrfToken;
  await api('/auth/login', 'POST', { displayName: username, password }); signedIn = true;
  csrf = (await api('/session')).csrfToken;
  const [health, runtime, updates, preferences, monitors, tasks, downloads, recommendations] = await Promise.all([
    api('/health'), api('/operations/runtime'), api('/operations/updates'), api('/music/playback-preferences'), api('/music/monitors'), api('/music/tasks'), api('/downloads'), api('/music/recommendations'),
  ]);
  for (const value of [monitors, tasks, downloads, recommendations]) if (!Array.isArray(value)) throw new Error('A music list was not a JSON array.');
  const artistId = 'a74b1b7f-71a5-4011-9441-d0b5e4122711';
  const albums = await api(`/music/discovery/artists/${artistId}/albums?offset=0`);
  const credit = albums.items.flatMap(album => album.artistCredits ?? []).find(credit => credit.mbid === artistId);
  if (!credit) throw new Error('Real MusicBrainz artist credit was not returned.');
  const artist = { ...credit, id: credit.mbid };
  const release = await api('/music/releases/0b6b4ba0-d36f-47bd-b4ea-6a5b91842d29');
  const [album, relations, recording, libraryArtists, listImports] = await Promise.all([
    api(`/music/discovery/albums/${release.metadata.groupId}`), api(`/music/discovery/albums/${release.metadata.groupId}/relations`),
    api(`/music/recordings/${release.tracks[0].recordingId}`), api('/music/library/artists'), api('/music/list-imports'),
  ]);
  if (health.version !== version || !release.metadata.artistCredits?.some(credit => credit.mbid === artist.id) || !album.artistCredits?.some(credit => credit.mbid === artist.id)
      || recording.id !== release.tracks[0].recordingId || !Array.isArray(relations) || !Array.isArray(libraryArtists.items) || !Array.isArray(listImports)) throw new Error('Real music identity/relationship contract was not returned.');
  let database;
  if (process.env.HOME_VERIFY_DATABASE) {
    const db = new DatabaseSync(process.env.HOME_VERIFY_DATABASE, { readOnly: true });
    try {
      database = {
        schema: db.prepare('PRAGMA user_version').get().user_version,
        downloads: db.prepare('SELECT COUNT(*) AS count FROM downloads').get().count,
        monitors: db.prepare('SELECT COUNT(*) AS count FROM music_monitors').get().count,
        musicTasks: db.prepare('SELECT COUNT(*) AS count FROM music_tasks').get().count,
        listImports: db.prepare('SELECT COUNT(*) AS count FROM music_list_jobs').get().count,
        videoLinks: db.prepare('SELECT COUNT(*) AS count FROM music_video_links').get().count,
        enabledSyntheticLibraries: db.prepare('SELECT COUNT(*) AS count FROM libraries WHERE is_demo=1 AND enabled=1').get().count,
      };
    } finally { db.close(); }
  }
  const previous = JSON.parse(await readFile(output, 'utf8').then(text => text.replace(/^\uFEFF/, '')).catch(() => '{}'));
  delete previous.downloadsBefore; // Older PowerShell array wrappers counted [] as one pipeline object.
  const result = { ...previous, verifiedAt: new Date().toISOString(), version: health.version, processId: runtime.processId, statePath: runtime.statePath,
    rollback: updates.rollback, musicPreferences: preferences, monitorCount: monitors.length, musicTaskCount: tasks.length, downloadsAfter: downloads.length,
    recommendationCount: recommendations.length, artist, artistAlbumsTotal: albums.total, artistAlbumsPageCount: albums.items.length, albumExamples: albums.items.slice(0, 3),
    verifiedRelease: release.metadata, verifiedAlbum: album, firstRecording: recording, relatedAlbumsAndRecordings: relations,
    libraryArtistCount: libraryArtists.total, listImportCount: listImports.length, database, internetMediaTransfersInitiatedByVerification: 0 };
  await writeFile(output, JSON.stringify(result, null, 2) + '\n');
  console.log(JSON.stringify({ version: result.version, schema: database?.schema, downloads: downloads.length, monitors: monitors.length, musicTasks: tasks.length, artist: artist.name, albums: albums.total }, null, 2));
} finally { if (signedIn) await api('/auth/logout', 'POST').catch(() => {}); }
