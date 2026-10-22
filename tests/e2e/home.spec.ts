import { expect, test, type Page } from '@playwright/test';
import { mkdir } from 'node:fs/promises';
import { createHash } from 'node:crypto';

const name = process.env.HOME_E2E_NAME || 'Home';
const password = process.env.HOME_E2E_PASSWORD || 'HomePreview2026!';

async function enter(page: Page) {
  await page.goto('/');
  // Let the app establish its initial CSRF cookie before the test reads session state.
  await expect(page.locator('.welcome, .workspace')).toBeVisible();
  const session = await (await page.request.get('/api/session')).json();
  expect(session.preview, 'Browser tests require the isolated Server -Preview instance.').toBe(true);
  if (await page.locator('.welcome').isVisible()) {
    await page.getByLabel('Adın', { exact: true }).fill(name);
    await page.getByLabel('Parola', { exact: true }).fill(password);
    await page.getByRole('button', { name: /^(Başlayalım|Kütüphaneme git)$/ }).click();
  }
  await expect(page.locator('.workspace')).toBeVisible();
}

async function ensureDemo(page: Page) {
  const libraries = await (await page.request.get('/api/libraries')).json();
  if (!libraries.some((library: { isDemo: boolean }) => library.isDemo)) {
    await page.getByRole('button', { name: 'Kütüphane ekle', exact: true }).click();
    const dialog = page.getByRole('dialog', { name: 'Kütüphane ekle' });
    await expect(dialog).toBeVisible();
    await dialog.getByRole('button', { name: 'Önce örnek kütüphaneyi dene' }).click();
    await expect(dialog.getByLabel('Kütüphane adı', { exact: true })).toHaveValue('Home Örnekleri');
    await dialog.getByRole('button', { name: 'Kütüphaneyi ekle', exact: true }).click();
    await expect(dialog).not.toBeVisible();
  }
  await expect.poll(async () => {
    const jobs = await (await page.request.get('/api/jobs')).json();
    return jobs.some((job: { status: string; processed: number }) => job.status === 'completed' && job.processed === 6);
  }, { timeout: 45_000 }).toBeTruthy();
  await expect(page.locator('.media-card')).toHaveCount(6);
}

test('local account, scan, search, range playback and persistent mini-player', async ({ page }) => {
  await mkdir('artifacts', { recursive: true });
  const errors: string[] = [];
  page.on('pageerror', error => errors.push(error.message));
  await page.goto('/');
  await expect(page.locator('.welcome, .workspace')).toBeVisible();
  if (await page.locator('.welcome-form-panel').isVisible()) {
    await expect(page.locator('.welcome-form-panel')).toHaveCSS('opacity', '1');
    await expect(page.locator('.welcome-copy')).toHaveCSS('opacity', '1');
  }
  await page.screenshot({ path: 'artifacts/home-welcome.png', fullPage: true });
  await enter(page);
  await ensureDemo(page);
  await expect.poll(() => page.locator('.poster img').evaluateAll(images => images.every(image => (image as HTMLImageElement).naturalWidth > 0))).toBeTruthy();
  await page.screenshot({ path: 'artifacts/home-desktop.png', fullPage: true });

  const card = page.locator('.media-card').first();
  const title = await card.locator('.media-card-title').innerText();
  await card.click();
  const details = page.getByRole('dialog', { name: title, exact: true });
  await expect(details).toBeVisible();
  await expect(details.getByText('Başlık dosya adından oluşturuldu.')).toBeVisible();
  await expect.poll(async () => Math.round((await details.boundingBox())!.width)).toBe(710);
  await page.screenshot({ path: 'artifacts/home-detail.png', fullPage: true });
  await details.locator('.detail-play').click();
  const player = page.getByTestId('media-player');
  await expect(player).toBeVisible();
  await expect.poll(() => player.locator('video').evaluate(video => video.currentTime)).toBeGreaterThan(0.1);
  await player.locator('video').evaluate(video => { video.dataset.instance = 'same-video'; });
  const before = await player.locator('video').evaluate(video => video.currentTime);
  await player.getByRole('button', { name: 'Oynatıcıyı küçült' }).click();
  await expect(player).toHaveAttribute('data-minimized', 'true');
  await expect.poll(async () => Math.round((await player.boundingBox())!.height)).toBe(78);
  await expect(player.locator('video')).toHaveAttribute('data-instance', 'same-video');
  await expect.poll(() => player.locator('video').evaluate(video => video.currentTime)).toBeGreaterThan(before);
  await page.screenshot({ path: 'artifacts/home-mini-player.png', fullPage: true });
  await player.getByRole('button', { name: 'Oynatıcıyı genişlet' }).click();
  await expect(player).toHaveAttribute('data-minimized', 'false');
  await expect(player.locator('video')).toHaveAttribute('data-instance', 'same-video');
  const seek = player.getByRole('slider', { name: 'Oynatma konumu' });
  const box = await seek.boundingBox();
  await seek.click({ position: { x: box!.width * 0.42, y: box!.height / 2 } });
  await player.getByRole('button', { name: 'Duraklat', exact: true }).click();
  await expect.poll(() => player.locator('video').evaluate(video => video.currentTime)).toBeGreaterThan(5);
  await player.getByRole('button', { name: 'Oynatıcıyı kapat', exact: true }).click();
  await expect(player).not.toBeVisible();
  await expect(page.getByRole('heading', { name: 'Kaldığın yerden', exact: true })).toBeVisible();
  await page.reload();
  await expect(page.getByRole('heading', { name: 'Kaldığın yerden', exact: true })).toBeVisible();

  await page.getByRole('button', { name: 'Kütüphane', exact: true }).click();
  await page.getByRole('textbox', { name: 'Kütüphanende ara' }).fill('YERYUZU');
  await expect(page.locator('.media-card')).toHaveCount(1);
  await expect(page.locator('.media-card-title')).toHaveText('Yeryüzü');
  await page.getByRole('button', { name: 'Liste görünümü' }).click();
  await expect(page.locator('.media-list-row')).toHaveCount(1);
  await page.screenshot({ path: 'artifacts/home-library.png', fullPage: true });
  await page.getByRole('button', { name: 'Aktivite', exact: true }).click();
  await expect(page.locator('.job-card--completed').first()).toBeVisible();
  await page.screenshot({ path: 'artifacts/home-activity.png', fullPage: true });
  await page.getByRole('button', { name: 'Sunucun ve ayarların', exact: true }).click();
  await page.getByRole('button', { name: 'Oturumu kapat' }).click();
  await expect(page.getByRole('heading', { name: 'Eve dönmek güzel.' })).toBeVisible();
  await enter(page);
  expect(errors).toEqual([]);
});

test.describe('mobile and reduced motion', () => {
  test.use({ viewport: { width: 390, height: 844 }, isMobile: true, hasTouch: true, reducedMotion: 'reduce' });
  test('touch layout, dialog focus, filters and overflow', async ({ page }) => {
    await enter(page);
    await ensureDemo(page);
    await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBeTruthy();
    await page.screenshot({ path: 'artifacts/home-mobile.png', fullPage: true });
    await page.locator('.media-card').first().click();
    const dialog = page.getByRole('dialog');
    await expect(dialog).toBeVisible();
    const dimensions = await dialog.boundingBox();
    expect(dimensions!.width).toBeLessThanOrEqual(390);
    await page.screenshot({ path: 'artifacts/home-mobile-detail.png', fullPage: true });
    await page.keyboard.press('Escape');
    await expect(dialog).not.toBeVisible();
    await page.getByRole('button', { name: 'Kütüphane', exact: true }).click();
    await page.getByRole('button', { name: 'Müzik', exact: true }).click();
    await expect(page.getByRole('heading', { name: 'Henüz bir eşleşme yok.' })).toBeVisible();
    await page.getByRole('button', { name: 'Filtreleri temizle' }).click();
    await expect(page.locator('.media-card')).toHaveCount(6);
    await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBeTruthy();
  });
});

test('metadata connection and matching controls', async ({ page }) => {
  await enter(page);
  await ensureDemo(page);
  await page.locator('.media-card').first().click();
  await page.getByRole('button', { name: 'Bilgileri eşleştir', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Bu dosya hangi içerik?' })).toBeVisible();
  await expect(page.getByLabel('İçerik adı', { exact: true })).not.toHaveValue('');
  await page.getByLabel('Metadata kaynağı').selectOption('tmdb');
  await expect(page.getByText("TMDB için okuma token'ını bağla.")).toBeVisible();
  await page.getByRole('button', { name: 'Ayarları aç' }).click();
  const settings = page.getByRole('dialog', { name: 'Sunucun ve ayarların' });
  await expect(settings.getByLabel("TMDB okuma token'ı")).toBeVisible();
  await expect(settings.getByRole('button', { name: 'Bağlantıyı dene' })).toBeDisabled();
  await settings.getByLabel('Tercih edilen TMDB dili').selectOption('en-US');
  await settings.getByRole('button', { name: 'Kaydet', exact: true }).click();
  await expect.poll(async () => (await (await page.request.get('/api/metadata/settings')).json()).language).toBe('en-US');
  await settings.getByLabel('Tercih edilen TMDB dili').selectOption('tr-TR');
  await settings.getByRole('button', { name: 'Kaydet', exact: true }).click();
  await expect.poll(async () => (await (await page.request.get('/api/metadata/settings')).json()).language).toBe('tr-TR');
  await settings.evaluate(element => { element.scrollTop = element.scrollHeight; });
  await expect(settings.getByRole('button', { name: 'Kapat', exact: true })).toBeInViewport();
  await page.screenshot({ path: 'artifacts/home-metadata-settings.png' });
});

test('built-in torrent upload and persistent transfer controls', async ({ page }) => {
  await enter(page);
  const system = await (await page.request.get('/api/system')).json();
  expect(system.downloadNetwork, 'Transfer UI tests require a loopback-only test server.').toBe('loopback');
  await page.getByRole('button', { name: 'İndirmeler', exact: true }).click();
  await page.getByRole('button', { name: 'İndirme ekle', exact: true }).click();
  const dialog = page.getByRole('dialog', { name: 'İndirme ekle' });
  await dialog.getByLabel('Magnet bağlantısı', { exact: true }).fill('not-a-magnet');
  await dialog.getByRole('button', { name: 'İndirmeyi başlat' }).click();
  await expect(dialog.getByRole('alert')).toContainText('Geçerli bir magnet');
  await dialog.getByRole('button', { name: 'Torrent dosyası', exact: true }).click();
  const title = `Home UI test ${Date.now()} (sentetik).mp4`;
  const content = Buffer.from('synthetic UI fixture, no peers or trackers');
  const torrent = bencode({ info: { name: title, length: content.length, 'piece length': 16384, pieces: createHash('sha1').update(content).digest(), private: 1 } });
  await dialog.getByLabel('Torrent dosyası seç').setInputFiles({ name: 'home-ui-test.torrent', mimeType: 'application/x-bittorrent', buffer: torrent });
  await dialog.getByRole('button', { name: 'İndirmeyi başlat' }).click();
  await expect(dialog).not.toBeVisible();
  const row = page.locator('.transfer-card').filter({ hasText: title });
  await expect(row).toBeVisible();
  await row.getByRole('button', { name: `${title} duraklat`, exact: true }).click();
  await expect(row.locator('.transfer-state')).toHaveText('Duraklatıldı');
  await page.reload();
  await page.getByRole('button', { name: 'İndirmeler', exact: true }).click();
  await expect(row.locator('.transfer-state')).toHaveText('Duraklatıldı');
  await row.getByRole('button', { name: `${title} devam et`, exact: true }).click();
  await expect(row.getByRole('button', { name: `${title} duraklat`, exact: true })).toBeVisible();
  await row.getByRole('button', { name: `${title} iptal et`, exact: true }).click();
  await expect(row.locator('.transfer-state')).toHaveText('İptal edildi');
  await page.screenshot({ path: 'artifacts/home-downloads.png', fullPage: true });
});

function bencode(value: string | number | Buffer | Record<string, unknown>): Buffer {
  if (typeof value === 'number') return Buffer.from(`i${value}e`);
  if (typeof value === 'string' || Buffer.isBuffer(value)) {
    const bytes = Buffer.isBuffer(value) ? value : Buffer.from(value);
    return Buffer.concat([Buffer.from(`${bytes.length}:`), bytes]);
  }
  return Buffer.concat([Buffer.from('d'), ...Object.keys(value).sort().flatMap(key => [bencode(key), bencode(value[key] as Parameters<typeof bencode>[0])]), Buffer.from('e')]);
}
