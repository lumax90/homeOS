import {Button} from '@heroui/react';
import {useInfiniteQuery,useMutation,useQueryClient} from '@tanstack/react-query';
import {Heart,LoaderCircle,Play,Plus} from 'lucide-react';
import {appUrl,clock,mutate,request,type MediaItem,type MusicLibraryTrack,type MusicPage} from '../api';
import {MusicCover} from '../components/MusicReleases';
import {runtimeText,translate as t,useUiLanguage} from '../i18n';

export function MusicTrackTable({tracks,onPlay,onQueue,onAlbum,compact=false}:{tracks:MusicLibraryTrack[];onPlay:(items:MediaItem[],index?:number)=>void;onQueue:(items:MediaItem[])=>void;onAlbum:(id:string)=>void;compact?:boolean}){
  useUiLanguage();const cache=useQueryClient();
  const favorite=useMutation({mutationFn:(track:MusicLibraryTrack)=>mutate(`/music/media/${track.mediaId}/favorite`,{favorite:!track.favorite},'PUT'),onSuccess:()=>{void cache.invalidateQueries({queryKey:['music-library-tracks']});void cache.invalidateQueries({queryKey:['music-favorite']});void cache.invalidateQueries({queryKey:['music-recommendations']});}});
  return <><div className={`music-track-table${compact?' is-compact':''}`} role="list" aria-label="Şarkılar">{tracks.map((track,index)=><div className={`music-song-row${track.media.available?'':' is-unavailable'}`} role="listitem" key={track.mediaId}>
    <button className="music-song-play" aria-label={t('Çal: {{v0}}',{v0:track.title})} disabled={!track.media.available} onClick={()=>onPlay(tracks.map(row=>({...row.media,title:row.title})),index)}><span className="music-song-number">{index+1}</span><Play size={17} fill="currentColor"/></button>
    <div className="music-song-art"><MusicCover url={appUrl(`/api/music/albums/${track.albumId}/artwork`)}/></div><div className="music-song-title"><button onClick={()=>onPlay(tracks.map(row=>({...row.media,title:row.title})),index)} disabled={!track.media.available}>{track.title}</button><span>{track.artist}{!track.media.available&&<> · {t('Dosya erişilemiyor')}</>}</span></div>
    {!compact&&<button className="music-song-album" onClick={()=>onAlbum(track.albumId)}>{track.album}</button>}<span className="music-song-duration">{clock(track.media.durationSeconds??0)}</span>
    <Button isIconOnly variant="ghost" className="music-song-favorite" aria-label={track.favorite?t('Favorilerimden çıkar'):t('Favorilerime ekle')} aria-pressed={track.favorite} isDisabled={favorite.isPending} onPress={()=>favorite.mutate(track)}><Heart size={17} fill={track.favorite?'currentColor':'none'}/></Button>
    <Button isIconOnly variant="ghost" className="music-song-add" aria-label={t('Sıraya ekle: {{v0}}',{v0:track.title})} isDisabled={!track.media.available} onPress={()=>onQueue([{...track.media,title:track.title}])}><Plus size={18}/></Button>
  </div>)}</div>{favorite.error&&<p className="form-error">{runtimeText(favorite.error.message)}</p>}</>;
}
export function MusicTracks({view,search,onPlay,onQueue,onAlbum}:{view:'all'|'liked'|'recent';search:string;onPlay:(items:MediaItem[],index?:number)=>void;onQueue:(items:MediaItem[])=>void;onAlbum:(id:string)=>void}){
  const data=useInfiniteQuery({queryKey:['music-library-tracks',view,search],initialPageParam:0,queryFn:({pageParam,signal})=>request<MusicPage<MusicLibraryTrack>>(`/music/library/tracks?${new URLSearchParams({view,q:search,offset:String(pageParam)})}`,{signal}),getNextPageParam:page=>page.items.length&&page.offset+page.items.length<page.total?page.offset+page.items.length:undefined});
  const tracks=data.data?.pages.flatMap(page=>page.items)??[];
  return <section><div className="music-section-heading"><div><h2>{view==='liked'?'Beğendiğin şarkılar':view==='recent'?'Son dinlediklerin':'Şarkıların'}</h2><p>{data.data?.pages[0]?.total??0} parça</p></div><Button variant="secondary" onPress={()=>onPlay(tracks.map(track=>({...track.media,title:track.title})))} isDisabled={!tracks.some(track=>track.media.available)}><Play size={16} fill="currentColor"/> Görünenleri çal</Button></div>
    <MusicTrackTable tracks={tracks} onPlay={onPlay} onQueue={onQueue} onAlbum={onAlbum}/>{data.isPending&&<p className="music-inline-note"><LoaderCircle className="spin" size={18}/> Şarkılar yükleniyor…</p>}{!data.isPending&&!data.error&&!tracks.length&&<div className="music-empty-note">{search?'Bu aramada şarkı bulunamadı.':view==='liked'?'Sevdiğin şarkılara kalp bırak; burada bir araya gelsinler.':view==='recent'?'Dinlediğin şarkılar burada görünecek.':'Müzik klasöründeki şarkılar tarandığında burada görünür.'}</div>}
    {data.hasNextPage&&<Button variant="ghost" onPress={()=>data.fetchNextPage()} isDisabled={data.isFetching}>Diğer şarkılar</Button>}{data.error&&<p className="form-error">{runtimeText(data.error.message)} <Button variant="ghost" onPress={()=>data.refetch()}>Yeniden dene</Button></p>}
  </section>;
}
