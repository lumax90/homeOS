import {test} from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import {settingsSections,visibleSettings,findSettings} from '../../web/src/settingsModel.ts';
import {readPagePosition,restorePagePosition,capturePagePosition,findPositionTarget,pagePositionHost} from '../../web/src/pagePosition.ts';
import {discoveryRegion} from '../../web/src/discoveryFeed.ts';
import {controllerDom} from '../helpers/controller-dom.mjs';

test('settings have unique categories and admin/acquisition visibility is independent',()=>{
  assert.equal(new Set(settingsSections.map(section=>section.id)).size,13);
  const viewer=visibleSettings({role:'viewer',canAcquire:false});assert.ok(viewer.some(section=>section.id==='account'));assert.ok(viewer.some(section=>section.id==='interface'));assert.equal(viewer.some(section=>section.group==='server'),false);assert.equal(viewer.some(section=>section.id==='downloads'),false);
  const member=visibleSettings({role:'member',canAcquire:true});assert.ok(member.some(section=>section.id==='downloads'));assert.equal(member.some(section=>section.id==='household'),false);
  assert.equal(visibleSettings({role:'admin',canAcquire:false}).length,13);
});
test('settings search supports English/Turkish keywords and does not disclose hidden categories',()=>{
  const admin={role:'admin',canAcquire:true};assert.equal(findSettings(admin,'altyazi')[0].id,'subtitles');assert.equal(findSettings(admin,'gamepad')[0].id,'interface');assert.equal(findSettings(admin,'password')[0].id,'account');assert.equal(findSettings(admin,'https')[0].id,'operations');
  assert.deepEqual(findSettings({role:'viewer',canAcquire:false},'tmdb'),[]);
});
test('settings retain visited drafts only while open and do not bounce through secondary modals',async()=>{
  const settings=await readFile(new URL('../../web/src/components/SettingsDialog.tsx',import.meta.url),'utf8');
  assert.match(settings,/props.open \? <SettingsContents/);assert.match(settings,/visited.includes\(section.id\)/);assert.match(settings,/hidden=\{section.id !== selected.id\}/);
  for(const panel of ['SourceSettings','SubtitleSettings','HouseholdBody','OperationsBody','LibraryList'])assert.ok(settings.includes(`<${panel}`));
  assert.match(settings,/className="settings-mobile-select"/);assert.match(settings,/<HomeSelect/);assert.match(settings,/role="tablist"/);assert.match(settings,/useControllerCommands\(pane/);
  const app=await readFile(new URL('../../web/src/App.tsx',import.meta.url),'utf8');assert.match(app,/openSettings\('account'\)/);assert.match(app,/onSettings=\{\(\) => openSettings\('metadata'\)\}/);assert.doesNotMatch(app,/<SettingsDialog[^\n]+onOperations=/);
});
test('settings dimensions belong to the positioning frame rather than active panel intrinsic size',async()=>{
  const dialog=await readFile(new URL('../../web/src/components/Dialogs.tsx',import.meta.url),'utf8');
  const css=await readFile(new URL('../../web/src/components/settings.css',import.meta.url),'utf8');
  assert.match(dialog,/ref=\{panel\} className="home-dialog-frame"/);
  assert.match(css,/\.settings-container>\.home-dialog-frame\s*\{[^}]*width: 100%;[^}]*height: 100%/);
  assert.match(css,/\.home-modal-container.settings-container\s*\{[^}]*height: var\(--settings-height\)/);
  assert.match(css,/\.settings-container \.home-dialog\s*\{[^}]*width: 100%;[^}]*height: 100%/);
});
test('legacy history positions migrate and malformed values cannot create unsafe scroll or selectors',()=>{
  assert.equal(readPagePosition({homeScroll:2100,homeFocus:'tmdb:movie:1'}).y,2100);
  const position=readPagePosition({homePosition:{y:Infinity,x:-10,focus:{key:'x'.repeat(1000)},regions:[{id:'safe',x:42,y:-5}]}});
  assert.equal(position.y,0);assert.equal(position.x,0);assert.equal(position.focus,null);assert.deepEqual(position.regions,[{id:'safe',x:42,y:0}]);
});
test('native animation-frame methods retain their Window receiver during restoration cancellation',()=>{
  const previousWindow=globalThis.window,previousCancel=globalThis.cancelAnimationFrame,previousFrame=globalThis.requestAnimationFrame;
  let cancelled=0;const hostWindow={requestAnimationFrame(callback){assert.equal(this,hostWindow);return 42;},cancelAnimationFrame(id){assert.equal(this,hostWindow);assert.equal(id,42);cancelled++;}};
  try{globalThis.window=hostWindow;globalThis.cancelAnimationFrame=hostWindow.cancelAnimationFrame;globalThis.requestAnimationFrame=hostWindow.requestAnimationFrame;
    const host=pagePositionHost();const id=host.frame(()=>{});host.cancelFrame(id);assert.equal(cancelled,1);
  }finally{globalThis.window=previousWindow;globalThis.cancelAnimationFrame=previousCancel;globalThis.requestAnimationFrame=previousFrame;}
});
function positionFixture(){
  let now=0,y=0,maxY=500,documentTop=null,focused=false,watched=true;let id=0;const frames=new Map(),scrolls=[],regions=[];
  const host={now:()=>now,frame:fn=>{frames.set(++id,fn);return id;},cancelFrame:id=>frames.delete(id),viewport:()=>({x:0,y,maxY}),scroll:(x,value)=>{y=value;scrolls.push(value);},restoreRegions:values=>regions.push(values),anchorTop:()=>documentTop===null?undefined:documentTop-y,focus:target=>{if(target&&documentTop===null)return false;focused=true;return true;},watch:callback=>{host.changed=callback;return()=>{watched=false;};}};
  return{host,frames,scrolls,regions,get y(){return y;},get focused(){return focused;},get watched(){return watched;},layout(top,height){documentTop=top;maxY=height;host.changed?.();},tick(time){now=time;const next=[...frames.values()];frames.clear();next.forEach(fn=>fn());}};
}
const position={x:0,y:2100,focus:{key:'movie:1',region:'genre:28'},anchor:{location:{key:'movie:1',region:'genre:28'},top:100},regions:[{id:'genre:28',x:850,y:0}]};
test('back restoration waits for delayed layout and restores the rail before anchor/focus',()=>{
  const f=positionFixture();let finished=0;restorePagePosition(position,f.host,()=>finished++);f.tick(16);assert.equal(f.y,500);assert.equal(f.focused,false);
  f.layout(2200,10000);f.tick(1000);assert.equal(f.y,2100);assert.equal(f.focused,true);assert.deepEqual(f.regions[0],position.regions);
  f.tick(1400);assert.equal(finished,1);assert.equal(f.watched,false);
});
test('late size changes above the selected card keep its viewport offset rather than jumping to the page top',()=>{
  const f=positionFixture();restorePagePosition(position,f.host,()=>{});f.layout(2200,10000);f.tick(16);assert.equal(f.y,2100);
  f.layout(2600,10400);f.tick(150);assert.equal(f.y,2500);f.tick(600);assert.equal(f.watched,false);
});
test('user interruption cancels retries and missing content has a bounded deadline',()=>{
  const f=positionFixture();let finished=0;const stop=restorePagePosition(position,f.host,()=>finished++);f.tick(16);stop();assert.equal(f.frames.size,0);assert.equal(f.watched,false);assert.equal(finished,0);
  const missing=positionFixture();restorePagePosition(position,missing.host,()=>finished++);missing.tick(4100);assert.equal(finished,1);assert.equal(missing.frames.size,0);
});
test('focus auto-scroll cannot override the requested return position',()=>{
  const f=positionFixture();f.layout(2200,10000);f.host.focus=()=>{f.host.scroll(0,0);return true;};restorePagePosition(position,f.host,()=>{});f.tick(16);assert.equal(f.y,2100);
});
test('the same movie in different shelves has distinct scoped restoration and controller identities',()=>{
  const f=controllerDom();Object.assign(globalThis,{scrollX:0,scrollY:2100});
  const first=f.doc.body.add(new f.Element('div',{'data-home-scroll':'trending'}));first.scrollLeft=0;first.scrollTop=0;const a=first.add(new f.Element('a',{href:'/', 'data-home-focus':'tmdb:movie:1'}));a.rect={left:0,right:100,top:30,bottom:130};
  const second=f.doc.body.add(new f.Element('div',{'data-home-scroll':'genre:28'}));second.scrollLeft=850;second.scrollTop=0;const b=second.add(new f.Element('a',{href:'/', 'data-home-focus':'tmdb:movie:1'}));b.rect={left:0,right:100,top:100,bottom:200};b.focus();
  const saved=capturePagePosition();assert.equal(saved.focus.region,'genre:28');assert.equal(saved.anchor.top,100);assert.equal(findPositionTarget(saved.focus),b);assert.equal(saved.regions[1].x,850);
  assert.notEqual(discoveryRegion({provider:'tmdb',kind:'movie',collection:'trending'}),discoveryRegion({provider:'tmdb',kind:'movie',collection:'popular',genre:28}));
});
test('hidden discovery does not recalculate columns at width zero and restore owns focus until user input',async()=>{
  const discovery=await readFile(new URL('../../web/src/components/DiscoveryBrowse.tsx',import.meta.url),'utf8');assert.match(discovery,/if \(width > 0\) setColumns/);assert.match(discovery,/data-home-scroll=\{region\}/);
  const navigation=await readFile(new URL('../../web/src/usePageNavigation.ts',import.meta.url),'utf8');assert.match(navigation,/restorePagePosition\(position/);assert.match(navigation,/cancelRestoration.current\(\)/);assert.match(navigation,/home-navigation-interaction/);assert.match(navigation,/window.addEventListener\('scroll'/);
  const controller=await readFile(new URL('../../web/src/controller/runtime.ts',import.meta.url),'utf8');assert.match(controller,/homeRestoringPosition === 'true'/);assert.match(controller,/dispatchEvent\(new Event\('home-navigation-interaction'\)\)/);
});
