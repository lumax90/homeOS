import {basePath} from '../base';
export interface NativeDisplayPreferences {fit:'contain'|'integer'|'fill';smooth:boolean;volume:number}
export const defaultNativeDisplay:NativeDisplayPreferences={fit:'contain',smooth:false,volume:80};
function key(user:string){return `home.native-display:${basePath}:${user}`;}
export function nativeDisplayPreferences(user:string):NativeDisplayPreferences{
  try{const data=JSON.parse(localStorage.getItem(key(user))??'{}');return {fit:['contain','integer','fill'].includes(data.fit)?data.fit:'contain',smooth:data.smooth===true,volume:typeof data.volume==='number'?Math.max(0,Math.min(100,data.volume)):80};}catch{return {...defaultNativeDisplay};}
}
export function saveNativeDisplay(user:string,value:NativeDisplayPreferences){try{localStorage.setItem(key(user),JSON.stringify(value));window.dispatchEvent(new Event('home-native-display'));}catch{}}
