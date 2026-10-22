export function readGamePack(buffer) {
  if (buffer.length < 88 || buffer.readUInt32LE(0) !== 0x43504447 || buffer.readUInt32LE(4) !== 1) throw new Error('Expected a Godot 3 PCK v1.');
  let offset = 84; const count = buffer.readUInt32LE(offset); offset += 4;
  if (!count || count > 10000) throw new Error('Invalid PCK entry count.');
  const entries = [];
  for (let i = 0; i < count; i++) {
    const length = buffer.readUInt32LE(offset); offset += 4;
    if (!length || length > 2048 || offset + length + 32 > buffer.length) throw new Error('Invalid PCK entry.');
    const name = buffer.toString('utf8', offset, offset + length).replace(/\0/g, ''); offset += length;
    const start = Number(buffer.readBigUInt64LE(offset)), size = Number(buffer.readBigUInt64LE(offset + 8)); offset += 32;
    if (!Number.isSafeInteger(start) || !Number.isSafeInteger(size) || start < 0 || size < 0 || start + size > buffer.length) throw new Error('Invalid PCK file bounds.');
    entries.push({ name, bytes: buffer.subarray(start, start + size) });
  }
  return entries;
}
export function gamePackMissingDependencies(entries) {
  const names = new Set(entries.map(entry => entry.name)); const missing = [];
  for (const entry of entries.filter(entry => /\.(?:tscn|tres)$/.test(entry.name))) {
    for (const match of entry.bytes.toString('utf8').matchAll(/\bpath="(res:\/\/[^"\r\n]+)"/g)) {
      const dependency = match[1];
      if (!names.has(dependency) && !names.has(dependency + '.remap') && !names.has(dependency + '.import')) missing.push({ source: entry.name, dependency });
    }
  }
  return missing;
}
