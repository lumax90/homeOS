import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import { readGamePack, gamePackMissingDependencies } from '../../tools/games/game-pack.mjs';

for (const id of ['tiny-crate', 'wrap-it-up']) test(`${id}: reviewed build ships real WASM/game data, matching hashes and licenses without unaudited fonts`, async () => {
  const root = new URL(`../../server/HomeServer.Api/GamePacks/${id}/`, import.meta.url);
  const manifest = JSON.parse((await readFile(new URL('manifest.json', root), 'utf8')).replace(/^\uFEFF/, ''));
  assert.equal(manifest.id, id); assert.equal(manifest.kind, 'web'); assert.match(manifest.sourceCommit, /^[a-f0-9]{40}$/); assert.match(manifest.sourceUrl, /^https:\/\/harmonymonroe\.itch\.io\//);
  for (const file of manifest.files) {
    const bytes = await readFile(new URL(file.path, root)); assert.equal(bytes.length, file.size); assert.equal(createHash('sha256').update(bytes).digest('hex'), file.sha256);
  }
  const wasm = await readFile(new URL('index.wasm', root)); assert.deepEqual([...wasm.subarray(0, 8)], [0, 97, 115, 109, 1, 0, 0, 0]); assert.ok(wasm.length > 1000000);
  const entries = readGamePack(await readFile(new URL('index.pck', root))); const names = entries.map(entry => entry.name); assert.ok(names.some(name => name.endsWith('/home/HomeBridge.gdc'))); assert.ok(names.some(name => name.endsWith('/home/Roboto-Regular.ttf')));
  assert.ok(names.some(name => name.endsWith('project.binary'))); assert.ok(names.some(name => name.endsWith('.tscn')));
  assert.equal(names.some(name => name.includes('/media/font/') && /\.(?:ttf|otf|woff2?)$/.test(name)), false);
  assert.deepEqual(gamePackMissingDependencies(entries), []);
  assert.equal(names.some(name => /(?:steam_api|\.keystore|\.pem|\.exe$)/i.test(name)), false);
  assert.ok((await readFile(new URL('licenses/Game.txt', root), 'utf8')).length > 500);
  assert.match(await readFile(new URL('licenses/Roboto.txt', root), 'utf8'), /Apache License/);
  assert.match(await readFile(new URL('licenses/Godot.txt', root), 'utf8'), /Permission is hereby granted, free of charge/);
  assert.ok((await readFile(new URL('licenses/Godot-third-party.txt', root))).length > 10000);
});
