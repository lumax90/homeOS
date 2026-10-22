import {writeFile} from 'node:fs/promises';
import {setTimeout as delay} from 'node:timers/promises';

// User-authorized emulator software setup only; never imports/launches/downloads ROMs, BIOS or media.
const allowed=['fceumm','snes9x','genesis_plus_gx','pcsx_rearmed'];
if(process.argv.length!==3||process.argv[2]!=='--install')throw new Error('Use --install to prepare only the approved basic emulator selection.');
const base=process.env.HOME_BASE_URL??'http://127.0.0.1:5080',cookies=new Map();let session={};
async function api(path,method='GET',body){const headers={Cookie:[...cookies].map(([key,value])=>key+'='+value).join('; '),'X-CSRF-TOKEN':session.csrfToken??'','X-Home-User':session.userId??'','X-Home-Session':session.sessionId??'',...(body?{'Content-Type':'application/json'}:{})};const response=await fetch(base+'/api'+path,{method,headers,body:body?JSON.stringify(body):undefined,signal:AbortSignal.timeout(20000)});for(const cookie of response.headers.getSetCookie()){const pair=cookie.split(';',1)[0],at=pair.indexOf('=');cookies.set(pair.slice(0,at),pair.slice(at+1));}if(!response.ok)throw new Error(path+': '+response.status+' '+await response.text());return response.status===204?null:response.json();}
if(!process.env.HOME_USERNAME||!process.env.HOME_PASSWORD)throw new Error('Existing owner credentials required.');
const cores=[];
try{
  session=await api('/session');await api('/auth/login','POST',{displayName:process.env.HOME_USERNAME,password:process.env.HOME_PASSWORD});session=await api('/session');
  if(session.role!=='admin'||!(await api('/games/native/status')).available)throw new Error('The existing owner/runtime is not ready.');
  for(const id of allowed){
    if((await api('/games/sessions/active')).length)throw new Error('An active game was preserved; setup stopped.');
    const before=(await api('/games/native/cores')).items.find(core=>core.id===id);if(!before?.adapterReady)throw new Error('Basic core adapter unavailable: '+id);
    const job=before.installed?null:await api(`/games/native/cores/${id}/install`,'POST',{acceptedLicense:true,acceptedUpstreamBuild:true});
    let current=before;
    for(let i=0;i<180&&!current.installed;i++){await delay(1000);current=(await api('/games/native/cores')).items.find(core=>core.id===id);if(current.status==='failed'||current.status==='cancelled')throw new Error(current.message??'Core setup failed.');}
    if(!current.installed)throw new Error('Basic emulator setup wait expired.');
    cores.push({id,license:current.license,channel:current.channel,binarySha256:current.version,jobId:job?.id??null,newInstall:job!==null});
  }
  const report={preparedAt:new Date().toISOString(),cores,softwareInstallCommands:cores.filter(core=>core.newInstall).length,existingMgbaReplaced:false,romOrBiosDownloaded:false,userGameLaunched:false,transferCommandsIssued:0};
  await writeFile('artifacts/basic-native-cores-v038.json',JSON.stringify(report,null,2),{flag:'wx'});console.log(JSON.stringify(report,null,2));
}finally{await api('/auth/logout','POST').catch(()=>{});}
