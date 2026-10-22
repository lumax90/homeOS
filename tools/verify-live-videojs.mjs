import {readFile,readdir,writeFile} from 'node:fs/promises';
import {createHash} from 'node:crypto';
import {DatabaseSync} from 'node:sqlite';
import {join} from 'node:path';

// Shell/API reads only. GET /watch returns HTML without running a browser or starting playback.
const base=process.env.HOME_BASE_URL??'http://127.0.0.1:5080';
const expected=process.env.HOME_EXPECTED_VERSION??'0.28.0';
if(!process.env.HOME_USERNAME||!process.env.HOME_PASSWORD)throw new Error('Set HOME_USERNAME and HOME_PASSWORD.');
const before=JSON.parse(await readFile('artifacts/preview-before-v028.json','utf8'));
const outputRoot='server/HomeServer.Api/wwwroot';
const cookies=new Map();let csrf='';let signedIn=false;
const sha=bytes=>createHash('sha256').update(bytes).digest('hex');
async function api(path,method='GET',body){
  const headers={Cookie:[...cookies].map(([name,value])=>name+'='+value).join('; ')};
  if(method!=='GET')headers['X-CSRF-TOKEN']=csrf;
  if(body!==undefined)headers['Content-Type']='application/json';
  const response=await fetch(base+'/api'+path,{method,headers,body:body===undefined?undefined:JSON.stringify(body),signal:AbortSignal.timeout(15000)});
  for(const cookie of response.headers.getSetCookie()){const pair=cookie.split(';',1)[0];const at=pair.indexOf('=');cookies.set(pair.slice(0,at),pair.slice(at+1));}
  if(!response.ok)throw new Error(`${path}: HTTP ${response.status}`);
  return response.status===204?null:response.json();
}
async function bytes(path){const response=await fetch(base+path,{cache:'no-store',signal:AbortSignal.timeout(15000)});if(!response.ok)throw new Error(`${path}: HTTP ${response.status}`);return new Uint8Array(await response.arrayBuffer());}
try{
  csrf=(await api('/session')).csrfToken;await api('/auth/login','POST',{displayName:process.env.HOME_USERNAME,password:process.env.HOME_PASSWORD});signedIn=true;
  const session=await api('/session');csrf=session.csrfToken;
  const [health,runtime,downloads,content]=await Promise.all([api('/health'),api('/operations/runtime'),api('/downloads'),api('/content')]);
  if(health.version!==expected||session.userId!=='1')throw new Error('Unexpected version or account.');
  const localHtml=await readFile(join(outputRoot,'index.html'),'utf8');const entry=/<script[^>]+src="([^"]+)"/.exec(localHtml)?.[1];
  if(!entry)throw new Error('Local client entry not found.');
  const client=await bytes(entry);if(sha(client)!==sha(await readFile(join(outputRoot,entry))))throw new Error('The new client is not being served.');
  if(process.env.HOME_REQUIRE_COMPACT_NAV&&!new TextDecoder().decode(client).includes(process.env.HOME_REQUIRE_COMPACT_NAV==='2'?'compact-v2':'compact-v1'))throw new Error('Compact navigation is not being served.');
  const assetNames=await readdir(join(outputRoot,'assets'));const players=assetNames.filter(name=>/^Player-.+\.js$/.test(name));
  if(!players.length)throw new Error('Lazy Video.js player chunk is missing.');
  for(const player of players){const payload=await bytes('/assets/'+player);if(sha(payload)!==sha(await readFile(join(outputRoot,'assets',player))))throw new Error('Wrong player chunk.');if(!new TextDecoder().decode(payload).includes('videojs-10'))throw new Error('New player marker is missing.');}
  const sample=content.find(item=>item.metadata?.kind==='movie')??content[0];
  if(!sample)throw new Error('No existing real title to verify.');
  const metadata=sample.metadata;const route=`/title/${metadata.provider}/${metadata.kind}/${metadata.providerId}`;
  const file=before.media.find(row=>row.id==='11ee851cbd6a40ae8c63f09131e365e8')??before.media[0];
  for(const path of [route,'/media/'+file.id,'/watch/'+file.id]){
    const html=new TextDecoder().decode(await bytes(path));if(!html.includes(entry))throw new Error('Deep-link shell missing: '+path);
  }
  const details=await api(`/discovery/${metadata.provider}/${metadata.kind}/${metadata.providerId}`);
  const tracks=await api(`/media/${file.id}/tracks`);
  const worker=new TextDecoder().decode(await bytes('/sw.js'));const localWorker=await readFile(join(outputRoot,'sw.js'),'utf8');if(worker!==localWorker)throw new Error('Wrong PWA worker.');
  const state=runtime.statePath;const db=new DatabaseSync(join(state,'home.db'),{readOnly:true});let schema,foreignKeys,preserved,counts;
  try{
    schema=db.prepare('PRAGMA user_version').get().user_version;foreignKeys=db.prepare('PRAGMA foreign_key_check').all().length;
    const media=db.prepare('SELECT id,library_id,relative_path,size_bytes FROM media').all();const requests=db.prepare('SELECT id,content_id,requested_by FROM content_requests').all();const transfers=db.prepare('SELECT id,info_hash,owner_id,archived FROM downloads').all();
    const contains=(old,current)=>old.every(row=>current.some(value=>JSON.stringify(value)===JSON.stringify(row)));
    preserved=contains(before.media,media)&&contains(before.requests,requests)&&contains(before.downloads,transfers);counts={media:media.length,requests:requests.length,downloads:transfers.length};
  }finally{db.close();}
  if(schema!==39||foreignKeys||!preserved)throw new Error('Existing data identities or references changed.');
  const active=JSON.parse(await readFile(join(state,'updates','active.json'),'utf8'));const rollback=JSON.parse(await readFile(join(state,'updates','rollback.json'),'utf8'));
  let personalDataRetained;
  if(process.env.HOME_PERSONAL_PREFLIGHT){
    const previous=JSON.parse(await readFile(process.env.HOME_PERSONAL_PREFLIGHT,'utf8'));const personalDb=new DatabaseSync(join(state,'home.db'),{readOnly:true});
    try{
      const current={personal:personalDb.prepare('SELECT user_id,content_id,favorite,rating FROM personal_content ORDER BY user_id,content_id').all(),collections:personalDb.prepare('SELECT id,user_id,name,shared FROM collections ORDER BY id').all(),items:personalDb.prepare('SELECT collection_id,content_id,created_at FROM collection_items ORDER BY collection_id,content_id').all()};
      personalDataRetained=['personal','collections','items'].every(key=>previous[key].every(row=>current[key].some(value=>JSON.stringify(value)===JSON.stringify(row))));
      if(!personalDataRetained)throw new Error('Personal records changed; inspect concurrent user changes before making any preservation claim.');
    }finally{personalDb.close();}
  }
  const result={verifiedAt:new Date().toISOString(),version:health.version,schema,processId:runtime.processId,clientEntry:entry,playerChunks:players,videoJsVersion:'10.1.0',deepLinksServed:[route,'/media/'+file.id,'/watch/'+file.id],metadataTitle:details.metadata.title,tracks:{audio:tracks.streams.filter(track=>track.kind==='audio').length,subtitles:tracks.subtitles.length,chapters:tracks.chapters.length},foreignKeyViolations:foreignKeys,existingMediaRequestsTransfersPreserved:preserved,counts,activeRelease:active,rollback,downloads:downloads.map(({id,status,progress})=>({id,status,progress})),browserAcceptance:'user-owned; no browser suite or playback started',playbackCommandsIssued:0,acquisitionCommandsIssued:0};
  if(personalDataRetained!==undefined)result.personalDataRetained=personalDataRetained;
  await writeFile(process.env.HOME_UI_VERIFY_OUTPUT??'artifacts/live-videojs-v028.json',JSON.stringify(result,null,2)+'\n');console.log(JSON.stringify(result,null,2));
}finally{if(signedIn)await api('/auth/logout','POST').catch(()=>{});}
