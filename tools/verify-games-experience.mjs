import {writeFile} from 'node:fs/promises';

// Read-only publication inspection. Never launches/imports a ROM, starts a download or commands enrichment.
const base=process.env.HOME_BASE_URL??'http://127.0.0.1:5080',version=process.env.HOME_VERIFY_VERSION??'0.40.1',tag=process.env.HOME_VERIFY_TAG??'v0401',cookies=new Map();let csrf='';if(!/^v\d+$/.test(tag))throw Error('Invalid evidence tag.');
async function api(path,method='GET',body){const response=await fetch(base+'/api'+path,{method,headers:{Cookie:[...cookies].map(([key,value])=>key+'='+value).join('; '),'X-CSRF-TOKEN':csrf,...(body?{'Content-Type':'application/json'}:{})},body:body?JSON.stringify(body):undefined,signal:AbortSignal.timeout(20000)});for(const cookie of response.headers.getSetCookie()){const pair=cookie.split(';',1)[0],at=pair.indexOf('=');cookies.set(pair.slice(0,at),pair.slice(at+1));}if(!response.ok)throw Error(path+': '+response.status);return response.status===204?null:response.json();}
if(!process.env.HOME_USERNAME||!process.env.HOME_PASSWORD)throw Error('Existing owner credentials required.');
try{
  csrf=(await api('/session')).csrfToken;await api('/auth/login','POST',{displayName:process.env.HOME_USERNAME,password:process.env.HOME_PASSWORD});csrf=(await api('/session')).csrfToken;
  const health=await api('/health');if(health.version!==version)throw Error('Unexpected release.');
  const collection=await api('/games/library/settings'),games=(await api('/games')).items.filter(game=>game.kind==='native');
  const metadata=[];for(const game of games){const info=await api(`/games/${game.id}/metadata`);metadata.push({id:game.id,title:game.title,hasArtwork:game.hasArtwork,matched:!!info.data,matchMethod:info.data?.matchMethod??null,status:info.status,message:info.message});}
  const html=await fetch(base+'/games').then(r=>r.text());const entry=/<script[^>]+src="([^"]+)"/.exec(html)?.[1];if(!entry)throw Error('No client entry.');const js=await fetch(base+entry).then(r=>r.text());
  const chunks=[...new Set([...js.matchAll(/assets\/[Gg]ames-[A-Za-z0-9_-]+\.js/g)].map(m=>m[0]))];let gaming='';for(const path of chunks)gaming+='\n'+await fetch(base+'/'+path).then(r=>r.text());
  for(const marker of ['console-shelf','rom-game-page','games-settings-trigger','/games/library/settings','/games/roms/detect'])if(!gaming.includes(marker))throw Error('Missing live client feature: '+marker);
  if(!js.includes('home-notifications-popover'))throw Error('The anchored notification client is missing.');
  const report={verifiedAt:new Date().toISOString(),version,clientEntry:entry,gamesFeaturesServed:true,anchoredNotificationsServed:true,collection,metadata,gameplayCommandsIssued:0,enrichmentCommandsIssued:0,mediaDownloadCommandsIssued:0,browserAcceptance:false};
  await writeFile(`artifacts/live-games-experience-${tag}.json`,JSON.stringify(report,null,2),{flag:'wx'});console.log(JSON.stringify(report,null,2));
}finally{await api('/auth/logout','POST').catch(()=>{});}
