import { readFile, writeFile } from 'node:fs/promises';
import { patchGodotSandbox } from './godot-sandbox.mjs';
if (process.argv.length !== 3) throw new Error('Usage: patch-godot-runtime.mjs <reviewed Godot index.js>');
const path = process.argv[2];
const source = await readFile(path, 'utf8');
const patched = patchGodotSandbox(source);
if (patched !== source) await writeFile(path, patched);
console.log('Godot 3.6.1 sandbox PWA guards verified.');
