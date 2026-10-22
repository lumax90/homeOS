import { readFile, writeFile } from 'node:fs/promises';
import { join } from 'node:path';
import { createHash } from 'node:crypto';
import { DatabaseSync } from 'node:sqlite';

// Read-only runtime/data/HTTP acceptance, never enables native execution or launches/downloads a game.
const mode=process.argv[2];if(!['--capture','--verify'].includes(mode))throw new Error('Use --capture or --verify.');
const base=process.env.HOME_BASE_URL??'http://127.0.0.1:5080',expected=process.env.HOME_VERIFY_VERSION??(mode==='--capture'?'0.33.0':'0.34.0');
const tag=process.env.HOME_VERIFY_TAG??'v034';if(!/^v\d+$/.test(tag))throw new Error('Invalid evidence tag.');
const beforePath=tag==='v034'?'artifacts/native-preview-data-before.json':`artifacts/native-preview-data-before-${tag}.json`;
const cookies=new Map();let csrf='',signedIn=false;
async function api(path,method='GET',body){
  const headers={Cookie:[...cookies].map(([key,value])=>key+'='+value).join('; ')};if(method!=='GET')headers['X-CSRF-TOKEN']=csrf;if(body!==undefined)headers['Content-Type']='application/json';
  const response=await fetch(base+'/api'+path,{method,headers,body:body===undefined?undefined:JSON.stringify(body),signal:AbortSignal.timeout(15000)});
  for(const cookie of response.headers.getSetCookie()){const pair=cookie.split(';',1)[0],at=pair.indexOf('=');cookies.set(pair.slice(0,at),pair.slice(at+1));}
  if(!response.ok)throw new Error(path+': HTTP '+response.status);return response.status===204?null:response.json();
}
function capture(path){
  const db=new DatabaseSync(join(path,'home.db'),{readOnly:true});
  try{
    const digest=(sql)=>{const rows=db.prepare(sql).all();return {count:rows.length,sha256:createHash('sha256').update(JSON.stringify(rows)).digest('hex')};};
    return {schema:db.prepare('PRAGMA user_version').get().user_version,foreignKeyViolations:db.prepare('PRAGMA foreign_key_check').all().length,
      media:digest('SELECT id,library_id,relative_path,size_bytes FROM media ORDER BY id'),requests:digest('SELECT id,content_id,requested_by FROM content_requests ORDER BY id'),
      personal:digest('SELECT * FROM personal_content ORDER BY user_id,content_id'),collections:digest('SELECT * FROM collections ORDER BY id'),
      gameUserState:digest('SELECT * FROM game_user_state ORDER BY user_id,game_id'),gameSaves:digest('SELECT * FROM game_saves ORDER BY user_id,game_id,game_version'),
      roms:db.prepare("SELECT COUNT(*) AS count FROM sqlite_master WHERE name='game_roms'").get().count?digest('SELECT * FROM game_roms ORDER BY user_id,id'):null,
      metadataLanguage:db.prepare("SELECT value FROM settings WHERE key='metadata.language'").get()?.value,
      downloads:db.prepare('SELECT id,info_hash,owner_id,archived,desired_state FROM downloads ORDER BY id').all()};
  }finally{db.close();}
}
try{
  if(!process.env.HOME_USERNAME||!process.env.HOME_PASSWORD)throw new Error('Set owner credentials.');
  csrf=(await api('/session')).csrfToken;await api('/auth/login','POST',{displayName:process.env.HOME_USERNAME,password:process.env.HOME_PASSWORD});signedIn=true;
  const session=await api('/session');csrf=session.csrfToken;const health=await api('/health');if(health.version!==expected)throw new Error('Unexpected runtime version.');
  const runtime=await api('/operations/runtime'),state=capture(runtime.statePath);if(state.foreignKeyViolations)throw new Error('Foreign key violation.');
  if(mode==='--capture'){
    if((await api('/games/sessions/active')).length)throw new Error('Active game: save and exit before capturing deployment preservation evidence.');
    const status=await api('/games/native/status'),catalog=await api('/games');
    await writeFile(beforePath,JSON.stringify({capturedAt:new Date().toISOString(),version:health.version,state,nativeEnabled:status.enabled,catalogIds:catalog.items.map(game=>game.id).sort()},null,2),{flag:'wx'});
    console.log(JSON.stringify({captured:true,version:health.version,schema:state.schema,downloads:state.downloads,gameStates:state.gameUserState.count}));
  }else{
    const evidence=JSON.parse(await readFile(beforePath,'utf8')),before=evidence.state;
    for(const key of ['media','requests','personal','collections','gameUserState','gameSaves','metadataLanguage','downloads'])if(JSON.stringify(before[key])!==JSON.stringify(state[key]))throw new Error('Existing user data changed: '+key);
    if(state.schema!==Number(process.env.HOME_VERIFY_SCHEMA??41))throw new Error('Native ROM migration missing.');
    if(before.roms!==undefined&&JSON.stringify(before.roms)!==JSON.stringify(state.roms))throw new Error('User ROM metadata changed.');
    const expectedEnabled=evidence.nativeEnabled??false;
    const status=await api('/games/native/status');if(!status.prepared||!status.canConfigure||status.enabled!==expectedEnabled||status.available!==expectedEnabled)throw new Error('User native enable state changed.');
    const catalog=await api('/games');if(catalog.serverRunnerAvailable!==expectedEnabled||(evidence.catalogIds?JSON.stringify(catalog.items.map(game=>game.id).sort())!==JSON.stringify(evidence.catalogIds):catalog.items.length!==7))throw new Error('Existing game catalog changed.');
    const source=await fetch(base+'/native/reader.js').then(response=>response.arrayBuffer());if(createHash('sha256').update(new Uint8Array(source)).digest('hex')!=='a802f229b803c33713d4c69c4cc0d480108a5bf384947aeee4aaf04268bf85c1')throw new Error('Official stream reader changed.');
    const html=await fetch(base+'/games',{cache:'no-store'}).then(response=>response.text());const entry=/<script[^>]+src="([^"]+)"/.exec(html)?.[1];if(!entry)throw new Error('Missing client shell.');
    const report={verifiedAt:new Date().toISOString(),version:health.version,schema:state.schema,existingDataPreserved:true,foreignKeyViolations:state.foreignKeyViolations,nativeStatus:status,clientEntry:entry,downloads:state.downloads,gameplayCommandsIssued:0,acquisitionCommandsIssued:0,browserAcceptance:'User-owned; no browser automation.'};
    if(tag!=='v034'){
      const wrapper=await fetch(base+'/native/home-reader.js').then(response=>response.text());
      if(!wrapper.includes('HomeNativeWebRTCReader')||!wrapper.includes("headers.set('Authorization', 'Bearer ' + conf.token)"))throw new Error('Scoped WHEP adapter missing from the live client.');
    }
    await writeFile(`artifacts/live-native-${tag}.json`,JSON.stringify(report,null,2));console.log(JSON.stringify(report,null,2));
  }
}finally{if(signedIn)await api('/auth/logout','POST').catch(()=>{});}
