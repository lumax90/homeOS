import {basePath} from '../base.ts';

export type NativeStreamingBackend='standard'|'selkies';
export function parseStreamingBackend(value:unknown):NativeStreamingBackend{return value==='selkies'?'selkies':'standard';}
function key(user:string){return `home.native-streaming:${basePath}:${user}`;}
export function nativeStreamingBackend(user:string):NativeStreamingBackend{
  try{return parseStreamingBackend(localStorage.getItem(key(user)));}catch{return 'standard';}
}
export function saveNativeStreamingBackend(user:string,value:NativeStreamingBackend){
  try{localStorage.setItem(key(user),parseStreamingBackend(value));}catch{}
}
export interface NativeStreamingStatus{defaultBackend:NativeStreamingBackend;items:{id:NativeStreamingBackend;available:boolean;experimental:boolean;reason:string|null}[]}
