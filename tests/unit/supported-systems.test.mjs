import assert from'node:assert/strict';
import{readFileSync}from'node:fs';
import test from'node:test';
const read=path=>readFileSync(new URL('../../'+path,import.meta.url),'utf8');
test('support popover sits directly beside settings and never installs or starts a game',()=>{
  assert.match(read('web/src/gaming/Games.tsx'),/<GamesSettingsButton\/><SupportedSystemsButton\/>/);
  const panel=read('web/src/gaming/SupportedSystemsButton.tsx');
  assert.match(panel,/\/games\/supported-systems/);assert.match(panel,/enabled:open/);
  assert.match(panel,/Henüz desteklenmeyen/);assert.match(panel,/Deneysel \/ hazırlık aşamasında/);
  assert.doesNotMatch(panel,/mutate\(|\/install|native-sessions/);
  assert.match(panel,/action==='back'\|\|action==='menu'/);assert.match(panel,/trigger\.current\?\.focus/);
});
test('source package names cannot silently change the existing DS and 3DS core identities',()=>{
  const pin=JSON.parse(read('deploy/handheld/runtime.json'));
  assert.deepEqual(pin.cores.map(core=>[core.system,core.id]),[['nds','desmume'],['3ds','citra']]);
  assert.equal(typeof pin.distribution.runtimeVerified,'boolean');assert.equal(typeof pin.distribution.sourceVerified,'boolean');
  assert.equal(pin.cores.find(core=>core.id==='citra').experimental,true);
});
