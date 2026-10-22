import {test} from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import {parseSync,traverse,types as t} from '@babel/core';

test('title and media pages have one personal toolbar inside the hero, not at the bottom',async()=>{
  for(const file of ['ProductViews.tsx','MediaDetails.tsx']){
    const code=await readFile(new URL(`../../web/src/components/${file}`,import.meta.url),'utf8');
    const ast=parseSync(code,{filename:file,babelrc:false,configFile:false,parserOpts:{plugins:['typescript','jsx']}});
    let count=0;
    traverse(ast,{JSXElement(path){
      if(!t.isJSXIdentifier(path.node.openingElement.name,{name:'PersonalContentPanel'}))return;
      count++;
      const placement=path.node.openingElement.attributes.find(value=>t.isJSXAttribute(value)&&value.name.name==='placement');
      assert.equal(placement?.value?.value,'hero');
      const hero=path.findParent(parent=>parent.isJSXElement()&&parent.node.openingElement.attributes.some(value=>t.isJSXAttribute(value)&&value.name.name==='className'&&value.value?.value==='title-hero'));
      assert.ok(hero,`${file}: personal toolbar must be within the hero`);
    }});
    assert.equal(count,1,`${file}: duplicate personal controls`);
  }
});

test('the clearly labeled playback panel shares the tested Home command port with Video.js',async()=>{
  const code=await readFile(new URL('../../web/src/components/Player.tsx',import.meta.url),'utf8');
  assert.match(code,/className="watch-settings-button"[^>]+aria-label="Ses, altyazı ve kalite"/);
  assert.match(code,/<span>Ses, altyazı ve kalite<\/span>/);
  assert.match(code,/mediaPort\.current\.audio\(Number\(event\.target\.value\)\)/);
  assert.match(code,/mediaPort\.current\.subtitle\(event\.target\.value\)/);
  assert.match(code,/mediaPort\.current\.quality\(event\.target\.value\)/);
  assert.match(code,/Bu dosyada tek ses izi var/);
  assert.match(code,/Bu dosyada altyazı yok/);
  assert.match(code,/Gelişmiş Home ayarları/);
});

test('personal actions preserve account data merging and collection ownership filtering',async()=>{
  const code=await readFile(new URL('../../web/src/components/PersonalCatalog.tsx',import.meta.url),'utf8');
  assert.match(code,/\.\.\.personal\.data, \.\.\.changes/);
  assert.match(code,/filter\(value => value\.owned\)/);
  assert.match(code,/isDisabled=\{!collection \|\| add\.isPending\}/);
  assert.match(code,/\/content\/save/);
  assert.match(code,/\/collections\/\$\{collection\}\/items/);
});
