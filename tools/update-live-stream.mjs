import {createReadStream} from 'node:fs';
import {stat,writeFile} from 'node:fs/promises';
import {setTimeout as delay} from 'node:timers/promises';

// Same authenticated signed-update API; stream the package rather than Windows PowerShell's buffered HTTP upload.
if(process.argv.length!==8||process.argv[2]!=='--current'||process.argv[4]!=='--next'||process.argv[6]!=='--package')throw new Error('Use --current <version> --next <version> --package <signed zip>');
const current=process.argv[3],next=process.argv[5],file=process.argv[7],base=process.env.HOME_BASE_URL??'http://127.0.0.1:5080';
const cookies=new Map();let csrf='',signedIn=false;
async function api(path,method='GET',body,contentType='application/json',timeout=15000,length){
  const headers={Cookie:[...cookies].map(([key,value])=>key+'='+value).join('; ')};if(method!=='GET')headers['X-CSRF-TOKEN']=csrf;if(body!==undefined)headers['Content-Type']=contentType;if(length!==undefined)headers['Content-Length']=String(length);
  const response=await fetch(base+'/api'+path,{method,headers,body:body===undefined?undefined:contentType==='application/json'?JSON.stringify(body):body,duplex:'half',signal:AbortSignal.timeout(timeout)});
  for(const cookie of response.headers.getSetCookie()){const pair=cookie.split(';',1)[0],at=pair.indexOf('=');cookies.set(pair.slice(0,at),pair.slice(at+1));}
  if(!response.ok)throw new Error(path+': HTTP '+response.status+' '+await response.text());return response.status===204?null:response.json();
}
async function idle(){
  const [playback,games,downloads,restore]=await Promise.all([api('/account/playback?all=true'),api('/games/sessions/active'),api('/downloads'),api('/operations/restore')]);
  if(playback.active.length||games.length||downloads.some(download=>download.desiredState==='running')||restore.pending)throw new Error('Playback/game/transfer/restore became active; update deferred without changing those states.');
  const recordings=await api('/live/recordings');if(recordings.some(recording=>['recording','finalizing','publishing'].includes(recording.status)))throw new Error('A live TV recording is active; update deferred.');
}
try{
  if(!process.env.HOME_USERNAME||!process.env.HOME_PASSWORD)throw new Error('Set existing owner credentials.');
  if((await api('/health')).version!==current)throw new Error('Unexpected current version.');csrf=(await api('/session')).csrfToken;
  await api('/auth/login','POST',{displayName:process.env.HOME_USERNAME,password:process.env.HOME_PASSWORD});signedIn=true;const owner=await api('/session');csrf=owner.csrfToken;
  await idle();const before=await api('/operations/updates');if(!before.trusted||!before.canApply)throw new Error('Trusted updater unavailable.');
  let pending=before.pending;
  if(pending){if(pending.version!==next)throw new Error('A different update is pending and was preserved.');}
  else{const size=(await stat(file)).size;if(size>1024*1024*1024)throw new Error('Package too large.');pending=await api('/operations/updates','POST',createReadStream(file),'application/zip',180000,size);}
  if(pending.version!==next)throw new Error('Unexpected prepared version.');
  await idle();await api('/operations/updates/apply','POST',undefined,'application/json',90000);
  let healthy=false;for(let i=0;i<150;i++){try{if((await api('/health','GET',undefined,'application/json',3000)).version===next){healthy=true;break;}}catch{}await delay(500);}
  if(!healthy)throw new Error('Updated version did not become healthy.');const session=await api('/session');csrf=session.csrfToken;if(!session.authenticated||session.userId!==owner.userId)throw new Error('Owner session not preserved.');
  const runtime=await api('/operations/runtime'),updates=await api('/operations/updates');
  const report={verifiedAt:new Date().toISOString(),fromVersion:current,version:next,accountRetained:true,processId:runtime.processId,statePath:runtime.statePath,rollback:updates.rollback,priorRollback:before.rollback,streamedPackage:true,resumedTransfers:0};
  await writeFile('artifacts/update-live-'+next+'.json',JSON.stringify(report,null,2));console.log(JSON.stringify(report,null,2));
}finally{if(signedIn)await api('/auth/logout','POST').catch(()=>{});}
