import {readFile,writeFile} from 'node:fs/promises';
import {createHash} from 'node:crypto';
import {pathToFileURL} from 'node:url';

export function verifyMediaArchive(bytes,pin){
  if(bytes.length!==pin.bytes||createHash('sha256').update(bytes).digest('hex')!==pin.sha256)
    throw Error('Media runtime does not match its pinned release size/digest.');
}

export async function stagePinnedMediaArchive(destination,pin,{fetcher=fetch}={}){
  const response=await fetcher(pin.url,{signal:AbortSignal.timeout(180000)});
  if(!response.ok||!response.body)throw Error(`Media runtime download HTTP ${response.status}`);
  const chunks=[];let length=0;
  for await(const chunk of response.body){
    length+=chunk.length;
    if(length>pin.bytes)throw Error('Media runtime exceeds its pinned size.');
    chunks.push(chunk);
  }
  const bytes=Buffer.concat(chunks);verifyMediaArchive(bytes,pin);
  await writeFile(destination,bytes,{flag:'wx'});
  return length;
}

export async function stageMediaRuntime(destination,platform,options={}){
  const manifest=JSON.parse(await readFile(new URL('../deploy/media/runtime.json',import.meta.url),'utf8'));
  const pin=platform==='source'?manifest.sourceArchive:manifest.packages[platform];
  if(!pin)throw Error('Unsupported media runtime platform.');
  const length=await stagePinnedMediaArchive(destination,pin,options);
  return {name:manifest.name,version:manifest.version,platform,sourceCommit:manifest.sourceCommit,sha256:pin.sha256,bytes:length};
}

if(process.argv[1]&&import.meta.url===pathToFileURL(process.argv[1]).href){
  if(!process.argv[2]||!process.argv[3])throw Error('Use <package output path> <linux-x64|win-x64|source>');
  console.log(JSON.stringify(await stageMediaRuntime(process.argv[2],process.argv[3])));
}
