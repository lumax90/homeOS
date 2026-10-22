import type {Game} from './model';
export type GameGrouping='system'|'category'|'alphabet'|'none';
export type GameSorting='recent'|'title'|'played';
export function systemName(system?:string|null):string {
  return ({gba:'Game Boy Advance',gb:'Game Boy',gbc:'Game Boy Color',nds:'Nintendo DS',ps1:'PlayStation',ps2:'PlayStation 2',psp:'PlayStation Portable',wii:'Wii',gamecube:'GameCube',nes:'NES',snes:'Super Nintendo',megadrive:'Mega Drive / Genesis',mastersystem:'Master System',gamegear:'Game Gear',segacd:'Mega-CD / Sega CD',sega32x:'Sega 32X',n64:'Nintendo 64','3ds':'Nintendo 3DS',wiiu:'Wii U',xbox:'Xbox',saturn:'Saturn',dreamcast:'Dreamcast',pcengine:'PC Engine / TurboGrafx-16',pcenginecd:'PC Engine CD',supergrafx:'SuperGrafx',atari2600:'Atari 2600',atari7800:'Atari 7800',lynx:'Atari Lynx',jaguar:'Atari Jaguar',ngp:'Neo Geo Pocket / Color',wonderswan:'WonderSwan / Color',virtualboy:'Virtual Boy',colecovision:'ColecoVision',pokemini:'Pokémon Mini',arcade:'Arcade / Neo Geo'} as Record<string,string>)[system??'']??system??'ROM';
}
export function romGroups(games:Game[],group:GameGrouping,sort:GameSorting):Array<{id:string;label:string;games:Game[]}> {
  const sorted=[...games].sort((a,b)=>sort==='title'?a.title.localeCompare(b.title):sort==='played'?b.state.playedSeconds-a.state.playedSeconds:(b.state.lastPlayedAt??b.importedAt??'').localeCompare(a.state.lastPlayedAt??a.importedAt??''));
  const groups=new Map<string,Game[]>();
  for(const game of sorted){const key=group==='system'?(game.systemLabel??systemName(game.system)):group==='category'?(game.category||''):group==='alphabet'?(game.title.trim()[0]?.toLocaleUpperCase()??'#'):'';groups.set(key,[...(groups.get(key)??[]),game]);}
  return [...groups].map(([id,games])=>({id,label:id,games}));
}
