import { readFile } from 'node:fs/promises';
import { readGamePack, gamePackMissingDependencies } from './game-pack.mjs';
if (process.argv.length !== 3) throw new Error('Usage: verify-game-pack.mjs <reviewed Godot PCK>');
const entries = readGamePack(await readFile(process.argv[2]));
const missing = gamePackMissingDependencies(entries);
if (missing.length) throw new Error('Missing game runtime dependencies: ' + JSON.stringify(missing));
if (entries.some(entry => entry.name.includes('/media/font/') && /\.(?:ttf|otf|woff2?)$/.test(entry.name))) throw new Error('An unreviewed source font was included.');
console.log(`Verified ${entries.length} PCK files; scene/resource dependencies are closed and source font binaries are excluded.`);
