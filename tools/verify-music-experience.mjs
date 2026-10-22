import {readFile,writeFile,readdir} from 'node:fs/promises';
import {join} from 'node:path';
import {createHash} from 'node:crypto';
import {DatabaseSync} from 'node:sqlite';

// Read-only library/public-text inspection. Never opens an audio stream, artwork, stock video or acquisition command.
const mode=process.argv[2];if(!['--capture','--verify'].includes(mode))throw Error('Use --capture or --verify.');
const base=process.env.HOME_BASE_URL??'http://127.0.0.1:5080',version=process.env.HOME_VERIFY_VERSION??(mode==='--capture'?'0.40.1':'0.41.0');
const tag=process.env.HOME_VERIFY_TAG??'v041';if(!/^v\d+$/.test(tag))throw Error('Invalid evidence tag.');
const before=`artifacts/music-data-before-${tag}.json`,cookies=new Map();let csrf='',signedIn=false;
async function api(path,method='GET',body){const response=await fetch(base+'/api'+path,{method,headers:{Cookie:[...cookies].map(([key,value])=>key+'='+value).join('; '),'X-CSRF-TOKEN':csrf,...(body?{'Content-Type':'application/json'}:{})},body:body?JSON.stringify(body):undefined,signal:AbortSignal.timeout(60000)});for(const cookie of response.headers.getSetCookie()){const pair=cookie.split(';',1)[0],at=pair.indexOf('=');cookies.set(pair.slice(0,at),pair.slice(at+1));}if(!response.ok)throw Error(path+': HTTP '+response.status);return response.status===204?null:response.json();}
async function capture(path){
  const db=new DatabaseSync(join(path,'home.db'),{readOnly:true});const tables={};
  try{for(const table of ['music_artists','music_albums','music_tracks','music_album_matches','music_track_matches','music_favorites','music_playback_preferences','playlists','playlist_items','playback_queues','user_watch_state','game_roms','game_user_state','game_saves']){const rows=db.prepare(`SELECT * FROM ${table} ORDER BY rowid`).all();tables[table]={count:rows.length,sha256:createHash('sha256').update(JSON.stringify(rows)).digest('hex')};}tables.schema=db.prepare('PRAGMA user_version').get().user_version;tables.foreignKeyViolations=db.prepare('PRAGMA foreign_key_check').all().length;tables.downloads=db.prepare('SELECT id,info_hash,owner_id,archived,desired_state FROM downloads ORDER BY id').all();}finally{db.close();}
  const files={};for(const kind of ['preferences','radio'])for(const entry of await readdir(join(path,'music',kind),{withFileTypes:true}).catch(()=>[])){if(entry.isFile()&&entry.name.endsWith('.json'))files[`${kind}/${entry.name}`]=createHash('sha256').update(await readFile(join(path,'music',kind,entry.name))).digest('hex');}
  return {tables,files};
}
try{
  if(!process.env.HOME_USERNAME||!process.env.HOME_PASSWORD)throw Error('Existing owner credentials required.');csrf=(await api('/session')).csrfToken;await api('/auth/login','POST',{displayName:process.env.HOME_USERNAME,password:process.env.HOME_PASSWORD});signedIn=true;csrf=(await api('/session')).csrfToken;
  const health=await api('/health');if(health.version!==version)throw Error('Unexpected live version.');const runtime=await api('/operations/runtime'),data=await capture(runtime.statePath);if(data.tables.foreignKeyViolations)throw Error('Foreign key violations.');
  if(mode==='--capture'){
    if((await api('/account/playback?all=true')).active.length||(await api('/games/sessions/active')).length)throw Error('Playback is active; defer the restart.');
    await writeFile(before,JSON.stringify({capturedAt:new Date().toISOString(),version,data},null,2),{flag:'wx'});console.log(JSON.stringify({captured:true,version,musicTracks:data.tables.music_tracks.count,roms:data.tables.game_roms.count,downloads:data.tables.downloads}));
  }else{
    const prior=JSON.parse(await readFile(before,'utf8'));for(const [key,value] of Object.entries(prior.data.tables))if(JSON.stringify(data.tables[key])!==JSON.stringify(value))throw Error('Existing data changed: '+key);for(const [key,value] of Object.entries(prior.data.files))if(data.files[key]!==value)throw Error('Existing music preferences changed: '+key);
    const preferences=await api('/music/experience'),progress=await api('/music/experience/progress'),tracks=await api('/music/library/tracks'),favorites=await api('/music/radio/favorites');
    const radios=preferences.radioDirectory?await api('/music/radio/stations?tag=jazz&country=TR'):null;
    if(!Array.isArray(tracks.items)||!Array.isArray(favorites)||radios&&!Array.isArray(radios.items))throw Error('Unexpected music response contract.');
    const html=await fetch(base+'/music').then(response=>response.text()),entry=/<script[^>]+src="([^"]+)"/.exec(html)?.[1];if(!entry)throw Error('Missing web client.');let source=await fetch(new URL(entry,base)).then(r=>r.text());
    const chunks=[...new Set([...source.matchAll(/["']\.\/([A-Za-z0-9_-]+\.js)["']/g)].map(match=>match[1]))].slice(0,35);for(const name of chunks)source+='\n'+await fetch(new URL(name,new URL(entry,base))).then(r=>r.text());
    for(const marker of ['music-workspace','music-settings-trigger','music-visual-mode','music-radio-dock','/music/radio/play','/music/experience'])if(!source.includes(marker))throw Error('Missing client feature: '+marker);
    if(tag==='v0411'){
      for(const marker of ['music-vinyl-record','music-discovery-search-input','data-controller-player-dock','focusplayer','rain-night','aquarium-boka'])if(!source.includes(marker))throw Error('Missing refined client feature: '+marker);
      if(source.includes('music-ambient-canvas'))throw Error('The removed procedural scenes are still served.');
      const artists=await api('/music/discovery/artists?q=Metallic&autocomplete=true');if(!artists.items.some(item=>item.name==='Metallica'))throw Error('The live prefix search contract is missing.');
    }
    const report={verifiedAt:new Date().toISOString(),version,processId:runtime.processId,existingDataPreserved:true,foreignKeyViolations:0,musicTracks:tracks.total,radioPageItems:radios?.items.length??null,preferences,progress,favoriteStations:favorites.length,clientEntry:entry,radioStreamsStarted:0,artworkRequests:0,stockMediaRequests:0,acquisitionCommands:0,browserAcceptance:'User-owned',data};
    await writeFile(`artifacts/live-music-experience-${tag}.json`,JSON.stringify(report,null,2),{flag:'wx'});console.log(JSON.stringify({version,existingDataPreserved:true,musicTracks:tracks.total,radioPageItems:report.radioPageItems,roms:data.tables.game_roms.count,gameStates:data.tables.game_user_state.count,gameSaves:data.tables.game_saves.count,pausedTransfers:data.tables.downloads.filter(item=>item.desired_state==='paused').length,radioStreamsStarted:0}));
  }
}finally{if(signedIn)await api('/auth/logout','POST').catch(()=>{});}
