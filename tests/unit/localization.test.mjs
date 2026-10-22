import {test} from 'node:test';
import assert from 'node:assert/strict';
import {readFile,readdir} from 'node:fs/promises';
import {fileURLToPath} from 'node:url';
import {join} from 'node:path';
import {runInNewContext} from 'node:vm';
import {parseSync,traverse} from '@babel/core';
import {build} from 'vite';
import {localizationResources,normalizeText,transformUi,uiLocalization} from '../../tools/ui-localization.mjs';
import {collectUiMessages,uncoveredUiMessages} from '../../tools/ui-message-audit.mjs';
import {settingsSections} from '../../web/src/settingsModel.ts';

const resources=localizationResources();
const source=JSON.parse(await readFile(new URL('../../web/src/i18n/source-messages.json',import.meta.url),'utf8'));
const translations=JSON.parse(await readFile(new URL('../../web/src/i18n/en.json',import.meta.url),'utf8'));
const neutral=JSON.parse(await readFile(new URL('../../web/src/i18n/neutral-messages.json',import.meta.url),'utf8'));

test('the interface catalog is complete and preserves interpolation parameters',()=>{
  assert.equal(Object.keys(translations).length,source.length);
  const parameters=value=>[...value.matchAll(/\{\{([^}]+)\}\}/g)].map(match=>match[1]).sort();
  for(const row of source){
    assert.ok(Object.hasOwn(resources.english,row.text),row.text);
    assert.ok(resources.english[row.text].trim(),row.text);
    assert.deepEqual(parameters(resources.english[row.text]),parameters(row.text),row.text);
  }
});

test('semantic UI messages across the application have translations even without Turkish characters',async()=>{
  const missing=new Set();
  async function walk(root){
    for(const file of await readdir(root,{withFileTypes:true})){
      const name=join(root,file.name);
      if(file.isDirectory()){if(file.name!=='i18n')await walk(name);continue;}
      if(!/\.tsx?$/.test(name)||name.endsWith('.d.ts'))continue;
      for(const message of uncoveredUiMessages(await readFile(name,'utf8'),name,resources.english,neutral))missing.add(`${name}:${message.line}: ${message.key}`);
    }
  }
  await walk(fileURLToPath(new URL('../../web/src/',import.meta.url)));
  assert.deepEqual([...missing],[]);
});
test('new untranslated ASCII UI text fails the build, while transport identifiers are not UI messages',()=>{
  const plugin=uiLocalization();
  assert.throws(()=>plugin.transform('function View(){return <h1>YeniMenuBasligi</h1>;}','/web/src/View.tsx'),/Missing UI translations/);
  assert.deepEqual(collectUiMessages(`const command={action:'resume',language:'tr',id:'settings'};`,'/web/src/command.ts'),[]);
  assert.doesNotThrow(()=>plugin.transform(`function View(){return <button value="resume" aria-label="Kaydet">Kaydet</button>;}`,'/web/src/View.tsx'));
});

// Exercise the built runtime, including storage and the document language, without a browser.
const output=await build({configFile:false,publicDir:false,envDir:false,logLevel:'silent',plugins:[uiLocalization()],build:{write:false,minify:false,target:'es2022',lib:{entry:fileURLToPath(new URL('../../web/src/i18n/index.ts',import.meta.url)),formats:['es']}}});
const code=(Array.isArray(output)?output[0]:output).output.find(item=>item.type==='chunk').code;
const storage=new Map();const workerMessages=[];
Object.assign(globalThis,{document:{documentElement:{},querySelector:selector=>selector==='meta[name="home-base"]'?{content:'/home'}:null},window:new EventTarget(),localStorage:{getItem:key=>storage.get(key)??null,setItem:(key,value)=>storage.set(key,value)}});
Object.defineProperty(globalThis,'navigator',{configurable:true,value:{serviceWorker:{controller:{postMessage:message=>workerMessages.push(message)},addEventListener(){}}}});
const runtime=await import('data:text/javascript;base64,'+Buffer.from(code).toString('base64'));

test('default English uses English for every settings heading and translated messages switch both ways',async()=>{
  await runtime.setUiLanguage('en');
  for(const [key,english] of [['Ayarlar','Settings'],['Oynatma','Playback'],['Bildirimler','Notifications'],['Film ve dizi bilgileri','Movie and TV metadata']]){
    assert.equal(runtime.translate(key),english);assert.equal(runtime.runtimeText(key),english);
  }
  const messages=['Settings','Playback','Notifications'];
  await runtime.setUiLanguage('tr');assert.deepEqual(messages.map(message=>runtime.runtimeText(message)),['Ayarlar','Oynatma','Bildirimler']);
  await runtime.setUiLanguage('en');assert.deepEqual(messages.map(message=>runtime.runtimeText(message)),messages);
});
test('every settings category and description follows the selected interface language',async()=>{
  await runtime.setUiLanguage('en');
  for(const section of settingsSections){assert.equal(runtime.translate(section.label),resources.english[section.label]);assert.notEqual(runtime.translate(section.label),section.label);assert.equal(runtime.translate(section.description),resources.english[section.description]);}
  await runtime.setUiLanguage('tr');for(const section of settingsSections){assert.equal(runtime.translate(section.label),section.label);assert.equal(runtime.translate(section.description),section.description);}
  await runtime.setUiLanguage('en');
});

test('language switches persist per base path and preserve user/provider text',async()=>{
  assert.equal(runtime.getUiLanguage(),'en');
  assert.equal(runtime.translate('Kütüphane'),'Library');
  assert.equal(runtime.translate('{{v0}} için kaynak ara',{v0:'İstanbul {{private}} <film>'}),'Search sources for İstanbul {{private}} <film>');
  const userText='A user title\n  with deliberate spacing {{private}}';
  assert.equal(runtime.runtimeText(userText),userText);
  assert.equal(runtime.runtimeText(3),3);
  await runtime.setUiLanguage('tr');
  assert.equal(storage.get('home.ui-language:/home/'),'tr');
  assert.equal(document.documentElement.lang,'tr');
  assert.equal(runtime.uiLocale(),'tr-TR');
  assert.equal(runtime.translate('Kütüphane'),'Kütüphane');
  assert.deepEqual(workerMessages.at(-1),{type:'home.language',language:'tr'});
  storage.set('home.ui-language:/home/','en');
  const changed=new Event('storage');Object.assign(changed,{key:'home.ui-language:/home/'});window.dispatchEvent(changed);
  await Promise.resolve();
  assert.equal(runtime.getUiLanguage(),'en');
  assert.equal(document.documentElement.lang,'en');
  assert.equal(runtime.runtimeText('Torrent bilgileri alınıyor.'),'Fetching torrent metadata.');
});

test('module-level navigation and option labels change without changing transport values',async()=>{
  const input=`const nav = [{id:'library',label:'Kütüphane'},{id:'downloads',label:'İşlemler'}];
    const options = [['movie','Film'],['show','Dizi']];
    const command = {method:'POST',body:JSON.stringify({action:'resume',language:'tr',source:'torrent'})};
    globalThis.result = {nav,options,command,render: value => ({title:value.title,label:'Kaynaklar',details:\`\${value.title} için kaynak ara\`})};`;
  const transformed=transformUi(input,'/web/src/fixture.ts');
  const evaluated=transformed.code.replace(/^import[\s\S]*?;\n/,'');
  const context={__homeText:runtime.translate};runInNewContext(evaluated,context);
  await runtime.setUiLanguage('en');
  assert.equal(context.result.nav[0].label,'Library');
  assert.equal(context.result.options[0][1],'Movie');
  const title='Kütüphane';assert.equal(context.result.render({title}).title,title);
  assert.equal(context.result.render({title}).details,'Search sources for Kütüphane');
  const before=JSON.stringify(context.result.command);
  await runtime.setUiLanguage('tr');
  assert.equal(context.result.nav[0].label,'Kütüphane');
  assert.equal(context.result.options[0][1],'Film');
  assert.equal(JSON.stringify(context.result.command),before);
  assert.equal(context.result.nav[0].id,'library');
  await runtime.setUiLanguage('en');
});

test('JSX translation subscribes before early returns and preserves controlled inputs',()=>{
  const transformed=transformUi(`function Screen({hidden,title,value}) {if(hidden)return null;return <section><h1>Kaynaklar</h1><input value={value} placeholder="Kaynak adı" aria-label={\`\${title} ayrıntıları\`}/><p>{title}</p><p className="form-error">{3} {value.message}</p></section>;}`,'/web/src/fixture.tsx');
  const ast=parseSync(transformed.code,{babelrc:false,configFile:false,parserOpts:{plugins:['jsx']}});
  const component=ast.program.body.find(node=>node.type==='FunctionDeclaration');
  assert.equal(component.body.body[0].expression.callee.name,'__homeUseLanguage');
  assert.equal(component.body.body[1].type,'IfStatement');
  const values=[];traverse(ast,{JSXAttribute(path){if(path.node.name.name==='value')values.push(path.node.value.expression.name);}});
  assert.deepEqual(values,['value']);
  assert.match(transformed.code,/<p>\{title\}<\/p>/);
  assert.match(transformed.code,/__homeText\("Kaynaklar"\)/);
});
