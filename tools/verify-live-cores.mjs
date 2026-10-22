// Read-only authenticated API inspection: no installation, ROM, BIOS, game or provider commands.
import {writeFile} from 'node:fs/promises';
const base=process.env.HOME_BASE_URL??'http://127.0.0.1:5080',cookies=new Map(),expected=process.env.HOME_VERIFY_VERSION??'0.36.0',gpuReady=process.env.HOME_VERIFY_GPU_READY==='1';let csrf='';
const tag=process.env.HOME_VERIFY_TAG??(gpuReady?'v037':'v036'),basic=process.env.HOME_VERIFY_BASIC_SELECTION==='1';if(!/^v\d+$/.test(tag))throw new Error('Invalid evidence tag.');
async function api(path,method='GET',body){const response=await fetch(base+'/api'+path,{method,headers:{Cookie:[...cookies].map(([key,value])=>key+'='+value).join('; '),'X-CSRF-TOKEN':csrf,...(body?{'Content-Type':'application/json'}:{})},body:body?JSON.stringify(body):undefined,signal:AbortSignal.timeout(20000)});for(const cookie of response.headers.getSetCookie()){const pair=cookie.split(';',1)[0],at=pair.indexOf('=');cookies.set(pair.slice(0,at),pair.slice(at+1));}if(!response.ok)throw new Error(path+': '+response.status);return response.status===204?null:response.json();}
if(!process.env.HOME_USERNAME||!process.env.HOME_PASSWORD)throw new Error('Set owner credentials.');
try{
  csrf=(await api('/session')).csrfToken;await api('/auth/login','POST',{displayName:process.env.HOME_USERNAME,password:process.env.HOME_PASSWORD});csrf=(await api('/session')).csrfToken;
  const health=await api('/health');if(health.version!==expected)throw new Error('Unexpected live version.');
  const catalog=await api('/games/native/cores'),systems=await api('/games/native/systems'),games=await api('/games'),status=await api('/games/native/status');
  if(catalog.commit!=='5a74858ab2f7a50cebb5a6330895bc38899531c0'||catalog.items.length!==326)throw new Error('Pinned official catalog missing.');
  for(const id of ['pcsx2','dolphin','xemu']){const core=catalog.items.find(core=>core.id===id);if(!core||(gpuReady?!core.adapterReady||core.reason:core.adapterReady||!core.reason))throw new Error('Unexpected native core readiness: '+id);}
  if(systems.length<30||!systems.some(system=>system.id==='gb')||!systems.some(system=>system.id==='ps1'))throw new Error('Multi-system import routes missing.');
  if(!status.available||!status.enabled||!catalog.items.find(core=>core.id==='mgba')?.installed)throw new Error('Existing reviewed mGBA changed.');
  const extraInstalled=catalog.items.filter(core=>core.installed&&core.id!=='mgba');
  const basicSystems=systems.filter(system=>system.basic).map(system=>system.id),basicCores=catalog.items.filter(core=>core.basicSystems?.length>0).map(core=>core.id).sort();
  if(basic){
    if(JSON.stringify(basicSystems)!==JSON.stringify(['gb','gbc','gba','nes','snes','megadrive','ps1'])||JSON.stringify(systems.slice(0,7).map(system=>system.id))!==JSON.stringify(basicSystems))throw new Error('Basic import selection/order missing.');
    if(JSON.stringify(basicCores)!==JSON.stringify(['fceumm','genesis_plus_gx','mgba','pcsx_rearmed','snes9x'])||basicCores.some(id=>!catalog.items.find(core=>core.id===id)?.installed))throw new Error('The approved basic emulator setup is incomplete.');
    if(!catalog.items.find(core=>core.id==='citra')?.installed||!systems.some(system=>system.id==='3ds'&&!system.basic))throw new Error('Existing advanced Citra/system was not preserved.');
  }
  const report={verifiedAt:new Date().toISOString(),version:health.version,catalogCommit:catalog.commit,cores:catalog.items.length,emulators:catalog.items.filter(core=>core.categories.includes('Emulator')).length,readyAdapters:catalog.items.filter(core=>core.adapterReady).length,importSystems:systems.length,extraInstalled:extraInstalled.map(core=>core.id),existingGameIds:games.items.map(game=>game.id),gbaCoreVersion:catalog.items.find(core=>core.id==='mgba').version,gpuReady,gameplayCommandsIssued:0,acquisitionCommandsIssued:0,browserAcceptance:'User-owned; not automated.'};
  await writeFile(`artifacts/live-core-catalog-${tag}.json`,JSON.stringify({...report,basicSystems,basicCores,basicSelectionVerified:basic},null,2));console.log(JSON.stringify({...report,basicSystems,basicCores,basicSelectionVerified:basic},null,2));
}finally{await api('/auth/logout','POST').catch(()=>{});}
