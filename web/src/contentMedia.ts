/** Embed only provider-supplied YouTube video keys, not arbitrary iframe URLs. */
export function youtubeTrailerUrl(key:string,language:'en'|'tr'):string|null{
  if(!/^[a-zA-Z0-9_-]{11}$/.test(key))return null;
  const url=new URL(`https://www.youtube-nocookie.com/embed/${key}`);
  url.searchParams.set('autoplay','0');url.searchParams.set('playsinline','1');url.searchParams.set('controls','1');url.searchParams.set('rel','0');url.searchParams.set('hl',language);
  return url.href;
}
export function creditsInRow(width:number):number{return Math.max(1,Math.min(8,Math.floor((width+18)/148)));}
