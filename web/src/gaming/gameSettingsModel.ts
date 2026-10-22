import type {NativeDisplayPreferences} from './nativePreferences.ts';
export interface DisplayOverride{fit?:'contain'|'integer'|'fill'|null;smooth?:boolean|null;volume?:number|null;quality?:'auto'|'quality'|'low'|null}
export interface SettingOption{key:string;label:string;info:string;values:string[];value:string;inheritedValue:string;source:'game'|'core'|'default';group:'video'|'audio'|'controls'|'compatibility';common:boolean;managed:boolean}
export interface ControllerChoice{port:number;device:number;items:Array<{id:number;name:string}>}
export interface ScopedController{port:number;device:number}
export interface GameSettingsView{gameId:string;coreId:string;coreName:string;binarySha256:string;revision:number;coreRevision:number;schemaReady:boolean;options:SettingOption[];controllerTypes:ControllerChoice[];coreControllers:ScopedController[];gameControllers:ScopedController[];coreDisplay:DisplayOverride;gameDisplay:DisplayOverride;restartRequired:boolean;serverQuality:string}
export function resolveGameDisplay(base:NativeDisplayPreferences,core:DisplayOverride={},game:DisplayOverride={}):NativeDisplayPreferences{
  return{fit:game.fit??core.fit??base.fit,smooth:game.smooth??core.smooth??base.smooth,volume:game.volume??core.volume??base.volume};
}
export function visibleSettingOptions(options:SettingOption[],section:string,search:string){const query=search.toLowerCase().trim();return options.filter(option=>!option.managed&&(section==='advanced'||option.common&&option.group===section)&&(!query||(option.label+' '+option.key).toLowerCase().includes(query)));}
export const settingSections=[{id:'video',label:'Görüntü'},{id:'audio',label:'Ses'},{id:'controls',label:'Kontroller'},{id:'compatibility',label:'Uyumluluk'},{id:'advanced',label:'Gelişmiş'}] as const;
