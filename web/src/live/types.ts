export interface TvChannel{id:string;sourceId:string;name:string;group:string;logo:string|null;radio:boolean;minimumAge:number|null;enabled:boolean;available:boolean;ordinal:number;epgId:string|null}
export interface TvProgramme{id:string;channelId:string;title:string;description:string;startsAt:string;endsAt:string;repeat:boolean|null;minimumAge:number|null}
export interface TvChannelCard{channel:TvChannel;sourceName:string;presetId:string|null;favorite:boolean;lastWatchedAt:string|null;now:TvProgramme|null;next:TvProgramme|null;playable:boolean}
export interface TvLibraryPage{items:TvChannelCard[];total:number;offset:number;limit:number;groups:{name:string;count:number}[];channels:number;favorites:number;sources:number}
export interface TvSource{id:string;name:string;enabled:boolean;revision:number;tuners:number;lastError:string|null;lastSync:string|null;nextSync:string;presetId:string|null;channels:number}
export interface TvPublicList{list:{id:string;name:string;category:string;playlistUrl:string;description:string;provider:string;license:string;sourceUrl:string};source:TvSource|null}
export interface TvSourceSettings{allowLan:boolean;playlistUrlConfigured:boolean;playlistFileConfigured:boolean;streamConfigured:boolean;epgConfigured:boolean;group:string;radio:boolean}
import type {MediaTracks} from '../api';
export interface LiveStreamPlan{mode:string;container:string;transport:'hls.js'|'native-hls';copyVideo:boolean;copyAudio:boolean;videoCodec:string|null;audioCodec:string|null}
export interface LiveWatch{id:string;url:string;channel:TvChannel;timeShiftMinutes:number;tracks?:MediaTracks|null;prepared?:boolean;plan?:LiveStreamPlan|null}
export interface LivePlaybackStatus{id:string;state:string;recoverable:boolean;error:string|null;plan:LiveStreamPlan|null;cacheBytes:number}
export interface TvSelection{channel:TvChannel;command:number}
export interface TvRecording{id:string;title:string;startsAt:string;endsAt:string;status:string;revision:number;message:string;bytes:number;mediaId:string|null}
export interface TvRule{id:string;channelId:string;title:string;enabled:boolean;onlyNew:boolean;paddingBefore:number;paddingAfter:number;priority:number;revision:number}
export function programmeProgress(programme:Pick<TvProgramme,'startsAt'|'endsAt'>|null,now=Date.now()){
  if(!programme)return null;const start=Date.parse(programme.startsAt),end=Date.parse(programme.endsAt);return !Number.isFinite(start)||!Number.isFinite(end)||end<=start?null:Math.min(1,Math.max(0,(now-start)/(end-start)));
}
export function liveWindow(start:number,end:number,position:number){const valid=Number.isFinite(start)&&Number.isFinite(end)&&end>start,current=valid?Math.min(end,Math.max(start,Number.isFinite(position)?position:start)):0;return{start:valid?start:0,end:valid?end:0,position:current,behind:valid?Math.max(0,end-current):0,ready:valid};}
