export interface MusicExperienceOptions{revision:number;onlineMetadata:boolean;onlineCovers:boolean;radioDirectory:boolean;onlineScenes:boolean;scene:'vinyl'|'fireplace'|'rain'|'aquarium'|'ocean'|'gallery';showNowPlaying:boolean;radioCountry:string;metadataGeneration:number;sceneVideo:string}
export const defaultMusicExperience:MusicExperienceOptions={revision:0,onlineMetadata:false,onlineCovers:false,radioDirectory:true,onlineScenes:false,scene:'fireplace',showNowPlaying:true,radioCountry:'',metadataGeneration:0,sceneVideo:''};
export const musicScenes=[{id:'vinyl',name:'Plak',description:'Sade bir plak. Çevrimdışı da yanında.'},{id:'fireplace',name:'Şömine',description:'Sıcak ışık, sakin bir akşam.'},{id:'rain',name:'Yağmur',description:'Pencerenin ardında yağmur.'},{id:'aquarium',name:'Akvaryum',description:'Suyun içinde yavaş hareketler.'},{id:'ocean',name:'Denizaltı',description:'Derin mavi, ışık ve dinginlik.'},{id:'gallery',name:'Sanat galerisi',description:'Müziğin eşliğinde sanat eserleri.'}] as const;
export const radioCategories=[{id:'',label:'Popüler'},{id:'jazz',label:'Jazz'},{id:'classical',label:'Klasik'},{id:'ambient',label:'Ambient'},{id:'chillout',label:'Chillout'},{id:'blues',label:'Blues'},{id:'rock',label:'Rock'},{id:'pop',label:'Pop'},{id:'electronic',label:'Elektronik'},{id:'soul',label:'Soul'},{id:'news',label:'Haber'}] as const;
export interface RadioStation{id:string;name:string;url:string;country:string;language:string;tags:string[];codec:string;bitrate:number;hls:boolean;homepage:string|null}
export interface RadioFavorite{station:RadioStation;category:string}
export interface RadioLaunch{id:string;url:string;station:RadioStation}
export interface RadioStationPage{items:RadioStation[];offset:number;limit:number;hasMore:boolean}
export interface MusicEnrichmentProgress{total:number;checked:number;matched:number;review:number;failed:number;updatedAt:string|null;nextPass:string|null}
export function radioGroups(favorites:RadioFavorite[],category:string,query='',country=''){
  const groups=new Map<string,RadioFavorite[]>();const search=query.trim().toLocaleLowerCase();
  for(const item of favorites){if(category&&item.category!==category&&!item.station.tags.includes(category))continue;if(country&&item.station.country!==country)continue;if(search&&!(item.station.name+' '+item.category+' '+item.station.tags.join(' ')).toLocaleLowerCase().includes(search))continue;const key=item.category||item.station.tags[0]||'Diğer';groups.set(key,[...(groups.get(key)??[]),item]);}
  return [...groups].map(([name,items])=>({name,items}));
}
