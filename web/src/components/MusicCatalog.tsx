import { Fragment, useState } from 'react';
import { Button } from '@heroui/react';
import { useInfiniteQuery, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ArrowLeft, Disc3, ExternalLink, Link2, Music2, Play, Search, Video, X } from 'lucide-react';
import { clock, mutate, request, type CatalogPage, type LocalMusicArtist, type MediaItem, type MusicAlbum, type MusicArtistCredit, type MusicPage, type MusicRecording, type MusicRelation, type MusicVideoLink, type ProviderAlbum } from '../api';
import { useSession } from './SessionContext';

export function MusicCreditLinks({ credits, fallback }: { credits?: MusicArtistCredit[]; fallback: string }) {
  if (!credits?.length) return <>{fallback}</>;
  return <span className="music-credit-links">{credits.map((credit, index) => <Fragment key={`${credit.mbid ?? credit.name}:${index}`}>{credit.mbid ? <a href={`https://musicbrainz.org/artist/${credit.mbid}`} target="_blank" rel="noreferrer" title={credit.name}>{credit.creditedName}</a> : <span>{credit.creditedName}</span>}{credit.joinPhrase}</Fragment>)}</span>;
}
export function LibraryArtists({ onAlbum }: { onAlbum: (album: MusicAlbum) => void }) {
  const [query, setQuery] = useState(''); const [search, setSearch] = useState(''); const [selected, setSelected] = useState<LocalMusicArtist | null>(null);
  const artists = useInfiniteQuery({ queryKey: ['music-library-artists', search], initialPageParam: 0,
    queryFn: ({ pageParam, signal }) => request<MusicPage<LocalMusicArtist>>(`/music/library/artists?offset=${pageParam}&q=${encodeURIComponent(search)}`, { signal }), getNextPageParam: page => page.offset + page.items.length < page.total ? page.offset + page.items.length : undefined });
  const albums = useInfiniteQuery({ queryKey: ['music-library-artist-albums', selected?.artist.id], initialPageParam: 0, enabled: !!selected,
    queryFn: ({ pageParam, signal }) => request<MusicPage<MusicAlbum>>(`/music/library/artists/${selected!.artist.id}/albums?offset=${pageParam}`, { signal }), getNextPageParam: page => page.offset + page.items.length < page.total ? page.offset + page.items.length : undefined });
  return <>{selected ? <><Button variant="ghost" onPress={() => setSelected(null)}><ArrowLeft size={15} /> Sanatçılarım</Button><h2>{selected.artist.name}</h2><p className="field-hint">Albüm ve parça katkıları · {selected.artist.source === 'musicbrainz' ? 'Yayın eşlemesi' : 'Dosya etiketleri'}</p>{selected.artist.mbid && <a className="provider-attribution" href={`https://musicbrainz.org/artist/${selected.artist.mbid}`} target="_blank" rel="noreferrer">MusicBrainz sanatçı kimliği <ExternalLink size={12} /></a>}
    <div className="provider-albums">{albums.data?.pages.flatMap(page => page.items).map(album => <article key={album.id}><Disc3 size={25} /><div><button className="tracking-title" onClick={() => onAlbum(album)}>{album.title}</button><span>{album.artist} · {album.tracks} parça</span></div></article>)}</div>{albums.hasNextPage && <Button variant="ghost" onPress={() => albums.fetchNextPage()} isDisabled={albums.isFetching}>Diğer albümler</Button>}</> : <>
    <form className="search-box music-search" onSubmit={event => { event.preventDefault(); setSearch(query.trim()); }}><Search size={18} /><input value={query} maxLength={120} onChange={event => setQuery(event.target.value)} aria-label="Kütüphane sanatçısı ara" placeholder="Kütüphanendeki sanatçı ve konuklar…" /><Button type="submit" variant="ghost" size="sm">Ara</Button></form>
    <div className="artist-grid">{artists.data?.pages.flatMap(page => page.items).map(row => <button key={row.artist.id} onClick={() => setSelected(row)}><Music2 size={24} /><strong>{row.artist.name}</strong><span>{row.albums} albüm · {row.tracks} katkı</span></button>)}</div>{artists.hasNextPage && <Button variant="ghost" onPress={() => artists.fetchNextPage()} isDisabled={artists.isFetching}>Diğer sanatçılar</Button>}{artists.data?.pages[0]?.total === 0 && <p className="field-hint">Erişilebilir müzik dosyalarının sanatçı etiketleri burada görünür. Mevcut kütüphanenin katkıları arka planda yeniden okunur.</p>}</>}
    {(artists.error || albums.error) && <p className="form-error">{artists.error?.message || albums.error?.message}</p>}
  </>;
}
export function MusicRelations({ groupId, onAlbum }: { groupId: string; onAlbum: (album: ProviderAlbum) => void }) {
  const [open, setOpen] = useState(false); const session = useSession();
  const query = useQuery({ queryKey: ['music-relations', groupId], queryFn: ({ signal }) => request<MusicRelation[]>(`/music/discovery/albums/${groupId}/relations`, { signal }), enabled: open && session.canDiscover });
  if (!session.canDiscover) return null;
  return <section className="music-relations"><Button variant="ghost" onPress={() => setOpen(value => !value)} aria-expanded={open}><Link2 size={15} /> İlişkili albümler ve kayıtlar</Button>{open && <><RelationItems items={query.data ?? []} onAlbum={onAlbum} />{query.data?.length === 0 && <p className="field-hint">MusicBrainz bu albüm için ilişkili kayıt bildirmiyor.</p>}{query.error && <p className="form-error">{query.error.message}</p>}</>}</section>;
}
function RelationItems({ items, onAlbum }: { items: MusicRelation[]; onAlbum: (album: ProviderAlbum) => void }) {
  const [visible, setVisible] = useState(30); const open = useMutation({ mutationFn: (id: string) => request<ProviderAlbum>(`/music/discovery/albums/${id}`), onSuccess: onAlbum });
  return <>{items.slice(0, visible).map(item => <div className="session-row" key={`${item.type}:${item.direction}:${item.id}`}><div>{item.targetType === 'release-group' ? <button className="tracking-title" onClick={() => open.mutate(item.id)} disabled={open.isPending}>{item.title}</button> : <a href={item.providerUrl} target="_blank" rel="noreferrer">{item.title} <ExternalLink size={12} /></a>}<span>{item.type} · {item.direction === 'backward' ? 'Bu kayda bağlı' : 'Bu kayıttan bağlantı'}{item.video ? ' · Video' : ''}</span></div></div>)}{items.length > visible && <Button variant="ghost" onPress={() => setVisible(value => value + 30)}>Diğer ilişkiler</Button>}{open.error && <p className="form-error">{open.error.message}</p>}</>;
}
export function MusicTrackLinks({ mediaId, onAlbum, onPlay }: { mediaId: string; onAlbum: (album: ProviderAlbum) => void; onPlay: (media: MediaItem) => void }) {
  const session = useSession(); const client = useQueryClient(); const [matching, setMatching] = useState(false); const [query, setQuery] = useState(''); const [search, setSearch] = useState(''); const [videoId, setVideoId] = useState(''); const [recordingId, setRecordingId] = useState('');
  const local = useQuery({ queryKey: ['music-videos', mediaId], queryFn: ({ signal }) => request<{ recordingMbid: string | null; items: MusicVideoLink[] }>(`/music/media/${mediaId}/videos`, { signal }) });
  const recording = useQuery({ queryKey: ['music-recording', local.data?.recordingMbid], enabled: !!local.data?.recordingMbid && session.canDiscover,
    queryFn: ({ signal }) => request<MusicRecording>(`/music/recordings/${local.data!.recordingMbid}`, { signal }) });
  const videos = useInfiniteQuery({ queryKey: ['music-video-search', search], enabled: matching, initialPageParam: 0,
    queryFn: ({ pageParam, signal }) => request<CatalogPage>(`/catalog?kind=video&q=${encodeURIComponent(search)}&offset=${pageParam}&limit=50`, { signal }), getNextPageParam: page => page.offset + page.items.length < page.total ? page.offset + page.items.length : undefined });
  const refresh = () => client.invalidateQueries({ queryKey: ['music-videos', mediaId] });
  const link = useMutation({ mutationFn: () => mutate(`/music/media/${mediaId}/videos`, { videoId, expectedRecordingMbid: local.data?.recordingMbid ?? null, videoRecordingMbid: recordingId.trim() || null }), onSuccess: () => { void refresh(); setMatching(false); } });
  const remove = useMutation({ mutationFn: (link: MusicVideoLink) => mutate(`/music/video-links/${link.id}?revision=${link.revision}`, undefined, 'DELETE'), onSuccess: refresh });
  return <section className="music-track-links"><h3>Müzik videosu ve ilişkili kayıtlar</h3>{local.data?.items.map(link => <div className="session-row" key={link.id}><Video size={19} /><div><strong>{link.video.title}</strong><span>{clock(link.video.durationSeconds ?? 0)} · {!link.current ? 'Önceki parça kimliğine bağlı · Yeniden eşleştir veya kaldır' : link.video.available ? 'Kütüphanende' : 'Dosya çevrimdışı'}</span></div><Button variant="ghost" size="sm" onPress={() => onPlay(link.video)} isDisabled={!link.video.available || !link.current}><Play size={15} /> İzle</Button>{session.role === 'admin' && <Button isIconOnly variant="ghost" size="sm" aria-label={`${link.video.title} bağlantısını kaldır`} onPress={() => remove.mutate(link)} isDisabled={remove.isPending}><X size={14} /></Button>}</div>)}
    {recording.data && <><MusicCreditLinks credits={recording.data.artistCredits} fallback="" /><RelationItems items={recording.data.relations} onAlbum={onAlbum} /></>}
    {local.data?.items.length === 0 && <p className="field-hint">Bu parçaya bağlı yerel müzik videosu yok.</p>}
    {session.role === 'admin' && <Button variant="ghost" size="sm" onPress={() => setMatching(value => !value)}><Link2 size={14} /> Kütüphaneden video bağla</Button>}
    {matching && <div className="home-form"><div className="search-box"><input aria-label="Yerel müzik videosu ara" value={query} maxLength={120} onChange={event => setQuery(event.target.value)} placeholder="Video başlığı" /><Button variant="ghost" onPress={() => { setSearch(query.trim()); setVideoId(''); }}>Ara</Button></div>
      <label className="field"><span>Yerel video</span><select value={videoId} onChange={event => setVideoId(event.target.value)}><option value="">Dosya seç</option>{videos.data?.pages.flatMap(page => page.items).map(video => <option key={video.id} value={video.id}>{video.title} · {video.relativePath}</option>)}</select></label>{videos.hasNextPage && <Button variant="ghost" onPress={() => videos.fetchNextPage()} isDisabled={videos.isFetching}>Diğer videolar</Button>}
      <label className="field"><span>Video recording MBID · isteğe bağlı</span><input value={recordingId} maxLength={36} onChange={event => setRecordingId(event.target.value)} /></label><p className="field-hint">Video ayrı bir kayıttır; bağlantı ses kaydının süresini veya geçmişini birleştirmez.</p>
      <Button className="secondary-button" onPress={() => link.mutate()} isDisabled={!videoId || !local.data || link.isPending}>Bu videoyu parçaya bağla</Button></div>}
    {(local.error || recording.error || videos.error || link.error || remove.error) && <p className="form-error">{local.error?.message || recording.error?.message || videos.error?.message || link.error?.message || remove.error?.message}</p>}
  </section>;
}
