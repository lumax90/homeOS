import { readFile, writeFile, readdir, mkdir, copyFile } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import { deflateSync } from 'node:zlib';
import { fileURLToPath } from 'node:url';
import { build as bundle } from 'vite';

const output = new URL('../server/HomeServer.Api/wwwroot/', import.meta.url);
const assets = (await readdir(new URL('assets/', output))).filter(name => !name.endsWith('.map'));
const shell = ['/', '/home.svg', '/home-192.png', '/home-512.png', '/manifest.webmanifest', ...assets.map(name => `/assets/${name}`)];
const html = await readFile(new URL('index.html', output));
const build = createHash('sha256').update(html).update(JSON.stringify(shell)).digest('hex').slice(0, 16);
const template = await readFile(new URL('../web/service-worker.js', import.meta.url), 'utf8');
const pushController = (await readFile(new URL('../web/push-controller.mjs', import.meta.url), 'utf8')).replace('export function createPushController', 'function createPushController');
const builtWorker = await bundle({ configFile: false, publicDir: false, envDir: false, logLevel: 'error', build: {
  write: false, minify: false, target: 'es2022', lib: { entry: fileURLToPath(new URL('../web/workers/offline.ts', import.meta.url)), formats: ['iife'], name: 'HomeOfflineWorker' },
} });
const offlineWorker = (Array.isArray(builtWorker) ? builtWorker[0] : builtWorker).output.find(chunk => chunk.type === 'chunk').code;

// Raster variants of Home's own SVG mark, using only Node's standard library.
function icon(size) {
  const pixels = Buffer.alloc((size * 4 + 1) * size);
  const segments = [[18,29,32,18],[32,18,46,29],[46,29,46,46],[46,46,18,46],[18,46,18,29]];
  const distance = (x, y, [ax, ay, bx, by]) => {
    const t = Math.max(0, Math.min(1, ((x - ax) * (bx - ax) + (y - ay) * (by - ay)) / ((bx - ax) ** 2 + (by - ay) ** 2)));
    return Math.hypot(x - ax - t * (bx - ax), y - ay - t * (by - ay));
  };
  for (let y = 0; y < size; y++) for (let x = 0; x < size; x++) {
    const px = (x + 0.5) * 64 / size, py = (y + 0.5) * 64 / size;
    const line = Math.min(...segments.map(segment => distance(px, py, segment))) < 2;
    const play = px >= 29 && px <= 39 && Math.abs(py - 35) <= (39 - px) * 0.6;
    const color = line ? [228,238,233] : play ? [181,236,209] : [23,30,41];
    const offset = y * (size * 4 + 1) + 1 + x * 4;
    color.forEach((value, i) => { pixels[offset + i] = value; }); pixels[offset + 3] = 255;
  }
  function chunk(type, data) {
    const bytes = Buffer.concat([Buffer.from(type), data]);
    let crc = 0xffffffff;
    for (const byte of bytes) { crc ^= byte; for (let bit = 0; bit < 8; bit++) crc = (crc >>> 1) ^ (crc & 1 ? 0xedb88320 : 0); }
    const result = Buffer.alloc(data.length + 12);
    result.writeUInt32BE(data.length); bytes.copy(result, 4); result.writeUInt32BE((crc ^ 0xffffffff) >>> 0, result.length - 4);
    return result;
  }
  const header = Buffer.alloc(13); header.writeUInt32BE(size); header.writeUInt32BE(size, 4); header[8] = 8; header[9] = 6;
  return Buffer.concat([Buffer.from([137,80,78,71,13,10,26,10]), chunk('IHDR', header), chunk('IDAT', deflateSync(pixels)), chunk('IEND', Buffer.alloc(0))]);
}
await Promise.all([192,512].map(size => writeFile(new URL(`home-${size}.png`, output), icon(size))));
await writeFile(new URL('sw.js', output), template.replace('__HOME_BUILD__', build).replace('__HOME_SHELL__', JSON.stringify(shell)).replace('__HOME_PUSH_CONTROLLER__', pushController) + '\n' + offlineWorker);
await mkdir(new URL('third-party/', output), { recursive: true });
await writeFile(new URL('third-party/hls.js-LICENSE.txt', output), await readFile(new URL('../node_modules/hls.js/LICENSE', import.meta.url)));
await writeFile(new URL('third-party/i18next-LICENSE.txt', output), await readFile(new URL('../node_modules/i18next/LICENSE', import.meta.url)));
for (const name of ['react','core','media','store','utils','spf','native-hls-video']) {
  await writeFile(new URL(`third-party/videojs-${name}-LICENSE.txt`,output),await readFile(new URL(`../node_modules/@videojs/${name}/LICENSE`,import.meta.url)));
}
await writeFile(new URL('third-party/react-compiler-runtime-LICENSE.txt',output),await readFile(new URL('../docs/licenses/React-compiler-runtime.txt',import.meta.url)));
await writeFile(new URL('third-party/pdfjs-LICENSE.txt',output),await readFile(new URL('../node_modules/pdfjs-dist/LICENSE',import.meta.url)));
for(const folder of ['cmaps','standard_fonts','wasm','iccs']) {
  const source=new URL(`../node_modules/pdfjs-dist/${folder}/`,import.meta.url);const destination=new URL(`pdfjs/${folder}/`,output);await mkdir(destination,{recursive:true});
  for(const file of await readdir(source)) {
    if(file.startsWith('quickjs-')||file.startsWith('Liberation')||file==='LICENSE_LIBERATION')continue;
    await copyFile(new URL(file,source),new URL(file,destination));
  }
}
console.log(`PWA shell: ${shell.length} public files · ${build}`);
