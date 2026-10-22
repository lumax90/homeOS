import {test} from 'node:test';
import assert from 'node:assert/strict';
import {readFile,readdir} from 'node:fs/promises';
import {createElement as h,Fragment} from 'react';
import {parseSync,traverse,types as t} from '@babel/core';
import {transformControls,uiControls} from '../../tools/ui-controls.mjs';
import {transformUi,localizationResources} from '../../tools/ui-localization.mjs';
import {homeSelectOptions,homeSelectValue,homeSelectKey,homeSelectChange} from '../../web/src/selectModel.ts';

test('real option values, zero, empty placeholders, fragments and disabled options retain their identities',()=>{
  const options=homeSelectOptions([
    h('option',{value:''},'Choose'),
    h(Fragment,null,[h('option',{value:0},'Specials'),h('option',{value:720},'720p')]),
    h('option',{value:720},'Duplicate'),
    h('option',{disabled:true,value:'bitmap'},'Bitmap'),
    h('optgroup',{disabled:true},h('option',{value:'group-disabled'},'Unavailable')),
    h('option',null,'Digital Media')
  ]);
  assert.deepEqual(options.map(option=>option.value),['','0','720','bitmap','group-disabled','Digital Media']);
  assert.equal(homeSelectValue(options,720).label,'720p');assert.equal(homeSelectValue(options,0).label,'Specials');
  assert.equal(homeSelectValue(options,'').label,'Choose');assert.equal(homeSelectValue(options,'missing').value,'');
  assert.equal(homeSelectOptions(undefined).length,0);
  assert.equal(homeSelectKey(''),'home-option:');
});
test('selection emits exactly the original value and cannot invoke commands with unknown or disabled keys',()=>{
  const options=homeSelectOptions([h('option',{value:''},'Off'),h('option',{value:3},'English'),h('option',{value:'pgs',disabled:true},'PGS')]);
  assert.deepEqual(homeSelectChange(options,homeSelectKey('3')),{target:{value:'3'}});
  assert.deepEqual(homeSelectChange(options,homeSelectKey('')),{target:{value:''}});
  assert.equal(homeSelectChange(options,homeSelectKey('pgs')),undefined);
  assert.equal(homeSelectChange(options,homeSelectKey('3'),true),undefined);
  assert.equal(homeSelectChange(options,'arbitrary-server-command'),undefined);
  assert.equal(homeSelectChange(options,null),undefined);
});
test('translation changes option labels and accessible names but not stored or playback values',()=>{
  const source=`function View(){return <label><span>Ses / dublaj</span><select value="3" disabled={busy} onChange={event=>mediaPort.current.audio(Number(event.target.value))}><option value="">Varsayılan</option><option value="3">Ses / dublaj</option></select></label>;}`;
  const output=transformUi(transformControls(source,'/src/View.tsx').code,'/src/View.tsx',localizationResources().english).code;
  assert.match(output,/__HomeSelect/);assert.match(output,/aria-label=\{__homeText\("Ses \/ dublaj"\)\}/);
  assert.match(output,/value="3"/);assert.match(output,/value=""/);assert.match(output,/disabled=\{busy\}/);
  assert.match(output,/mediaPort.current.audio\(Number\(event.target.value\)\)/);
});
test('every single-choice control across all application screens uses the same renderer and has a name',async()=>{
  let count=0;let files=0;let nativeMultiple=0;
  async function scan(directory){for(const entry of await readdir(directory,{withFileTypes:true})){
    const url=new URL(entry.name+(entry.isDirectory()?'/':''),directory);if(entry.isDirectory()){await scan(url);continue;}
    if(!entry.name.endsWith('.tsx'))continue;
    const source=await readFile(url,'utf8');const compiled=transformControls(source,url.pathname);
    if(!compiled.count)continue;files++;count+=compiled.count;
    const ast=parseSync(compiled.code,{filename:url.pathname,babelrc:false,configFile:false,parserOpts:{plugins:['typescript','jsx']}});
    traverse(ast,{JSXElement(path){const open=path.node.openingElement;
      if(t.isJSXIdentifier(open.name,{name:'select'})){assert.ok(open.attributes.some(a=>t.isJSXAttribute(a)&&a.name.name==='multiple'));nativeMultiple++;}
      if(!t.isJSXIdentifier(open.name,{name:'__HomeSelect'}))return;
      const attributes=open.attributes.filter(a=>t.isJSXAttribute(a));
      assert.ok(attributes.some(a=>['aria-label','aria-labelledby'].includes(a.name.name)),url.pathname);
      assert.ok(attributes.every(a=>['value','onChange','disabled','required','className','aria-label','aria-labelledby'].includes(a.name.name)),'Unsupported native props must be migrated explicitly.');
      const handler=path.get('openingElement.attributes').find(p=>p.isJSXAttribute()&&p.node.name.name==='onChange')?.get('value.expression');
      if(handler?.isArrowFunctionExpression()){const parameter=handler.node.params[0].name;
        handler.traverse({ReferencedIdentifier(p){if(p.node.name===parameter)assert.ok(p.parentPath.isMemberExpression()&&t.isIdentifier(p.parent.property,{name:'target'}),'Only the typed target.value payload is supported.');},MemberExpression(p){if(p.node.object?.object?.name===parameter)assert.equal(p.node.property.name,'value');}});
      }
    }});
  }}
  // Validate every discovered control above, rather than freezing the number of application screens.
  await scan(new URL('../../web/src/',import.meta.url));assert.ok(count>0);assert.ok(files>0);assert.equal(nativeMultiple,1);
});
test('the control stage is shared by development and production and refuses unlabeled controls',async()=>{
  assert.throws(()=>transformControls('const view=<select value="x"><option>x</option></select>;','/src/View.tsx'),/accessible label/);
  const plugin=uiControls();assert.equal(plugin.enforce,'pre');assert.equal(plugin.transform('<select/>','/outside.tsx'),undefined);
  const vite=await readFile(new URL('../../web/vite.config.ts',import.meta.url),'utf8');
  assert.match(vite,/plugins: \[uiControls\(\), uiLocalization\(\), react\(\)/);
});
test('popover, menu focus, scroll bounds and reduced motion share one material with navigation',async()=>{
  const component=await readFile(new URL('../../web/src/components/HomeSelect.tsx',import.meta.url),'utf8');
  const navigation=await readFile(new URL('../../web/src/components/MainNavigation.tsx',import.meta.url),'utf8');
  const css=await readFile(new URL('../../web/src/components/home-controls.css',import.meta.url),'utf8');
  for(const part of ['Select.Trigger','Select.Value','Select.Indicator','Select.Popover','ListBox.Item','ListBox.ItemIndicator'])assert.ok(component.includes(part));
  assert.match(component,/isDisabled=\{option.disabled\}/);assert.match(component,/textValue=\{option.label\}/);
  assert.match(component,/document.fullscreenElement/);assert.match(navigation,/home-menu-surface home-navigation-popover/);
  assert.match(css,/max-height: min\(520px, 70dvh\)/);assert.match(css,/prefers-reduced-motion: reduce/);
  assert.match(css,/data-focus-visible/);assert.match(css,/\.home-menu-item\[data-selected\]/);
});
test('Video.js menu appearance consumes the same tokens without replacing its control or layout behavior',async()=>{
  const css=await readFile(new URL('../../web/src/components/home-controls.css',import.meta.url),'utf8');
  const installed=await readFile(new URL('../../node_modules/@videojs/react/dist/default/presets/video/skin.css',import.meta.url),'utf8');
  const menu=css.slice(css.indexOf('.home-video-skin .media-menu-popup {'),css.indexOf('@media (max-width: 720px)'));
  for(const token of ['--media-popover','--media-accent','--media-surface-border','--media-menu-item-radius','--media-popup-radius','--media-backdrop-filter-surface','--media-shadow-surface'])assert.ok(installed.includes(token),`Recheck ${token} on Video.js upgrades.`);
  assert.match(menu,/--media-popover: var\(--home-menu-background\)/);
  assert.doesNotMatch(menu,/position:|height:|width:|translate:|transform:|overflow:/);
});
