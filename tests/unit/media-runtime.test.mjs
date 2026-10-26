import {test} from 'node:test';
import assert from 'node:assert/strict';
import {readFile,writeFile,mkdtemp,rm,readdir} from 'node:fs/promises';
import {tmpdir} from 'node:os';
import {join} from 'node:path';
import {createHash} from 'node:crypto';
import {verifyMediaArchive,stageMediaRuntime,stagePinnedMediaArchive} from '../../tools/stage-media-runtime.mjs';

test('runtime package integrity requires both exact size and digest',()=>{
  const bytes=Buffer.from('ISOLATED software integrity contract');
  const pin={bytes:bytes.length,sha256:createHash('sha256').update(bytes).digest('hex')};
  verifyMediaArchive(bytes,pin);
  assert.throws(()=>verifyMediaArchive(bytes,{...pin,bytes:pin.bytes+1}),/size\/digest/);
  assert.throws(()=>verifyMediaArchive(Buffer.from(bytes.toString().toLowerCase()),pin),/size\/digest/);
});
test('untrusted package HTTP, truncated and oversized responses never publish a file',async()=>{
  const root=await mkdtemp(join(tmpdir(),'home-media-package-'));
  try{
    const manifest=JSON.parse(await readFile(new URL('../../deploy/media/runtime.json',import.meta.url),'utf8'));
    let called=false;
    await assert.rejects(stageMediaRuntime(join(root,'none.deb'),'unknown',{fetcher:()=>{called=true;}}),/Unsupported/);
    assert.equal(called,false);
    await assert.rejects(stageMediaRuntime(join(root,'http.deb'),'linux-x64',{fetcher:async()=>new Response(null,{status:404})}),/HTTP 404/);
    await assert.rejects(stageMediaRuntime(join(root,'short.deb'),'linux-x64',{fetcher:async()=>new Response('not software')}),/size\/digest/);
    const body={async *[Symbol.asyncIterator](){yield new Uint8Array(manifest.packages['linux-x64'].bytes+1);}};
    await assert.rejects(stageMediaRuntime(join(root,'long.deb'),'linux-x64',{fetcher:async()=>({ok:true,body})}),/exceeds/);
    assert.deepEqual(await readdir(root),[]);
  }finally{await rm(root,{recursive:true,force:true});}
});
test('media runtime is an immutable software pin, not a downloaded server or media payload',async()=>{
  const pin=JSON.parse(await readFile(new URL('../../deploy/media/runtime.json',import.meta.url),'utf8'));
  assert.match(pin.sourceCommit,/^[a-f0-9]{40}$/);assert.equal(pin.name,'jellyfin-ffmpeg');
  for(const [platform,value]of Object.entries(pin.packages)){
    assert.ok(['linux-x64','win-x64'].includes(platform));assert.ok(value.bytes>0&&value.bytes<100*1024*1024);
    assert.match(value.sha256,/^[a-f0-9]{64}$/);assert.match(value.url,/^https:\/\/github\.com\/jellyfin\/jellyfin-ffmpeg\/releases\/download\/v8\.1\.3-1\/jellyfin-ffmpeg/);
  }
});
test('verified software publication refuses to overwrite an existing destination',async()=>{
  const root=await mkdtemp(join(tmpdir(),'home-media-package-'));
  const bytes=Buffer.from('ISOLATED package publication contract');
  const pin={url:'https://example.invalid/isolated-software',bytes:bytes.length,sha256:createHash('sha256').update(bytes).digest('hex')};
  try{
    const destination=join(root,'existing');await writeFile(destination,'preserved');
    const fetcher=async()=>new Response(bytes);
    await assert.rejects(stagePinnedMediaArchive(destination,pin,{fetcher}),{code:'EEXIST'});
    assert.equal(await readFile(destination,'utf8'),'preserved');
    await stagePinnedMediaArchive(join(root,'new'),pin,{fetcher});
    assert.deepEqual(await readFile(join(root,'new')),bytes);
  }finally{await rm(root,{recursive:true,force:true});}
});
