import {mkdir,readFile,writeFile,chmod} from 'node:fs/promises';
import {join,dirname,resolve} from 'node:path';
import {createHash} from 'node:crypto';

// Pinned software source files only. No source archives containing ROM/test-media payloads are fetched.
const output=resolve(process.argv[2]??'');if(!process.argv[2])throw Error('Use <isolated source directory>');
const pins=JSON.parse(await readFile(new URL('../deploy/native/sources.json',import.meta.url),'utf8'));
const sha=(bytes,algorithm='sha256')=>createHash(algorithm).update(bytes).digest('hex');
const blob=bytes=>createHash('sha1').update(`blob ${bytes.length}\0`).update(bytes).digest('hex');
const files=[];
function selected(path){return !/(^|\/)(?:\.github|screenshots|roms|bios)(\/|$)/i.test(path)&&(/\.(?:c|h|cpp|cc|cxx|hpp|inl|S|s|asm|mk|sh|py|def|txt|in|inc|cmake|m4|md)$/i.test(path)||/(?:^|\/)(?:Makefile[^/]*|CMakeLists.txt|COPYING[^/]*|LICENSE[^/]*|COPYRIGHT[^/]*|README[^/]*|configure|config.sub|config.guess|version.all|libretro-version-script|link.T)$/.test(path));}
async function download(url){let last;for(let attempt=0;attempt<3;attempt++){try{const response=await fetch(url,{signal:AbortSignal.timeout(60000)});if(!response.ok)throw Error(`HTTP ${response.status}: ${url}`);return Buffer.from(await response.arrayBuffer());}catch(error){last=error;}}throw last;}
for(const source of pins.sources){
  const tree=JSON.parse((await download(`https://api.github.com/repos/${source.repository}/git/trees/${source.commit}?recursive=1`)).toString());if(tree.truncated||!tree.tree)throw Error('Complete pinned source tree required: '+source.id);
  const entries=tree.tree.filter(entry=>entry.type==='blob'&&['100644','100755'].includes(entry.mode)&&selected(entry.path));
  if(!entries.length||entries.length>10000||entries.some(e=>e.size>16*1024*1024||e.path.split('/').some(part=>!part||part==='..'))||entries.reduce((n,e)=>n+e.size,0)>256*1024*1024)throw Error('Software source bounds exceeded.');
  const folder=join(output,source.id);await mkdir(folder,{recursive:true});let next=0;
  await Promise.all(Array.from({length:12},async()=>{for(;;){const entry=entries[next++];if(!entry)return;const path=join(folder,entry.path);let bytes;
    try{bytes=await readFile(path);}catch(error){if(error.code!=='ENOENT')throw error;}
    if(!bytes&&process.env.HOME_NATIVE_SOURCE_CACHE&&source.cache)try{const cached=await readFile(join(process.env.HOME_NATIVE_SOURCE_CACHE,source.cache,entry.path));if(blob(cached)===entry.sha)bytes=cached;}catch{}
    bytes??=await download(`https://raw.githubusercontent.com/${source.repository}/${source.commit}/${entry.path}`);if(bytes.length!==entry.size||blob(bytes)!==entry.sha)throw Error('Pinned source content changed: '+source.id+'/'+entry.path);
    await mkdir(dirname(path),{recursive:true});try{await writeFile(path,bytes,{flag:'wx'});}catch(error){if(error.code!=='EEXIST')throw error;}if(entry.mode==='100755'&&process.platform!=='win32')await chmod(path,0o755);
    entry.sha256=sha(bytes);files.push(source.id+'/'+entry.path);
  }}));
  const receipt={...source,files:entries.map(e=>({path:e.path,bytes:e.size,sha256:e.sha256,gitBlobSha1:e.sha})),softwareOnly:true};await writeFile(join(folder,'home-source.json'),JSON.stringify(receipt,null,2));files.push(source.id+'/home-source.json');console.log(JSON.stringify({source:source.id,commit:source.commit,files:entries.length}));
}
const fix=await download(`https://raw.githubusercontent.com/libretro/RetroArch/${pins.inputFix.commit}/input/input_driver.c`);if(sha(fix)!==pins.inputFix.sha256)throw Error('Pinned input fix mismatch.');await writeFile(join(output,'input-driver-upstream.c'),fix);files.push('input-driver-upstream.c');
const media=await download(pins.mediamtx.url);if(sha(media)!==pins.mediamtx.sha256)throw Error('MediaMTX release SHA256 mismatch.');await writeFile(join(output,'mediamtx.tar.gz'),media);
await writeFile(join(output,'sources.json'),JSON.stringify(pins,null,2));files.push('sources.json');await writeFile(join(output,'source-files.txt'),[...new Set(files)].sort().join('\n')+'\n');
