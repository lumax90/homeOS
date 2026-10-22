import {readFile,writeFile} from 'node:fs/promises';
import {DatabaseSync} from 'node:sqlite';
import {join} from 'node:path';

// Read the deployed shell and existing database only. Does not create a playback
// session, run a browser suite or issue a media acquisition command.
const base=process.env.HOME_BASE_URL??'http://127.0.0.1:5080';
const version=process.env.HOME_EXPECTED_VERSION??'0.27.2';
async function text(path){
  const response=await fetch(base+path,{cache:'no-store',signal:AbortSignal.timeout(10000)});
  if(!response.ok)throw new Error(`${path}: HTTP ${response.status}`);
  return response.text();
}
const health=JSON.parse(await text('/api/health'));
if(health.version!==version)throw new Error('Unexpected deployed version.');
const html=await text('/');const entry=/<script[^>]+src="([^"]+)"/.exec(html)?.[1];
if(!entry||entry!=='/assets/index-D6oXmH4G.js')throw new Error('New playback client is not being served.');
const client=await text(entry);
if(!client.includes('native-hls')||!client.includes('hls.js')||!client.includes('Interface language'))throw new Error('Transport or language client is missing.');
const worker=await text('/sw.js');
if(!worker.includes('846db06ce716846a'))throw new Error('Unexpected PWA build.');
const state=join(process.cwd(),'.data','preview');const active=JSON.parse(await readFile(join(state,'updates','active.json'),'utf8'));
const rollback=JSON.parse(await readFile(join(state,'updates','rollback.json'),'utf8'));
const db=new DatabaseSync(join(state,'home.db'),{readOnly:true});let schema,foreignKeys,download,media;
try{
  schema=db.prepare('PRAGMA user_version').get().user_version;
  foreignKeys=db.prepare('PRAGMA foreign_key_check').all().length;
  download=db.prepare('SELECT id,status,desired_state,progress,imported_at,owner_id FROM downloads WHERE id=?').get('9aa5a5878e2847ea83deea5824bf93f8');
  media=db.prepare('SELECT id,title,size_bytes,video_codec,audio_codec,available FROM media WHERE id=?').get('11ee851cbd6a40ae8c63f09131e365e8');
}finally{db.close();}
if(schema!==39||foreignKeys!==0||active.Version!==version||download?.progress!==100||!download?.imported_at||download.owner_id!=='1'||!media?.available)throw new Error('Existing completed media or state references changed.');
const result={verifiedAt:new Date().toISOString(),version:health.version,schema,foreignKeyViolations:foreignKeys,clientEntry:entry,pwaBuild:'846db06ce716846a',mseFirstClientServed:true,activeRelease:active,rollback,existingDownload:download,existingMedia:media,playbackCommandsIssued:0,acquisitionCommandsIssued:0,browserPlaybackAcceptance:'user-owned; not run by this check'};
await writeFile('artifacts/live-hls-v0272.json',JSON.stringify(result,null,2)+'\n');
console.log(JSON.stringify(result,null,2));
