import {test} from 'node:test';
import assert from 'node:assert/strict';
import {resolveGameDisplay,visibleSettingOptions,type SettingOption} from '../../web/src/gaming/gameSettingsModel.ts';
const option=(key:string,group:SettingOption['group'],common=true,managed=false):SettingOption=>({key,group,common,managed,label:key,info:'',values:['off','on'],value:'off',inheritedValue:'off',source:'default'});
test('game display overrides emulator defaults which override browser profile preferences without losing false/zero',()=>{
  const base={fit:'contain' as const,smooth:true,volume:80};assert.deepEqual(resolveGameDisplay(base,{fit:'integer',smooth:true,volume:50},{smooth:false,volume:0}),{fit:'integer',smooth:false,volume:0});assert.deepEqual(resolveGameDisplay(base,{fit:'integer'},{fit:null}),{fit:'integer',smooth:true,volume:80});assert.deepEqual(base,{fit:'contain',smooth:true,volume:80});
});
test('common settings are grouped and managed paths never appear even in advanced search',()=>{
  const options=[option('resolution','video'),option('audio','audio'),option('clock','compatibility',false),option('save_path','compatibility',false,true)];assert.deepEqual(visibleSettingOptions(options,'video','').map(o=>o.key),['resolution']);assert.deepEqual(visibleSettingOptions(options,'advanced','').map(o=>o.key),['resolution','audio','clock']);assert.deepEqual(visibleSettingOptions(options,'advanced','CLOCK').map(o=>o.key),['clock']);assert.equal(options.length,4);
});
