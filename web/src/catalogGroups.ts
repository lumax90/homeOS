import type {MediaItem} from './api.ts';

/** The server groups before pagination; preserve its stable identities across pages. */
export function catalogGroups(files:readonly MediaItem[]):MediaItem[]{
  const groups=new Map<string,MediaItem>();
  for(const file of files){
    const key=file.catalogGroup?.key??(file.content?`${file.content.id}:${file.content.seasonNumber}:${file.content.episodeNumber}`:file.id);
    if(!groups.has(key))groups.set(key,file);
  }
  return [...groups.values()];
}
export function catalogCardKey(item:MediaItem):string{return item.catalogGroup?.key??item.id;}
export function catalogTitle(item:MediaItem):string{return item.catalogGroup?.kind==='show'?item.catalogGroup.title:item.title;}
