import {useSyncExternalStore} from 'react';
import {createInstance} from 'i18next';
import {english,turkish} from 'virtual:home-translations';
import {appUrl} from '../base';

export type UiLanguage='en'|'tr';
const storageKey='home.ui-language:'+appUrl('/');
function stored():UiLanguage{try{return typeof localStorage!=='undefined'&&localStorage.getItem(storageKey)==='tr'?'tr':'en';}catch{return'en';}}
const i18n=createInstance();
void i18n.init({lng:stored(),supportedLngs:['en','tr'],fallbackLng:'en',resources:{en:{translation:english},tr:{translation:turkish}},keySeparator:false,nsSeparator:false,initAsync:false,interpolation:{escapeValue:false},returnNull:false});
export const getUiLanguage=():UiLanguage=>i18n.language==='tr'?'tr':'en';
export const uiLocale=()=>getUiLanguage()==='tr'?'tr-TR':'en-US';
function subscribe(listener:()=>void){i18n.on('languageChanged',listener);return()=>{i18n.off('languageChanged',listener);};}
export function useUiLanguage(){return useSyncExternalStore(subscribe,getUiLanguage,()=> 'en' as UiLanguage);}
function documentLanguage(){
  if(typeof document!=='undefined'){
    document.documentElement.lang=getUiLanguage();document.documentElement.dir='ltr';
    document.title=getUiLanguage()==='tr'?'Home — Medyanın evi':'Home — A home for your media';
    document.querySelector('meta[name="description"]')?.setAttribute('content',getUiLanguage()==='tr'?'Medyan için daha kişisel bir yer. Kendi sunucunda, kendi kontrolünde.':'A personal place for your media. Your server. Your control.');
  }
  if(typeof navigator!=='undefined'&&'serviceWorker' in navigator)navigator.serviceWorker.controller?.postMessage({type:'home.language',language:getUiLanguage()});
}
export async function setUiLanguage(language:UiLanguage){try{localStorage.setItem(storageKey,language);}catch{}await i18n.changeLanguage(language);documentLanguage();}
documentLanguage();
if(typeof window!=='undefined'){
  window.addEventListener('storage',event=>{if(event.key===storageKey||event.key===null)void i18n.changeLanguage(stored()).then(documentLanguage);});
  if('serviceWorker'in navigator)navigator.serviceWorker.addEventListener('controllerchange',documentLanguage);
}
export function translate(value:string,parameters:Record<string,unknown>={}):string{
  const key=value.replace(/\s+/g,' ').trim();
  const leading=/^\s/.test(value)?' ':'';const trailing=/\s$/.test(value)?' ':'';
  return leading+i18n.t(key,{...parameters,defaultValue:key})+trailing;
}
export function runtimeText(value:string|null|undefined):string;
export function runtimeText<T>(value:T):T|string;
export function runtimeText(value:unknown):unknown{
  if(typeof value!=='string')return value??'';
  return Object.hasOwn(english,value.replace(/\s+/g,' ').trim())?translate(value):value;
}
