import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import { dirname, join, resolve } from 'node:path';

// Filtered source/header download only. Never acquire ROMs, BIOS, media fixtures or a whole source archive.
if (process.argv.length !== 4 || process.argv[2] !== '--output-root') throw new Error('Use --output-root <isolated build cache>');
const root = resolve(process.argv[3]);
const sources = [
  { directory: 'retroarch-source-1222', repository: 'libretro/RetroArch', version: '1.22.2', commit: '69a4f0ea1e8aaf442ae4858f2e7f2b31a1776576', license: 'GPL-3.0',
    select: path => /\.(?:c|h|cpp|cc|cxx|hpp|inl|S|s|asm|mk|sh|py|rc|def|txt|in|m|mm|inc)$/i.test(path) || /(?:^|\/)(?:Makefile[^/]*|COPYING[^/]*|LICENSE[^/]*|README[^/]*)$/.test(path) || ['version.all', 'media/retroarch.ico', 'media/rarch.manifest'].includes(path) },
  { directory: 'ffmpeg-headers-423', repository: 'FFmpeg/FFmpeg', version: '4.2.3', commit: 'd3b963cc41824a3c5b2758ac896fb23e20a87875', license: 'LGPL-2.1-or-later / build-specific GPL',
    select: path => /^(?:libavcodec|libavformat|libavutil|libswscale|libswresample|libavdevice)\/[^/]+\.h$/.test(path) || /^(?:COPYING[^/]*|LICENSE\.md|README\.md)$/.test(path) },
];
const gitHash = bytes => createHash('sha1').update(`blob ${bytes.length}\0`).update(bytes).digest('hex');
for (const source of sources) {
  const response = await fetch(`https://api.github.com/repos/${source.repository}/git/trees/${source.commit}?recursive=1`);
  if (!response.ok) throw new Error('Pinned source tree unavailable: ' + source.repository);
  const tree = await response.json(); if (tree.truncated) throw new Error('Complete source tree required.');
  const files = tree.tree.filter(entry => entry.type === 'blob' && entry.mode === '100644' && source.select(entry.path));
  if (!files.length || files.length > 5000 || files.reduce((sum, file) => sum + file.size, 0) > 128 * 1024 * 1024 ||
      files.some(entry => entry.size > 16 * 1024 * 1024 || entry.path.split('/').some(part => !part || part === '..') || /\.(?:gba|gb|gbc|bin|rom|bios|nes|nds|sfc)$/i.test(entry.path))) throw new Error('Unsafe source selection.');
  const folder = join(root, source.directory); await mkdir(folder, { recursive: true }); let next = 0;
  await Promise.all(Array.from({ length: 8 }, async () => {
    for (;;) {
      const entry = files[next++]; if (!entry) return;
      const path = join(folder, entry.path); let bytes;
      try { bytes = await readFile(path); } catch (error) { if (error.code !== 'ENOENT') throw error; }
      if (!bytes) {
        const download = await fetch(`https://raw.githubusercontent.com/${source.repository}/${source.commit}/${entry.path}`, { signal: AbortSignal.timeout(30000) });
        if (!download.ok) throw new Error('Missing pinned source: ' + entry.path);
        bytes = Buffer.from(await download.arrayBuffer());
        if (bytes.length !== entry.size || gitHash(bytes) !== entry.sha) throw new Error('Pinned source mismatch: ' + entry.path);
        await mkdir(dirname(path), { recursive: true }); await writeFile(path, bytes, { flag: 'wx' });
      } else if (bytes.length !== entry.size || gitHash(bytes) !== entry.sha) throw new Error('Conflicting source preserved: ' + entry.path);
      entry.sha256 = createHash('sha256').update(bytes).digest('hex');
    }
  }));
  const manifest = { version: source.version, commit: source.commit, repository: source.repository, license: source.license,
    files: files.map(entry => ({ path: entry.path, bytes: entry.size, gitBlobSha1: entry.sha, sha256: entry.sha256 })), downloadedRomOrBios: false };
  const text = JSON.stringify(manifest, null, 2), receipt = join(folder, 'home-source-review.json');
  try { await writeFile(receipt, text, { flag: 'wx' }); } catch (error) {
    if (error.code !== 'EEXIST') throw error;
    const previousText = await readFile(receipt, 'utf8');
    if (previousText !== text) {
      const previous = JSON.parse(previousText);
      if (previous.commit !== source.commit || previous.repository !== source.repository || previous.files.some(file => !manifest.files.some(next => JSON.stringify(next) === JSON.stringify(file)))) throw new Error('Conflicting source receipt preserved.');
      const expanded = join(folder, 'home-source-review-expanded.json');
      try { await writeFile(expanded, text, { flag: 'wx' }); } catch (failure) { if (failure.code !== 'EEXIST' || await readFile(expanded, 'utf8') !== text) throw failure; }
    }
  }
  console.log(JSON.stringify({ repository: source.repository, version: source.version, commit: source.commit, files: files.length, downloadedRomOrBios: false }));
}
const fixCommit = 'cfb1204314dd6b061a7b1cda77221735939bf5ba';
const fixTree = await fetch(`https://api.github.com/repos/libretro/RetroArch/git/trees/${fixCommit}?recursive=1`).then(response => response.json());
if (fixTree.truncated) throw new Error('Complete upstream fix tree required.');
const entry = fixTree.tree.find(entry => entry.path === 'input/input_driver.c' && entry.type === 'blob');
const result = await fetch(`https://raw.githubusercontent.com/libretro/RetroArch/${fixCommit}/input/input_driver.c`);
if (!entry || !result.ok) throw new Error('Pinned upstream fix unavailable.');
const bytes = Buffer.from(await result.arrayBuffer());
if (bytes.length !== entry.size || gitHash(bytes) !== entry.sha) throw new Error('Upstream fix identity mismatch.');
const folder = join(root, 'retroarch-upstream-input-fix'); await mkdir(folder, { recursive: true });
for (const [name, content] of [['input_driver.c', bytes], ['home-fix-review.json', JSON.stringify({ commit: fixCommit, path: entry.path, bytes: bytes.length, sha256: createHash('sha256').update(bytes).digest('hex'), gitBlobSha1: entry.sha }, null, 2)]]) {
  try { await writeFile(join(folder, name), content, { flag: 'wx' }); } catch (error) { if (error.code !== 'EEXIST' || !Buffer.from(content).equals(await readFile(join(folder, name)))) throw error; }
}
console.log(JSON.stringify({ upstreamInputFix: fixCommit, installed: false, openedListener: false }));
