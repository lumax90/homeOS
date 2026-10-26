import { useEffect, useState } from 'react';
import { Button } from '@heroui/react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ArrowLeft, CircleAlert, ExternalLink, HardDrive, Layers3, Play, Sparkles, Unlink } from 'lucide-react';
import { bytes, date, duration, mutate, request, resolution, type MediaItem } from '../api';
import { Artwork } from './Artwork';
import { MetadataMatchPanel } from './Metadata';
import { useSession } from './SessionContext';
import { MediaAccessEditor, PersonalContentPanel } from './PersonalCatalog';
import type { ProviderContent } from '../api';
import { LocalMetadataDetails } from './LibraryManager';
import { CatalogIdentityPanel, CatalogSourceLinks, EpisodeFileEditor, EpisodeNumberingPanel, MoviePartsPanel } from './CatalogControls';
import type { ContentEntry } from '../api';
import { SubtitleMediaPanel, SubtitleSyncPanel } from './SubtitleTools';
import { SegmentEditor } from './PlaybackSegments';
import { ReceiverPanel } from './Receivers';
import { OfflineDownloadAction } from './Offline';
import {ContentPeople}from'./CatalogFeeds';
import {ContentMedia}from'./ContentMedia';
import './title-page.css';
import { usePageTitle } from '../usePageTitle';

export function MediaDetails({ selection = null, mediaId, onClose, onPlay, onSettings, onOffline }: {
  selection?: { item: MediaItem; origin: string } | null; mediaId?: string;
  onClose: () => void; onPlay: (item: MediaItem, episodeId?: string, selectedPart?: boolean) => void; onSettings: () => void; onOffline: () => void;
}) {
  const account = useSession();
  const client = useQueryClient();
  const [matching, setMatching] = useState(false);
  const id = mediaId ?? selection?.item.id;
  useEffect(() => setMatching(false), [id]);
  const live = useQuery({ queryKey: ['media', id], queryFn: ({ signal }) => request<MediaItem>(`/media/${id}`, { signal }), enabled: !!id });
  const item = live.data ?? selection?.item ?? null;
  usePageTitle(item?.title);
  const content = item?.content;
  const entry = useQuery({ queryKey: ['content-entry', content?.id], queryFn: () => request<ContentEntry>(`/content/${content!.id}`), enabled: !!content });
  const contentFiles = useQuery({ queryKey: ['content-files', content?.id], queryFn: () => request<MediaItem[]>(`/content/${content!.id}/files`), enabled: content?.kind === 'movie' && account.role === 'admin' });
  const versions = useQuery({ queryKey: ['versions', item?.id], queryFn: () => request<MediaItem[]>(`/media/${item!.id}/versions`), enabled: !!item && !!content });
  async function updated(media: MediaItem) {
    client.setQueryData(['media', media.id], media);
    await client.invalidateQueries({ predicate: query => ['catalog', 'versions', 'continue'].includes(String(query.queryKey[0])) });
    setMatching(false);
  }
  const remove = useMutation({ mutationFn: () => mutate<MediaItem>(`/media/${item!.id}/match`, undefined, 'DELETE'), onSuccess: updated });
  return <article className="title-page"><Button className="title-back" variant="ghost" onPress={onClose}><ArrowLeft size={18} /> Geri</Button>
    {!item && (live.error ? <div className="search-empty" role="alert"><CircleAlert size={28} /><h1>İçerik açılamadı.</h1><p>{live.error.message}</p><Button onPress={() => live.refetch()}>Yeniden dene</Button></div> : <div className="title-page-skeleton home-skeleton" role="status" aria-label="İçerik bilgileri" />)}
    {item && (matching ? <div className="title-page-body"><MetadataMatchPanel key={item.id} item={item} onDone={updated} onBack={() => setMatching(false)} onSettings={onSettings} /></div> : <>
      <div className="title-hero"><div className="title-backdrop"><Artwork item={item} eager mode="backdrop" /></div><div className="title-shade" /><div className="title-identity"><div className="title-poster"><Artwork item={item} eager /></div><section><span className="eyebrow">{item.isDemo ? 'ÖRNEK DOSYA · ' : ''}{content ? `${content.kind === 'movie' ? 'FİLM' : 'DİZİ'} · ${content.releaseDate?.slice(0, 4) || ''}` : item.libraryName.toLocaleUpperCase('tr')}</span><h1 data-page-heading tabIndex={-1}>{item.title}</h1><div className="title-facts"><span>{duration(item.durationSeconds)}</span><span>{resolution(item)}</span></div><div className="title-main-actions"><Button className="play-button" onPress={() => onPlay(item)} isDisabled={!item.available}><Play size={18} fill="currentColor" />{item.positionSeconds > 0 && !item.isWatched ? 'Kaldığın yerden devam et' : 'Oynat'}</Button></div>
        {content && <PersonalContentPanel placement="hero" key={content.id} metadata={entry.data?.metadata ?? content as ProviderContent} contentId={content.id} />}
      </section></div></div>
      <div className="detail-body"><div className="detail-meta"><span className="quality-badge">{resolution(item)}</span><span>{duration(item.durationSeconds)}</span><span>{bytes(item.sizeBytes)}</span><span className={item.available ? 'available-tag' : 'unavailable-tag'}>{item.available ? 'Dosya erişilebilir' : 'Dosya erişilemiyor'}</span></div>
        {content?.episodeTitle && <h3 className="episode-title">{content.episodeTitle}</h3>}
        {content?.genres.length ? <div className="genre-list">{content.genres.slice(0, 4).map(genre => <span key={genre}>{genre}</span>)}</div> : null}
        <p className="detail-description">{content ? content.episodeOverview || content.overview || 'Sağlayıcıda açıklama bulunmuyor.' : 'Kendi kütüphanendeki bu içerik sunucundan oynatılır. Ses, altyazı ve medya bilgileri dosyanın kendisinden okunur.'}</p>
        {content && <a className="content-source" href={content.providerUrl} target="_blank" rel="noreferrer">Bilgiler: {content.provider === 'tvmaze' ? 'TVmaze · CC BY-SA' : 'TMDB'} <ExternalLink size={12} /></a>}
        {content&&<><ContentPeople key={content.id+'-people'} metadata={content} expanded/><ContentMedia key={content.id+'-media'} metadata={content}/></>}
        {entry.data && <CatalogSourceLinks metadata={entry.data.metadata} />}
        {item.isDemo && content && <p className="demo-match-note">Bu oynatılabilir dosya bir örnektir; eşleştirilen yapımın tam videosu değildir.</p>}
        <div className="detail-actions"><Button className="play-button detail-play" onPress={() => onPlay(item)} isDisabled={!item.available}><Play size={17} fill="currentColor" />{item.positionSeconds > 0 && !item.isWatched ? 'Kaldığın yerden devam et' : 'Oynat'}</Button>{account.role === 'admin' && item.kind === 'video' && <Button className="metadata-match-button" variant="ghost" onPress={() => setMatching(true)}><Sparkles size={16} />{content ? 'Eşleştirmeyi değiştir' : 'Bilgileri eşleştir'}</Button>}</div>
        <OfflineDownloadAction key={item.id + '-offline'} item={item} onOpen={onOffline} />
        {account.role === 'admin' && <MediaAccessEditor key={item.id} mediaId={item.id} />}
        {item.moviePart && <p className="field-hint">{item.moviePart.name} · {item.moviePart.number}/{item.moviePart.count}. parça</p>}
        {item.episodes?.length > 1 && <p className="field-hint">Bu dosya: {item.episodes.map(episode => `S${episode.season} E${episode.number}`).join(' · ')}</p>}
        {account.role === 'admin' && <EpisodeFileEditor item={item} />}
        {account.role === 'admin' && item.kind === 'video' && <><SubtitleMediaPanel key={item.id + '-subtitles'} mediaId={item.id} /><SubtitleSyncPanel key={item.id + '-sync'} mediaId={item.id} /></>}
        {account.role==='admin'&&item.kind==='video'&&<SegmentEditor item={item}/>}
        <ReceiverPanel item={item} position={item.positionSeconds}/>
        {entry.data && account.role === 'admin' && <><CatalogIdentityPanel content={entry.data} />{entry.data.metadata.kind === 'show' ? <EpisodeNumberingPanel content={entry.data} /> : <MoviePartsPanel content={entry.data} files={contentFiles.data ?? []} />}</>}
        <LocalMetadataDetails key={item.id + '-local'} mediaId={item.id} />
        {content && versions.data && <section className="version-section"><h3><Layers3 size={15} />{versions.data.length} dosya sürümü</h3>{versions.data.map(version => <div className={`version-row ${version.id === item.id ? 'current' : ''}`} key={version.id}><div><strong>{version.content?.edition || resolution(version)}{version.moviePart ? ` · ${version.moviePart.number}/${version.moviePart.count}. parça` : ''}</strong><span>{version.libraryName} · {bytes(version.sizeBytes)} · {version.videoCodec || version.audioCodec || 'codec bilinmiyor'}</span></div>{version.id === item.id ? <span className="version-current">Seçili</span> : <Button size="sm" variant="ghost" onPress={() => onPlay(version, undefined, !!version.moviePart)} isDisabled={!version.available}><Play size={14} /> Oynat</Button>}</div>)}</section>}
        <div className="detail-divider" /><h3>Dosya bilgileri</h3><dl className="file-info"><div><dt>Kütüphane</dt><dd>{item.libraryName}</dd></div><div><dt>Görüntü</dt><dd>{item.width && item.height ? `${item.width} × ${item.height}` : '—'}</dd></div><div><dt>Video / Ses</dt><dd>{[item.videoCodec, item.audioCodec].filter(Boolean).join(' / ') || '—'}</dd></div><div><dt>Eklenme</dt><dd>{date(item.addedAt)}</dd></div></dl><div className="file-path"><HardDrive size={15} /><span>{item.relativePath}</span></div>
        {content ? <div className="match-footer"><span>{content.provider.toUpperCase()} #{content.providerId} · {date(content.matchedAt)}</span>{account.role === 'admin' && <Button variant="ghost" size="sm" aria-label="Eşleştirmeyi kaldır" onPress={() => remove.mutate()} isDisabled={remove.isPending}><Unlink size={12} /> Kaldır</Button>}</div> : <p className="detail-source">Başlık dosya adından oluşturuldu.</p>}
        {remove.error && <p className="form-error" role="alert">{remove.error.message}</p>}
      </div>
    </>)}
  </article>;
}
