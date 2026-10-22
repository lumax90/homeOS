import { readFile, mkdir, writeFile } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import { join, resolve } from 'node:path';

// Configure the already reviewed local prototype. No downloads, drivers, default desktop capture or game launch.
if(process.argv.length!==8||process.argv[2]!=='--review-root'||process.argv[4]!=='--core-build'||process.argv[6]!=='--state-path')throw new Error('Use --review-root <cache> --core-build <receipt> --state-path <Home data>');
const root=resolve(process.argv[3]),state=resolve(process.argv[7]);const core=JSON.parse(await readFile(process.argv[5],'utf8'));
if(core.sourceVersion!=='0.10.5'||core.sourceCommit!=='26b7884bc25a5933960f3cdcd98bac1ae14d42e2')throw new Error('Released reviewed core required.');
const retro=JSON.parse(await readFile(join(root,'retroarch','review.json'),'utf8')),media=JSON.parse(await readFile(join(root,'mediamtx','review.json'),'utf8'));
const convert=(id,file)=>({path:join(root,id,'files',file.path),bytes:file.bytes,sha256:file.sha256});
const one=(review,id,name)=>{const files=review.files.filter(file=>file.path.split('/').at(-1)===name);if(files.length!==1)throw new Error('Unique reviewed file required: '+name);return convert(id,files[0]);};
const profile={format:1,enabled:true,coreVersion:core.sourceVersion,coreCommit:core.sourceCommit,retroArch:one(retro,'retroarch','retroarch.exe'),core:{path:core.corePath,bytes:core.coreBytes,sha256:core.coreSha256},coreInfo:one(retro,'retroarch','mgba_libretro.info'),mediaMtx:one(media,'mediamtx','mediamtx.exe'),dependencies:retro.files.map(file=>convert('retroarch',file))};
for(const file of [...profile.dependencies,profile.core,profile.mediaMtx]){const bytes=await readFile(file.path);if(bytes.length!==file.bytes||createHash('sha256').update(bytes).digest('hex')!==file.sha256)throw new Error('Reviewed runtime file changed: '+file.path);}
await mkdir(join(state,'games'),{recursive:true});await writeFile(join(state,'games','native-runtime-profile.json'),JSON.stringify(profile,null,2),{flag:'wx'});
console.log(JSON.stringify({configured:true,localOnly:true,ownerOnly:true,launchedGame:false,statePath:state}));
