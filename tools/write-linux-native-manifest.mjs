import {readFile,writeFile,copyFile,mkdir} from 'node:fs/promises';
import {join,resolve} from 'node:path';
import {createHash} from 'node:crypto';
const [sourceArg,outputArg]=process.argv.slice(2);if(!sourceArg||!outputArg)throw Error('Use <source directory> <runtime directory>');const source=resolve(sourceArg),output=resolve(outputArg);
const pins=JSON.parse(await readFile(join(source,'sources.json'),'utf8')),catalog=JSON.parse(await readFile(new URL('../server/HomeServer.Api/NativeCoreCatalog/catalog.json',import.meta.url),'utf8'));
const asset=async path=>{const bytes=await readFile(join(output,path));return{path,bytes:bytes.length,sha256:createHash('sha256').update(bytes).digest('hex')};};
const cores=[];
for(const id of ['mgba','fceumm','snes9x','genesis_plus_gx','pcsx_rearmed']){
  const entry=pins.sources.find(s=>s.id===id),info=catalog.entries.find(c=>c.id===id);if(!info)throw Error('Core metadata missing.');await writeFile(join(output,'cores',id+'_libretro.info'),info.info);await mkdir(join(output,'licenses',id),{recursive:true});
  const reviewed=JSON.parse(await readFile(join(source,id,'home-source.json'),'utf8'));for(const file of reviewed.files.filter(f=>/(?:^|\/)(?:COPYING|LICENSE|COPYRIGHT)[^/]*$/i.test(f.path))){const name=file.path.replaceAll('/','_');await copyFile(join(source,id,file.path),join(output,'licenses',id,name));}
  cores.push({id,version:entry.version,commit:entry.commit,license:entry.license,sourceUrl:`https://github.com/${entry.repository}/tree/${entry.commit}`,binary:await asset('cores/'+id+'_libretro.so'),info:await asset('cores/'+id+'_libretro.info')});
}
const definition={format:1,platform:'linux-x64',frontendVersion:'1.22.2',frontendCommit:pins.sources[0].commit,inputFix:pins.inputFix.commit,adapter:'home-linux-host-1',mediaMtxVersion:pins.mediamtx.version,frontend:await asset('retroarch'),mediaMtx:await asset('mediamtx'),cores,headlessGraphics:false};
await writeFile(join(output,'build-manifest.json'),JSON.stringify(definition,null,2));
