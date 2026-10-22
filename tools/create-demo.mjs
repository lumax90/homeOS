import { createWriteStream, existsSync } from 'node:fs';
import { mkdir } from 'node:fs/promises';
import { spawnSync } from 'node:child_process';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { Readable } from 'node:stream';
import { pipeline } from 'node:stream/promises';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const option = process.argv.indexOf('--ffmpeg');
const ffmpeg = option >= 0 ? process.argv[option + 1] : process.env.Home__FfmpegPath || 'ffmpeg';
const output = join(root, '.data', 'demo-media');
const images = join(root, 'web', 'public', 'demo');
await mkdir(output, { recursive: true });
await mkdir(images, { recursive: true });

const samples = [
  { file: 'sessiz-kiyi', title: 'Sessiz Kıyı', photo: 'photo-1518837695005-2083093ee35b' },
  { file: 'orman', title: 'Ormanın İçinde', photo: 'photo-1441974231531-c6227db76b6e' },
  { file: 'gun-dogarken', title: 'Gün Doğarken', photo: 'photo-1470252649378-9c29740c9fa8' },
  { file: 'uzak-yollar', title: 'Uzak Yollar', photo: 'photo-1500530855697-b586d89ba3ee' },
  { file: 'mavi-saat', title: 'Mavi Saat', photo: 'photo-1519681393784-d120267933ba' },
  { file: 'yeryuzu', title: 'Yeryüzü', photo: 'photo-1464822759023-fed622ff2c3b' },
];

for (const sample of samples) {
  const image = join(images, sample.file + '.jpg');
  if (!existsSync(image)) {
    const response = await fetch(`https://images.unsplash.com/${sample.photo}?auto=format&fit=crop&w=1600&q=85&fm=jpg`);
    if (!response.ok || !response.body) throw new Error(`Image download failed: ${sample.file} (${response.status})`);
    await pipeline(Readable.fromWeb(response.body), createWriteStream(image));
  }
  const video = join(output, sample.title + '.mp4');
  if (existsSync(video)) continue;
  const result = spawnSync(ffmpeg, [
    '-hide_banner', '-loglevel', 'error', '-nostdin', '-y',
    '-loop', '1', '-i', image,
    '-f', 'lavfi', '-i', 'anullsrc=channel_layout=stereo:sample_rate=48000',
    '-t', '20', '-vf', 'scale=1280:-2,format=yuv420p', '-r', '24',
    '-c:v', 'libx264', '-preset', 'ultrafast', '-crf', '25', '-threads', '2',
    '-c:a', 'aac', '-b:a', '64k', '-shortest', '-movflags', '+faststart',
    '-metadata', `title=${sample.title}`, '-metadata', 'comment=Synthetic Home demo clip. Not a full movie.', video,
  ], { encoding: 'utf8', windowsHide: true });
  if (result.error || result.status !== 0) throw new Error(result.error?.message || result.stderr);
  console.log(`Demo clip created: ${sample.title}`);
}
console.log(`Demo media: ${output}`);
