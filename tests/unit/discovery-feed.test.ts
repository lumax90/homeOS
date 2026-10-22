import {test} from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import {discoveryColumns, discoveryNext, personalNext, pausedEmptyPages, uniqueDiscovery} from '../../web/src/discoveryFeed.ts';

test('responsive columns grow with available width instead of a fixed twenty-card window',()=>{
  assert.equal(discoveryColumns(320),2);assert.equal(discoveryColumns(700),4);assert.equal(discoveryColumns(1400),8);assert.equal(discoveryColumns(4000),10);
});
test('provider cursors, page caps and empty-batch brakes prevent repeated or unbounded automatic requests',()=>{
  assert.equal(discoveryNext({page:1,hasMore:true,nextPage:5}),5);
  assert.equal(discoveryNext({page:5,hasMore:true,nextPage:5}),undefined);
  assert.equal(discoveryNext({page:500,hasMore:true}),undefined);
  assert.equal(discoveryNext({page:1,hasMore:false}),undefined);
  assert.equal(pausedEmptyPages([{items:[]},{items:[]},{items:[]}]),true);
  assert.equal(pausedEmptyPages([{items:[]},{items:[1]},{items:[]}]),false);
  assert.equal(personalNext({offset:30,limit:30,total:80}),60);
  assert.equal(personalNext({offset:5000,limit:30,total:10000}),undefined);
});
test('different provider/type identities survive deduplication while repeated pages do not duplicate cards',()=>{
  const movie={provider:'tmdb',kind:'movie',providerId:1};const show={...movie,kind:'show'};const other={...show,provider:'tvmaze'};
  assert.equal(uniqueDiscovery([movie,movie,show,other]).length,3);
});
test('shelves fetch lazily, labels follow UI language, hidden discovery cannot auto-load and images are lazy',async()=>{
  const code=await readFile(new URL('../../web/src/components/DiscoveryBrowse.tsx',import.meta.url),'utf8');
  assert.match(code,/enabled: visible\.near/);assert.match(code,/DeferredResultsGrid/);assert.match(code,/loading="lazy"/);
  assert.match(code,/language: genreLanguage/);assert.match(code,/genreLanguage: input\.genreLanguage/);
  assert.match(code,/useAutomaticPage\(active &&/);assert.match(code,/cancelRefetch: false/);
  assert.doesNotMatch(code,/items\.slice\(0, visible\)|setVisible\(40\)/);
  assert.match(code,/shelf="history"/);assert.match(code,/shelf="watchlist"/);assert.match(code,/shelf="continue"/);
});
test('Home loading placeholders do not share HeroUI skeleton class or transform keyframes',async()=>{
  const css=await readFile(new URL('../../web/src/styles.css',import.meta.url),'utf8');
  const native=await readFile(new URL('../../node_modules/@heroui/styles/dist/themes/shared/theme.css',import.meta.url),'utf8');
  assert.match(css,/\.home-skeleton\s*\{[^}]*animation:\s*home-skeleton-shimmer/);
  assert.doesNotMatch(css,/(?:^|\n)\.skeleton\s*\{|@keyframes\s+skeleton\s*\{/);
  const frame=css.slice(css.indexOf('@keyframes home-skeleton-shimmer'),css.indexOf('@media',css.indexOf('@keyframes home-skeleton-shimmer')));
  assert.match(frame,/background-position:\s*-200% 0/);assert.doesNotMatch(frame,/transform:|translate:/);
  assert.match(native,/@keyframes skeleton\s*\{[^}]*transform:\s*translateX\(200%\)/);
});
test('all owned loading sites are namespaced and discovery keeps horizontal scrolling inside its rails',async()=>{
  let count=0;
  for(const file of ['App.tsx','components/DiscoveryBrowse.tsx','components/Downloads.tsx','components/MediaDetails.tsx','components/ProductViews.tsx','gaming/Games.tsx']){
    const code=await readFile(new URL('../../web/src/'+file,import.meta.url),'utf8');
    for(const [,classes] of code.matchAll(/className="([^"]+)"/g)){assert.ok(!classes.split(/\s+/).includes('skeleton'),file);if(classes.split(/\s+/).includes('home-skeleton'))count++;}
  }
  assert.equal(count,14);
  const css=await readFile(new URL('../../web/src/components/discovery.css',import.meta.url),'utf8');
  assert.match(css,/\.discovery-rail\{[^}]*overflow-x:auto/);
  assert.doesNotMatch(css,/\.discovery-browse\s*\{[^}]*overflow(?:-x)?:\s*(?:hidden|clip)/);
});
