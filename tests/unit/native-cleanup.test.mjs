import {test} from 'node:test';
import assert from 'node:assert/strict';
import {mkdtemp,mkdir,writeFile,readFile,readdir,rm} from 'node:fs/promises';
import {createHash} from 'node:crypto';
import {join} from 'node:path';
import {execFile} from 'node:child_process';
import {promisify} from 'node:util';
import {fileURLToPath} from 'node:url';

const run=promisify(execFile),script=fileURLToPath(new URL('../../tools/prune-native-development-builds.mjs',import.meta.url));
const digest=bytes=>createHash('sha256').update(bytes).digest('hex');
async function fixture(){
  const root=await mkdtemp(join(process.env.LOCALAPPDATA,'Temp','opencode','native-cleanup-test-'));
  const review=join(root,'review'),state=join(root,'state'),keep='retroarch-backport-build-'+ 'a'.repeat(32),obsolete='retroarch-backport-build-'+ 'b'.repeat(32);
  await mkdir(join(review,keep),{recursive:true});await mkdir(join(review,obsolete),{recursive:true});await mkdir(join(state,'games','saves'),{recursive:true});await mkdir(join(review,'unrelated-work'));
  const content=Buffer.from('original fixture runtime');const path=join(review,keep,'retroarch.exe');await writeFile(path,content);
  const asset={path,bytes:content.length,sha256:digest(content)};
  for(const name of ['native-runtime-profile','native-runtime-candidate','native-gpu-profile'])await writeFile(join(state,'games',name+'.json'),JSON.stringify({frontend:asset}));
  await writeFile(join(review,obsolete,'home-frontend-build.json'),JSON.stringify({changedSourceFiles:['adapter.c']}));await writeFile(join(review,obsolete,'adapter.c'),'original fixture source');await writeFile(join(review,obsolete,'objects.o'),'disposable fixture object');await writeFile(join(state,'games','saves','owned-save'),'original preserved save');
  return {root,review,state,keep,obsolete,path,args:['--review-root',review,'--state',state]};
}
test('cleanup removes only explicit unused builds and retains exact runtime, save and source history',async()=>{
  const home=await fixture();try{
    const plan=join(home.root,'plan.json');await run(process.execPath,[script,...home.args,'--evidence',plan]);assert.equal(JSON.parse(await readFile(plan)).applied,false);assert((await readdir(home.review)).includes(home.obsolete));
    const report=join(home.root,'applied.json');await run(process.execPath,[script,...home.args,'--apply','--evidence',report]);const result=JSON.parse(await readFile(report));assert.equal(result.removedBuilds.length,1);assert.equal(result.removedBuilds[0].name,home.obsolete);assert.deepEqual(result.keptBuilds,[home.keep]);
    assert.equal(await readFile(home.path,'utf8'),'original fixture runtime');assert.equal(await readFile(join(home.state,'games','saves','owned-save'),'utf8'),'original preserved save');assert.equal(await readFile(join(home.review,'frontend-build-history',home.obsolete,'adapter.c'),'utf8'),'original fixture source');assert((await readdir(home.review)).includes('unrelated-work'));assert(!(await readdir(home.review)).includes(home.obsolete));
  }finally{await rm(home.root,{recursive:true,force:true});}
});
test('changed runtime or existing evidence stops cleanup before deleting anything',async()=>{
  const home=await fixture();try{
    const report=join(home.root,'report.json');await writeFile(report,'existing proof');await assert.rejects(run(process.execPath,[script,...home.args,'--apply','--evidence',report]));assert((await readdir(home.review)).includes(home.obsolete));assert.equal(await readFile(report,'utf8'),'existing proof');
    await writeFile(home.path,'changed runtime');await assert.rejects(run(process.execPath,[script,...home.args,'--apply','--evidence',join(home.root,'new-proof.json')]));assert((await readdir(home.review)).includes(home.obsolete));
  }finally{await rm(home.root,{recursive:true,force:true});}
});
