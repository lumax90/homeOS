import {readFile,writeFile} from 'node:fs/promises';
import {createHash} from 'node:crypto';

const target=process.argv[2];if(!target)throw Error('Use <package output path>');
const pin=JSON.parse(await readFile(new URL('../deploy/selkies/runtime.json',import.meta.url),'utf8'));
const response=await fetch(pin.package.url,{signal:AbortSignal.timeout(180000)});
if(!response.ok)throw Error(`Selkies package HTTP ${response.status}`);
const chunks=[];let length=0;
for await(const chunk of response.body){length+=chunk.length;if(length>pin.package.bytes)throw Error('Selkies package exceeds pinned size');chunks.push(chunk);}
const bytes=Buffer.concat(chunks);
if(length!==pin.package.bytes||createHash('sha256').update(bytes).digest('hex')!==pin.package.sha256)throw Error('Selkies package does not match its release digest');
await writeFile(target,bytes,{flag:'wx'});
console.log(`Verified Selkies ${pin.version} package (${length} bytes)`);
