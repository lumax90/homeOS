import { useState } from 'react';
import { Button } from '@heroui/react';
import { useInfiniteQuery, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ArrowLeft, ArrowRight, CalendarDays, Check, ChevronRight, CircleAlert, ExternalLink, Film, Heart, LoaderCircle, Play, Plus, Search, Sparkles, Star, Tv, X } from 'lucide-react';
import { date, mutate, request, type ContentEntry, type EpisodePage, type MediaItem, type ProviderContent, type QualityProfile, type ReleaseTarget } from '../api';
import { useSession } from './SessionContext';
import { RequestInbox } from './Requests';
import { AutomationHistory, AutomationPanel } from './Automation';
import { CollectionsView, PersonalContentPanel } from './PersonalCatalog';
import { CatalogIdentityPanel, CatalogSourceLinks, EpisodeNumberingPanel, MoviePartsPanel } from './CatalogControls';
import { ContentPeople,PeopleBrowser } from './CatalogFeeds';
import {ContentMedia,TrailerAction}from'./ContentMedia';
import { appUrl } from '../base';
import './title-page.css';
import { usePageTitle } from '../usePageTitle';

const episodeLabels: Record<string, string> = { available: 'Kütüphanede', missing: 'Eksik', upcoming: 'Yakında', unmonitored: 'Takip dışı', unknown: 'Tarih bekleniyor' };
const nextEpisodePage = (page: EpisodePage) => page.offset + page.items.length < page.total ? page.offset + page.limit : undefined;

export { DiscoveryView } from './DiscoveryBrowse';

type ContentActions = { onSearch: (target: ReleaseTarget) => void; onPlay: (item: MediaItem, episodeId?: string, selectedPart?: boolean) => void; notify: (message: string) => void };
export function ContentPage({ provider, kind, id, onBack, ...actions }: ContentActions & { provider: string; kind: 'movie' | 'show'; id: number; onBack: () => void }) {
  const details = useQuery({ queryKey: ['content-details', provider, kind, id], queryFn: ({ signal }) => request<{ metadata: ProviderContent; saved: ContentEntry | null }>(`/discovery/${provider}/${kind}/${id}`, { signal }) });
  usePageTitle(details.data?.saved?.metadata.title ?? details.data?.metadata.title);
  return <article className="title-page"><Button className="title-back" variant="ghost" onPress={onBack}><ArrowLeft size={18} /> Geri</Button>
    {details.data ? <ContentBody key={`${provider}-${kind}-${id}`} selected={details.data.metadata} {...actions} />
      : details.error ? <div className="search-empty" role="alert"><CircleAlert size={28} /><h1>İçerik açılamadı.</h1><p>{details.error.message}</p><Button onPress={() => details.refetch()}>Yeniden dene</Button></div>
      : <div className="title-page-skeleton home-skeleton" aria-label="İçerik bilgileri" role="status" />}
  </article>;
}

function ContentBody({ selected, onSearch, onPlay, notify }: ContentActions & { selected: ProviderContent }) {
  const account = useSession();
  const client = useQueryClient();
  const key = ['content-details', selected.provider, selected.kind, selected.providerId];
  const details = useQuery({ queryKey: key, queryFn: ({ signal }) => request<{ metadata: ProviderContent; saved: ContentEntry | null }>(`/discovery/${selected.provider}/${selected.kind}/${selected.providerId}`, { signal }) });
  const profiles = useQuery({ queryKey: ['quality-profiles'], queryFn: () => request<QualityProfile[]>('/quality-profiles') });
  const metadata = details.data?.saved?.metadata ?? details.data?.metadata ?? selected;
  const saved = details.data?.saved;
  const [profile, setProfile] = useState('');
  const [mode, setMode] = useState('');
  const [season, setSeason] = useState('');
  const [scope, setScope] = useState('all');
  const [requestSeason, setRequestSeason] = useState('1');
  const [requestEpisode, setRequestEpisode] = useState('1');
  const effectiveProfile = profile || saved?.qualityProfileId || 'balanced';
  const effectiveMode = mode || saved?.monitorMode || 'all';
  const episodes = useInfiniteQuery({ queryKey: ['episodes', saved?.id, season], initialPageParam: 0,
    queryFn: ({ pageParam, signal }) => request<EpisodePage>(`/content/${saved!.id}/episodes?offset=${pageParam}${season ? `&season=${season}` : ''}`, { signal }),
    getNextPageParam: nextEpisodePage, enabled: !!saved && metadata.kind === 'show',
    refetchInterval: saved?.syncState === 'syncing' || saved?.syncState === 'pending' ? 5000 : false,
  });
  const files = useQuery({ queryKey: ['content-files', saved?.id], queryFn: () => request<MediaItem[]>(`/content/${saved!.id}/files`), enabled: !!saved });
  async function updated(value: ContentEntry, message: string) {
    client.setQueryData(key, { metadata, saved: value });
    await client.invalidateQueries({ queryKey: ['content'] });
    notify(message);
  }
  const add = useMutation({ mutationFn: () => mutate<ContentEntry>('/content/requests', { provider: selected.provider, kind: selected.kind, providerId: selected.providerId, monitorMode: effectiveMode, qualityProfileId: effectiveProfile,
    season: selected.kind === 'show' && scope !== 'all' ? Number(requestSeason) : null, episode: selected.kind === 'show' && scope === 'episode' ? Number(requestEpisode) : null }),
    onSuccess: value => { client.invalidateQueries({ queryKey: ['requests'] }); return updated(value, value.requestStatus === 'pending' ? 'İsteğin yönetici onayına gönderildi.' : 'İçerik takip listesine eklendi.'); } });
  const change = useMutation({ mutationFn: () => mutate<ContentEntry>(`/content/${saved!.id}/tracking`, { monitorMode: effectiveMode, qualityProfileId: effectiveProfile }, 'PUT'), onSuccess: value => updated(value, 'Takip tercihi kaydedildi.') });
  const cancel = useMutation({ mutationFn: () => mutate(`/content/${saved!.id}/request`, undefined, 'DELETE'), onSuccess: async () => { await client.invalidateQueries({ queryKey: key }); await client.invalidateQueries({ queryKey: ['content'] }); notify('Takip isteği kaldırıldı.'); } });
  const seasons = episodes.data?.pages[0]?.seasons ?? [];
  const visibleEpisodes = episodes.data?.pages.flatMap(page => page.items) ?? [];
  const searchEntry = saved ? { ...saved, qualityProfileId: effectiveProfile } : null;
  const playable = files.data?.find(file => file.available && file.positionSeconds > 0 && !file.isWatched) ?? files.data?.find(file => file.available);
  return <>
    <div className="title-hero">
      {metadata.backdropUrl && <img className="title-backdrop" src={metadata.backdropUrl} alt="" />}<div className="title-shade" />
      <div className="title-identity"><div className="title-poster">{metadata.posterUrl ? <img src={metadata.posterUrl} alt="" /> : <Film size={60} />}</div>
      <section><p className="eyebrow">{metadata.kind === 'movie' ? 'FİLM' : 'DİZİ'} · {metadata.provider.toUpperCase()}</p><h1 data-page-heading tabIndex={-1}>{metadata.title}</h1>
        {metadata.originalTitle && metadata.originalTitle !== metadata.title && <p className="title-original">{metadata.originalTitle}</p>}
        <div className="title-facts"><span>{metadata.releaseDate?.slice(0, 4)}</span>{metadata.rating ? <span><Star size={15} /> {metadata.rating.toFixed(1)} / 10</span> : null}{metadata.runtimeMinutes ? <span>{metadata.runtimeMinutes} dk</span> : null}{metadata.ageLabel && <span className="title-age">{metadata.ageLabel}</span>}</div>
        {!!metadata.genres.length && <div className="genre-list">{metadata.genres.map(genre => <span key={genre}>{genre}</span>)}</div>}
        <div className="title-main-actions">{playable && <Button className="play-button" onPress={() => onPlay(playable)}><Play size={18} fill="currentColor" />{playable.positionSeconds > 0 && !playable.isWatched ? 'Kaldığın yerden devam et' : 'Oynat'}</Button>}
          {!saved?.requested && account.canRequest && <Button className={playable ? 'secondary-button' : 'play-button'} onPress={() => add.mutate()} isDisabled={add.isPending}>{add.isPending ? <LoaderCircle className="spin" size={16} /> : <Plus size={17} />} İçeriği iste</Button>}
           {saved?.requested && account.canAcquire && <Button variant="ghost" className="title-source-button" onPress={() => onSearch({ entry: searchEntry! })}><Search size={17} /> Kaynaklarda ara</Button>}
           <TrailerAction key={`${metadata.provider}:${metadata.kind}:${metadata.providerId}`} metadata={metadata}/>
        </div>
        <span className="title-library-state">{playable ? 'Kütüphanende' : saved?.requested ? saved.requestStatus === 'pending' ? 'Onay bekliyor' : 'Takipte' : 'Henüz kütüphanende yok'}</span>
        <PersonalContentPanel placement="hero" metadata={metadata} contentId={saved?.id} onSaved={value => { client.setQueryData(['content-details', selected.provider, selected.kind, selected.providerId], { metadata, saved: value }); }} />
      </section></div>
    </div>
    <div className="title-page-body">
      <section className="title-overview"><h2>Hikâye</h2>
      <p className="detail-description">{metadata.overview || 'Sağlayıcıda açıklama bulunmuyor.'}</p><a className="content-source" href={metadata.providerUrl} target="_blank" rel="noreferrer">{metadata.provider.toUpperCase()} kaynak sayfası <ExternalLink size={12} /></a>
      <CatalogSourceLinks metadata={metadata} />
      </section>
       <ContentPeople metadata={metadata} expanded/>
       <ContentMedia key={`${metadata.provider}:${metadata.kind}:${metadata.providerId}`} metadata={metadata} mediaId={playable?.id}/>
      {details.error && <p className="form-error" role="alert">{details.error.message}</p>}
      <div className="content-availability"><span className={saved?.fileCount ? 'status-dot' : 'status-dot offline'} /><strong>{saved?.fileCount ? `${saved.fileCount} dosya kütüphanende` : files.data?.length ? 'Kayıtlı dosyalara şu anda erişilemiyor' : 'Henüz kütüphanende yok'}</strong>{saved?.requested && <span>{saved.requestStatus === 'pending' ? 'Onay bekliyor' : 'Takipte'}</span>}</div>
      <details className="title-management"><summary>Takip ve edinme tercihleri</summary><div className="title-management-body"><div className="content-preferences">
        <label className="field"><span>Kalite tercihi</span><select value={effectiveProfile} onChange={e => setProfile(e.target.value)}>{profiles.data?.map(p => <option key={p.id} value={p.id}>{p.name}</option>)}</select></label>
        {metadata.kind === 'show' && <label className="field"><span>Takip kapsamı</span><select value={effectiveMode} onChange={e => setMode(e.target.value)}><option value="all">Tüm bölümler</option><option value="future">Takip tarihinden sonraki bölümler</option><option value="missing">Eksikleri takip et</option><option value="none">Takibi duraklat</option></select></label>}
      </div>
      {account.canRequest && metadata.kind === 'show' && <div className="content-preferences request-scope"><label className="field"><span>İstediğin kapsam</span><select value={scope} onChange={event => setScope(event.target.value)}><option value="all">Tüm dizi</option><option value="season">Bir sezon</option><option value="episode">Bir bölüm</option></select></label>
        {scope !== 'all' && <label className="field"><span>Sezon</span><input type="number" min={0} max={1000} value={requestSeason} onChange={event => setRequestSeason(event.target.value)} /></label>}
        {scope === 'episode' && <label className="field"><span>Bölüm</span><input type="number" min={1} max={10000} value={requestEpisode} onChange={event => setRequestEpisode(event.target.value)} /></label>}
      </div>}
      <div className="content-actions">
        {saved?.requested ? <>
          {account.canAcquire && <Button className="primary-button" onPress={() => onSearch({ entry: searchEntry! })}><Search size={16} /> Kaynaklarda ara</Button>}
          {account.role === 'admin' && <Button variant="ghost" onPress={() => change.mutate()} isDisabled={change.isPending}>Tercihleri kaydet</Button>}
          {account.role !== 'admin' && <Button variant="ghost" aria-label="İsteğimi kaldır" onPress={() => cancel.mutate()} isDisabled={cancel.isPending}>İsteğimi kaldır</Button>}
        </> : account.canRequest && <Button className="primary-button" onPress={() => add.mutate()} isDisabled={add.isPending || details.isPending || !!details.error}>{add.isPending ? <LoaderCircle className="spin" size={16} /> : <Plus size={17} />} İçeriği iste</Button>}
        {saved?.requested && metadata.kind === 'show' && account.canRequest && scope !== 'all' && <Button variant="ghost" onPress={() => add.mutate()} isDisabled={add.isPending}>Bu kapsamı iste</Button>}
      </div>
      {(add.error || change.error || cancel.error || profiles.error) && <p className="form-error" role="alert">{add.error?.message || change.error?.message || cancel.error?.message || profiles.error?.message}</p>}
      {saved?.lastError && <p className="form-error">{saved.lastError}</p>}
      {saved && account.role === 'admin' && <AutomationPanel contentId={saved.id} />}
      {saved && account.role === 'admin' && <><CatalogIdentityPanel content={saved} />{metadata.kind === 'show' ? <EpisodeNumberingPanel content={saved} /> : <MoviePartsPanel content={saved} files={files.data ?? []} />}</>}
      </div></details>
      {saved && <p className="detail-source">{saved.syncState === 'pending' || saved.syncState === 'syncing' ? 'Yayın bilgileri güncelleniyor…' : saved.lastSyncAt ? `Son güncelleme: ${date(saved.lastSyncAt)}` : 'Yayın bilgileri bekleniyor.'} İndirme, seçtiğin sürüm için verdiğin komutla başlar.</p>}
      {!!files.data?.length && <section className="content-files"><h3>Kütüphanendeki dosyalar</h3>{files.data.map(file => <div className="version-row" key={file.id}><div><strong>{file.title}</strong><span>{file.libraryName}{file.moviePart ? ` · ${file.moviePart.number}/${file.moviePart.count}. parça` : ''}</span><a href={appUrl(`/media/${file.id}`)} className="content-source">Dosya bilgileri</a></div><Button size="sm" variant="ghost" onPress={() => onPlay(file, undefined, !!file.moviePart)} isDisabled={!file.available}>Oynat <ChevronRight size={15} /></Button></div>)}</section>}
      {saved && metadata.kind === 'show' && <section className="episode-section">
        <div className="section-heading"><h3>Bölümler{episodes.data ? ` · ${episodes.data.pages[0].total}` : ''}</h3><select aria-label="Sezon filtresi" value={season} onChange={e => setSeason(e.target.value)}><option value="">Tüm sezonlar</option>{seasons.map(s => <option key={s} value={s}>{s === 0 ? 'Özel bölümler' : `${s}. sezon`}</option>)}</select></div>
        {season && account.canAcquire && <Button variant="ghost" size="sm" onPress={() => onSearch({ entry: searchEntry!, season: Number(season) })}><Search size={14} /> Sezonu kaynaklarda ara</Button>}
        {episodes.isPending ? <p className="muted">Bölümler hazırlanıyor…</p> : visibleEpisodes.map(ep => <div className="episode-row" key={ep.id}>
          <span className="episode-number">S{String(ep.seasonNumber).padStart(2, '0')} E{String(ep.episodeNumber).padStart(2, '0')}</span><div><strong>{ep.title}</strong><small>{ep.airDate || 'Yayın tarihi bilinmiyor'}</small></div>
          <span className={`episode-state ${ep.state}`}>{episodeLabels[ep.state]}</span>
          {(ep.state === 'missing' || ep.state === 'available') && account.canAcquire && <Button isIconOnly size="sm" variant="ghost" aria-label={`${ep.title} için kaynak ara`} onPress={() => onSearch({ entry: searchEntry!, season: ep.seasonNumber, episode: ep.episodeNumber })}><Search size={15} /></Button>}
          {ep.state === 'available' && files.data?.some(file => file.available && file.episodes?.some(segment => segment.episodeId === ep.id)) && <Button size="sm" variant="ghost" onPress={() => onPlay(files.data!.find(file => file.available && file.episodes?.some(segment => segment.episodeId === ep.id))!, ep.id)}>Oynat</Button>}
        </div>)}
        {episodes.hasNextPage && <div className="load-more"><Button variant="ghost" onPress={() => episodes.fetchNextPage()} isDisabled={episodes.isFetching}>Diğer bölümleri göster</Button></div>}
        {episodes.error && <p className="form-error">{episodes.error.message}</p>}
        {!episodes.isPending && !episodes.error && !visibleEpisodes.length && <p className="field-hint">Bu kapsamda henüz bölüm bilgisi yok.</p>}
      </section>}
    </div>
  </>;
}

export function TrackingView({ onOpen, onSearch, onDiscover }: { onOpen: (item: ProviderContent) => void; onSearch: (target: ReleaseTarget) => void; onDiscover: () => void }) {
  const account = useSession();
  const tracked = useQuery({ queryKey: ['content'], queryFn: () => request<ContentEntry[]>('/content') });
  const [tab, setTab] = useState('list');
  const calendar = useInfiniteQuery({ queryKey: ['calendar'], initialPageParam: 0, enabled: tab === 'calendar',
    queryFn: ({ pageParam, signal }) => request<EpisodePage>(`/calendar?offset=${pageParam}`, { signal }), getNextPageParam: nextEpisodePage });
  const scheduled = calendar.data?.pages.flatMap(page => page.items) ?? [];
  return <section>
    <div className="filter-pills product-subnav"><button className={tab === 'list' ? 'filter-pill selected' : 'filter-pill'} onClick={() => setTab('list')}><Heart size={15} /> Takip listem</button><button className={tab === 'calendar' ? 'filter-pill selected' : 'filter-pill'} onClick={() => setTab('calendar')}><CalendarDays size={15} /> Yayın takvimi</button><button className={tab === 'requests' ? 'filter-pill selected' : 'filter-pill'} onClick={() => setTab('requests')}>İstekler ve onaylar</button>{account.role === 'admin' && <button className={tab === 'automation' ? 'filter-pill selected' : 'filter-pill'} onClick={() => setTab('automation')}>Otomasyon</button>}</div>
    {tracked.error && <p className="form-error">{tracked.error.message}</p>}
    <Button variant="ghost" size="sm" onPress={() => setTab('collections')}>Favoriler ve koleksiyonlar</Button>
    {tab === 'collections' ? <CollectionsView onOpen={onOpen} /> : tab === 'automation' ? <AutomationHistory /> : tab === 'requests' ? <RequestInbox onOpen={onOpen} /> : tab === 'list' ? tracked.isPending ? <div className="job-skeleton home-skeleton" /> : tracked.data?.length ? <div className="tracking-grid">
      {tracked.data.map(entry => <article className="tracking-card" key={entry.id}>
        <a className="tracking-art" href={appUrl(`/title/${entry.metadata.provider}/${entry.metadata.kind}/${entry.metadata.providerId}`)} data-home-focus={entry.id} aria-label={`${entry.metadata.title} ayrıntıları`} onClick={event => { if (event.button === 0 && !event.ctrlKey && !event.metaKey && !event.shiftKey && !event.altKey) { event.preventDefault(); onOpen(entry.metadata); } }}>{entry.metadata.posterUrl ? <img src={entry.metadata.posterUrl} alt="" loading="lazy" /> : <Film size={28} />}</a>
        <div className="tracking-body"><button className="tracking-title" onClick={() => onOpen(entry.metadata)}>{entry.metadata.title}</button><p>{entry.metadata.releaseDate?.slice(0, 4)} · {entry.metadata.kind === 'movie' ? 'Film' : 'Dizi'}{entry.monitorMode === 'none' ? ' · Takip duraklatıldı' : ''}</p>
          <span className={`tracking-state ${entry.fileCount ? 'available' : ''}`}>{entry.requestStatus === 'pending' ? 'Yönetici onayı bekleniyor' : entry.fileCount ? `${entry.fileCount} dosya mevcut` : 'Dosya bekleniyor'}{entry.metadata.kind === 'show' && entry.missingCount > 0 ? ` · ${entry.missingCount} eksik bölüm` : ''}</span>
          {entry.lastError && <small className="tracking-error">{entry.lastError}</small>}
          <div>{account.canAcquire && <Button variant="ghost" size="sm" onPress={() => onSearch({ entry })}><Search size={14} /> Kaynaklarda ara</Button>}<Button variant="ghost" size="sm" onPress={() => onOpen(entry.metadata)}>Ayrıntılar <ChevronRight size={14} /></Button></div>
        </div>
      </article>)}
    </div> : <div className="search-empty"><Heart size={32} /><h2>Sıradaki hikâyeni seç.</h2><p>Gerçek film ve dizileri keşfedip takip listene ekle.</p><Button className="primary-button" onPress={onDiscover}>Kataloğu keşfet <ArrowRight size={16} /></Button></div> : <div className="calendar-list">
      {calendar.isPending ? <div className="job-skeleton home-skeleton" /> : scheduled.length ? scheduled.map(ep => <article className="calendar-row" key={ep.id}>
        <div className="calendar-date">{ep.airDate}</div><div><strong>{ep.contentTitle}</strong><p>S{ep.seasonNumber} E{ep.episodeNumber} · {ep.title}</p></div><span className={`episode-state ${ep.state}`}>{episodeLabels[ep.state]}</span>
      </article>) : <div className="search-empty"><CalendarDays size={30} /><h2>Yaklaşan yayın görünmüyor.</h2><p>Takip ettiğin dizilerin önümüzdeki 30 günü burada yer alır.</p></div>}
      {calendar.hasNextPage && <div className="load-more"><Button variant="ghost" onPress={() => calendar.fetchNextPage()} isDisabled={calendar.isFetching}>Daha fazla yayın</Button></div>}
      {calendar.error && <p className="form-error">{calendar.error.message}</p>}
    </div>}
  </section>;
}
