import {readFile,writeFile} from 'node:fs/promises';
import {DatabaseSync} from 'node:sqlite';

// Shell/state reads only. No UI clicks, media playback, provider fetches or acquisition.
const base=process.env.HOME_BASE_URL??'http://127.0.0.1:5080';
const expectedVersion=process.env.HOME_VERIFY_VERSION??'0.29.1';
const tag=process.env.HOME_VERIFY_TAG??'v0291';
if(!/^v\d+$/.test(tag))throw new Error('Use a bounded artifact version tag.');
if(!process.env.HOME_USERNAME||!process.env.HOME_PASSWORD)throw new Error('Set HOME_USERNAME and HOME_PASSWORD.');
const before=JSON.parse(await readFile(`artifacts/preview-before-${tag}.json`,'utf8'));
const metadataBaseline=process.env.HOME_VERIFY_METADATA_BASELINE?JSON.parse(await readFile(process.env.HOME_VERIFY_METADATA_BASELINE,'utf8')):null;
if(metadataBaseline&&metadataBaseline.schema!==39)throw new Error('Unexpected metadata baseline schema.');
const expectedMetadataLanguage=metadataBaseline?.metadataLanguage??before.metadataLanguage;
const cookies=new Map();let csrf='';let signedIn=false;
async function api(path,method='GET',body){
  const headers={Cookie:[...cookies].map(([name,value])=>name+'='+value).join('; ')};
  if(method!=='GET')headers['X-CSRF-TOKEN']=csrf;if(body!==undefined)headers['Content-Type']='application/json';
  const response=await fetch(base+'/api'+path,{method,headers,body:body===undefined?undefined:JSON.stringify(body),signal:AbortSignal.timeout(15000)});
  for(const cookie of response.headers.getSetCookie()){const pair=cookie.split(';',1)[0];const index=pair.indexOf('=');cookies.set(pair.slice(0,index),pair.slice(index+1));}
  if(!response.ok)throw new Error(`${path}: HTTP ${response.status}`);return response.status===204?null:response.json();
}
try{
  csrf=(await api('/session')).csrfToken;await api('/auth/login','POST',{displayName:process.env.HOME_USERNAME,password:process.env.HOME_PASSWORD});signedIn=true;csrf=(await api('/session')).csrfToken;
  const health=await api('/health');if(health.version!==expectedVersion)throw new Error('Unexpected preview version.');
  const [settings,downloads]=await Promise.all([api('/metadata/settings'),api('/downloads')]);
  if(settings.language!==expectedMetadataLanguage)throw new Error('Metadata language changed from the verification baseline.');
  const db=new DatabaseSync('.data/preview/home.db',{readOnly:true});let retained,schema,fk,counts;
  try{
    schema=db.prepare('PRAGMA user_version').get().user_version;fk=db.prepare('PRAGMA foreign_key_check').all().length;
    const current={personal:db.prepare('SELECT user_id,content_id,favorite,rating FROM personal_content ORDER BY user_id,content_id').all(),collections:db.prepare('SELECT id,user_id,name,shared FROM collections ORDER BY id').all(),items:db.prepare('SELECT collection_id,content_id,created_at FROM collection_items ORDER BY collection_id,content_id').all(),media:db.prepare('SELECT id,library_id,relative_path,size_bytes FROM media').all(),requests:db.prepare('SELECT id,content_id,requested_by FROM content_requests').all(),downloads:db.prepare('SELECT id,info_hash,owner_id,archived FROM downloads').all()};
    retained=Object.entries(current).every(([key,rows])=>before[key].every(old=>rows.some(row=>JSON.stringify(old)===JSON.stringify(row))));
    counts=Object.fromEntries(Object.entries(current).map(([key,rows])=>[key,rows.length]));
  }finally{db.close();}
  if(!retained||schema!==39||fk!==0)throw new Error('Data preservation failed.');
  const html=await(await fetch(base+'/discover',{cache:'no-store'})).text();
  const entry=/<script[^>]+src="([^"]+)"/.exec(html)?.[1];const style=/<link[^>]+rel="stylesheet"[^>]+href="([^"]+)"/.exec(html)?.[1];
  if(!entry||!style)throw new Error('Shell does not reference the expected assets.');
  const [client,css]=await Promise.all([fetch(new URL(entry,base)).then(response=>response.text()),fetch(new URL(style,base)).then(response=>response.text())]);
  if(!client.includes('select-v1')||!client.includes('home-option:'))throw new Error('The shared selection renderer is not served.');
  if(!css.includes('--home-menu-background')||!css.includes('home-select-popover')||!css.includes('media-menu-popup'))throw new Error('The shared menu material is not served.');
  const controllerLayerServed=client.includes('home-controller-v1:')&&client.includes('gamepadconnected')&&css.includes('data-home-input')&&css.includes('controller-keyboard');
  if(process.env.HOME_VERIFY_CONTROLLER==='1'&&!controllerLayerServed)throw new Error('The controller layer is not served.');
  const loadingNamespaceServed=client.includes('home-skeleton')&&css.includes('home-skeleton-shimmer');
  if(process.env.HOME_VERIFY_SKELETON==='1'&&!loadingNamespaceServed)throw new Error('The isolated loading animation is not served.');
  const settingsNavigationServed=client.includes('organized-v1')&&client.includes('home-page-v2')&&client.includes('data-home-scroll')&&css.includes('settings-shell');
  if(process.env.HOME_VERIFY_SETTINGS==='1'&&!settingsNavigationServed)throw new Error('The settings/navigation slice is not served.');
  const result={verifiedAt:new Date().toISOString(),version:health.version,schema,foreignKeyViolations:fk,existingDataRetained:retained,counts,metadataLanguage:settings.language,metadataLanguageInitialSnapshot:before.metadataLanguage,metadataLanguageBaseline:metadataBaseline?`pre-apply backup ${metadataBaseline.backupId}`:'initial snapshot',sharedRendererServed:true,sharedMenuMaterialServed:true,controllerLayerServed,loadingNamespaceServed,settingsNavigationServed,clientEntry:entry,styleEntry:style,downloadStates:downloads.map(item=>({id:item.id,status:item.status,desiredState:item.desiredState})),playbackCommandsIssued:0,acquisitionCommandsIssued:0,browserAcceptance:'User-owned; shell and state reads only. No physical TV/gamepad acceptance claimed.',fullBackendRegression:'Not run for this UI-only slice; VERIFY-27 remains open.'};
  await writeFile(`artifacts/live-controls-${tag}.json`,JSON.stringify(result,null,2)+'\n');console.log(JSON.stringify(result,null,2));
}finally{if(signedIn)await api('/auth/logout','POST').catch(()=>{});}
