import {readdir,readFile,writeFile,mkdir,lstat,copyFile,unlink,rmdir} from 'node:fs/promises';
import {createReadStream,constants} from 'node:fs';
import {createHash} from 'node:crypto';
import {resolve,join,relative,dirname} from 'node:path';

// Narrow, explicit development-cache cleanup. Never removes a runtime asset, ROM, save, source checkout or toolchain.
const options=new Map();let apply=false;
for(let i=2;i<process.argv.length;i++){if(process.argv[i]==='--apply'){apply=true;continue;}const key=process.argv[i];if(!['--review-root','--state','--evidence'].includes(key)||options.has(key)||!process.argv[i+1]||process.argv[i+1].startsWith('--'))throw new Error('Unknown, duplicate or incomplete cleanup option.');options.set(key,process.argv[++i]);}
const reviewRoot=options.get('--review-root')??(process.env.LOCALAPPDATA?join(process.env.LOCALAPPDATA,'Temp','opencode','native-gaming-runtime-v034-review'):null);
if(!reviewRoot)throw new Error('Specify --review-root when LOCALAPPDATA is unavailable.');
const root=resolve(reviewRoot);
const state=resolve(options.get('--state')??'.data/preview');
const evidence=resolve(options.get('--evidence')??'artifacts/native-build-cleanup-v038.json');
const name=/^retroarch-backport-build-[a-f0-9]{32}$/;
try{await lstat(evidence);throw new Error('Existing cleanup evidence is preserved; choose a new report path.');}catch(error){if(error.code!=='ENOENT')throw error;}
async function noLink(path){if((await lstat(path)).isSymbolicLink())throw new Error('Link encountered; cleanup stopped: '+path);}
async function hash(path){const digest=createHash('sha256');for await(const chunk of createReadStream(path))digest.update(chunk);return digest.digest('hex');}
await noLink(root);await noLink(state);
const profiles=[];
for(const file of ['native-runtime-profile.json','native-runtime-candidate.json','native-gpu-profile.json']){
  const path=join(state,'games',file);await noLink(path);profiles.push({path,sha256:await hash(path),data:JSON.parse(await readFile(path,'utf8'))});
}
const assets=[];
function collect(value){if(!value||typeof value!=='object')return;if(typeof value.path==='string'&&typeof value.sha256==='string'&&typeof value.bytes==='number')assets.push(value);for(const child of Object.values(value))if(typeof child==='object')collect(child);}
profiles.forEach(profile=>collect(profile.data));
for(const asset of assets){await noLink(asset.path);if((await lstat(asset.path)).size!==asset.bytes||await hash(asset.path)!==asset.sha256)throw new Error('Referenced native asset changed: '+asset.path);}
const protectedNames=new Set(assets.map(asset=>relative(root,asset.path).split(/[\\/]/)[0]).filter(value=>name.test(value)));
const directories=(await readdir(root,{withFileTypes:true})).filter(entry=>entry.isDirectory()&&name.test(entry.name));
const files=new Map();
async function walk(path,result){await noLink(path);for(const entry of await readdir(path,{withFileTypes:true})){const child=join(path,entry.name);await noLink(child);if(entry.isDirectory())await walk(child,result);else if(entry.isFile())result.push({path:child,bytes:(await lstat(child)).size});else throw new Error('Unexpected filesystem entry: '+child);}}
for(const directory of directories){const list=[];await walk(join(root,directory.name),list);files.set(directory.name,list);}
// Retain receipts and changed text source from every attempted build, including the published host's exact adapter sources.
const history=join(root,'frontend-build-history');const archived=[];
if(apply){await mkdir(history,{recursive:true});await noLink(history);}
for(const directory of directories){
  const path=join(root,directory.name);let receipt=null;try{receipt=JSON.parse(await readFile(join(path,'home-frontend-build.json'),'utf8'));}catch(error){if(error.code!=='ENOENT')throw error;}
  let adapter=null;try{adapter=JSON.parse(await readFile(join(path,'home-host-adapter.json'),'utf8'));}catch(error){if(error.code!=='ENOENT')throw error;}
  const selected=new Set(['home-frontend-build.json','home-host-adapter.json','build.log',...(receipt?.changedSourceFiles??[]),...(adapter?.changes?.map(change=>change.path)??[])]);
  for(const file of files.get(directory.name).filter(file=>selected.has(relative(path,file.path).replaceAll('\\','/')))){
    const local=relative(path,file.path);if(!/\.(json|log|c|h|cpp|hpp)$/.test(local))throw new Error('Unexpected history file: '+local);
    const sha256=await hash(file.path);archived.push({build:directory.name,path:local,bytes:file.bytes,sha256});
    if(apply){const destination=join(history,directory.name,local);await mkdir(dirname(destination),{recursive:true});try{await noLink(destination);if(await hash(destination)!==sha256)throw new Error('Existing history differs; preserved: '+destination);}catch(error){if(error.code!=='ENOENT')throw error;await copyFile(file.path,destination,constants.COPYFILE_EXCL);}if(await hash(destination)!==sha256)throw new Error('History copy did not verify.');}
  }
}
const removed=directories.filter(directory=>!protectedNames.has(directory.name)).map(directory=>({name:directory.name,files:files.get(directory.name).length,bytes:files.get(directory.name).reduce((sum,file)=>sum+file.bytes,0)}));
if(apply){
  for(const profile of profiles)if(await hash(profile.path)!==profile.sha256)throw new Error('Runtime profile changed; cleanup stopped.');
  for(const build of removed){const target=join(root,build.name);if(dirname(target)!==root||!name.test(build.name))throw new Error('Cleanup target boundary failed.');await erase(target);}
  for(const asset of assets)if(await hash(asset.path)!==asset.sha256)throw new Error('Runtime preservation verification failed.');
}
async function erase(path){await noLink(path);for(const entry of await readdir(path,{withFileTypes:true})){const child=join(path,entry.name);await noLink(child);if(entry.isDirectory())await erase(child);else await unlink(child);}await rmdir(path);}
const report={checkedAt:new Date().toISOString(),applied:apply,reviewRoot:root,originalBuildCount:directories.length,keptBuilds:[...protectedNames],removedBuilds:removed,removedBytes:removed.reduce((sum,build)=>sum+build.bytes,0),historyDirectory:history,retainedHistoryFiles:archived.length,retainedHistoryBytes:archived.reduce((sum,file)=>sum+file.bytes,0),runtimeProfilesPreserved:profiles.map(({path,sha256})=>({path,sha256})),referencedRuntimeFilesPreserved:assets.length,romsAndSavesTouched:false};
await mkdir(dirname(evidence),{recursive:true});await writeFile(evidence,JSON.stringify({...report,history:archived},null,2),{flag:'wx'});console.log(JSON.stringify(report,null,2));
