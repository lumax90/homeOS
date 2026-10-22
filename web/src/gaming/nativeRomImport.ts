export interface RomProbeResult{systemId:string|null;candidates:string[];entry:string;missingFiles:string[];reason:string;reads?:Array<{name:string;offset:number;length:number}>|null}
export interface RomProbeInput{entry:string;files:Array<{name:string;bytes:number;prefixBase64:string|null;samples?:Array<{offset:number;base64:string}>}>}
const descriptor=(name:string)=>/\.(cue|m3u|gdi)$/i.test(name);
export function defaultRomEntry(files:ReadonlyArray<{name:string}>):string{
  const playlists=files.filter(file=>/\.m3u$/i.test(file.name));if(playlists.length===1)return playlists[0].name;
  const entries=files.filter(file=>descriptor(file.name));return entries.length===1?entries[0].name:files.length===1?files[0].name:'';
}
export function romEntryFiles<T extends {name:string}>(files:T[]):T[]{const entries=files.filter(file=>descriptor(file.name));return entries.length?entries:files;}
export async function romProbeInput(files:File[],entry:string,signal:AbortSignal):Promise<RomProbeInput>{
  if(files.length<1||files.length>128)throw new Error('ROM dosyaları sistemin boyut veya dosya sayısı sınırını aşıyor.');
  // At most eight 64-KiB prefixes, not an upload of the whole game. Unselected filesystem files are never read.
  const prioritized=[...files.filter(file=>file.name===entry),...files.filter(file=>file.name!==entry&&descriptor(file.name)),...files.filter(file=>file.name!==entry&&!descriptor(file.name))].slice(0,8);
  const samples=new Map<string,string>();
  for(const file of prioritized){signal.throwIfAborted();const bytes=new Uint8Array(await file.slice(0,65536).arrayBuffer());let binary='';for(let i=0;i<bytes.length;i+=8192)binary+=String.fromCharCode(...bytes.subarray(i,i+8192));samples.set(file.name,btoa(binary));}
  signal.throwIfAborted();return{entry,files:files.map(file=>({name:file.name,bytes:file.size,prefixBase64:samples.get(file.name)??null}))};
}
export async function detectRomFiles(files:File[],entry:string,signal:AbortSignal,send:(body:RomProbeInput)=>Promise<RomProbeResult>):Promise<RomProbeResult>{
  const input=await romProbeInput(files,entry,signal);let sampled=input.files.reduce((sum,file)=>sum+(file.prefixBase64?atob(file.prefixBase64).length:0),0);const seen=new Set<string>();
  for(let round=0;round<4;round++){
    signal.throwIfAborted();const result=await send(input);signal.throwIfAborted();if(result.systemId||!result.reads?.length)return result;
    if(round===3)return{...result,reads:[],reason:'ambiguous'};
    if(result.reads.length>4)throw new Error('ROM algılama örneği çok büyük veya geçersiz.');
    for(const read of result.reads){const file=files.find(file=>file.name===read.name),item=input.files.find(file=>file.name===read.name);const key=read.name+':'+read.offset+':'+read.length;
      if(!file||!item||!Number.isSafeInteger(read.offset)||!Number.isInteger(read.length)||read.offset<0||read.length<1||read.length>65536||read.offset+read.length>file.size||seen.has(key)||(sampled+=read.length)>8*65536)throw new Error('ROM algılama örneği çok büyük veya geçersiz.');
      seen.add(key);const bytes=new Uint8Array(await file.slice(read.offset,read.offset+read.length).arrayBuffer());signal.throwIfAborted();let binary='';for(let i=0;i<bytes.length;i+=8192)binary+=String.fromCharCode(...bytes.subarray(i,i+8192));(item.samples??=[]).push({offset:read.offset,base64:btoa(binary)});
    }
  }
  throw new Error('Oyun sistemi algılanamadı. Uygun sistemi elle seçebilirsin.');
}
export function romUploadBody(files:File[],entry:string):{body:FormData|File;multipart:boolean}{
  const file=files.find(file=>file.name===entry);if(!file)throw new Error('Disk başlangıç dosyası seç.');
  if(descriptor(entry)){const body=new FormData();for(const part of files)body.append('files',part,part.name);return{body,multipart:true};}
  if(files.length!==1)throw new Error('Bir kartuş dosyası seç veya disk paketi için başlangıç dosyasını belirt.');
  return{body:file,multipart:false};
}
