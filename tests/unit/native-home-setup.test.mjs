import {test} from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';

test('Home native enable is an explicit checked mutation and does not launch, acquire or change OS rules',async()=>{
  const ui=await readFile(new URL('../../web/src/gaming/NativeGames.tsx',import.meta.url),'utf8');
  assert.match(ui,/acceptedPrototype:accepted/);assert.match(ui,/expectedVersion:availability\.data\?\.version/);
  assert.match(ui,/checked=\{accepted\}/);assert.match(ui,/!accepted\|\|!availability\.data\?\.prepared/);
  assert.match(ui,/onPress=\{\(\)=>configure\.mutate/);assert.doesNotMatch(ui,/window\.open|\.exe|New-NetFirewallRule/);
  const stage=await readFile(new URL('../../tools/stage-native-runtime-candidate.mjs',import.meta.url),'utf8');
  assert.match(stage,/format:1,enabled:false/);assert.match(stage,/native-runtime-candidate\.json/);
  assert.match(stage,/proof\.frontendSha256 !== frontend\.executableSha256/);
  assert.doesNotMatch(stage,/native-runtime-profile\.json|https:\/\/|Process\.Start/);
});
test('native player keeps receiver audio/video together and respects browser audio activation without holding up server resume',async()=>{
  const player=await readFile(new URL('../../web/src/gaming/NativeGamePlayer.tsx',import.meta.url),'utf8');
  assert.match(player,/new MediaStream\(\)/);assert.match(player,/media\.addTrack\(event\.track\)/);
  assert.match(player,/muted=\{muted\}/);assert.match(player,/Oyun sesini aç/);
  assert.match(player,/playVideo\(true\);resuming\.current=true/);
  assert.doesNotMatch(player,/await video\.current\?\.play/);
  assert.match(player,/action!=='heartbeat'\|\|result\.paused/);
  assert.match(player,/track=>track\.stop\(\)/);
});
