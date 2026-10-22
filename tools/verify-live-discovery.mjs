import {readFile,writeFile} from 'node:fs/promises';
import {DatabaseSync} from 'node:sqlite';

// Public provider metadata and owner-local shelf reads only. No media/playback commands.
const base=process.env.HOME_BASE_URL??'http://127.0.0.1:5080';
if(!process.env.HOME_USERNAME||!process.env.HOME_PASSWORD)throw new Error('Set HOME_USERNAME and HOME_PASSWORD.');
const before=JSON.parse(await readFile('artifacts/preview-before-v029.json','utf8'));
const cookies=new Map();let csrf='';let signedIn=false;
async function api(path,method='GET',body){
  const headers={Cookie:[...cookies].map(([name,value])=>name+'='+value).join('; ')};
  if(method!=='GET')headers['X-CSRF-TOKEN']=csrf;if(body!==undefined)headers['Content-Type']='application/json';
  const response=await fetch(base+'/api'+path,{method,headers,body:body===undefined?undefined:JSON.stringify(body),signal:AbortSignal.timeout(45000)});
  for(const cookie of response.headers.getSetCookie()){const pair=cookie.split(';',1)[0];const index=pair.indexOf('=');cookies.set(pair.slice(0,index),pair.slice(index+1));}
  if(!response.ok)throw new Error(`${path}: HTTP ${response.status}`);return response.status===204?null:response.json();
}
try{
  csrf=(await api('/session')).csrfToken;await api('/auth/login','POST',{displayName:process.env.HOME_USERNAME,password:process.env.HOME_PASSWORD});signedIn=true;csrf=(await api('/session')).csrfToken;
  const health=await api('/health');if(health.version!=='0.29.0')throw new Error('Unexpected preview version.');
  const [english,turkish,settings]=await Promise.all([api('/discovery/genres?provider=tmdb&kind=movie&language=en-US'),api('/discovery/genres?provider=tmdb&kind=movie&language=tr-TR'),api('/metadata/settings')]);
  if(english.find(item=>item.id===28)?.name!=='Action'||turkish.find(item=>item.id===28)?.name!=='Aksiyon')throw new Error('Genre labels do not follow the requested UI language.');
  if(settings.language!==before.metadataLanguage)throw new Error('The UI label request changed metadata preferences.');
  const action=await api('/discovery?provider=tmdb&kind=movie&collection=popular&genre=28&genreLanguage=en-US&page=1');
  if(!action.items.length||!action.items.every(item=>item.genres.includes('Action')))throw new Error('Real genre discovery labels are not English.');
  const shelves={};for(const shelf of ['watchlist','history','continue'])shelves[shelf]=await api('/discovery/personal?kind=movie&shelf='+shelf+'&limit=30');
  const db=new DatabaseSync('.data/preview/home.db',{readOnly:true});let retained,schema,fk,ownShelves;
  try{
    schema=db.prepare('PRAGMA user_version').get().user_version;fk=db.prepare('PRAGMA foreign_key_check').all().length;
    const contains=(old,current)=>old.every(row=>current.some(value=>JSON.stringify(value)===JSON.stringify(row)));
    retained=contains(before.personal,db.prepare('SELECT user_id,content_id,favorite,rating FROM personal_content ORDER BY user_id,content_id').all())&&contains(before.collections,db.prepare('SELECT id,user_id,name,shared FROM collections ORDER BY id').all())&&contains(before.items,db.prepare('SELECT collection_id,content_id,created_at FROM collection_items ORDER BY collection_id,content_id').all())&&contains(before.media,db.prepare('SELECT id,library_id,relative_path,size_bytes FROM media').all())&&contains(before.requests,db.prepare('SELECT id,content_id,requested_by FROM content_requests').all())&&contains(before.downloads,db.prepare('SELECT id,info_hash,owner_id,archived FROM downloads').all());
    const watchedIds=new Set(db.prepare("SELECT DISTINCT content_id FROM content_watch_state WHERE user_id='1' AND (position_seconds>0 OR watched=1)").all().map(row=>row.content_id));
    ownShelves=shelves.history.items.every(item=>watchedIds.has(item.id));
  }finally{db.close();}
  if(!retained||schema!==39||fk!==0||!ownShelves)throw new Error('Data preservation or owner shelf boundary failed.');
  const html=await(await fetch(base+'/discover',{cache:'no-store'})).text();const entry=/<script[^>]+src="([^"]+)"/.exec(html)?.[1];
  const client=await(await fetch(new URL(entry,base))).text();if(!client.includes('shelves-v1')||!client.includes('/discovery/personal'))throw new Error('New discovery client is not being served.');
  const result={verifiedAt:new Date().toISOString(),version:health.version,schema,foreignKeyViolations:fk,existingDataRetained:retained,metadataLanguage:settings.language,uiGenreLanguagesVerified:['en-US','tr-TR'],englishGenres:english.length,genreReleases:action.items.length,nextPage:action.nextPage,shelves:Object.fromEntries(Object.entries(shelves).map(([key,value])=>[key,{total:value.total,returned:value.items.length,ids:value.items.map(item=>item.id)}])),personalHistoryOwnerVerified:ownShelves,clientEntry:entry,newShelfClientServed:true,playbackCommandsIssued:0,acquisitionCommandsIssued:0,browserAcceptance:'user-owned; metadata/shell reads only'};
  await writeFile('artifacts/live-discovery-v029.json',JSON.stringify(result,null,2)+'\n');console.log(JSON.stringify(result,null,2));
}finally{if(signedIn)await api('/auth/logout','POST').catch(()=>{});}
