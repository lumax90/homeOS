import { useEffect, useRef, useState } from 'react';
import { Button, Input, Label, Modal, TextField } from '@heroui/react';
import { useInfiniteQuery, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Check, Disc3, Heart, Link2, Search, X } from 'lucide-react';
import { appUrl, clock, mutate, request, type MusicAlbum, type MusicAssignment, type MusicMatchPreview, type MusicPage, type MusicRelease, type MusicReleaseSummary, type ProviderAlbum } from '../api';
import { Dialog } from './Dialogs';
import { MusicAcquireDialog } from './MusicAcquisition';
import { useSession } from './SessionContext';
import { MusicMonitorPanel } from './MusicAutomation';
import { MusicCreditLinks, MusicRelations } from './MusicCatalog';

export function MusicCover({ url }: { url: string }) {
  const [failed, setFailed] = useState(false); const [revision, setRevision] = useState(0);
  const failedRef=useRef(failed);failedRef.current=failed;
  useEffect(() => { setFailed(false); }, [url]);
  useEffect(() => { const retry=()=>{if(failedRef.current){setFailed(false);setRevision(value=>value+1);}};window.addEventListener('home-music-artwork-changed',retry);return()=>window.removeEventListener('home-music-artwork-changed',retry); }, []);
  return failed ? <Disc3 size={38} strokeWidth={1} /> : <img src={url+(revision?(url.includes('?')?'&':'?')+'art='+revision:'')} alt="" loading="lazy" onError={() => setFailed(true)} />;
}
export function ProviderAlbumDialog({ album, onClose }: { album: ProviderAlbum | null; onClose: () => void }) {
  const [current, setCurrent] = useState(album); useEffect(() => setCurrent(album), [album]);
  return <Dialog open={!!album} onClose={onClose} title={current?.title ?? 'Albüm yayınları'} className="detail-container"><div className="dialog-content"><p className="eyebrow"><MusicCreditLinks credits={current?.artistCredits} fallback={current?.artist ?? ''} /></p><Modal.Heading className="dialog-title">{current?.title}</Modal.Heading>{current && <AlbumReleases key={current.id} album={current} onNavigate={setCurrent} />}</div></Dialog>;
}
function AlbumReleases({ album, onNavigate }: { album: ProviderAlbum; onNavigate: (album: ProviderAlbum) => void }) {
  const session = useSession(); const [acquire, setAcquire] = useState<MusicReleaseSummary | null>(null);
  const client = useQueryClient(); const [selected, setSelected] = useState('');
  const releases = useInfiniteQuery({ queryKey: ['music-releases', album.id], initialPageParam: 0,
    queryFn: ({ pageParam }) => request<MusicPage<MusicReleaseSummary>>(`/music/discovery/albums/${album.id}/releases?offset=${pageParam}`), getNextPageParam: page => page.offset + page.items.length < page.total ? page.offset + page.items.length : undefined });
  const release = useQuery({ queryKey: ['music-release', selected], queryFn: () => request<MusicRelease>(`/music/releases/${selected}`), enabled: !!selected });
  const follow = useMutation({ mutationFn: () => mutate(`/music/discovery/albums/${album.id}/follow`), onSuccess: () => client.invalidateQueries({ queryKey: ['album-follows'] }) });
  return <><div className="provider-album-heading"><div className="provider-cover"><MusicCover url={appUrl(`/api/music/artwork/release-group/${album.id}`)} /></div><div><p className="field-hint">{album.date} · {album.type}</p><Button className="secondary-button" onPress={() => follow.mutate()} isDisabled={follow.isPending}><Heart size={15} /> Albüm listeme ekle</Button></div></div>
    <label className="field"><span>Yayın / baskı</span><select value={selected} onChange={event => setSelected(event.target.value)}><option value="">Bir yayın seç</option>{releases.data?.pages.flatMap(page => page.items).map(value => <option value={value.id} key={value.id}>{value.date || 'Tarih yok'} · {value.country || 'Ülke yok'} · {value.formats.join('/')} · {value.trackCount} parça · {value.status}</option>)}</select></label>
    {releases.hasNextPage && <Button variant="ghost" onPress={() => releases.fetchNextPage()} isDisabled={releases.isFetching}>Diğer yayınlar</Button>}
    {release.data && session.canAcquire && <Button className="secondary-button" onPress={() => setAcquire(release.data!.metadata)}>Bu yayını kaynaklarda ara</Button>}
    {release.data?.tracks.map(track => <div className="session-row" key={track.id}><span>{track.disc}.{track.number}</span><div><strong>{track.title}</strong><span><MusicCreditLinks credits={track.artistCredits} fallback={track.artist} /></span></div><small>{clock((track.lengthMs ?? 0) / 1000)}</small></div>)}
    <MusicRelations groupId={album.id} onAlbum={onNavigate} />
    {(releases.error || release.error || follow.error) && <p className="form-error">{releases.error?.message || release.error?.message || follow.error?.message}</p>}
    <p className="provider-attribution">Yayın/recording bilgisi MusicBrainz; görseller Cover Art Archive. Kapakların hakları kendi sahiplerine aittir.</p>
    <MusicAcquireDialog release={acquire} onClose={() => setAcquire(null)} />
    {session.canDiscover && <MusicMonitorPanel kind="album" mbid={album.id} name={album.title} />}
  </>;
}
export function MusicAlbumMatcher({ album, onDone }: { album: MusicAlbum; onDone: () => void }) {
  const client = useQueryClient(); const [query, setQuery] = useState(album.title); const [artist, setArtist] = useState(album.artist); const [search, setSearch] = useState(false);
  const [group, setGroup] = useState(album.releaseGroupMbid ?? ''); const [releaseId, setReleaseId] = useState(''); const [assignments, setAssignments] = useState<MusicAssignment[]>([]);
  const groups = useQuery({ queryKey: ['music-album-search', query, artist], queryFn: () => request<MusicPage<ProviderAlbum>>(`/music/discovery/albums?q=${encodeURIComponent(query)}&artist=${encodeURIComponent(artist)}`), enabled: search });
  const releases = useInfiniteQuery({ queryKey: ['music-releases', group], initialPageParam: 0, enabled: !!group,
    queryFn: ({ pageParam }) => request<MusicPage<MusicReleaseSummary>>(`/music/discovery/albums/${group}/releases?offset=${pageParam}`), getNextPageParam: page => page.offset + page.items.length < page.total ? page.offset + page.items.length : undefined });
  const preview = useMutation({ mutationFn: () => mutate<MusicMatchPreview>(`/music/albums/${album.id}/match-preview`, { releaseId, assignments: assignments.length ? assignments : null }) });
  const save = useMutation({ mutationFn: () => mutate(`/music/albums/${album.id}/match`, { releaseId, assignments: assignments.length ? assignments : null }, 'PUT'), onSuccess: async () => {
    await Promise.all(['music-albums', 'music-tracks', 'catalog', 'music-matched-release'].map(key => client.invalidateQueries({ queryKey: [key] }))); onDone();
  } });
  return <div className="home-form source-form"><div className="settings-heading"><h3>Albüm yayınıyla eşleştir</h3><Button isIconOnly variant="ghost" aria-label="Albüm eşleştirmesini kapat" onPress={onDone}><X size={15} /></Button></div>
    <div className="form-columns"><TextField className="field"><Label>Albüm</Label><Input value={query} onChange={event => { setSearch(false); setQuery(event.target.value); }} /></TextField><TextField className="field"><Label>Sanatçı</Label><Input value={artist} onChange={event => { setSearch(false); setArtist(event.target.value); }} /></TextField></div><Button variant="ghost" onPress={() => setSearch(true)}><Search size={15} /> MusicBrainz'de ara</Button>
    {groups.data && <label className="field"><span>Release-group</span><select value={group} onChange={event => { setGroup(event.target.value); setReleaseId(''); setAssignments([]); preview.reset(); }}><option value="">Albümü seç</option>{groups.data.items.map(item => <option key={item.id} value={item.id}>{item.artist} · {item.title} · {item.date}</option>)}</select></label>}
    {!!group && <label className="field"><span>Yayın</span><select value={releaseId} onChange={event => { setReleaseId(event.target.value); setAssignments([]); preview.reset(); }}><option value="">Baskıyı seç</option>{releases.data?.pages.flatMap(page => page.items).map(item => <option value={item.id} key={item.id}>{item.date} · {item.country} · {item.formats.join('/')} · {item.trackCount} parça</option>)}</select></label>}{releases.hasNextPage && <Button variant="ghost" onPress={() => releases.fetchNextPage()} isDisabled={releases.isFetching}>Diğer baskılar</Button>}
    <Button className="secondary-button" onPress={() => preview.mutate()} isDisabled={!releaseId || preview.isPending}><Link2 size={15} /> Eşleşmeleri denetle</Button>
    {preview.data?.files.map(file => { const manual = assignments.find(value => value.mediaId === file.mediaId); return <div className="music-match-row" key={file.mediaId}><strong>{file.fileTitle}</strong><select aria-label={`${file.fileTitle} yayın parçası`} value={`${manual?.disc ?? file.disc}:${manual?.position ?? file.position}`} onChange={event => { const [disc, position] = event.target.value.split(':').map(Number); setAssignments(values => [...values.filter(value => value.mediaId !== file.mediaId), { mediaId: file.mediaId, disc, position }]); }}><option value="0:0">Parça seç</option>{preview.data!.release.tracks.map(track => <option value={`${track.disc}:${track.position}`} key={track.id}>{track.disc}.{track.number} · {track.title}</option>)}</select>{file.reasons.map(reason => <small key={reason}>{reason}</small>)}</div>; })}
    {preview.data && <><p className="field-hint">{preview.data.unavailableTracks} yayın parçası için yerel dosya yok. Kaynak dosya etiketleri korunur; eşleşme Home'da saklanır.</p><Button className="primary-button" onPress={() => save.mutate()} isDisabled={save.isPending || !preview.data.canApply && !assignments.length}><Check size={15} /> Yayın kimliklerini kaydet</Button></>}
    {(groups.error || releases.error || preview.error || save.error) && <p className="form-error">{groups.error?.message || releases.error?.message || preview.error?.message || save.error?.message}</p>}
  </div>;
}
export function FollowedAlbums({ onOpen }: { onOpen: (album: ProviderAlbum) => void }) {
  const client = useQueryClient(); const rows = useQuery({ queryKey: ['album-follows'], queryFn: () => request<{ groupId: string; metadata: ProviderAlbum; createdAt: string }[]>('/music/album-follows') });
  const remove = useMutation({ mutationFn: (id: string) => mutate(`/music/discovery/albums/${id}/follow`, undefined, 'DELETE'), onSuccess: () => client.invalidateQueries({ queryKey: ['album-follows'] }) });
  return <>{rows.data?.map(item => <div className="session-row" key={item.groupId}><Disc3 size={23} /><div><button className="tracking-title" onClick={() => onOpen(item.metadata)}>{item.metadata.title}</button><span>{item.metadata.artist}</span></div><Button variant="ghost" size="sm" onPress={() => remove.mutate(item.groupId)} isDisabled={remove.isPending}>Kaldır</Button></div>)}{!rows.data?.length && <p className="field-hint">Sanatçı keşfinde bir albümü açarak bu listeye ekleyebilirsin.</p>}{(rows.error || remove.error) && <p className="form-error">{rows.error?.message || remove.error?.message}</p>}</>;
}
