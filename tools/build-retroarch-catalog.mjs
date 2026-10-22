// Upstream metadata only. Never downloads cores, ROMs, BIOS or game assets.
import {mkdir,writeFile} from 'node:fs/promises';
import {createHash} from 'node:crypto';
const commit='5a74858ab2f7a50cebb5a6330895bc38899531c0';
const root=new URL('../server/HomeServer.Api/NativeCoreCatalog/',import.meta.url);
async function text(url){const r=await fetch(url,{signal:AbortSignal.timeout(30000)});if(!r.ok)throw new Error(`${r.status} ${url}`);return r.text();}
const tree=JSON.parse(await text(`https://api.github.com/repos/libretro/libretro-core-info/git/trees/${commit}`));
const names=tree.tree.map(x=>x.path).filter(n=>/^[A-Za-z0-9_-]+_libretro\.info$/.test(n)).sort();
const index=await text('https://buildbot.libretro.com/nightly/windows/x86_64/latest/.index');
const available=new Set(index.split(/\r?\n/));
const entries=[];let next=0;
await Promise.all(Array.from({length:8},async()=>{while(next<names.length){const name=names[next++],info=await text(`https://raw.githubusercontent.com/libretro/libretro-core-info/${commit}/${name}`);const fields=Object.fromEntries([...info.matchAll(/^([a-zA-Z0-9_]+)\s*=\s*(?:"(.*)"|(\d+))\s*$/gm)].map(m=>[m[1],m[2]??m[3]]));
const id=name.replace('_libretro.info','');entries.push({id,name:fields.corename??id,displayName:fields.display_name??id,system:fields.systemid??'',systemName:fields.systemname??'',databases:(fields.database??'').split('|').filter(Boolean),extensions:(fields.supported_extensions??'').split('|').filter(Boolean),license:fields.license??'Unknown',categories:(fields.categories??'').split('|'),hardware:fields.hw_render==='true',hardwareApi:fields.required_hw_api??'',saveState:fields.savestate==='true',diskControl:fields.disk_control==='true',experimental:fields.is_experimental==='true',version:fields.display_version??'Not specified by upstream',firmware:Array.from({length:Math.min(128,Number(fields.firmware_count??0))},(_,i)=>({path:fields[`firmware${i}_path`]??'',description:fields[`firmware${i}_desc`]??'',optional:fields[`firmware${i}_opt`]==='true'})),notes:fields.notes??'',description:fields.description??'',windowsBuild:available.has(`${id}_libretro.dll.zip`),info,infoSha256:createHash('sha256').update(info).digest('hex')});}}));
await mkdir(root,{recursive:true});
await writeFile(new URL('catalog.json',root),JSON.stringify({format:1,commit,reviewedAt:'2026-10-20',source:`https://github.com/libretro/libretro-core-info/tree/${commit}`,buildSource:'https://buildbot.libretro.com/nightly/windows/x86_64/latest/',entries:entries.sort((a,b)=>a.id.localeCompare(b.id))},null,2)+'\n');
await writeFile(new URL('LICENSE.txt',root),await text(`https://raw.githubusercontent.com/libretro/libretro-core-info/${commit}/COPYING`));
console.log(JSON.stringify({commit,cores:entries.length,emulators:entries.filter(e=>e.categories.includes('Emulator')).length,windowsBuilds:entries.filter(e=>e.windowsBuild).length}));
