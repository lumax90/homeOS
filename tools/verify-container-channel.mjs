const [image, version] = process.argv.slice(2);
if (!/^ghcr\.io\/[a-z0-9_.-]+\/[a-z0-9_.-]+$/.test(image ?? '') || !/^\d+\.\d+\.\d+$/.test(version ?? '')) throw new Error('A GHCR image and stable application version are required.');
const repository = image.slice('ghcr.io/'.length);
const timeout = AbortSignal.timeout(30000);
async function json(url, headers = {}, missing = false) {
  const response = await fetch(url, { headers, signal: timeout });
  if (missing && response.status === 404) return null;
  if (!response.ok) throw new Error(`Public registry verification failed: HTTP ${response.status}`);
  let size = 0; const parts = [];
  for await (const part of response.body) { size += part.length; if (size > 1024 * 1024) throw new Error('Registry metadata exceeds its bound.'); parts.push(part); }
  return JSON.parse(Buffer.concat(parts).toString('utf8'));
}
const token = await json(`https://ghcr.io/token?service=ghcr.io&scope=repository:${repository}:pull`);
const headers = { Authorization: `Bearer ${token.token}`, Accept: 'application/vnd.oci.image.index.v1+json, application/vnd.oci.image.manifest.v1+json, application/vnd.docker.distribution.manifest.list.v2+json, application/vnd.docker.distribution.manifest.v2+json' };
let manifest = await json(`https://ghcr.io/v2/${repository}/manifests/stable`, headers, true);
if (manifest) {
  if (manifest.manifests) {
    const platform = manifest.manifests.find(item => item.platform?.os === 'linux' && item.platform?.architecture === 'amd64');
    if (!platform) throw new Error('The current stable channel has no Linux amd64 image.');
    manifest = await json(`https://ghcr.io/v2/${repository}/manifests/${platform.digest}`, headers);
  }
  if (!/^sha256:[a-f0-9]{64}$/.test(manifest.config?.digest ?? '')) throw new Error('The stable manifest is invalid.');
  const config = await json(`https://ghcr.io/v2/${repository}/blobs/${manifest.config.digest}`, headers);
  const previous = config.config?.Labels?.['org.opencontainers.image.version'];
  if (!/^\d+\.\d+\.\d+$/.test(previous ?? '')) throw new Error('The stable image has no recognized version label.');
  const before = previous.split('.').map(Number), next = version.split('.').map(Number);
  for (let i = 0; i < 3; i++) {
    if (next[i] < before[i]) throw new Error('An older application version cannot replace the stable channel.');
    if (next[i] > before[i]) break;
  }
  console.log(`Stable channel version checked: ${previous} -> ${version}`);
} else console.log(`First stable channel publication: ${version}`);
