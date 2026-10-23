import { writeFile } from 'node:fs/promises';
import { createHash } from 'node:crypto';

const destination = process.argv[2];
if (!destination) throw new Error('Choose the Watchtower archive destination.');
const url = 'https://github.com/nicholas-fedor/watchtower/releases/download/v1.23.2/watchtower_linux_amd64_1.23.2.tar.gz';
const sha256 = 'e8429673b67a61ff00cd6891c781b744a39f479ada9d8bfddc89c324d11c7ae8';
const response = await fetch(url, { signal: AbortSignal.timeout(120000) });
if (!response.ok || !response.body) throw new Error(`Watchtower software download failed: HTTP ${response.status}`);
const chunks = []; let length = 0;
for await (const chunk of response.body) {
  length += chunk.length;
  if (length > 32 * 1024 * 1024) throw new Error('Watchtower archive exceeds its bound.');
  chunks.push(chunk);
}
const bytes = Buffer.concat(chunks);
if (createHash('sha256').update(bytes).digest('hex') !== sha256) throw new Error('Watchtower release SHA-256 does not match.');
await writeFile(destination, bytes, { flag: 'wx' });
await writeFile(destination + '.json', JSON.stringify({ version: '1.23.2', source: 'https://github.com/nicholas-fedor/watchtower/tree/cd5946be205ea0a60dd31235a11f04ac9851ebf6', archive: url, sha256, license: 'Apache-2.0', modified: false }, null, 2) + '\n', { flag: 'wx' });
console.log('Verified Watchtower 1.23.2 / linux-amd64 / Apache-2.0.');
