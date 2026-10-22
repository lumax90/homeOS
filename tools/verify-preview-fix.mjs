import {readFile,writeFile} from 'node:fs/promises';
import {DatabaseSync} from 'node:sqlite';
import {join} from 'node:path';
import {setTimeout as delay} from 'node:timers/promises';

// Observe the user's existing transfer. Only sign-in/sign-out mutate server state.
const base=process.env.HOME_BASE_URL??'http://127.0.0.1:5080';
if(!process.env.HOME_USERNAME||!process.env.HOME_PASSWORD)throw new Error('Set HOME_USERNAME and HOME_PASSWORD.');
const before=JSON.parse(await readFile('artifacts/preview-before-v0271.json','utf8'));
const cookies=new Map();let csrf='';let signedIn=false;
async function api(path,method='GET',body){
  const headers={Cookie:[...cookies].map(([key,value])=>`${key}=${value}`).join('; ')};
  if(method!=='GET')headers['X-CSRF-TOKEN']=csrf;
  if(body!==undefined)headers['Content-Type']='application/json';
  const response=await fetch(`${base}/api${path}`,{method,headers,body:body===undefined?undefined:JSON.stringify(body),signal:AbortSignal.timeout(15000)});
  for(const cookie of response.headers.getSetCookie()){const pair=cookie.split(';',1)[0];const separator=pair.indexOf('=');cookies.set(pair.slice(0,separator),pair.slice(separator+1));}
  if(!response.ok)throw new Error(`${path}: HTTP ${response.status}`);
  return response.status===204?null:response.json();
}
try{
  csrf=(await api('/session')).csrfToken;
  await api('/auth/login','POST',{displayName:process.env.HOME_USERNAME,password:process.env.HOME_PASSWORD});signedIn=true;
  const session=await api('/session');csrf=session.csrfToken;
  const [health,runtime,catalog]=await Promise.all([api('/health'),api('/operations/runtime'),api('/source-catalog')]);
  if(health.version!=='0.27.1'||session.userId!=='1'||catalog.length!==12)throw new Error('Unexpected preview version, account or source catalog.');
  const html=await(await fetch(base+'/',{cache:'no-store'})).text();
  const entry=/<script[^>]+src="([^"]+)"/.exec(html)?.[1];
  if(!entry)throw new Error('Client entry not found.');
  const client=await(await fetch(new URL(entry,base))).text();
  if(!['Interface language','home.ui-language:','English','Türkçe','/source-setups/test'].every(text=>client.includes(text)))throw new Error('Localized client is not being served.');
  const worker=await(await fetch(base+'/sw.js',{cache:'no-store'})).text();
  if(!worker.includes('home.language')||!worker.includes('2e48c73c60f1b64d'))throw new Error('Localized PWA worker is not being served.');
  const samples=[];const started=Date.now();
  const selected=before.downloads.filter(download=>download.desired_state==='running'&&!download.archived).map(download=>download.id);
  do{
    const downloads=await api('/downloads');
    samples.push({at:new Date().toISOString(),downloads:downloads.filter(download=>selected.includes(download.id)).map(({id,name,sourceKind,desiredState,status,progress,totalBytes,receivedBytes,downloadSpeed,peers,message})=>({id,name,sourceKind,desiredState,status,progress,totalBytes,receivedBytes,downloadSpeed,peers,message}))});
    if(samples.at(-1).downloads.some(download=>download.receivedBytes>0||download.status==='failed'||download.desiredState!=='running')||selected.length===0)break;
    await delay(2000);
  }while(Date.now()-started<80000);
  const db=new DatabaseSync(join(runtime.statePath,'home.db'),{readOnly:true});
  let preserved,schema,foreignKeys,downloads;
  try{
    schema=db.prepare('PRAGMA user_version').get().user_version;
    foreignKeys=db.prepare('PRAGMA foreign_key_check').all().length;
    const requests=db.prepare('SELECT id,content_id,requested_by FROM content_requests').all();
    downloads=db.prepare('SELECT id,info_hash,owner_id,desired_state,archived,revision,received_bytes FROM downloads').all();
    preserved=before.requests.every(row=>requests.some(current=>JSON.stringify(current)===JSON.stringify(row)))&&before.downloads.every(row=>downloads.some(current=>current.id===row.id&&current.info_hash===row.info_hash&&current.owner_id===row.owner_id&&current.archived===row.archived&&current.desired_state===row.desired_state));
  }finally{db.close();}
  if(schema!==39||foreignKeys!==0||!preserved)throw new Error('Existing requests, ownership or schema references were not preserved.');
  const result={verifiedAt:new Date().toISOString(),version:health.version,processId:runtime.processId,schema,foreignKeyViolations:foreignKeys,existingRequestsAndTransfersPreserved:preserved,clientEntry:entry,pwaBuild:'2e48c73c60f1b64d',uiLanguages:['en','tr'],sourceProfiles:catalog.length,mediaAcquisitionCommandsIssued:0,samples,downloads};
  await writeFile('artifacts/live-preview-fix-v0271.json',JSON.stringify(result,null,2)+'\n');
  console.log(JSON.stringify({...result,samples:samples.slice(-1)},null,2));
}finally{if(signedIn)await api('/auth/logout','POST').catch(()=>{});}
