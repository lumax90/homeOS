// Isolated verification origin for locally generated HLS fixtures; never used by the application.
import http from 'node:http';
import {readFile,stat} from 'node:fs/promises';
import {join} from 'node:path';
const root=process.argv[2];
if(!root)throw new Error('Choose an isolated fixture directory.');
const calls=new Map();let failures=0;
http.createServer(async(req,res)=>{
  const path=new URL(req.url,'http://fixture.invalid').pathname;
  if(path==='/stats'){res.setHeader('Content-Type','application/json');res.end(JSON.stringify({failures,calls:Object.fromEntries(calls)}));return;}
  const match=/^\/(aac|ac3)\/(index\.m3u8|segment\d{9}\.ts)$/.exec(path);
  if(!match){res.writeHead(404).end();return;}
  const count=(calls.get(path)??0)+1;calls.set(path,count);
  if(match[2]==='segment000000001.ts'&&count===1){failures++;res.writeHead(503,{'Retry-After':'0'}).end();return;}
  try{
    const file=join(root,match[1],match[2]);await stat(file);let bytes=await readFile(file);
    if(match[2].endsWith('.m3u8')){bytes=Buffer.from(bytes.toString().replace(/^#EXT-X-ENDLIST\r?\n?/gm,'').replace(/^#EXT-X-PLAYLIST-TYPE:.*\r?\n?/gm,''));res.setHeader('Content-Type','application/vnd.apple.mpegurl');}
    else res.setHeader('Content-Type','video/mp2t');
    res.setHeader('Cache-Control','no-store');res.setHeader('Accept-Ranges','bytes');
    const range=/^bytes=(\d+)-(\d*)$/.exec(req.headers.range??'');
    if(range){const start=Number(range[1]),end=Math.min(bytes.length-1,range[2]?Number(range[2]):bytes.length-1);if(start>end){res.writeHead(416).end();return;}res.statusCode=206;res.setHeader('Content-Range',`bytes ${start}-${end}/${bytes.length}`);bytes=bytes.subarray(start,end+1);}
    res.setHeader('Content-Length',bytes.length);res.end(req.method==='HEAD'?undefined:bytes);
  }catch{res.writeHead(404).end();}
}).listen(8090,'0.0.0.0');
