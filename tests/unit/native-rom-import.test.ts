import {test} from 'node:test';
import assert from 'node:assert/strict';
import {defaultRomEntry,romEntryFiles,romUploadBody,romProbeInput,detectRomFiles} from '../../web/src/gaming/nativeRomImport.ts';

test('CUE is the entry even if BIN is selected first and both original files are sent as multipart',()=>{
  const files=[new File(['original binary'],'Original Track.bin'),new File(['FILE "Original Track.bin" BINARY\n'],'Original.cue')];
  assert.equal(defaultRomEntry(files),'Original.cue');assert.deepEqual(romEntryFiles(files).map(file=>file.name),['Original.cue']);
  const upload=romUploadBody(files,'Original.cue');assert(upload.multipart);assert(upload.body instanceof FormData);assert.deepEqual(upload.body.getAll('files').map(value=>(value as File).name),files.map(file=>file.name));assert.equal(files.length,2);
});
test('multiple disc entries require an explicit choice; one M3U takes precedence over its CUE children',()=>{
  const files=[{name:'one.cue'},{name:'two.cue'},{name:'track.bin'}];assert.equal(defaultRomEntry(files),'');assert.equal(defaultRomEntry([...files,{name:'all.m3u'}]),'all.m3u');
  assert.throws(()=>romUploadBody([new File(['data'],'test.bin')],''),/başlangıç/);
});
test('ordinary cartridge upload stays binary; several cartridges are never silently merged',()=>{
  const file=new File(['original fixture'],'test.gba');const upload=romUploadBody([file],file.name);assert.equal(upload.body,file);assert.equal(upload.multipart,false);
  assert.throws(()=>romUploadBody([file,new File(['other'],'other.gba')],file.name),/Bir kartuş/);
});
test('detection samples only selected files and is bounded to eight 64 KiB prefixes',async()=>{
  const files=Array.from({length:12},(_,i)=>new File([new Uint8Array(128*1024)],i+'.bin'));const cue=new File(['FILE "0.bin" BINARY\n'],'entry.cue');files.push(cue);
  const input=await romProbeInput(files,cue.name,new AbortController().signal);assert.equal(input.files.length,13);const sampled=input.files.filter(file=>file.prefixBase64!==null);assert.equal(sampled.length,8);assert(sampled.some(file=>file.name===cue.name));assert(sampled.every(file=>Buffer.from(file.prefixBase64!,'base64').length<=65536));
  const abort=new AbortController();abort.abort();await assert.rejects(romProbeInput(files,cue.name,abort.signal),{name:'AbortError'});
});
test('automatic disc detection follows bounded additional reads and returns the detected console',async()=>{
  const file=new File([new Uint8Array(200000)],'disc.bin');let calls=0;
  const result=await detectRomFiles([file],file.name,new AbortController().signal,async input=>{calls++;if(calls===1)return{systemId:null,candidates:['ps1','ps2'],entry:file.name,missingFiles:[],reason:'reading-disc',reads:[{name:file.name,offset:100000,length:2048}]};assert.equal(input.files[0].samples?.[0].offset,100000);assert.equal(atob(input.files[0].samples![0].base64).length,2048);return{systemId:'ps1',candidates:['ps1','ps2'],entry:file.name,missingFiles:[],reason:'header'};});
  assert.equal(result.systemId,'ps1');assert.equal(calls,2);
});
test('a provider read cannot access an unselected file or cause an unbounded detection loop',async()=>{
  const file=new File([new Uint8Array(200000)],'disc.bin');const reply={systemId:null,candidates:[],entry:file.name,missingFiles:[],reason:'reading-disc'};
  await assert.rejects(detectRomFiles([file],file.name,new AbortController().signal,async()=>({...reply,reads:[{name:'other.bin',offset:0,length:100}]})),/geçersiz/);
  await assert.rejects(detectRomFiles([file],file.name,new AbortController().signal,async()=>({...reply,reads:[{name:file.name,offset:100000,length:2048}]})),/geçersiz/);
});
