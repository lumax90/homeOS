import { lazy, Suspense, useEffect, useRef, useState, type FormEvent } from 'react';
import { Button, Input, Label, TextField } from '@heroui/react';
import { useInfiniteQuery, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { AnimatePresence, LayoutGroup, motion } from 'motion/react';
import { Activity, ArrowDownLeft, ArrowDownToLine, ArrowRight, Check, CheckCheck, ChevronRight, CircleAlert, Clock3, Eye, EyeOff, Film, Folder, FolderPlus, Grid2X2, HardDrive, House, Info, Layers3, List, LoaderCircle, Music2, Play, Plus, RefreshCw, Search, Server, ShieldCheck, Sparkles, Tv, UsersRound, X } from 'lucide-react';
import { broadcastAuthChange, bytes, date, duration, loadSession, mutate, request, resolution, type CatalogPage, type Download, type Library, type MediaItem, type ProviderContent, type ReleaseTarget, type ScanJob, type Session, type Stats, type SystemInfo, type VideoSequence } from './api';
import { AddLibraryDialog, SettingsDialog } from './components/Dialogs';
import { Glass } from './components/Glass';
import { MusicPlayer } from './components/MusicPlayer';
import { Artwork } from './components/Artwork';
import { MediaDetails } from './components/MediaDetails';
import { AddDownloadDialog, DownloadsView } from './components/Downloads';
import { ContentPage, DiscoveryView, TrackingView } from './components/ProductViews';
import { ReleaseDialog, SourcesDialog } from './components/SourceDialogs';
import { SessionContext } from './components/SessionContext';
import { NotificationBell } from './components/Requests';
import { MusicView } from './components/Music';
import { DeviceLinkLogin } from './components/Receivers';
import { LanguageSettings } from './components/LanguageSettings';
import { ReceiverPlaybackProvider } from './components/ReceiverPlayback';
import { LiveTvView } from './components/LiveTv';
import type { TvSelection } from './live/types';
import { Shelves } from './components/Shelves';
import { PodcastPlayer,type PodcastWatch } from './components/Podcasts';
import { RadioPlayer } from './music/RadioPlayer';
import type { RadioStation } from './music/experience';
import './music-catalog.css';
import { appUrl } from './base';
import { ProfileSwitcher } from './components/Profiles';
import { OfflineLibrary } from './components/Offline';
import { useWatchController, useWatchState, WatchRoomsView } from './components/WatchRooms';
import { detailsPath, parsePageRoute, type Section } from './pageRoutes';
import { usePageNavigation } from './usePageNavigation';
import { basePath } from './base';
import { MainNavigation } from './components/MainNavigation';
import { useController, useControllerCommands } from './controller/react';
import { controllerScope } from './controller/dom';
import type { SettingsSection } from './settingsModel';
import { catalogGroups,catalogCardKey,catalogTitle } from './catalogGroups';
import {LibrarySeries}from'./components/LibrarySeries';

type View = Section;
type Selection = { item: MediaItem; origin: string };
const Player = lazy(() => import('./components/Player').then(module => ({ default: module.Player })));
const LivePlayer = lazy(() => import('./live/LivePlayer').then(module => ({ default: module.LivePlayer })));
const GamesLibrary = lazy(() => import('./gaming/Games').then(module => ({ default: module.GamesLibrary })));
const GameDetails = lazy(() => import('./gaming/Games').then(module => ({ default: module.GameDetails })));
const GamePlayer = lazy(() => import('./gaming/GamePlayer').then(module => ({ default: module.GamePlayer })));

export default function App() {
  const client = useQueryClient();
  const [localMode, setLocalMode] = useState(() => !navigator.onLine);
  const session = useQuery({ queryKey: ['session'], queryFn: loadSession, retry: false, staleTime: 60_000, networkMode: 'always' });
  useEffect(() => {
    const expired = () => {
      client.setQueryData<Session>(['session'], old => old ? { ...old, authenticated: false } : old);
      client.removeQueries({ predicate: query => query.queryKey[0] !== 'session' });
      void client.invalidateQueries({ queryKey: ['session'] });
    };
    const changed = (event: StorageEvent) => { if (event.key === 'home-auth-change') location.reload(); };
    window.addEventListener('home-session-expired', expired); window.addEventListener('storage', changed);
    return () => { window.removeEventListener('home-session-expired', expired); window.removeEventListener('storage', changed); };
  }, [client]);
  if (localMode) return <OfflineLibrary standalone onOnline={() => { setLocalMode(false); void session.refetch(); }} />;
  if (session.isPending) return <div className="connection-screen"><Brand /><LoaderCircle className="spin" /><p>Evine bağlanıyoruz.</p><Button variant="ghost" onPress={() => setLocalMode(true)}>Cihaz kopyalarımı aç</Button></div>;
  if (session.error) return <div className="connection-screen"><Brand /><CircleAlert size={28} /><h1>Sunucuna ulaşamadık.</h1><p>Home sunucusunun çalıştığından emin ol.</p><Button onPress={() => session.refetch()} className="primary-button">Yeniden dene</Button><Button variant="ghost" onPress={() => setLocalMode(true)}><HardDrive size={16} /> Çevrimdışı cihaz kopyalarım</Button></div>;
  const invited = new URLSearchParams(location.hash.slice(1)).has('invite');
  return <SessionContext.Provider value={session.data}>{session.data.authenticated && !invited ? <ReceiverPlaybackProvider key={session.data.userId + ':' + session.data.sessionId}><Workspace session={session.data} /></ReceiverPlaybackProvider> : <Welcome session={session.data} />}</SessionContext.Provider>;
}

function Brand() { return <div className="brand" aria-label="Home"><span className="brand-icon"><House size={21} strokeWidth={1.6} /><Play size={9} fill="currentColor" className="brand-play" /></span><span>home<span className="brand-period">.</span></span></div>; }

function Welcome({ session }: { session: Session }) {
  const invite = new URLSearchParams(location.hash.slice(1)).get('invite');
  const [name, setName] = useState('');
  const [password, setPassword] = useState('');
  const [visible, setVisible] = useState(false);
  const client = useQueryClient();
  const auth = useMutation({
    mutationFn: () => invite ? mutate('/invitations/accept', { token: invite, displayName: name, password }) : mutate(session.configured ? '/auth/login' : '/setup', { displayName: name, password }),
    onSuccess: async () => { const notifications = new URLSearchParams(location.hash.slice(1)).has('notifications'); history.replaceState(null, '', location.pathname + (notifications ? '#notifications' : '')); client.removeQueries({ predicate: query => query.queryKey[0] !== 'session' }); await client.invalidateQueries({ queryKey: ['session'] }); broadcastAuthChange(); },
  });
  function submit(event: FormEvent) { event.preventDefault(); auth.mutate(); }
  return <main className="welcome">
    <div className="welcome-landscape" />
    <header className="welcome-header"><Brand /><span className="welcome-version">KİŞİSEL MEDYA SUNUCUN</span></header>
    <div className="welcome-body">
      <motion.div className="welcome-copy" initial={{ opacity: 0, y: 20 }} animate={{ opacity: 1, y: 0 }}>
        <span className="eyebrow"><span className="status-dot" /> SANA AİT BİR ALAN</span>
        <h1>Bütün hikâyelerin.<br /><span>Tek bir yerde.</span></h1>
        <p>Bir film gecesi. Tekrar dinlediğin bir albüm.<br />Sevdiğin her şey, kendi sunucunda.</p>
        <div className="welcome-features"><span><Film size={17} /> İzle</span><span><Music2 size={17} /> Dinle</span><span><Layers3 size={17} /> Bir araya getir</span></div>
      </motion.div>
      <motion.section className="welcome-form-panel" initial={{ opacity: 0, y: 28 }} animate={{ opacity: 1, y: 0 }} transition={{ delay: 0.1 }}>
        <div className="welcome-form-symbol"><ArrowDownLeft size={25} strokeWidth={1.5} /></div>
        <p className="eyebrow">{session.configured ? 'TEKRAR HOŞ GELDİN' : 'MERHABA, HOME'}</p>
        <h2>{invite ? 'Haneye hoş geldin.' : session.configured ? 'Eve dönmek güzel.' : 'Kendine bir yer aç.'}</h2>
        <p className="muted">{session.configured ? 'Kütüphanene kaldığın yerden devam et.' : 'Yerel hesabını oluştur. Sonra medyanı bağlayalım.'}</p>
        <form className="home-form" onSubmit={submit}>
          <TextField isRequired name="displayName" className="field"><Label>Adın</Label><Input value={name} onChange={e => setName(e.target.value)} autoComplete="username" placeholder="Sana nasıl seslenelim?" maxLength={60} /></TextField>
          <TextField isRequired name="password" className="field"><Label>Parola</Label><div className="password-input"><Input type={visible ? 'text' : 'password'} value={password} onChange={e => setPassword(e.target.value)} autoComplete={session.configured ? 'current-password' : 'new-password'} minLength={session.configured ? undefined : 10} placeholder={session.configured ? 'Parolan' : 'En az 10 karakter'} /><Button isIconOnly variant="ghost" aria-label={visible ? 'Parolayı gizle' : 'Parolayı göster'} onPress={() => setVisible(!visible)}>{visible ? <EyeOff size={17} /> : <Eye size={17} />}</Button></div></TextField>
          {auth.error && <p className="form-error" role="alert">{auth.error.message}</p>}
          <Button type="submit" className="primary-button form-submit" isDisabled={auth.isPending || !name.trim() || !password}>
            {auth.isPending ? 'Hazırlanıyor…' : invite ? 'Haneye katıl' : session.configured ? 'Kütüphaneme git' : 'Başlayalım'}<ArrowRight size={18} />
          </Button>
        </form>
        <ProfileSwitcher />
        <LanguageSettings/>
        {session.configured&&!invite&&<DeviceLinkLogin onLinked={()=>{client.removeQueries({predicate:query=>query.queryKey[0]!=='session'});void client.invalidateQueries({queryKey:['session']});broadcastAuthChange();}}/>}
        <p className="welcome-local"><ShieldCheck size={16} /> Hesabın bu sunucuda saklanır.</p>
      </motion.section>
    </div>
    <footer className="welcome-footer"><span>Kendi sunucunda. Kendi kontrolünde.</span><span>{session.preview ? 'HOME · GELİŞTİRME ÖNİZLEMESİ' : 'HOME · İLK SÜRÜM'}</span></footer>
  </main>;
}

function Workspace({ session }: { session: Session }) {
  const controller = useController(); const controllerRoot = useRef<HTMLElement>(document.body);
  const navigation = usePageNavigation(session.canDiscover ? 'discover' : 'library');
  const route = navigation.route;
  const [view, setView] = useState<View>(route.kind === 'section' ? route.section : session.canDiscover ? 'discover' : 'library');
  const [watchLoadError, setWatchLoadError] = useState('');
  useEffect(() => { if (route.kind === 'section') setView(route.section === 'downloads' && !session.canAcquire || route.section === 'discover' && !session.canDiscover ? 'library' : route.section); else if (route.kind === 'game' || route.kind === 'game-play') setView('games'); }, [route, session.canDiscover, session.canAcquire]);
  const [search, setSearch] = useState('');
  const [debounced, setDebounced] = useState('');
  const [kind, setKind] = useState('');
  const [libraryFilter, setLibraryFilter] = useState('');
  const [layout, setLayout] = useState<'grid' | 'list'>('grid');
  const [addOpen, setAddOpen] = useState(false);
  const [downloadOpen, setDownloadOpen] = useState(false);
  const [settingsOpen, setSettingsOpen] = useState(false);
  const [settingsSection, setSettingsSection] = useState<SettingsSection>('account');
  const [sourcesOpen, setSourcesOpen] = useState(false);
  const [releaseTarget, setReleaseTarget] = useState<ReleaseTarget | null>(null);
  const [playing, setPlaying] = useState<MediaItem | null>(null);
  const [queue, setQueue] = useState<MediaItem[]>([]); const [queueIndex, setQueueIndex] = useState(0);
  const [playbackKey, setPlaybackKey] = useState(0);
  const playGeneration = useRef(0);
  const [shuffle, setShuffle] = useState(false); const [repeat, setRepeat] = useState('off');
  const queueLoaded = useRef(false); const queueSequence = useRef(0);
  const queueClient = useRef([...crypto.getRandomValues(new Uint8Array(16))].map(value => value.toString(16).padStart(2, '0')).join(''));
  const [toast, setToast] = useState('');
  const client = useQueryClient();
  const toastTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const queryInput = useRef<HTMLInputElement>(null);
  useEffect(() => controller?.clearFocus(), [controller, session.userId, session.sessionId]);
  useControllerCommands(controllerRoot, action => {
    if (controllerScope() !== document.body) return false;
    if (action === 'back') { navigation.back(route.kind === 'section' ? '/home' : '/' + view); return true; }
    if (action === 'search' && !(route.kind === 'section' && (route.section === 'library' || route.section === 'discover' || route.section === 'games'))) {
      navigation.visit('/library'); requestAnimationFrame(() => controller?.edit(queryInput.current)); return true;
    }
    return false;
  });
  const roomController = useWatchController(session); const roomState = useWatchState(roomController);
  const roomPlayback = useRef<string | null>(null); const playingRef = useRef(playing); playingRef.current = playing;
  const sharedMember = roomState.connection?.role === 'player' ? roomState.connection.memberId : null;
  const sharedMedia = sharedMember ? roomState.connection!.room.mediaId : null;
  useEffect(() => {
    if (!sharedMember || !sharedMedia) { if (roomPlayback.current) { roomPlayback.current = null; setPlaying(null); } return; }
    roomPlayback.current = sharedMember;
    if (playingRef.current?.id === sharedMedia) return;
    const controller = new AbortController(); const generation = ++playGeneration.current;
    void request<MediaItem>(`/media/${sharedMedia}`, { signal: controller.signal }).then(item => {
      if (generation !== playGeneration.current || controller.signal.aborted) return;
      setRadioWatch(null); setLiveWatch(null); setPodcastWatch(null); setQueue([item]); setQueueIndex(0); setPlaying(item); setPlaybackKey(value => value + 1); navigation.visit(`/watch/${item.id}`);
    }).catch(error => { if (!controller.signal.aborted) notify(error.message); });
    return () => controller.abort();
  }, [sharedMember, sharedMedia]);
  useEffect(() => {
    if (route.kind !== 'watch' || playingRef.current?.id === route.id) return;
    const id = route.id; const controller = new AbortController(); const generation = ++playGeneration.current;
    setWatchLoadError('');
    void request<MediaItem>(`/media/${id}`, { signal: controller.signal }).then(async item => {
      const sequence = item.kind === 'video' ? await request<VideoSequence>(`/media/${id}/sequence`, { signal: controller.signal }) : { items: [item], index: 0 };
      if (controller.signal.aborted || generation !== playGeneration.current) return;
      setRadioWatch(null); setLiveWatch(null); setPodcastWatch(null); queueLoaded.current = true; setQueue(sequence.items); setQueueIndex(sequence.index); setPlaying(sequence.items[sequence.index]); setPlaybackKey(value => value + 1);
    }).catch(error => { if (!controller.signal.aborted && generation === playGeneration.current) setWatchLoadError((error as Error).message); });
    return () => controller.abort();
  }, [route]);

  const system = useQuery({ queryKey: ['system'], queryFn: () => request<SystemInfo>('/system') });
  const stats = useQuery({ queryKey: ['stats'], queryFn: () => request<Stats>('/stats') });
  const libraries = useQuery({ queryKey: ['libraries'], queryFn: () => request<Library[]>('/libraries') });
  const jobs = useQuery({ queryKey: ['jobs'], queryFn: () => request<ScanJob[]>('/jobs'), enabled: session.role === 'admin' });
  const downloads = useQuery({ queryKey: ['downloads'], queryFn: () => request<Download[]>('/downloads'), enabled: session.canAcquire });
  const continuing = useQuery({ queryKey: ['continue'], queryFn: () => request<MediaItem[]>('/continue?grouped=true') });
  const savedQueue = useQuery({ queryKey: ['queue'], queryFn: () => request<{ items: MediaItem[]; index: number; shuffle: boolean; repeat: string }>('/queue') });
  useEffect(() => { if (savedQueue.data && !queueLoaded.current) { queueLoaded.current = true; setQueue(savedQueue.data.items); setQueueIndex(savedQueue.data.index); setShuffle(savedQueue.data.shuffle); setRepeat(savedQueue.data.repeat); } }, [savedQueue.data]);
  const catalog = useInfiniteQuery({
    queryKey: ['catalog', debounced, kind, libraryFilter],
    initialPageParam: 0,
    queryFn: ({ pageParam }) => request<CatalogPage>(`/catalog?${new URLSearchParams({ q: debounced, kind, library: libraryFilter, offset: String(pageParam), limit: '60',grouped:'true' })}`),
    getNextPageParam: last => last.offset + last.items.length < last.total ? last.offset + last.items.length : undefined,
  });
  const files = catalog.data?.pages.flatMap(page => page.items) ?? [];
  const items = catalogGroups(files);
  // Home has its own unfiltered, fresh catalog page. Library filters and loaded
  // pagination must not become an invisible filter on manually imported files.
  const homeCatalog = useQuery({queryKey:['catalog','home-recent'],enabled:view==='home',
    queryFn:({signal})=>request<CatalogPage>('/catalog?offset=0&limit=60&grouped=true',{signal})});
  const homeItems = catalogGroups(homeCatalog.data?.items??[]);
  const total = catalog.data?.pages[0]?.total ?? 0;
  const activeJobs = jobs.data?.filter(job => ['queued', 'running'].includes(job.status)) ?? [];
  const featured = homeItems.find(item => item.available && item.kind === 'video' && item.hasThumbnail) ?? homeItems[0];
  const isDemo = !!libraries.data?.some(library => library.isDemo);

  useEffect(() => { const timer = setTimeout(() => setDebounced(search), 220); return () => clearTimeout(timer); }, [search]);
  useEffect(() => {
    let timer: ReturnType<typeof setTimeout> | undefined;
    const events = new EventSource(appUrl('/api/events'));
    const dirty = new Set<string>();
    const update = (event: Event) => {
      let type = '';
      try { type = JSON.parse((event as MessageEvent).data)?.Type || ''; } catch { /* Resynchronize on connection events. */ }
      if (type === 'playback.changed') window.dispatchEvent(new Event('home-playback-changed'));
      if (type === 'notifications.changed') ['notifications', 'notification-channels', 'notification-deliveries', 'notification-attempts', 'push-device', 'push-configuration'].forEach(key => dirty.add(key));
      if (type === 'offline.changed') { dirty.add('offline-policy'); window.dispatchEvent(new Event('home-offline-changed')); }
      if (type === 'rooms.changed') { dirty.add('watch-rooms'); dirty.add('watch-events'); window.dispatchEvent(new Event('home-rooms-changed')); }
      if (type === 'downloads.changed' || type === 'download-budgets.changed') { dirty.add('download-budget-usage'); dirty.add('download-budget-history'); dirty.add('downloads'); }
      if (type === 'catalog.changed') ['episode-catalog', 'episode-orders', 'episode-boundaries'].forEach(key => dirty.add(key));
      if (['catalog.changed','requests.changed','tracking.changed','product.changed'].includes(type)) dirty.add('discovery-personal');
       if (type === 'music.changed') ['music-library-artists', 'music-library-artist-albums', 'music-album-details', 'music-videos', 'lyrics','music-library-tracks'].forEach(key => dirty.add(key));
       if(type==='music.preferences.changed'){dirty.add('music-experience');dirty.add('music-enrichment-progress');window.dispatchEvent(new Event('home-music-artwork-changed'));}
       if(type==='music.enrichment.changed'){dirty.add('music-enrichment-progress');dirty.add('music-online-info');window.dispatchEvent(new Event('home-music-artwork-changed'));}
       if(type==='music.radio.changed')dirty.add('music-radio-favorites');
      if (type === 'music.lists.changed') ['music-list-imports', 'music-list-import', 'music-follows', 'album-follows', 'music-monitors'].forEach(key => dirty.add(key));
      if (type === 'product.changed') ['product-jobs','catalog-feeds','catalog-feed-items','collections','collection-items','games'].forEach(key=>dirty.add(key));
      if(type==='segments.changed')dirty.add('media-segments');
       if(type==='live.changed')['live-library','live-channel','live-player-channels','live-neighbours','live-public-lists','live-channels','live-sources','live-guide','dvr-recordings','dvr-rules','product-jobs'].forEach(key=>dirty.add(key));
      if(type==='shelves.changed')['shelf-assets','book-info','libraries'].forEach(key=>dirty.add(key));
      if(type==='podcasts.changed')['podcasts','podcast-episodes','product-jobs'].forEach(key=>dirty.add(key));
      if(type==='games.changed'||type==='product.changed')dirty.add('games');
      const keys = type === 'playback.changed' ? ['playback-usage', 'playback-devices', 'user-playback-limits'] : type === 'catalog.changed' ? ['catalog', 'stats', 'media', 'versions', 'continue', 'content', 'content-details', 'content-entry', 'content-files', 'catalog-identity', 'episode-numbering', 'movie-editions', 'episodes', 'requests', 'collections'] : type === 'music.automation.changed' ? ['music-monitors', 'music-monitor-albums', 'music-automation-jobs', 'music-automation-blocked', 'downloads', 'notifications'] : type === 'subtitles.changed' ? ['subtitle-wanted', 'subtitle-connection', 'subtitle-profile', 'subtitle-policy', 'subtitle-versions', 'subtitle-sync', 'media-tracks', 'notifications'] : type === 'music.changed' ? ['music-albums', 'music-tracks', 'music-tasks', 'music-recommendations', 'music-tag-preview', 'catalog', 'stats'] : type === 'system.changed' ? ['backups', 'system-jobs'] : type === 'downloads.changed' ? ['downloads'] : type === 'automation.changed' ? ['automation', 'automation-rule', 'automation-jobs', 'automation-blocked', 'notifications'] : type === 'tracks.changed' ? ['media-tracks', 'subtitle-versions'] : type === 'requests.changed' ? ['requests', 'notifications', 'content', 'request-history'] : type === 'tracking.changed' ? ['content', 'content-details', 'content-files', 'episodes', 'calendar', 'requests', 'notifications'] : ['catalog', 'stats', 'libraries', 'jobs', 'media', 'versions', 'continue', 'downloads', 'content', 'content-files', 'requests', 'notifications'];
      if (!['notifications.changed','offline.changed','rooms.changed','download-budgets.changed','music.lists.changed','music.preferences.changed','music.enrichment.changed','music.radio.changed'].includes(type)) keys.forEach(key => dirty.add(key));
      if (timer) return;
      timer = setTimeout(() => {
        client.invalidateQueries({ predicate: query => dirty.has(String(query.queryKey[0])) });
        dirty.clear();
        timer = undefined;
      }, 250);
    };
    events.addEventListener('change', update);
    events.addEventListener('ready', update);
    return () => { events.close(); clearTimeout(timer); };
  }, [client]);
  useEffect(() => {
    const shortcut = (event: KeyboardEvent) => {
      if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'k') {
        event.preventDefault(); navigation.visit('/library'); requestAnimationFrame(() => queryInput.current?.focus());
      }
    };
    window.addEventListener('keydown', shortcut);
    return () => window.removeEventListener('keydown', shortcut);
  }, [navigation.visit]);

  function notify(message: string) {
    if (toastTimer.current) clearTimeout(toastTimer.current);
    setToast(message); toastTimer.current = setTimeout(() => setToast(''), 4500);
  }
  function navigate(next: View) {
    navigation.visit('/' + next); setView(next); setSearch(''); setDebounced(''); setKind(''); setLibraryFilter('');
    window.scrollTo({ top: 0, behavior: 'instant' });
  }
  function chooseMedia(selection: Selection) { client.setQueryData(['media', selection.item.id], selection.item); navigation.visit(detailsPath(selection.item)); }
  function chooseContent(item: ProviderContent) { navigation.visit(`/title/${item.provider}/${item.kind}/${item.providerId}`); }
  function persistQueue(items: MediaItem[], index: number, random = shuffle, repeating = repeat) {
    void mutate('/queue', { mediaIds: items.map(item => item.id), index, shuffle: random, repeat: repeating, clientId: queueClient.current, sequence: ++queueSequence.current, expectedUserId: session.userId }, 'PUT').catch(error => notify(error.message));
  }
  function playQueue(items: MediaItem[], index = 0, ordered = false) {
    roomPlayback.current = null; if (roomState.connection || roomState.busy) void roomController.leave();
    ++playGeneration.current;
    const selectedId = items[index]?.id; const available = items.filter(item => item.available).slice(0, 500);
    if (!available.length) { notify('Kuyrukta erişilebilir medya yok.'); return; }
    setLiveWatch(null); setPodcastWatch(null); setRadioWatch(null);
    queueLoaded.current = true; const position = Math.max(0, available.findIndex(item => item.id === selectedId));
    if (ordered) { setShuffle(false); setRepeat('off'); }
    setQueue(available); setQueueIndex(position); setPlaying(available[position]); setPlaybackKey(value => value + 1); persistQueue(available, position, ordered ? false : shuffle, ordered ? 'off' : repeat);
    if (available[position].kind === 'video' || libraries.data?.some(library => library.id === available[position].libraryId && library.kind === 'audiobooks')) navigation.visit(`/watch/${available[position].id}`);
  }
   function appendMusicQueue(items: MediaItem[]) {
    const additions=items.filter(item=>item.available&&item.kind==='audio');if(!additions.length){notify('Kuyrukta erişilebilir medya yok.');return;}
    const next=[...queue,...additions].slice(0,500);queueLoaded.current=true;setQueue(next);persistQueue(next,Math.min(queueIndex,Math.max(0,next.length-1)));notify(next.length-queue.length<additions.length?'Çalma sırası en fazla 500 parça alır.':'Şarkılar sıraya eklendi.');
   }
   async function play(item: MediaItem, episodeId?: string, selectedPart = false) {
    const generation = ++playGeneration.current;
    if (item.kind !== 'video') { playQueue([item]); return; }
    try {
      const query = new URLSearchParams(); if (episodeId) query.set('episode', episodeId); if (selectedPart) query.set('part', 'true');
      const sequence = await request<VideoSequence>(`/media/${item.id}/sequence?${query}`);
      if (generation === playGeneration.current) playQueue(sequence.items, sequence.index, !!sequence.editionId);
    } catch (error) { if (generation === playGeneration.current) notify((error as Error).message); }
  }
  function advance(delta: number, automatic = false) {
    if (!queue.length) return;
    let next = queueIndex + delta;
    if (automatic && !shuffle && next >= queue.length && repeat !== 'all') return;
    if (shuffle && queue.length > 1) { const choices = queue.map((_, index) => index).filter(index => index !== queueIndex); next = choices[Math.floor(Math.random() * choices.length)]; }
    next = (next + queue.length) % queue.length; setQueueIndex(next); setPlaying(queue[next]); setPlaybackKey(value => value + 1); persistQueue(queue, next);
    if (route.kind === 'watch') navigation.visit(`/watch/${queue[next].id}`, true);
  }
  async function joinRoom(id: string, role: 'player' | 'controller') {
    roomPlayback.current = null; ++playGeneration.current; setPlaying(null); setRadioWatch(null); setLiveWatch(null); setPodcastWatch(null);
    await roomController.join(id, role);
  }
  function closePlayer() {
    if (route.kind === 'watch') navigation.back(playing ? detailsPath(playing) : '/library');
    roomPlayback.current = null; if (roomController.snapshot().connection?.role === 'player') void roomController.leave();
    ++playGeneration.current; setPlaying(null); client.invalidateQueries({ queryKey: ['continue'] }); client.invalidateQueries({ queryKey: ['catalog'] });
  }
  function openSettings(section: SettingsSection = 'account') { setSettingsSection(section); setSettingsOpen(true); }
  function openSources() { setReleaseTarget(null); if (session.role === 'admin') openSettings('sources'); else { setSettingsOpen(false); setSourcesOpen(true); } }
   const [liveWatch,setLiveWatch]=useState<TvSelection|null>(null);const liveCommand=useRef(0);const [podcastWatch,setPodcastWatch]=useState<PodcastWatch|null>(null);const [radioWatch,setRadioWatch]=useState<{station:RadioStation;key:number}|null>(null);const radioKey=useRef(0);
   useEffect(() => { if (route.kind === 'game-play') { closePlayer(); setLiveWatch(null); setPodcastWatch(null); setRadioWatch(null); } }, [route.kind]);
  function searchContent(target: ReleaseTarget) { setReleaseTarget(target); }
  const hour = new Date().getHours();
  const greeting = hour >= 18 ? 'İyi akşamlar' : hour < 12 ? 'Günaydın' : 'Merhaba';

  return <LayoutGroup>
    <div id="app-content" className="workspace" data-offline={view === 'offline' || view === 'rooms'} hidden={route.kind === 'watch' && playing?.id === route.id || route.kind === 'game-play'}>
      <header className="app-header">
        <button className="brand-button" onClick={() => navigate('home')} aria-label="Home ana sayfa" aria-current={view === 'home' ? 'page' : undefined}><Brand /></button>
        <MainNavigation view={view} access={session} pageKey={location.pathname} activeJobs={activeJobs.length} onNavigate={navigate}/>
        <div className="header-right"><button className="server-status" onClick={() => openSettings('about')}><span className={system.error ? 'status-dot offline' : 'status-dot'} />{system.error ? 'Bağlantı bekleniyor' : 'Sunucun çevrimiçi'}</button><ProfileSwitcher /><NotificationBell onSettings={()=>openSettings('notifications')}/><Button isIconOnly className="account-button" aria-label="Ayarlar" onPress={() => openSettings('account')}>{session.displayName?.slice(0, 1).toLocaleUpperCase('tr')}</Button></div>
      </header>

      <main className="main-content" onClick={event => {
        if (event.defaultPrevented || event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
        const anchor = (event.target as HTMLElement).closest('a'); if (!anchor || anchor.target || anchor.hasAttribute('download')) return;
        const url = new URL(anchor.href); if (url.origin !== location.origin || parsePageRoute(url.pathname, basePath).kind === 'not-found') return;
        event.preventDefault(); navigation.visit(url.pathname);
      }}>
        {route.kind === 'title' && <ContentPage provider={route.provider} kind={route.contentKind} id={route.id} onBack={() => navigation.back('/' + view)} onSearch={searchContent} onPlay={play} notify={notify} />}
        {route.kind === 'media' && <MediaDetails mediaId={route.id} onClose={() => navigation.back('/' + view)} onPlay={play} onSettings={() => openSettings('metadata')} onOffline={() => navigate('offline')} />}
        {route.kind==='series'&&<LibrarySeries key={route.id} id={route.id} onBack={()=>navigation.back('/'+view)} onPlay={play}/>}
        {route.kind === 'game' && <Suspense fallback={<div className="games-hero-skeleton home-skeleton" />}><GameDetails id={route.id} onBack={() => navigation.back('/games')} onOpen={navigation.visit} notify={notify} /></Suspense>}
        {route.kind === 'not-found' && <div className="search-empty"><CircleAlert size={28} /><h1>Sayfa bulunamadı.</h1><Button onPress={() => navigate('library')}>Kütüphaneye dön</Button></div>}
        {route.kind === 'watch' && playing?.id !== route.id && <div className="connection-screen">{watchLoadError ? <><CircleAlert size={28} /><p className="form-error">{watchLoadError}</p><Button onPress={() => navigation.back('/library')}>Geri</Button></> : <><LoaderCircle className="spin" /><p>Oynatman hazırlanıyor…</p></>}</div>}
        <div hidden={route.kind !== 'section'}>
          {view==='games'||view==='music'||view==='live'?null:view==='shelves'?<div className="page-heading"><div><p className="eyebrow">FOTOĞRAFLAR, KİTAPLAR VE SESLER</p><h1>Kendi rafların.</h1><p className="page-subtitle">Fotoğrafların, kitapların, çizgi romanların ve podcastlerin.</p></div>{session.role==='admin'&&<Button className="secondary-button" onPress={()=>setAddOpen(true)}><Plus size={16}/> Kütüphane ekle</Button>}</div>:<>
        <div className="page-heading"><div><p className="eyebrow">{view === 'discover' ? 'GERÇEK İÇERİK KATALOĞU' : view === 'tracking' ? 'İSTEKLERİN VE TAKİP ETTİKLERİN' : view === 'home' ? 'MEDYANIN EVİ' : view === 'library' ? 'SANA AİT KOLEKSİYON' : view === 'downloads' ? 'KÜTÜPHANENE DOĞRU' : 'HER ŞEY YOLUNDA'}</p><h1>{view === 'discover' ? 'Sıradaki hikâyeni bul.' : view === 'tracking' ? 'Gözün burada olsun.' : view === 'home' ? `${greeting}, ${session.displayName}.` : view === 'library' ? 'Her şey, bir arada.' : view === 'downloads' ? 'Yeni hikâyeler yolda.' : 'Perde arkasında.'}</h1><p className="page-subtitle">{view === 'discover' ? 'Gerçek filmler, diziler ve merak ettiklerin.' : view === 'tracking' ? 'İstediğin içerikler, yaklaşan bölümler ve eksikler.' : view === 'home' ? 'Bugün hangi hikâyeye yer açalım?' : view === 'library' ? `${stats.data?.items ?? 0} dosya · ${stats.data?.libraries ?? 0} kütüphane · ${bytes(stats.data?.sizeBytes ?? 0)}` : view === 'downloads' ? 'İndirmeden kütüphanene, tek bir akış.' : 'Kütüphanende neler olup bittiğini tek yerden takip et.'}</p></div>{session.role === 'admin' && (view === 'discover' || view === 'tracking' ? <Button className="secondary-button" onPress={openSources}><SettingsIcon /> Kaynaklar ve kalite</Button> : <Button className="secondary-button add-library-button" onPress={() => view === 'downloads' ? setDownloadOpen(true) : setAddOpen(true)}><Plus size={18} />{view === 'downloads' ? 'İndirme ekle' : 'Kütüphane ekle'}</Button>)}</div>

        </>}
        {view === 'discover' && <DiscoveryView active={route.kind === 'section'} onOpen={chooseContent} onSettings={() => openSettings('metadata')} />}
         {view === 'music' && <MusicView onPlay={playQueue} onQueue={appendMusicQueue} onRadio={station=>{closePlayer();setLiveWatch(null);setPodcastWatch(null);setRadioWatch({station,key:++radioKey.current});}} onLibrary={session.role==='admin'?()=>setAddOpen(true):undefined} queue={queue} notify={notify} />}
        {view === 'games' && <Suspense fallback={<div className="games-hero-skeleton home-skeleton" />}><GamesLibrary active={route.kind === 'section'} onOpen={navigation.visit} notify={notify} /></Suspense>}
         {view==='live'&&<LiveTvView active={route.kind==='section'} onLive={channel=>{closePlayer();setRadioWatch(null);setPodcastWatch(null);setLiveWatch({channel,command:++liveCommand.current});}} onPlay={play}/>}
         {view==='shelves'&&<Shelves libraries={libraries.data??[]} onPlay={playQueue} onPodcast={watch=>{closePlayer();setRadioWatch(null);setLiveWatch(null);setPodcastWatch(watch);}}/>}
        {view === 'offline' && <OfflineLibrary />}
        {view === 'rooms' && <WatchRoomsView controller={roomController} onJoin={joinRoom} />}
        {view === 'tracking' && <TrackingView onOpen={chooseContent} onSearch={searchContent} onDiscover={() => navigate(session.canDiscover ? 'discover' : 'library')} />}
        {(view === 'downloads' || view === 'activity') && <div className="filter-pills product-subnav"><button className={view === 'downloads' ? 'filter-pill selected' : 'filter-pill'} onClick={() => navigate('downloads')}><ArrowDownToLine size={14} /> İndirmeler</button><button className={view === 'activity' ? 'filter-pill selected' : 'filter-pill'} onClick={() => navigate('activity')}><Activity size={14} /> Aktivite</button></div>}

        {view !== 'offline' && ((view==='home'?homeCatalog.error:catalog.error) || stats.error) && <div className="inline-error" role="alert"><CircleAlert size={18} /><span>{(view==='home'?homeCatalog.error:catalog.error)?.message || stats.error?.message}</span><Button size="sm" variant="ghost" onPress={() => client.invalidateQueries()}>Yeniden dene</Button></div>}

        {view === 'home' && <>
          {featured ? <motion.section className="feature" initial={{ opacity: 0 }} animate={{ opacity: 1 }}>
            <div className="feature-art"><Artwork item={featured} eager mode="backdrop" /></div><div className="feature-shade" />
            <div className="feature-copy"><span className="feature-overline"><span className="feature-rule" /> KÜTÜPHANENDEN BİR HİKÂYE</span><h2>{catalogTitle(featured)}</h2><div className="feature-meta"><span>{featured.isDemo ? 'Örnek içerik' : featured.libraryName}</span><span className="meta-dot">·</span><span>{featured.catalogGroup?.kind==='show'?`${featured.catalogGroup.episodeCount} bölüm`:duration(featured.durationSeconds)}</span><span className="quality-badge">{resolution(featured)}</span></div><p className="feature-description">{featured.content ? featured.catalogGroup?.kind==='show'?featured.content.overview:featured.content.episodeOverview || featured.content.overview : <>Kendi arşivinden. İstediğin zaman.<br />Biraz dur, güzel bir şey aç.</>}</p><div className="feature-actions"><Button className="play-button" onPress={() => featured.catalogGroup?.kind==='show'?chooseMedia({item:featured,origin:`feature-${catalogCardKey(featured)}`}):play(featured)} isDisabled={!featured.available}>{featured.catalogGroup?.kind==='show'?<Layers3 size={17}/>:<Play size={17} fill="currentColor"/>}{featured.catalogGroup?.kind==='show'?'Bölümleri gör':'Şimdi oynat'}</Button><Button className="feature-info-button" onPress={() => chooseMedia({ item: featured, origin: `feature-${catalogCardKey(featured)}` })}><Info size={18} /> Ayrıntılar</Button></div></div>
            <Glass className="feature-note"><HardDrive size={18} /><div><strong>Tam burada, evinde.</strong><span>{bytes(stats.data?.sizeBytes ?? 0)} · Kendi depolama alanında</span></div><span className="status-dot" /></Glass>
            <span className="feature-index">01 <span>/ {String(homeCatalog.data?.total??0).padStart(2, '0')}</span></span>
          </motion.section> : !homeCatalog.isPending && !homeCatalog.error && (session.role === 'admin' ? <EmptyLibrary active={activeJobs.length > 0} onAdd={() => setAddOpen(true)} onActivity={() => navigate('activity')} /> : <div className="search-empty"><Layers3 size={32} /><h2>Paylaşılan kütüphanende henüz medya yok.</h2><p>Yöneticin sana kütüphane erişimi verdiğinde içeriklerini burada göreceksin.</p></div>)}
          {homeCatalog.isPending && <div className="feature-skeleton home-skeleton" />}

          {!!continuing.data?.length && <section className="media-section continue-section"><SectionTitle title="Kaldığın yerden" subtitle="Hikâyen seni bekliyor." /><div className="continue-grid">{continuing.data.map(item => <button className="continue-card" key={item.id} onClick={() => play(item)}><div className="continue-art"><Artwork item={item} /><span className="continue-play"><Play size={19} fill="currentColor" /></span></div><div className="continue-info"><strong>{item.title}</strong><span>{duration(Math.max(0, (item.durationSeconds ?? 0) - item.positionSeconds))} kaldı</span><div className="progress-track"><span style={{ width: `${Math.min(100, item.positionSeconds / (item.durationSeconds || 1) * 100)}%` }} /></div></div></button>)}</div></section>}

          {(homeItems.length > 0 || homeCatalog.isPending) && <section className="media-section"><SectionTitle title="Yeni eklenenler" subtitle="Arşivinden son gelenler." action={<Button variant="ghost" className="section-link" onPress={() => navigate('library')}>Tümünü gör <ArrowRight size={16} /></Button>} /><div className="poster-grid">{homeCatalog.isPending ? Array.from({ length: 6 }, (_, i) => <div key={i} className="poster-skeleton home-skeleton" />) : homeItems.slice(0, 6).map(item => <MediaCard key={catalogCardKey(item)} item={item} origin={`home-${catalogCardKey(item)}`} onSelect={chooseMedia} />)}</div></section>}

          <div className="home-bottom"><div className="home-bottom-copy"><div className="small-home-symbol"><House size={22} /></div><div><h3>İyi ki evdesin.</h3><p>İzlediklerin, dinlediklerin ve kaldığın yer. Hepsi bir arada.</p></div></div><div className="home-bottom-stats"><span><Film size={16} /> {stats.data?.videos ?? 0} video</span><span><Music2 size={16} /> {stats.data?.audio ?? 0} ses</span><span><Folder size={16} /> {stats.data?.libraries ?? 0} klasör</span></div></div>
        </>}

        {view === 'library' && <>
          <div className="library-toolbar"><div className="search-box"><Search size={19} /><input ref={queryInput} aria-label="Kütüphanende ara" placeholder="Kütüphanende bir şey bul…" value={search} onChange={event => setSearch(event.target.value)} /><kbd>⌘ K</kbd>{search && <button aria-label="Aramayı temizle" onClick={() => setSearch('')}><X size={16} /></button>}</div><div className="view-switch" aria-label="Görünüm"><button className={layout === 'grid' ? 'active' : ''} aria-label="Kart görünümü" aria-pressed={layout === 'grid'} onClick={() => setLayout('grid')}><Grid2X2 size={18} /></button><button className={layout === 'list' ? 'active' : ''} aria-label="Liste görünümü" aria-pressed={layout === 'list'} onClick={() => setLayout('list')}><List size={20} /></button></div></div>
          <div className="filter-row"><div className="filter-pills">{[{ id: '', label: 'Tümü', icon: Layers3 }, { id: 'video', label: 'Videolar', icon: Film }, { id: 'audio', label: 'Müzik', icon: Music2 }].map(({ id, label, icon: Icon }) => <button key={id} onClick={() => setKind(id)} className={kind === id ? 'filter-pill selected' : 'filter-pill'} aria-pressed={kind === id}><Icon size={15} />{label}{id === '' && <span>{stats.data?.items ?? 0}</span>}</button>)}</div><label className="library-filter"><span className="sr-only">Kütüphane filtresi</span><select value={libraryFilter} onChange={e => setLibraryFilter(e.target.value)}><option value="">Tüm kütüphaneler</option>{libraries.data?.map(library => <option value={library.id} key={library.id}>{library.name}</option>)}</select></label></div>
          {catalog.isPending ? <div className="poster-grid">{Array.from({ length: 6 }, (_, i) => <div key={i} className="poster-skeleton home-skeleton" />)}</div> : items.length ? <>
            <div className={layout === 'grid' ? 'poster-grid library-grid' : 'media-list'}>{items.map(item => layout === 'grid' ? <MediaCard key={catalogCardKey(item)} item={item} origin={`library-${catalogCardKey(item)}`} onSelect={chooseMedia} /> : <button className="media-list-row" key={catalogCardKey(item)} data-home-focus={catalogCardKey(item)} onClick={() => chooseMedia({ item, origin: `list-${catalogCardKey(item)}` })}><div className="media-list-art"><Artwork item={item} /></div><div className="media-list-title"><strong>{catalogTitle(item)}</strong><span>{item.libraryName}{item.isDemo ? ' · Örnek içerik' : ''}</span></div><span>{item.catalogGroup?.kind==='show'?`${item.catalogGroup.episodeCount} bölüm`:duration(item.durationSeconds)}</span><span className="list-resolution">{resolution(item)}</span><span className="list-size">{item.catalogGroup?.kind==='show'?`${item.catalogGroup.seasonCount} sezon`:bytes(item.sizeBytes)}</span>{item.available ? <Play size={17} /> : <CircleAlert size={17} />}</button>)}</div>
            <div className="catalog-footer"><span>{items.length} / {total} içerik · {catalog.data?.pages[0]?.fileTotal??total} dosya</span>{catalog.hasNextPage && <Button className="secondary-button" onPress={() => catalog.fetchNextPage()} isDisabled={catalog.isFetchingNextPage}>Daha fazla göster</Button>}</div>
          </> : <div className="search-empty"><Search size={32} strokeWidth={1.3} /><h2>{search || kind || libraryFilter ? 'Henüz bir eşleşme yok.' : 'Kütüphanen içerik bekliyor.'}</h2><p>{search || kind || libraryFilter ? 'Başka bir arama veya filtre deneyebilirsin.' : session.role === 'admin' ? 'Medya klasörünü bağladığında içeriklerin burada görünecek.' : 'Yöneticinin paylaştığı medya burada görünecek.'}</p>{(search || kind || libraryFilter || session.role === 'admin') && <Button variant="ghost" onPress={() => { if (search || kind || libraryFilter) { setSearch(''); setKind(''); setLibraryFilter(''); } else setAddOpen(true); }}>{search || kind || libraryFilter ? 'Filtreleri temizle' : 'Kütüphane ekle'}<ArrowRight size={16} /></Button>}</div>}
        </>}

        {view === 'activity' && <ActivityView jobs={jobs.data ?? []} libraries={libraries.data ?? []} loading={jobs.isPending} notify={notify} onAdd={() => setAddOpen(true)} />}
        {view === 'downloads' && <DownloadsView downloads={downloads.data ?? []} loading={downloads.isPending} error={downloads.error} notify={notify} onAdd={() => setDownloadOpen(true)} onLibrary={id => { navigate('library'); setLibraryFilter(id); }} />}

        <footer className="app-footer"><span><span className="footer-mark">h.</span> Medyanın evi.</span><span>{isDemo && <span className="demo-indicator"><Sparkles size={12} /> Örnek içerik içerir</span>}<span>HOME {system.data?.version ?? '…'} · {session.preview ? 'ÖNİZLEME' : 'YEREL'}</span></span></footer>
        </div>
      </main>
    </div>

    {route.kind === 'game-play' && <Suspense fallback={<div className="video-player-loading" role="status"><LoaderCircle className="spin" /><p>Oyunun hazırlanıyor…</p></div>}><GamePlayer key={route.id + ':' + session.sessionId} id={route.id} onExit={() => navigation.back('/games/' + route.id)} /></Suspense>}

     <AddLibraryDialog open={addOpen} initialKind={view==='music'?'music':'movies'} onClose={() => setAddOpen(false)} system={system.data} onAdded={() => notify('Kütüphanen bağlandı. Home dosyalarını tarıyor.')} />
    <SettingsDialog open={settingsOpen} initialSection={settingsSection} onClose={() => setSettingsOpen(false)} session={session} system={system.data} libraries={libraries.data ?? []} onAdd={() => setAddOpen(true)} notify={notify} />
    <AddDownloadDialog open={downloadOpen} onClose={() => setDownloadOpen(false)} notify={notify} />
    <SourcesDialog open={sourcesOpen} onClose={() => setSourcesOpen(false)} notify={notify} />
    <ReleaseDialog target={releaseTarget} onClose={() => setReleaseTarget(null)} onSources={openSources} onDownload={() => { setReleaseTarget(null); navigate('downloads'); }} notify={notify} />
     {liveWatch&&<Suspense fallback={<div className="video-player-loading" role="status"><LoaderCircle className="spin" size={28}/><p>Kanal açılıyor…</p></div>}><LivePlayer selection={liveWatch} onClose={()=>setLiveWatch(null)}/></Suspense>}{podcastWatch&&<PodcastPlayer key={podcastWatch.id} watch={podcastWatch} onClose={()=>setPodcastWatch(null)}/>}
     {radioWatch&&<RadioPlayer key={radioWatch.key} station={radioWatch.station} onClose={()=>setRadioWatch(null)}/>}
    <Suspense fallback={<div className="video-player-loading" role="status"><LoaderCircle className="spin" size={28}/><p>Oynatman hazırlanıyor…</p></div>}><AnimatePresence>{playing && (playing.kind === 'audio' && !sharedMember && !libraries.data?.some(library=>library.id===playing.libraryId&&library.kind==='audiobooks') ? <MusicPlayer item={playing} queue={queue} index={queueIndex} playbackKey={playbackKey} shuffle={shuffle} repeat={repeat} roomController={roomController}
       onTrack={index => { setQueueIndex(index); setPlaying(queue[index]); persistQueue(queue, index); }} onSelect={index=>playQueue(queue,index)} onNext={() => advance(1)} onPrevious={() => advance(-1)} onEnded={() => advance(1, true)}
      onShuffle={() => { setShuffle(value => !value); persistQueue(queue, queueIndex, !shuffle); }} onRepeat={() => { const value = repeat === 'off' ? 'all' : repeat === 'all' ? 'one' : 'off'; setRepeat(value); persistQueue(queue, queueIndex, shuffle, value); }}
      onClose={closePlayer} /> : <Player item={playing} playbackKey={playbackKey} queueLength={queue.length} shuffle={shuffle} repeat={repeat} roomController={roomController} page={route.kind === 'watch' && route.id === playing.id}
        onMinimize={() => navigation.back(detailsPath(playing))} onExpand={() => navigation.visit(`/watch/${playing.id}`)} onNext={() => advance(1)} onPrevious={() => advance(-1)} onEnded={() => advance(1, true)}
      onShuffle={() => { setShuffle(value => !value); persistQueue(queue, queueIndex, !shuffle); }} onRepeat={() => { const value = repeat === 'off' ? 'all' : repeat === 'all' ? 'one' : 'off'; setRepeat(value); persistQueue(queue, queueIndex, shuffle, value); }}
      onClose={closePlayer} />)}</AnimatePresence></Suspense>
    <div className="toast-region" aria-live="polite"><AnimatePresence>{toast && <motion.div className="toast" key={toast} initial={{ opacity: 0, y: 16, scale: 0.96 }} animate={{ opacity: 1, y: 0, scale: 1 }} exit={{ opacity: 0, y: 8 }}><Check size={17} /><span>{toast}</span><button aria-label="Bildirimi kapat" onClick={() => setToast('')}><X size={15} /></button></motion.div>}</AnimatePresence></div>
  </LayoutGroup>;
}

function SectionTitle({ title, subtitle, action }: { title: string; subtitle: string; action?: React.ReactNode }) { return <div className="section-heading"><div><h2>{title}</h2><p>{subtitle}</p></div>{action}</div>; }
function SettingsIcon() { return <Layers3 size={16} />; }

function MediaCard({ item, origin, onSelect }: { item: MediaItem; origin: string; onSelect: (selection: Selection) => void }) {
  const series=item.catalogGroup?.kind==='show';
  return <motion.a href={appUrl(detailsPath(item))} data-home-focus={catalogCardKey(item)} className={`media-card ${!item.available ? 'media-card--unavailable' : ''}`} onClick={event => { if (event.button === 0 && !event.ctrlKey && !event.metaKey && !event.shiftKey && !event.altKey) { event.preventDefault(); event.currentTarget.focus({ preventScroll: true }); onSelect({ item, origin }); } }} whileHover={{ y: -5 }} whileTap={{ scale: 0.98 }} aria-label={`${catalogTitle(item)} ayrıntıları`}>
    <motion.div className="poster" layoutId={origin}>
      <Artwork item={item} /><div className="poster-shade" />
      <span className="poster-topline"><span className="quality-badge">{resolution(item)}</span>{!series&&item.isWatched && <span className="watched-badge" aria-label="İzlendi"><CheckCheck size={15} /></span>}</span>
      {item.isDemo && !item.content && <div className="demo-poster-title"><span>HOME ORIGINAL SAMPLE</span><strong>{item.title}</strong></div>}
      {!series&&item.content && item.content.versionCount > 1 && <span className="poster-versions"><Layers3 size={11} />{item.content.versionCount} sürüm</span>}
      <span className="poster-play">{series?<Layers3 size={23}/>:<Play size={23} fill="currentColor"/>}</span>
      {!item.available && <span className="unavailable-label"><CircleAlert size={13} /> Dosya erişilemiyor</span>}
      {!series&&item.positionSeconds > 0 && !item.isWatched && <div className="poster-progress"><span style={{ width: `${Math.min(100, item.positionSeconds / (item.durationSeconds || 1) * 100)}%` }} /></div>}
    </motion.div>
    <span className="media-card-title">{catalogTitle(item)}</span><span className="media-card-meta">{item.isDemo ? 'Örnek içerik' : item.libraryName}<span>·</span>{series?`${item.catalogGroup!.episodeCount} bölüm`:duration(item.durationSeconds)}</span>
  </motion.a>;
}

function EmptyLibrary({ active, onAdd, onActivity }: { active: boolean; onAdd: () => void; onActivity: () => void }) {
  return <section className="empty-library"><div className="empty-library-art"><div className="empty-disc" /><div className="empty-poster"><Film size={50} strokeWidth={1} /><span>YOUR NEXT<br />GOOD STORY.</span></div><div className="empty-folder"><Folder size={54} strokeWidth={1} /></div></div><div className="empty-library-copy"><p className="eyebrow">{active ? 'İLK HİKÂYELERİN GELİYOR' : 'BURASI SANA AYRILDI'}</p><h2>{active ? 'Evin hazırlanıyor.' : 'Bir klasörle başlar.'}</h2><p>{active ? 'Home medya dosyalarını bulup bilgilerini hazırlıyor. İlk içerikler birazdan burada görünecek.' : 'Filmlerini, dizilerini ve müziklerini bir araya getir. Sevdiğin şeylere daha güzel bir yer aç.'}</p><Button className="primary-button" onPress={active ? onActivity : onAdd}>{active ? <LoaderCircle size={17} className="spin" /> : <FolderPlus size={18} />}{active ? 'Taramayı takip et' : 'İlk kütüphaneni bağla'}<ArrowRight size={17} /></Button></div></section>;
}

function ActivityView({ jobs, libraries, loading, notify, onAdd }: { jobs: ScanJob[]; libraries: Library[]; loading: boolean; notify: (message: string) => void; onAdd: () => void }) {
  const client = useQueryClient();
  const action = useMutation({ mutationFn: ({ path }: { path: string }) => mutate(path), onSuccess: () => client.invalidateQueries({ queryKey: ['jobs'] }), onError: error => notify(error.message) });
  const statuses = { queued: 'Sırada', running: 'Taranıyor', completed: 'Tamamlandı', failed: 'İlgilenmen gerekiyor', cancelled: 'İptal edildi' };
  return <div className="activity-layout"><section className="activity-jobs">
    <div className="section-heading"><h2>İşlem geçmişi</h2><span className="count-label">{jobs.length} işlem</span></div>
    {loading && <div className="job-skeleton home-skeleton" />}
    {!jobs.length && !loading && <div className="activity-empty"><CheckCheck size={34} strokeWidth={1.2} /><h3>Şimdilik her şey sakin.</h3><p>Bir kütüphane eklediğinde tarama işlemlerini burada göreceksin.</p><Button variant="ghost" onPress={onAdd}>Kütüphane ekle <ArrowRight size={16} /></Button></div>}
    {jobs.map(job => <article className={`job-card job-card--${job.status}`} key={job.id}><div className="job-icon">{job.status === 'running' ? <RefreshCw className="spin-slow" size={20} /> : job.status === 'completed' ? <Check size={22} /> : job.status === 'failed' ? <CircleAlert size={20} /> : job.status === 'queued' ? <Clock3 size={20} /> : <X size={20} />}</div><div className="job-body"><div className="job-title"><h3>{job.libraryName}</h3><span className="job-status">{statuses[job.status]}</span></div><p>{job.message}</p><div className="job-meta"><span>{date(job.createdAt)}</span><span>{job.processed} dosya</span>{job.added > 0 && <span>{job.added} yeni içerik</span>}{job.attempt > 1 && <span>{job.attempt}. deneme</span>}</div>{job.status === 'running' && <div className="job-progress"><span /></div>}</div><div className="job-action">{['queued', 'running'].includes(job.status) ? <Button size="sm" variant="ghost" isDisabled={action.isPending || job.cancelRequested} onPress={() => action.mutate({ path: `/jobs/${job.id}/cancel` })}>{job.cancelRequested ? 'Durduruluyor' : 'Durdur'}</Button> : ['failed', 'cancelled'].includes(job.status) && <Button size="sm" variant="ghost" isDisabled={action.isPending} onPress={() => action.mutate({ path: `/libraries/${job.libraryId}/scan` })}><RefreshCw size={14} /> Yeniden dene</Button>}</div></article>)}
  </section><aside className="library-aside"><div className="aside-icon"><Server size={23} strokeWidth={1.5} /></div><h3>Bağlı kütüphaneler</h3><p>Home, içeriklerini burada buluyor.</p>{libraries.map(library => <div className="aside-library" key={library.id}><span>{library.kind === 'music' ? <Music2 size={18} /> : library.kind === 'shows' ? <Tv size={18} /> : <Film size={18} />}</span><div><strong>{library.name}</strong><small>{library.itemCount} içerik</small></div><Button size="sm" isIconOnly variant="ghost" aria-label={`${library.name} yeniden tara`} onPress={() => action.mutate({ path: `/libraries/${library.id}/scan` })} isDisabled={action.isPending}><RefreshCw size={15} /></Button></div>)}<Button className="aside-add" variant="ghost" onPress={onAdd}><Plus size={16} /> Kütüphane bağla<ChevronRight size={15} /></Button><div className="aside-note"><ShieldCheck size={15} /><span>Tarama, kaynak dosyalarını yalnızca okur.</span></div></aside></div>;
}
