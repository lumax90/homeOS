import {test} from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import {visibleNavigation,secondarySelection} from '../../web/src/navigationModel.ts';
import {pagePath,parsePageRoute,type Section} from '../../web/src/pageRoutes.ts';

test('Games follows Music and every other screen keeps a unique destination',()=>{
  const nav=visibleNavigation({canDiscover:true,canAcquire:true,role:'admin'});
  assert.deepEqual(nav.primary.map(item=>item.id),['discover','library','music','games']);
  const ids=[...nav.primary,...nav.groups.flatMap(group=>group.items)].map(item=>item.id);
  assert.equal(new Set(ids).size,ids.length);
  assert.deepEqual([...ids].sort(),['discover','library','music','games','live','shelves','tracking','rooms','offline','downloads','activity'].sort());
  assert.equal(ids.includes('home'),false);
  for(const id of ids)assert.deepEqual(parsePageRoute(pagePath({kind:'section',section:id})),{kind:'section',section:id});
});
test('discovery, acquisition and administration visibility are filtered independently',()=>{
  const viewer=visibleNavigation({canDiscover:false,canAcquire:false,role:'viewer'});
  assert.deepEqual(viewer.primary.map(item=>item.id),['library','music','games']);
  assert.equal(viewer.groups.some(group=>group.id==='operations'),false);
  assert.equal(viewer.groups.flatMap(group=>group.items).some(item=>item.id==='downloads'||item.id==='activity'),false);
  const member=visibleNavigation({canDiscover:true,canAcquire:true,role:'member'});
  assert.deepEqual(member.groups.find(group=>group.id==='operations')?.items.map(item=>item.id),['downloads']);
});
test('More indicates the selected secondary screen without adding another primary tab',()=>{
  const nav=visibleNavigation({canDiscover:true,canAcquire:true,role:'admin'});
  for(const id of ['live','shelves','tracking','rooms','offline','downloads','activity'] as Section[])assert.equal(secondarySelection(id,nav.groups)?.id,id);
  assert.equal(secondarySelection('library',nav.groups),null);assert.equal(secondarySelection('home',nav.groups),null);
  assert.equal(nav.primary.length,4);
});
test('the shell uses a grouped accessible dropdown, keeps Home on the brand and avoids absolute header overlap',async()=>{
  const shell=await readFile(new URL('../../web/src/App.tsx',import.meta.url),'utf8');
  const component=await readFile(new URL('../../web/src/components/MainNavigation.tsx',import.meta.url),'utf8');
  const css=await readFile(new URL('../../web/src/components/navigation.css',import.meta.url),'utf8');
  assert.match(shell,/<MainNavigation view=\{view\} access=\{session\}/);assert.doesNotMatch(shell,/visibleNav\.map/);
  assert.match(shell,/className="brand-button"[^\n]+navigate\('home'\)/);
  for(const name of ['Dropdown.Trigger','Dropdown.Popover','Dropdown.Menu','Dropdown.Section','Dropdown.Item','Dropdown.ItemIndicator'])assert.ok(component.includes(name));
  assert.match(component,/selectedKeys=\{selected \? \[selected\.id\] : \[\]\}/);
  assert.match(css,/\.workspace > \.app-header > \.home-navigation\s*\{\s*position: relative/);
  assert.match(css,/max-width: 720px/);assert.match(css,/flex-direction: column/);
});
test('compact navigation does not inherit the legacy fixed-width scrolling rules',async()=>{
  const component=await readFile(new URL('../../web/src/components/MainNavigation.tsx',import.meta.url),'utf8');
  const css=await readFile(new URL('../../web/src/components/navigation.css',import.meta.url),'utf8');
  const legacy=await readFile(new URL('../../web/src/music-catalog.css',import.meta.url),'utf8');
  assert.match(component,/<Glass className="home-navigation" data-navigation="compact-v2"/);
  assert.doesNotMatch(component,/className="navigation home-navigation"/);
  assert.doesNotMatch(legacy,/min-width:\s*690px/);
  assert.match(css,/\.home-navigation > nav[^}]+min-width: 0; width: max-content/);
  const capsule=css.slice(css.indexOf('.workspace > .app-header > .home-navigation {'),css.indexOf('.home-navigation > nav'));
  assert.match(capsule,/overflow: visible/);assert.doesNotMatch(capsule,/overflow: (auto|scroll)/);
});
