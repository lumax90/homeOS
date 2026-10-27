import assert from 'node:assert/strict';
import {readFileSync}from'node:fs';
import test from'node:test';

const read=path=>readFileSync(new URL('../../'+path,import.meta.url),'utf8');
test('PSP correction uses the authenticated PUT route with explicit confirmation',()=>{
  const ui=read('web/src/gaming/PspSystemCorrectionAction.tsx'),api=read('server/HomeServer.Api/Gaming/GameEndpoints.cs');
  assert.match(ui,/\/system`,\{system:'psp',confirmed:true\},'PUT'/);
  assert.match(api,/MapPut\("\/games\/\{id\}\/system"/);
  assert.match(ui,/session\.role!=='admin'/);
});
test('game uninstall is confirmed and save-preserving, then polls actual durable status',()=>{
  const ui=read('web/src/gaming/GameUninstallAction.tsx'),details=read('web/src/gaming/Games.tsx');
  assert.match(ui,/confirmed:true,keepSaves:true/);assert.match(ui,/game\.uninstallStatus==='completed'&&!game\.state\.inLibrary/);
  assert.match(details,/'queued','running','applying'\]\.includes\(query\.state\.data\?\.uninstallStatus/);
  assert.doesNotMatch(ui,/keepSaves:false|DELETE/);
});
test('PSP is a separate pinned package and cannot replace the seven-system frontend',()=>{
  const docker=read('Dockerfile'),pin=JSON.parse(read('deploy/psp/runtime.json'));
  assert.equal(pin.version,'1.20.4');assert.equal(pin.sourceCommit,'fa50bb1976065c4f8b1b47af227d367fe9771555');
  assert.equal(pin.memoryAdapter,'linux-anonymous-memfd-v1');assert.match(docker,/COPY --from=psp-proof --chown=0:0 \/app\/PspRuntime/);
  assert.match(docker,/FROM ghcr\.io\/lumax90\/homeos:0\.43\.0@sha256:017e90f/);
  assert.match(docker,/--verify-psp \/app\/NativeRuntime \/app\/PspRuntime/);
});
test('PSP source and native qualification remain explicit release gates',()=>{
  const workflow=read('.github/workflows/publish-container.yml');
  assert.match(workflow,/assert psp\['distribution'\]\['sourceVerified'\]/);
  assert.match(workflow,/assert psp\['distribution'\]\['runtimeVerified'\]/);
  assert.match(workflow,/deploy\/media deploy\/psp/);
});
test('native PSP proof stages the core info exactly as Home does before querying status',()=>{
  const probe=read('tools/Home.NativeLinuxProbe/PspProbe.cs'),runtime=read('server/HomeServer.Api/Gaming/NativeGameManager.cs');
  assert.match(probe,/File\.Copy\(core,stagedCore,false\)/);
  assert.match(probe,/Path\.Combine\(work,"info","ppsspp_libretro\.info"\)/);
  assert.match(probe,/"-L",stagedCore/);
  assert.match(runtime,/File\.Copy\(coreInfo\.Path,Path\.Combine\(engineRoot,"info"/);
});
