import { readFile } from 'node:fs/promises';
import { createHash } from 'node:crypto';

const root = new URL('../', import.meta.url);
const lock = JSON.parse(await readFile(new URL('deploy/native/runtime-lock.json', root), 'utf8'));
if (lock.format !== 1 || lock.platform !== 'linux-x64' || !/^ghcr\.io\/lumax90\/homeos:\d+\.\d+\.\d+@sha256:[a-f0-9]{64}$/.test(lock.image)) throw new Error('A reviewed immutable native-runtime image is required.');
for (const [path, expected] of Object.entries(lock.inputs)) {
  if (path.startsWith('/') || path.split('/').includes('..')) throw new Error('Invalid native input path.');
  const actual = createHash('sha256').update(await readFile(new URL(path, root))).digest('hex');
  if (actual !== expected) throw new Error(`Native software input changed: ${path}. Verify and publish a compatible runtime before updating its lock.`);
}
const dockerfile = await readFile(new URL('Dockerfile', root), 'utf8');
if (!dockerfile.includes(`FROM ${lock.image} AS native-runtime`)) throw new Error('Docker native-runtime pin and reviewed lock differ.');
const manifest = JSON.parse(await readFile(process.argv[2] ?? new URL('runtime-manifest.json', root), 'utf8'));
if (manifest.platform !== lock.platform || manifest.frontend.sha256 !== lock.assets.frontend || manifest.mediaMtx.sha256 !== lock.assets.mediaMtx) throw new Error('Native host identity differs from the reviewed runtime.');
for (const core of manifest.cores) if (lock.assets[core.id] !== core.binary.sha256) throw new Error(`Native core identity changed: ${core.id}`);
if (manifest.cores.length !== Object.keys(lock.assets).length - 2) throw new Error('Native core inventory changed.');
console.log('Verified immutable native runtime, source inputs and existing save/settings identities.');
