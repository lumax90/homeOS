import {writeFile} from 'node:fs/promises';

const mode=process.argv[2];if(!['--configure','--verify'].includes(mode))throw Error('Use --configure or --verify.');
const base=process.env.HOME_BASE_URL??'http://127.0.0.1:5080';const cookies=new Map();let csrf='',signedIn=false;
async function api(path,method='GET',value){const response=await fetch(base+'/api'+path,{method,headers:{Cookie:[...cookies].map(([key,item])=>key+'='+item).join('; '),'X-CSRF-TOKEN':csrf,...(value===undefined?{}:{'Content-Type':'application/json'})},body:value===undefined?undefined:JSON.stringify(value),signal:AbortSignal.timeout(20000)});for(const cookie of response.headers.getSetCookie()){const pair=cookie.split(';',1)[0],at=pair.indexOf('=');cookies.set(pair.slice(0,at),pair.slice(at+1));}if(!response.ok)throw Error(path+': HTTP '+response.status);return response.status===204?null:response.json();}
try{
  if(!process.env.HOME_USERNAME||!process.env.HOME_PASSWORD)throw Error('Owner credentials required.');csrf=(await api('/session')).csrfToken;await api('/auth/login','POST',{displayName:process.env.HOME_USERNAME,password:process.env.HOME_PASSWORD});signedIn=true;const owner=await api('/session');csrf=owner.csrfToken;
  const network=await api('/operations/network'),health=await api('/health');
  if(mode==='--configure'){
    if((await api('/account/playback?all=true')).active.length||(await api('/games/sessions/active')).length)throw Error('An active player owns the server.');
    const before=network.pending??network.current;if(before.requireHttps||before.httpsPort||before.publicUrl||before.pathBase)throw Error('Existing HTTPS/proxy settings need an explicit configuration decision.');
    const next={...before,bindAddress:'0.0.0.0',httpPort:Number(new URL(base).port||5080)};await api('/operations/network','PUT',next);
    await writeFile('artifacts/lan-configuration-v043.json',JSON.stringify({at:new Date().toISOString(),version:health.version,before,next,requiresRestart:true},null,2),{flag:'wx'});console.log(JSON.stringify({pending:true,bindAddress:next.bindAddress,httpPort:next.httpPort}));
  }else{
    if(health.version!=='0.43.0'||network.current.bindAddress!=='0.0.0.0'||!network.current.httpPort)throw Error('LAN listener configuration is not active.');
    const status=await api('/games/native/status');if(status.localOnly||status.clientLocal||!status.available||!status.canConfigure)throw Error('The non-loopback owner cannot use native server emulation.');
    const reader=await fetch(base+'/native/home-reader.js').then(r=>r.text());if(!reader.includes("credentials: 'same-origin'"))throw Error('Device-owned WHEP client missing.');
    const result={at:new Date().toISOString(),version:health.version,url:base,remoteAddress:network.remoteAddress,native:status,media:(await api('/catalog?limit=1')).total,liveChannels:(await api('/live/library')).total,streamsStarted:0,downloadsStarted:0};
    await writeFile('artifacts/live-lan-v043.json',JSON.stringify(result,null,2),{flag:'wx'});console.log(JSON.stringify({version:health.version,url:base,remoteAddress:network.remoteAddress,nativeAvailable:status.available,localOnly:status.localOnly}));
  }
}finally{if(signedIn)await api('/auth/logout','POST').catch(()=>{});}
