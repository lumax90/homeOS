import { mkdir, writeFile } from 'node:fs/promises';
import { createHash } from 'node:crypto';

// Metadata/source-art review only. Does not download or install a game archive.
const definitions = [
  { id: '2048', title: '2048', publisher: 'Gabriele Cirulli', repository: 'gabrielecirulli/2048', commit: '478b6ec346e3787f589e4af751378d06ded4cbbc', license: 'MIT', licensePath: 'LICENSE.txt', sourceUrl: 'https://play2048.co/', image: 'meta/apple-touch-icon.png', genres: ['puzzle'], adapter: '2048', input: 'digital', estimatedBytes: 2_000_000 },
  { id: 'a-dark-room', title: 'A Dark Room', publisher: 'doublespeak games', repository: 'doublespeakgames/adarkroom', commit: '1fada4620b6c66bd07bf15a3f1eb8223df8bc1d7', license: 'MPL-2.0', licensePath: 'LICENSE.md', sourceUrl: 'https://adarkroom.doublespeakgames.com/', image: 'img/adr.png', genres: ['adventure', 'strategy'], adapter: 'a-dark-room', input: 'cursor', estimatedBytes: 50_000_000 },
  { id: 'hexgl', title: 'HexGL', publisher: 'Thibaut Despoulain (BKcore)', repository: 'BKcore/HexGL', commit: '6addc95a2fce3bf05f4d751823cc054c61a16d68', license: 'MIT', licensePath: 'LICENSE', sourceUrl: 'https://hexgl.bkcore.com/', image: 'icon_256.png', genres: ['racing'], adapter: 'hexgl', input: 'native', estimatedBytes: 100_000_000 },
  { id: 'hextris', title: 'Hextris', publisher: 'Logan Engstrom, Garrett Finucane, Noah Moroze, Michael Yang', repository: 'Hextris/hextris', commit: '3f4847dc8fd7dab3d1c87e6324b9159d92fbd396', license: 'GPL-3.0-or-later', licensePath: 'LICENSE.md', sourceUrl: 'https://hextris.github.io/hextris/', image: 'images/icons/apple-touch-512.png', genres: ['arcade', 'puzzle'], adapter: 'hextris', input: 'digital', estimatedBytes: 5_000_000 },
  { id: 'oh-hi', title: '0h h1 Classic', publisher: 'Q42 / Martin Kool', repository: 'florisluiten/0hh1', commit: '25910e580d6ea6bc1dfbfd29b2637e47844a96f9', license: 'MIT', licensePath: 'LICENSE', sourceUrl: 'https://0hh1.com/', image: 'img/icon-512.png', genres: ['puzzle'], adapter: 'oh-hi', input: 'cursor', estimatedBytes: 2_000_000 }
];
const output = new URL('../server/HomeServer.Api/Gaming/Store/', import.meta.url);
await mkdir(new URL('artwork/', output), { recursive: true });
const games = [];
for (const definition of definitions) {
  const response = await fetch(`https://api.github.com/repos/${definition.repository}/git/trees/${definition.commit}?recursive=1`);
  if (!response.ok) throw new Error(`Source tree lookup failed: ${definition.repository}, HTTP ${response.status}`);
  const tree = await response.json(); if (tree.truncated) throw new Error('A complete reviewed source tree is required.');
  const sourceFiles = tree.tree.filter(entry => entry.type === 'blob' && !entry.path.split('/').some(part => part.startsWith('.')) && !/\.(?:ttf|otf|woff2?|eot|fnt)$/i.test(entry.path) && (/\.(?:html|js|css|json|png|jpe?g|svg|webp|ico|wav|ogg|mp3|txt|md)$/i.test(entry.path) || entry.path === definition.licensePath))
    .map(entry => ({ path: entry.path, size: entry.size, gitBlobSha1: entry.sha }));
  const htmlResponse = await fetch(`https://raw.githubusercontent.com/${definition.repository}/${definition.commit}/index.html`); if (!htmlResponse.ok) throw new Error('Web entry missing.');
  const html = await htmlResponse.text();
  const withoutInline = html.replace(/<script\b(?![^>]*\bsrc=)[^>]*>[\s\S]*?<\/script>/gi, '');
  let scripts = [...withoutInline.matchAll(/<script\b[^>]*\bsrc=["']([^"']+)["'][^>]*>/gi)].map(match => match[1]).filter(path => !/^(?:https?:)?\/\//.test(path));
  if (definition.adapter === 'a-dark-room') scripts.unshift('lib/jquery.min.js');
  if (definition.adapter === 'oh-hi') scripts = ['config.js', 'js/index.js', 'js/jquery-2.1.0.min.js', 'js/utils.js', 'js/state.js', 'js/game.js', 'js/grid.js', 'js/tile.js', 'js/hint.js', 'js/tutorial.js', 'js/levels.js', 'js/backgroundservice.js'];
  const missingScripts = scripts.filter(script => !sourceFiles.some(file => file.path === script));
  if (!sourceFiles.some(file => file.path === 'index.html') || missingScripts.length || !sourceFiles.some(file => file.path === definition.licensePath)) throw new Error(`Incomplete static web build or license for ${definition.id}: ${JSON.stringify(missingScripts)}`);
  const licenseResponse = await fetch(`https://raw.githubusercontent.com/${definition.repository}/${definition.commit}/${definition.licensePath}`); if (!licenseResponse.ok) throw new Error('License missing.');
  const licenseText = await licenseResponse.text(); await writeFile(new URL(`artwork/${definition.id}-LICENSE.txt`, output), licenseText);
  const artResponse = await fetch(`https://raw.githubusercontent.com/${definition.repository}/${definition.commit}/${definition.image}`); if (!artResponse.ok) throw new Error('Reviewed game artwork missing.');
  const artwork = Buffer.from(await artResponse.arrayBuffer()); if (artwork.length > 256 * 1024) throw new Error('Catalog artwork too large.');
  await writeFile(new URL(`artwork/${definition.id}.png`, output), artwork);
  games.push({ ...definition, version: `web-${definition.commit.slice(0, 12)}-home.1`, archiveUrl: `https://codeload.github.com/${definition.repository}/zip/${definition.commit}`, archiveRoot: `${definition.repository.split('/')[1]}-${definition.commit}/`, scripts, sourceFiles,
    artworkSha256: createHash('sha256').update(artwork).digest('hex'), expandedBytes: sourceFiles.reduce((sum, file) => sum + file.size, 0) });
}
await writeFile(new URL('catalog.json', output), JSON.stringify({ format: 1, reviewedAt: '2026-10-19', games }, null, 2) + '\n');
console.log(JSON.stringify(games.map(game => ({ id: game.id, sourceFiles: game.sourceFiles.length, bytes: game.expandedBytes, version: game.version })), null, 2));
