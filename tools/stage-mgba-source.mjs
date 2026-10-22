import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import { resolve, dirname, join } from 'node:path';

// Build source only. Upstream cinema/ contains ROM tests, so a complete source archive is deliberately NOT downloaded.
if (process.argv.length !== 4 || process.argv[2] !== '--output-root') throw new Error('Use --output-root <isolated source cache>');
const root = resolve(process.argv[3]), commit = '26b7884bc25a5933960f3cdcd98bac1ae14d42e2';
const response = await fetch(`https://api.github.com/repos/mgba-emu/mgba/git/trees/${commit}?recursive=1`);
if (!response.ok) throw new Error('Pinned mGBA source tree unavailable.');
const tree = await response.json(); if (tree.truncated) throw new Error('Complete source tree required.');
const files = tree.tree.filter(entry => entry.type === 'blob' && !entry.path.startsWith('cinema/') && !entry.path.startsWith('opt/') &&
  (/\.(?:c|h|cpp|cc|cxx|hpp|cmake|txt|in|s|S|asm|py|sh|rc|manifest|glsl|fs|vs|json)$/i.test(entry.path) || /(?:^|\/)(?:LICENSE|COPYING|VERSION|README(?:\.md)?)$/.test(entry.path)));
if (files.length > 3000 || files.some(entry => entry.path.split('/').some(part => !part || part === '..')) || files.some(entry => /\.(?:gba|gb|gbc|bin|nds)$/i.test(entry.path))) throw new Error('Unsafe build-source selection.');
await mkdir(root, { recursive: true });
let next = 0;
await Promise.all(Array.from({ length: 6 }, async () => {
  for (;;) {
    const index = next++; if (index >= files.length) return;
    const entry = files[index], path = join(root, entry.path); let bytes;
    try { bytes = await readFile(path); } catch (error) { if (error.code !== 'ENOENT') throw error; }
    const gitHash = value => createHash('sha1').update(`blob ${value.length}\0`).update(value).digest('hex');
    if (bytes) { if (bytes.length !== entry.size || gitHash(bytes) !== entry.sha) throw new Error('Conflicting source file was preserved: ' + entry.path); }
    else {
      const result = await fetch(`https://raw.githubusercontent.com/mgba-emu/mgba/${commit}/${entry.path}`); if (!result.ok) throw new Error('Missing build source: ' + entry.path);
      bytes = Buffer.from(await result.arrayBuffer()); if (bytes.length !== entry.size || gitHash(bytes) !== entry.sha) throw new Error('Pinned source identity mismatch: ' + entry.path);
      await mkdir(dirname(path), { recursive: true }); await writeFile(path, bytes, { flag: 'wx' });
    }
    entry.sha256 = createHash('sha256').update(bytes).digest('hex');
  }
}));
const manifest = { version: '0.10.5', commit, license: 'MPL-2.0', files: files.map(entry => ({ path: entry.path, bytes: entry.size, gitBlobSha1: entry.sha, sha256: entry.sha256 })), downloadedRomOrBios: false };
try { await writeFile(join(root, 'home-source-review.json'), JSON.stringify(manifest, null, 2), { flag: 'wx' }); } catch (error) { if (error.code !== 'EEXIST') throw error; }
console.log(JSON.stringify({ version: manifest.version, commit, sourceFiles: files.length, downloadedRomOrBios: false }));
