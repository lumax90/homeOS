import { chromium, expect, request } from '@playwright/test';
import { mkdir, writeFile } from 'node:fs/promises';

const baseURL = process.env.HOME_LIVE_URL || 'http://127.0.0.1:5082';
const api = await request.newContext({ baseURL });
let mediaId;
let applied = false;
let browser;

async function sessionHeaders() {
  const session = await (await api.get('/api/session')).json();
  if (!session.preview) throw new Error('Live verification is restricted to an isolated preview server.');
  return { 'X-CSRF-TOKEN': session.csrfToken };
}

try {
  let headers = await sessionHeaders();
  const login = await api.post('/api/auth/login', { headers, data: { displayName: 'Home', password: 'HomePreview2026!' } });
  if (!login.ok()) throw new Error(`Preview sign-in failed: ${login.status()}`);
  headers = await sessionHeaders();
  const system = await (await api.get('/api/system')).json();
  if (!system.dataPath.endsWith('e2e-v02')) throw new Error('Use the separate e2e-v02 data directory.');
  const catalog = await (await api.get('/api/catalog?q=Yeryuzu')).json();
  const media = catalog.items.find(item => item.isDemo && !item.content);
  if (!media) throw new Error('An unmatched demonstration file is required.');
  mediaId = media.id;
  const response = await api.get('/api/metadata/search?provider=tvmaze&kind=show&q=Sherlock');
  if (!response.ok()) throw new Error(`TVmaze search failed: ${response.status()} ${await response.text()}`);
  const results = await response.json();
  const show = results.find(item => item.title === 'Sherlock' && item.releaseDate?.startsWith('2010'));
  if (!show) throw new Error('The intended 2010 show was not returned; no automatic fallback is used.');
  const matchedResponse = await api.put(`/api/media/${mediaId}/match`, { headers, data: {
    provider: 'tvmaze', kind: 'show', providerId: show.providerId, seasonNumber: 1, episodeNumber: 1,
    edition: 'Canlı doğrulama örneği',
  } });
  if (!matchedResponse.ok()) throw new Error(`TVmaze episode resolution failed: ${matchedResponse.status()}`);
  applied = true;
  const matched = await matchedResponse.json();
  if (!matched.content?.episodeTitle || matched.content.seasonNumber !== 1 || matched.content.episodeNumber !== 1)
    throw new Error('Resolved episode identity is incomplete.');
  const artwork = await api.get(`/api/media/${mediaId}/artwork/poster`);
  if (!artwork.ok()) throw new Error(`Artwork proxy failed: ${artwork.status()}`);
  const result = {
    verifiedAt: new Date().toISOString(), provider: 'tvmaze', showId: show.providerId,
    showTitle: show.title, seasonNumber: 1, episodeNumber: 1, episodeTitle: matched.content.episodeTitle,
    artworkStatus: artwork.status(), artworkBytes: (await artwork.body()).length,
    scope: 'Public TVmaze API; temporary match on a synthetic file in isolated e2e-v02 storage.',
    tmdbLiveVerified: false,
  };
  await mkdir('artifacts', { recursive: true });
  await writeFile('artifacts/live-metadata.json', JSON.stringify(result, null, 2));
  browser = await chromium.launch();
  const page = await browser.newPage({ viewport: { width: 1440, height: 1050 }, storageState: await api.storageState() });
  await page.goto(baseURL);
  await page.getByRole('button', { name: `${matched.title} ayrıntıları`, exact: true }).click();
  await page.getByText(matched.content.episodeTitle, { exact: true }).waitFor();
  const image = page.locator('.detail-art img');
  await image.evaluate(async element => { if (!element.complete) await new Promise(resolve => { element.onload = resolve; element.onerror = resolve; }); });
  await expect(page.locator('.detail-art')).toHaveCSS('transform', 'none');
  await page.screenshot({ path: 'artifacts/home-live-metadata.png' });
  console.log(JSON.stringify(result, null, 2));
} finally {
  if (applied) {
    const cleanup = await api.delete(`/api/media/${mediaId}/match`, { headers: await sessionHeaders() });
    if (!cleanup.ok()) throw new Error(`Temporary match cleanup failed: ${cleanup.status()}`);
  }
  await browser?.close();
  await api.dispose();
}
