import {useState}from'react';
import {useInfiniteQuery}from'@tanstack/react-query';
import {Button}from'@heroui/react';
import {ArrowLeft,CircleAlert,Play,Tv}from'lucide-react';
import {appUrl,bytes,duration,request,type LibrarySeriesPage,type MediaItem}from'../api';
import {Artwork}from'./Artwork';
import {usePageTitle}from'../usePageTitle';
import {catalogTitle}from'../catalogGroups';
import {ContentPeople}from'./CatalogFeeds';
import {ContentMedia}from'./ContentMedia';
import './title-page.css';

/** Local, access-filtered series episodes; opening a saved show needs no provider request. */
export function LibrarySeries({id,onBack,onPlay}:{id:string;onBack:()=>void;onPlay:(item:MediaItem,episodeId?:string)=>void}){
  const [season,setSeason]=useState('');
  const query=useInfiniteQuery({queryKey:['catalog','series',id,season],initialPageParam:0,
    queryFn:({pageParam,signal})=>request<LibrarySeriesPage>(`/media/${id}/series?offset=${pageParam}&limit=60${season?`&season=${season}`:''}`,{signal}),
    getNextPageParam:last=>last.offset+last.items.length<last.total?last.offset+last.items.length:undefined});
  const page=query.data?.pages[0],item=page?.item,episodes=query.data?.pages.flatMap(p=>p.items)??[];
  usePageTitle(item?catalogTitle(item):undefined);
  return <article className="title-page"><Button className="title-back" variant="ghost" onPress={onBack}><ArrowLeft size={18}/> Geri</Button>
    {!item?(query.error?<div className="search-empty" role="alert"><CircleAlert size={28}/><h1>Dizi açılamadı.</h1><p>{query.error.message}</p><Button onPress={()=>query.refetch()}>Yeniden dene</Button></div>:<div className="title-page-skeleton home-skeleton" role="status" aria-label="İçerik bilgileri"/>):<>
      <div className="title-hero"><div className="title-backdrop"><Artwork item={item} eager mode="backdrop"/></div><div className="title-shade"/>
        <div className="title-identity"><div className="title-poster"><Artwork item={item} eager/></div><section><p className="eyebrow">DİZİ · {item.libraryName}</p>
          <h1 data-page-heading tabIndex={-1}>{catalogTitle(item)}</h1><div className="title-facts"><span>{item.catalogGroup?.episodeCount??page?.total??0} bölüm</span><span>{page?.seasons.length??0} sezon</span></div>
          {item.content?.overview&&<p className="detail-description">{item.content.overview}</p>}
          {item.content&&<a className="content-source" href={appUrl(`/title/${item.content.provider}/show/${item.content.providerId}`)}>Dizi bilgileri</a>}
        </section></div></div>
      <div className="title-page-body"><section className="episode-section"><div className="section-heading"><h2><Tv size={18}/> Bölümler</h2>
        <select aria-label="Sezon filtresi" value={season} onChange={event=>setSeason(event.target.value)}><option value="">Tüm sezonlar</option>{page?.seasons.map(value=><option key={value} value={value}>{value===0?'Özel bölümler':`${value}. sezon`}</option>)}</select></div>
        {episodes.map(row=><div className="episode-row" key={row.file.id}><span className="episode-number">{row.season!==null&&row.episode!==null?`S${String(row.season).padStart(2,'0')} E${String(row.episode).padStart(2,'0')}`:'—'}</span>
          <div><strong>{row.title}</strong><small>{duration(row.file.durationSeconds)} · {bytes(row.file.sizeBytes)} · {row.file.libraryName}</small><a className="content-source" href={appUrl(`/media/${row.file.id}`)}>Dosya bilgileri</a></div>
          <span className={`episode-state ${row.file.available?'available':'missing'}`}>{row.file.isWatched?'İzlendi':row.file.available?'Kütüphanede':'Dosya erişilemiyor'}</span>
          <Button size="sm" variant="ghost" isDisabled={!row.file.available} onPress={()=>onPlay(row.file)}><Play size={15}/>{row.file.positionSeconds>0&&!row.file.isWatched?'Devam et':'Oynat'}</Button>
        </div>)}
        {query.hasNextPage&&<div className="load-more"><Button variant="ghost" onPress={()=>query.fetchNextPage()} isDisabled={query.isFetchingNextPage}>Diğer bölümleri göster</Button></div>}
        {query.error&&<p className="form-error" role="alert">{query.error.message}</p>}
      </section>{item.content&&<><ContentPeople metadata={item.content} expanded/><ContentMedia key={item.content.id} metadata={item.content}/></>}</div></>}
  </article>;
}
