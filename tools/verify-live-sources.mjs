import { readFile, writeFile } from 'node:fs/promises';
import { DatabaseSync } from 'node:sqlite';
import { join } from 'node:path';

const base = process.env.HOME_BASE_URL ?? 'http://localhost:5080';
if (!process.env.HOME_USERNAME || !process.env.HOME_PASSWORD) throw new Error('Set HOME_USERNAME and HOME_PASSWORD.');
const before = JSON.parse((await readFile(process.env.HOME_PREFLIGHT ?? 'artifacts/preview-before-v027.json', 'utf8')).replace(/^\uFEFF/, ''));
const cookies = new Map(); let csrf = ''; let signedIn = false;
async function api(path, method = 'GET', body) {
  const headers = { Cookie: [...cookies].map(([key,value]) => `${key}=${value}`).join('; ') };
  if (method !== 'GET') headers['X-CSRF-TOKEN'] = csrf;
  if (body !== undefined) headers['Content-Type'] = 'application/json';
  const response = await fetch(`${base}/api${path}`, { method, headers, body: body === undefined ? undefined : JSON.stringify(body), signal: AbortSignal.timeout(15000) });
  for (const cookie of response.headers.getSetCookie()) { const pair = cookie.split(';',1)[0]; const index = pair.indexOf('='); cookies.set(pair.slice(0,index),pair.slice(index+1)); }
  if (!response.ok) throw new Error(`${path}: HTTP ${response.status}`);
  return response.status === 204 ? null : response.json();
}
try {
  csrf = (await api('/session')).csrfToken;
  await api('/auth/login','POST',{displayName:process.env.HOME_USERNAME,password:process.env.HOME_PASSWORD}); signedIn = true;
  const session = await api('/session'); csrf = session.csrfToken;
  const [health,runtime,catalog,requests,content,updates] = await Promise.all([api('/health'),api('/operations/runtime'),api('/source-catalog'),api('/requests'),api('/content'),api('/operations/updates')]);
  if (health.version !== '0.27.0' || session.userId !== before.userId || catalog.length < 12) throw new Error('Expected preview/catalog/account was not returned.');
  for (const id of ['yts','eztv','the-pirate-bay','torrents-csv','nyaa-rss','internet-archive','nzbgeek']) if (!catalog.some(provider => provider.id === id)) throw new Error('A source profile is missing.');
  if (!before.requestIds.every(id => requests.some(request => request.id === id)) || !before.contentIds.every(id => content.some(item => item.id === id))) throw new Error('An existing request/content identity was lost.');
  const indexResponse = await fetch(`${base}/`,{cache:'no-store'}); if (!indexResponse.ok) throw new Error('Preview shell missing.'); const index = await indexResponse.text();
  const script = /<script[^>]+src="([^"]+)"/.exec(index)?.[1]; if (!script) throw new Error('Client entry missing.');
  const scriptUrl = new URL(script,base+'/'); if (scriptUrl.origin !== new URL(base).origin) throw new Error('Unexpected client origin.');
  const bundleResponse = await fetch(scriptUrl); if (!bundleResponse.ok) throw new Error('Client bundle missing.'); const bundle = await bundleResponse.text();
  if (!['/source-catalog','/source-setups/test','/source-setups/connect','/discovery/genres','Site seçerek kaynak ekle'].every(value => bundle.includes(value))) throw new Error('New source/genre client is not being served.');
  const workerResponse = await fetch(`${base}/sw.js`,{cache:'no-store'}); if (!workerResponse.ok) throw new Error('Worker missing.'); const worker = await workerResponse.text();
  if (!worker.includes('a31eeb5a8e4325b1')) throw new Error('Unexpected PWA build.');
  const db = new DatabaseSync(join(runtime.statePath,'home.db'),{readOnly:true}); let schema; let foreignKeys;
  try { schema = db.prepare('PRAGMA user_version').get().user_version; foreignKeys = db.prepare('PRAGMA foreign_key_check').all().length; } finally { db.close(); }
  if (schema !== 39 || foreignKeys !== 0) throw new Error('Preview schema/reference validation failed.');
  const result = {verifiedAt:new Date().toISOString(),version:health.version,sourceVersion:'0.27.0-dev.1',schema,sourceProfiles:catalog.length,sourceIds:catalog.map(provider=>provider.id),requestCount:requests.length,preservedRequests:before.requestIds.length,preservedContent:before.contentIds.length,foreignKeyViolations:foreignKeys,clientEntry:script,sourceWizardServed:true,genreFilterServed:true,pwaBuild:'a31eeb5a8e4325b1',rollback:updates.rollback,mediaActionsInitiatedByVerification:0};
  await writeFile(process.env.HOME_SOURCE_VERIFY_OUTPUT ?? 'artifacts/live-sources-v027.json',JSON.stringify(result,null,2)+'\n');
  console.log(JSON.stringify({version:result.version,schema,sourceProfiles:result.sourceProfiles,preservedRequests:result.preservedRequests,sourceWizardServed:true,genreFilterServed:true},null,2));
} finally { if (signedIn) await api('/auth/logout','POST').catch(()=>{}); }
