import {test} from 'node:test';
import assert from 'node:assert/strict';
import {visibleNativeCores,visibleNativeSystems} from '../../web/src/gaming/nativeSelection.ts';

test('basic, installed and advanced views do not confuse focus with installation or verified compatibility',()=>{
  const cores=[{id:'mgba',categories:['Emulator'],basicSystems:['gb','gbc','gba'],installed:true},{id:'citra',categories:['Emulator'],basicSystems:[],installed:true},{id:'snes9x',categories:['Emulator'],basicSystems:['snes'],installed:false},{id:'tool',categories:['Tool'],basicSystems:[],installed:true}];
  assert.deepEqual(visibleNativeCores(cores,'basic').map(core=>core.id),['mgba','snes9x']);
  assert.deepEqual(visibleNativeCores(cores,'installed').map(core=>core.id),['mgba','citra']);
  assert.deepEqual(visibleNativeCores(cores,'all').map(core=>core.id),['mgba','citra','snes9x']);
  assert.equal(cores.length,4);assert.equal(cores[2].installed,false);
});
test('a selected advanced import remains visible when the toggle closes, without clearing the selection',()=>{
  const systems=[{id:'gba',basic:true},{id:'ps1',basic:true},{id:'3ds',basic:false},{id:'ps2',basic:false}];
  assert.deepEqual(visibleNativeSystems(systems,false,'gba').map(system=>system.id),['gba','ps1']);
  assert.deepEqual(visibleNativeSystems(systems,false,'3ds').map(system=>system.id),['gba','ps1','3ds']);
  assert.deepEqual(visibleNativeSystems(systems,true,'3ds'),systems);assert.equal(systems.length,4);
});
