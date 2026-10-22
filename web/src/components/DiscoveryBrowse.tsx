import { useEffect, useRef, useState, type CSSProperties, type RefObject } from 'react';
import { Button } from '@heroui/react';
import { useInfiniteQuery, useQuery } from '@tanstack/react-query';
import { ArrowLeft, ArrowRight, Check, ChevronLeft, ChevronRight, Film, LoaderCircle, Search, Sparkles, Star, Tv, X } from 'lucide-react';
import { appUrl, request, type ContentEntry, type DiscoveryGenre, type DiscoveryPage, type DiscoveryPersonalPage, type MetadataStatus, type ProviderContent } from '../api';
import { discoveryColumns, discoveryKey, discoveryNext, discoveryRegion, pausedEmptyPages, personalNext, uniqueDiscovery } from '../discoveryFeed';
import { useUiLanguage, uiLocale, translate as t } from '../i18n';
import { PeopleBrowser } from './CatalogFeeds';
import './discovery.css';

type Actions = { onOpen: (item: ProviderContent) => void };
type ProviderQuery = { provider: string; kind: string; collection: string; genre?: number; genreLanguage: string; metadataLanguage?: string };
function useNear(active: boolean, root?: RefObject<HTMLElement | null>, margin = '350px 0px') {
  const ref = useRef<HTMLDivElement>(null); const [near, setNear] = useState(false);
  useEffect(() => {
    if (!active) { setNear(false); return; }
    const element = ref.current; if (!element) return;
    if (!('IntersectionObserver' in window)) { setNear(true); return; }
    const observer = new IntersectionObserver(([entry]) => setNear(!!entry?.isIntersecting), { root: root?.current ?? null, rootMargin: margin });
    observer.observe(element); return () => observer.disconnect();
  }, [active, root, margin]);
  return { ref, near: active && near };
}
function useColumns() {
  const ref = useRef<HTMLDivElement>(null); const [columns, setColumns] = useState(4);
  useEffect(() => {
    if (!ref.current) return;
    const element = ref.current; const update = () => { const width = element.getBoundingClientRect().width; if (width > 0) setColumns(discoveryColumns(width)); }; update();
    if (typeof ResizeObserver === 'undefined') { window.addEventListener('resize', update); return () => window.removeEventListener('resize', update); }
    const observer = new ResizeObserver(update); observer.observe(element); return () => observer.disconnect();
  }, []);
  return { ref, columns };
}
function useAutomaticPage(near: boolean, loading: boolean, more: boolean, paused: boolean, fetch: () => Promise<unknown>) {
  const busy = useRef(false); const latest = useRef(fetch); latest.current = fetch;
  useEffect(() => {
    if (!near || loading || !more || paused || busy.current) return;
    busy.current = true;
    void latest.current().catch(() => {}).finally(() => { busy.current = false; });
  }, [near, loading, more, paused]);
}
async function providerPage(input: ProviderQuery, page: number, signal: AbortSignal, search = '') {
  return request<DiscoveryPage>(`/discovery?${new URLSearchParams({ provider: input.provider, kind: input.kind, collection: input.collection,
    page: String(page), q: search, genreLanguage: input.genreLanguage, ...(input.genre ? { genre: String(input.genre) } : {}) })}`, { signal });
}
function DiscoveryCard({ item, known, onOpen }: Actions & { item: ProviderContent; known?: ContentEntry }) {
  return <a className="media-card discovery-card" href={appUrl(`/title/${item.provider}/${item.kind}/${item.providerId}`)} data-home-focus={discoveryKey(item)}
    onClick={event => { if (event.button === 0 && !event.ctrlKey && !event.metaKey && !event.shiftKey && !event.altKey) { event.preventDefault(); event.currentTarget.focus({ preventScroll: true }); onOpen(item); } }}>
    <div className="poster">{item.posterUrl ? <img src={item.posterUrl} alt="" loading="lazy" decoding="async"/> : <div className="art-placeholder"><Film size={36}/></div>}<div className="poster-shade"/>
      {known && <span className="discovery-saved"><Check size={12}/>{known.fileCount ? 'Kütüphanende' : 'Takipte'}</span>}
    </div><span className="media-card-title">{item.title}</span><span className="media-card-meta">{item.releaseDate?.slice(0, 4) || 'Tarih bekleniyor'}{item.rating ? <><Star size={10}/>{item.rating.toFixed(1)}</> : null}</span>
  </a>;
}
function useKnown(entries: ContentEntry[] | undefined) {
  const map = new Map<string, ContentEntry>();
  for (const entry of entries ?? []) { map.set(discoveryKey(entry.metadata), entry); for (const alias of entry.metadata.aliases ?? []) if (alias.provider !== 'name') map.set(discoveryKey(alias), entry); }
  return map;
}
function ShelfRail({ title, items, known, loading, error, more, next, active, onAll, onOpen, region }: Actions & {
  title: string; items: ProviderContent[]; known: Map<string, ContentEntry>; loading: boolean; error: Error | null; more: boolean; region: string;
  next: () => Promise<unknown>; active: boolean; onAll?: () => void;
}) {
  const rail = useRef<HTMLDivElement>(null); const sentinel = useNear(active, rail, '0px 250px'); const layout = useColumns();
  useAutomaticPage(sentinel.near, loading, more, !!error, next);
  const scroll = (direction: number) => { if (rail.current) rail.current.scrollBy({ left: direction * rail.current.clientWidth * .85,
    behavior: matchMedia('(prefers-reduced-motion: reduce)').matches ? 'instant' : 'smooth' }); };
  return <div ref={layout.ref} className="discovery-shelf" style={{ '--discovery-columns': layout.columns } as CSSProperties}>
    <header className="discovery-shelf-heading"><h2>{title}</h2><div>{onAll && <Button variant="ghost" size="sm" onPress={onAll}>Tümünü gör <ArrowRight size={14}/></Button>}
      <button className="discovery-rail-arrow" type="button" aria-label={`${title} önceki içerikler`} onClick={() => scroll(-1)}><ChevronLeft size={17}/></button><button className="discovery-rail-arrow" type="button" aria-label={`${title} sonraki içerikler`} onClick={() => scroll(1)}><ChevronRight size={17}/></button></div></header>
    <div ref={rail} className="discovery-rail" data-home-scroll={region}>{items.map(item => <DiscoveryCard key={discoveryKey(item)} item={item} known={known.get(discoveryKey(item))} onOpen={onOpen}/>)}
      {!items.length && loading && Array.from({ length: layout.columns }, (_, index) => <div className="poster-skeleton home-skeleton" key={index}/>)}
      <div ref={sentinel.ref} className="discovery-rail-sentinel">{loading && items.length > 0 ? <LoaderCircle className="spin" size={20}/> : more ? <Button variant="ghost" onPress={() => { void next(); }}>Daha fazla göster</Button> : null}</div>
    </div>
    {error && <p className="form-error" role="alert">{error.message}</p>}
    {!loading && !error && !items.length && <p className="field-hint">Bu rafta henüz içerik yok.</p>}
  </div>;
}
function ProviderShelf({ input, title, active, known, onAll, onOpen }: Actions & { input: ProviderQuery; title: string; active: boolean; known: Map<string, ContentEntry>; onAll: () => void }) {
  const visible = useNear(active);
  const query = useInfiniteQuery({ queryKey: ['discovery-shelf', input], initialPageParam: 1, enabled: visible.near,
    staleTime: 15 * 60_000, refetchOnWindowFocus: false, queryFn: ({ pageParam, signal }) => providerPage(input, pageParam, signal), getNextPageParam: discoveryNext });
  const items = uniqueDiscovery(query.data?.pages.flatMap(page => page.items) ?? []);
  return <div ref={visible.ref} className="discovery-lazy-shelf"><ShelfRail region={discoveryRegion(input)} title={title} items={items} known={known} loading={query.isPending || query.isFetchingNextPage}
    error={query.error} more={!!query.hasNextPage && !pausedEmptyPages(query.data?.pages)} next={() => query.fetchNextPage({ cancelRefetch: false })} active={visible.near} onAll={onAll} onOpen={onOpen}/></div>;
}
function PersonalShelf({ shelf, title, kind, active, onOpen }: Actions & { shelf: string; title: string; kind: string; active: boolean }) {
  const visible = useNear(active);
  const query = useInfiniteQuery({ queryKey: ['discovery-personal', shelf, kind], initialPageParam: 0, enabled: visible.near,
    staleTime: 30_000, refetchOnWindowFocus: false, queryFn: ({ pageParam, signal }) => request<DiscoveryPersonalPage>(`/discovery/personal?${new URLSearchParams({ shelf, kind, offset: String(pageParam), limit: '30' })}`, { signal }), getNextPageParam: personalNext });
  const entries = query.data?.pages.flatMap(page => page.items) ?? [];
  const known = useKnown(entries);
  if (query.data && !entries.length) return <div ref={visible.ref} className="discovery-personal-empty"><h2>{title}</h2><p>{shelf === 'watchlist' ? 'Favorilerine, koleksiyonuna veya takip listene eklediğin içerikler burada görünür.' : 'İzleme geçmişin oluştuğunda bu raf sana ait içerikleri gösterecek.'}</p></div>;
  return <div ref={visible.ref} className="discovery-lazy-shelf"><ShelfRail region={`discovery:personal:${kind}:${shelf}`} title={title} items={uniqueDiscovery(entries.map(entry => entry.metadata))} known={known}
    loading={query.isPending || query.isFetchingNextPage} error={query.error} more={!!query.hasNextPage} next={() => query.fetchNextPage({ cancelRefetch: false })} active={visible.near} onOpen={onOpen}/></div>;
}
function ResultsGrid({ input, search, active, known, title, onBack, onOpen }: Actions & { input: ProviderQuery; search: string; active: boolean; known: Map<string, ContentEntry>; title: string; onBack: () => void }) {
  const sentinel = useNear(active, undefined, '500px 0px'); const layout = useColumns();
  const query = useInfiniteQuery({ queryKey: ['discovery-grid', input, search], initialPageParam: 1, enabled: active && search.length !== 1,
    staleTime: 15 * 60_000, refetchOnWindowFocus: false, queryFn: ({ pageParam, signal }) => providerPage(input, pageParam, signal, search), getNextPageParam: discoveryNext });
  const items = uniqueDiscovery(query.data?.pages.flatMap(page => page.items) ?? []); const paused = pausedEmptyPages(query.data?.pages);
  useAutomaticPage(active && (sentinel.near || !!query.data && items.length < layout.columns * 2), query.isFetching, !!query.hasNextPage, paused || !!query.error, () => query.fetchNextPage({ cancelRefetch: false }));
  return <div ref={layout.ref} className="discovery-results" style={{ '--discovery-columns': layout.columns } as CSSProperties}>
    <header className="discovery-shelf-heading"><h2>{title}</h2><Button variant="ghost" onPress={onBack}><ArrowLeft size={14}/> Raflara dön</Button></header>
    <div className="discovery-results-grid" data-home-scroll={discoveryRegion(input, search)}>{items.map(item => <DiscoveryCard key={discoveryKey(item)} item={item} known={known.get(discoveryKey(item))} onOpen={onOpen}/>)}
      {query.isPending && Array.from({ length: layout.columns * 2 }, (_, index) => <div className="poster-skeleton home-skeleton" key={index}/>)}</div>
    {query.error && <p className="form-error" role="alert">{query.error.message}</p>}
    {!query.isPending && !items.length && <p className="field-hint">Bir eşleşme bulunamadı.</p>}
    <div ref={sentinel.ref} className="discovery-grid-sentinel">{query.isFetchingNextPage && <LoaderCircle className="spin" size={22}/>}<span>{items.length} içerik</span>
      {query.hasNextPage && <Button variant="ghost" isDisabled={query.isFetching} onPress={() => { void query.fetchNextPage({ cancelRefetch: false }); }}>Daha fazla göster</Button>}</div>
    {paused && <p className="field-hint">Boş sağlayıcı sayfaları otomatik yüklemeyi durdurdu. İstersen devam edebilirsin.</p>}
  </div>;
}
function DeferredResultsGrid(props: Parameters<typeof ResultsGrid>[0]) {
  const visible = useNear(props.active);
  return <div ref={visible.ref} className="discovery-lazy-shelf"><ResultsGrid {...props} active={visible.near}/></div>;
}

export function DiscoveryView({ onOpen, onSettings, active = true }: Actions & { onSettings: () => void; active?: boolean }) {
  const language = useUiLanguage(); const genreLanguage = uiLocale();
  const status = useQuery({ queryKey: ['metadata-settings'], queryFn: () => request<MetadataStatus>('/metadata/settings') });
  const tracked = useQuery({ queryKey: ['content'], queryFn: () => request<ContentEntry[]>('/content') }); const known = useKnown(tracked.data);
  const [provider, setProvider] = useState('tmdb'); const [kind, setKind] = useState('movie'); const [collection, setCollection] = useState('browse');
  const [genre, setGenre] = useState(''); const [q, setQ] = useState(''); const [search, setSearch] = useState(''); const [people, setPeople] = useState(false);
  useEffect(() => { const timer = setTimeout(() => setSearch(q.trim()), 350); return () => clearTimeout(timer); }, [q]);
  const connected = provider === 'tvmaze' || !!status.data?.tmdbConfigured;
  const genres = useQuery({ queryKey: ['discovery-genres', provider, kind, language], enabled: active && connected && provider === 'tmdb', staleTime: 24 * 60 * 60_000,
    queryFn: ({ signal }) => request<DiscoveryGenre[]>(`/discovery/genres?${new URLSearchParams({ provider, kind, language: genreLanguage })}`, { signal }) });
  const source = { provider, kind, genreLanguage, metadataLanguage: status.data?.language };
  const trend = useInfiniteQuery({ queryKey: ['discovery-shelf', { ...source, collection: 'trending' }], initialPageParam: 1, enabled: active && connected && provider === 'tmdb' && collection === 'browse' && !q.trim() && !genre,
    staleTime: 15 * 60_000, refetchOnWindowFocus: false, queryFn: ({ pageParam, signal }) => providerPage({ ...source, collection: 'trending' }, pageParam, signal), getNextPageParam: discoveryNext });
  const featured = trend.data?.pages[0]?.items.find(item => item.backdropUrl);
  const selectedGenre = genres.data?.find(item => String(item.id) === genre);
  const input = { ...source, collection: collection === 'browse' ? 'popular' : collection, genre: genre ? Number(genre) : undefined };
  const reset = () => { setGenre(''); setCollection('browse'); setQ(''); setSearch(''); };
  const browse = provider === 'tmdb' && collection === 'browse' && !genre && !q.trim();
  const showAll = (category: string, genreId?: number) => { setCollection(category); setGenre(genreId ? String(genreId) : ''); window.scrollTo({ top: 0, behavior: 'instant' }); };
  return <section className="discovery-browse" data-discovery="shelves-v1">
    <div className="discovery-browse-controls"><div className="search-box"><Search size={18}/><input aria-label="Gerçek içerik kataloğunda ara" placeholder="Bir film veya dizi bul…" value={q} maxLength={120} onChange={event => setQ(event.target.value)}/>{q && <button aria-label="Aramayı temizle" onClick={() => { setQ(''); setSearch(''); }}><X size={15}/></button>}</div>
      <select aria-label="Katalog kaynağı" value={provider} onChange={event => { setProvider(event.target.value); if (event.target.value === 'tvmaze') setKind('show'); reset(); }}><option value="tmdb">TMDB</option><option value="tvmaze">TVmaze · Dizi</option></select>
      <Button variant="ghost" aria-expanded={people} onPress={() => setPeople(value => !value)}>{people ? 'Kişi gezintisini kapat' : 'Oyuncu ve yönetmen keşfet'}</Button></div>
    {people && <PeopleBrowser onOpen={onOpen}/>}
    <div className="discovery-browse-filters"><div className="filter-pills"><button className={`filter-pill ${kind === 'movie' ? 'selected' : ''}`} aria-pressed={kind === 'movie'} disabled={provider === 'tvmaze'} onClick={() => { setKind('movie'); reset(); }}><Film size={15}/> Filmler</button><button className={`filter-pill ${kind === 'show' ? 'selected' : ''}`} aria-pressed={kind === 'show'} onClick={() => { setKind('show'); reset(); }}><Tv size={15}/> Diziler</button></div>
      {provider === 'tmdb' && <><select aria-label="Film ve dizi türü" value={genre} onChange={event => { setGenre(event.target.value); setCollection(event.target.value ? 'popular' : 'browse'); }}><option value="">Tür seç</option>{genres.data?.map(item => <option key={item.id} value={item.id}>{item.name}</option>)}</select>
        <select aria-label="Keşif koleksiyonu" value={collection} onChange={event => setCollection(event.target.value)}><option value="browse">Senin için raflar</option><option value="trending">Bu hafta öne çıkanlar</option><option value="popular">Popüler</option><option value="top">Yüksek puanlı</option><option value="upcoming">Yakında</option></select></>}
    </div>
    {genres.error && <p className="form-error">{genres.error.message}</p>}{status.error && <p className="form-error">{status.error.message}</p>}
    {!connected ? <div className="real-data-connect"><Sparkles size={30}/><h2>Gerçek film kataloğunu bağla.</h2><p>TMDB okuma token'ını eklediğinde gerçek filmler, afişler ve güncel bilgiler burada görünür.</p><Button className="primary-button" onPress={onSettings}>İçerik bilgileri ayarları</Button><Button variant="ghost" onPress={() => { setProvider('tvmaze'); setKind('show'); }}>Anahtarsız dizi kataloğunu aç</Button></div>
      : browse ? <>
        {featured && <div className="discovery-feature"><img src={featured.backdropUrl!} alt="" decoding="async"/><div className="discovery-feature-shade"/><div><p className="eyebrow">BU HAFTA ÖNE ÇIKAN</p><h2>{featured.title}</h2><span>{featured.releaseDate?.slice(0, 4)}{featured.rating ? ` · ${featured.rating.toFixed(1)} / 10` : ''}</span><p>{featured.overview}</p><Button className="play-button" onPress={() => onOpen(featured)}>İçeriği keşfet <ArrowRight size={16}/></Button></div></div>}
        <ProviderShelf input={{ ...source, collection: 'trending' }} title={t('Bu hafta öne çıkanlar')} active={active} known={known} onAll={() => showAll('trending')} onOpen={onOpen}/>
        <PersonalShelf shelf="continue" title={t('İzlemeye devam et')} kind={kind} active={active} onOpen={onOpen}/>
        <PersonalShelf shelf="watchlist" title={t('Daha sonra izle')} kind={kind} active={active} onOpen={onOpen}/>
        <PersonalShelf shelf="history" title={t('İzleme geçmişin')} kind={kind} active={active} onOpen={onOpen}/>
        {['popular', 'top', 'upcoming'].map(category => <ProviderShelf key={category + kind} input={{ ...source, collection: category }} title={t(category === 'popular' ? 'Şu an popüler' : category === 'top' ? 'Yüksek puanlı' : 'Yakında')} active={active} known={known} onAll={() => showAll(category)} onOpen={onOpen}/>)}
        {genres.data?.map((item, index) => <ProviderShelf key={kind + ':' + item.id} input={{ ...source, collection: index % 2 ? 'top' : 'popular', genre: item.id }} title={item.name} active={active} known={known} onAll={() => showAll('popular', item.id)} onOpen={onOpen}/>)}
        <DeferredResultsGrid input={{ ...source, collection: 'popular' }} search="" title={t('Keşfetmeye devam et')} active={active} known={known} onBack={reset} onOpen={onOpen}/>
      </> : q.trim().length === 1 ? <p className="field-hint">Aramak için en az iki karakter gir.</p> : <ResultsGrid input={input} search={search} title={q.trim() ? t('Arama sonuçları') : selectedGenre?.name ?? t(provider === 'tvmaze' ? 'Dizi kataloğu' : collection === 'top' ? 'Yüksek puanlı' : collection === 'trending' ? 'Bu hafta öne çıkanlar' : collection === 'upcoming' ? 'Yakında' : 'Popüler')} active={active && q.trim() === search} known={known} onBack={reset} onOpen={onOpen}/>}
    <p className="provider-attribution">{provider === 'tmdb' ? 'This product uses the TMDB API but is not endorsed or certified by TMDB.' : 'TVmaze · CC BY-SA'}</p>
    {provider === 'tmdb' && <p className="field-hint">Başlık ve açıklamalar ayrı TMDB dil tercihini kullanır. <button className="discovery-language-link" onClick={onSettings}>İçerik dili ayarları</button></p>}
  </section>;
}
