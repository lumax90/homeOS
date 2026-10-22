import { writeFile, readFile } from 'node:fs/promises';
import { DatabaseSync } from 'node:sqlite';

const base = process.env.HOME_BASE_URL ?? 'http://localhost:5080';
if (!process.env.HOME_USERNAME || !process.env.HOME_PASSWORD) throw new Error('Set HOME_USERNAME and HOME_PASSWORD for an existing account.');
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
  const [health, runtime, updates, downloads, tracked, movie, show] = await Promise.all([
    api('/health'), api('/operations/runtime'), api('/operations/updates'), api('/downloads'), api('/content'),
    api('/discovery/tmdb/movie/27205'), api('/discovery/tvmaze/show/335'),
  ]);
  if (!Array.isArray(downloads) || !Array.isArray(tracked)) throw new Error('Catalog API did not return arrays.');
  const identity = movie.saved ? await api(`/content/${movie.saved.id}/identity`) : null;
  let preview = null;
  const search = await api('/metadata/search?provider=tmdb&kind=show&q=Sherlock');
  const candidate = search.find(item => item.originalTitle === 'Sherlock' && item.releaseDate?.startsWith('2010'));
  if (show.saved && candidate && show.saved.metadata.provider !== 'tmdb') {
    const value = await api(`/content/${show.saved.id}/identity/preview`, 'POST', { provider: 'tmdb', kind: 'show', providerId: candidate.providerId });
    preview = { canApply: value.canApply, evidence: value.evidence, conflicts: value.conflicts, sourceId: value.sourceId, targetId: value.targetId,
      episodeCount: value.episodes.length, unmappedEpisodes: value.episodes.filter(episode => !episode.targetId).length };
  }
  const groups = [];
  for (const content of tracked.filter(item => item.metadata.provider === 'tmdb' && item.metadata.kind === 'show').slice(0, 1)) {
    groups.push({ contentId: content.id, items: await api(`/content/${content.id}/numbering/groups`) });
  }
  let database;
  if (process.env.HOME_VERIFY_DATABASE) {
    const db = new DatabaseSync(process.env.HOME_VERIFY_DATABASE, { readOnly: true });
    try {
      database = { schema: db.prepare('PRAGMA user_version').get().user_version,
        aliases: db.prepare('SELECT COUNT(*) AS count FROM content_aliases').get().count,
        episodeMemberships: db.prepare('SELECT COUNT(*) AS count FROM media_episode_segments').get().count,
        movieEditions: db.prepare('SELECT COUNT(*) AS count FROM movie_editions').get().count,
        mergedRecords: db.prepare('SELECT COUNT(*) AS count FROM content WHERE merged_into IS NOT NULL').get().count,
        downloads: db.prepare('SELECT COUNT(*) AS count FROM downloads').get().count,
        enabledSyntheticLibraries: db.prepare('SELECT COUNT(*) AS count FROM libraries WHERE is_demo=1 AND enabled=1').get().count };
    } finally { db.close(); }
  }
  const upgrade = JSON.parse(await readFile('artifacts/update-live-0.17.0.json', 'utf8').then(value => value.replace(/^\uFEFF/, '')).catch(() => 'null'));
  const result = { verifiedAt: new Date().toISOString(), version: health.version, processId: runtime.processId, statePath: runtime.statePath,
    rollback: updates.rollback, upgrade, downloads: downloads.length, tracked: tracked.length, database,
    movie: { provider: movie.metadata.provider, providerId: movie.metadata.providerId, title: movie.metadata.title, imdbId: movie.metadata.imdbId },
    movieIdentity: identity ? { id: identity.content.id, revision: identity.content.catalogRevision, aliases: identity.aliases, names: identity.names } : null,
    show: { provider: show.metadata.provider, providerId: show.metadata.providerId, title: show.metadata.title, imdbId: show.metadata.imdbId, tvdbId: show.metadata.tvdbId },
    mergePreview: preview, episodeGroups: groups, catalogMutationsByVerification: 0, internetMediaTransfersInitiatedByVerification: 0 };
  await writeFile(process.env.HOME_VERIFY_OUTPUT ?? 'artifacts/live-runtime-v017.json', JSON.stringify(result, null, 2) + '\n');
  console.log(JSON.stringify({ version: result.version, schema: database?.schema, downloads: downloads.length, aliases: database?.aliases, movie: result.movie.title, show: result.show.title, mergePreview: preview }, null, 2));
} finally { if (signedIn) await api('/auth/logout', 'POST').catch(() => {}); }
