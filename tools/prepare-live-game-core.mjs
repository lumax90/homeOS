import {writeFile} from 'node:fs/promises';
import {setTimeout as delay} from 'node:timers/promises';
// Fulfil the requested setup for an already imported game using Home's software-install command.
// No ROM/BIOS acquisition, game launch, core selection change, media transfer or frontend replacement.
if(process.argv.length!==4||process.argv[2]!=='--game')throw new Error('Use --game <existing native game ID>');
const gameId=process.argv[3],base=process.env.HOME_BASE_URL??'http://127.0.0.1:5080',cookies=new Map();let session={};
async function api(path,method='GET',body){const headers={Cookie:[...cookies].map(([key,value])=>key+'='+value).join('; '),'X-CSRF-TOKEN':session.csrfToken??'','X-Home-User':session.userId??'','X-Home-Session':session.sessionId??'',...(body?{'Content-Type':'application/json'}:{})};const response=await fetch(base+'/api'+path,{method,headers,body:body?JSON.stringify(body):undefined,signal:AbortSignal.timeout(20000)});for(const cookie of response.headers.getSetCookie()){const pair=cookie.split(';',1)[0],at=pair.indexOf('=');cookies.set(pair.slice(0,at),pair.slice(at+1));}if(!response.ok)throw new Error(path+': '+response.status+' '+await response.text());return response.status===204?null:response.json();}
if(!process.env.HOME_USERNAME||!process.env.HOME_PASSWORD)throw new Error('Set the existing owner credentials.');
try{
  session=await api('/session');await api('/auth/login','POST',{displayName:process.env.HOME_USERNAME,password:process.env.HOME_PASSWORD});session=await api('/session');
  if((await api('/games/sessions/active')).length)throw new Error('A game is active; its runtime was preserved.');
  const game=await api('/games/'+gameId);if(game.runtime!=='native')throw new Error('An existing user-imported ROM is required.');
  const choice=await api('/games/'+gameId+'/core'),selected=choice.items.find(core=>core.id===choice.selection.coreId);if(!selected?.adapterReady)throw new Error('The selected core is not prepared for this host.');
  let job=null;if(!selected.installed)job=await api(`/games/native/cores/${selected.id}/install`,'POST',{acceptedLicense:true,acceptedUpstreamBuild:true});
  let installed=selected.installed;for(let i=0;i<180&&!installed;i++){await delay(1000);const state=await api('/games/'+gameId+'/core'),core=state.items.find(core=>core.id===selected.id);if(state.selection.coreId!==selected.id)throw new Error('The user changed the selected core.');if(core?.status==='failed'||core?.status==='cancelled')throw new Error(core.message??'Core installation failed.');installed=core?.installed===true;}
  if(!installed)throw new Error('The bounded core-install wait expired; inspect the Home job.');
  const core=(await api('/games/native/cores')).items.find(core=>core.id===selected.id),current=await api('/games/'+gameId);
  const report={preparedAt:new Date().toISOString(),gameId,system:game.system,coreId:core.id,license:core.license,channel:core.channel,binarySha256:core.version,jobId:job?.id??null,softwareInstallCommands:job?1:0,coreSelectionPreserved:true,playable:current.playable,reason:current.blockReason,gameLaunched:false,romOrBiosDownloaded:false,mediaTransfersChanged:false};
  await writeFile(`artifacts/prepared-native-core-${core.id}-v037.json`,JSON.stringify(report,null,2),{flag:'wx'});console.log(JSON.stringify(report,null,2));
}finally{await api('/auth/logout','POST').catch(()=>{});}
