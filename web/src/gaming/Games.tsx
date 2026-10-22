import { useRef, useState } from 'react';
import { Button } from '@heroui/react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ArrowDownToLine, ArrowLeft, ArrowRight, Check, CircleAlert, ExternalLink, Gamepad2, Heart, Keyboard, LoaderCircle, Monitor, Play, Plus, Search, ShieldCheck, X, Library, Globe, SlidersHorizontal } from 'lucide-react';
import { appUrl, bytes, date, mutate, request } from '../api';
import { translate as t, useUiLanguage } from '../i18n';
import { useController, useControllerCommands } from '../controller/react';
import { filterGames, hasGameCheckpoint, type Game, type GameFilter, type GameState } from './model';
import { useSession } from '../components/SessionContext';
import { NativeGameDetails, NativeRomImportDialog } from './NativeGames';
import {GamesSettingsButton,type GamesLibraryStatus} from './GamesSettings';
import {ConsoleArt,consoleStyle} from './ConsoleArt';
import './games.css';
import './library.css';
import {romGroups,systemName,type GameGrouping,type GameSorting} from './libraryModel';

const gamePath = (id: string) => '/games/' + id;
function description(game: Game) {
  switch (game.id) {
    case '2048': return t('Aynı sayıları birleştir, tahtayı yönet ve 2048’e ulaş. Gabriele Cirulli’nin açık kaynak tarayıcı klasiği.');
    case 'a-dark-room': return t('Ateşi yak. Bir odadan başlayan minimalist macera, keşif ve kaynak yönetimiyle büyüsün.');
    case 'hexgl': return t('Fütüristik bir pistte hızını koru ve zamana karşı yarış. WebGL kullanan klasik tarayıcı yarışı.');
    case 'hextris': return t('Altıgeni döndür, aynı renkleri bir araya getir ve yükselen tempoya ayak uydur.');
    case 'oh-hi': return t('Kırmızı ve mavi kareleri üç basit kurala göre yerleştir. Q42’nin açık kaynak klasik mantık oyunu.');
    case 'tiny-crate': return t('Kasaları taşı, bulmacaları çöz ve bir sonraki platforma ulaş. Tiny Crate’in klasik açık kaynak sürümü.');
    default: return t('Zıpla, ekranın diğer tarafından geri dön ve şekerleri topla. Kısa bölümler, tekrar oynanacak küçük bir meydan okuma.');
  }
}
function genre(id: string) { return id === 'puzzle' ? t('Bulmaca') : id === 'arcade' ? t('Arcade') : id === 'racing' ? t('Yarış') : id === 'adventure' ? t('Macera') : id === 'strategy' ? t('Strateji') : t('Platform'); }
function Artwork({ game, backdrop = false }: { game: Game; backdrop?: boolean }) { return game.kind === 'native'&&!game.hasArtwork ? <div className="native-rom-art" aria-hidden="true"><Gamepad2 size={54} strokeWidth={1.1} /><span>{systemName(game.system)}</span></div> : <img key={game.title+String(game.hasArtwork)} src={appUrl(`/api/games/${game.id}/artwork/${backdrop ? 'backdrop' : 'cover'}.png`)} alt={game.title} loading={backdrop ? 'eager' : 'lazy'} decoding="async" />; }
function useGamePreference(notify: (message: string) => void) {
  const client = useQueryClient();
  return useMutation({ mutationFn: ({ game, inLibrary = game.state.inLibrary, favorite = game.state.favorite }: { game: Game; inLibrary?: boolean; favorite?: boolean }) => mutate<GameState>(`/games/${game.id}/preference`, { inLibrary, favorite }, 'PUT'),
    onSuccess: () => client.invalidateQueries({ queryKey: ['games'] }), onError: error => notify(error.message) });
}
function InstallButton({ game, notify }: { game: Game; notify: (message: string) => void }) {
  const session = useSession(), client = useQueryClient(); const busy = ['queued', 'running'].includes(game.installStatus ?? '');
  const install = useMutation({ mutationFn: () => mutate(`/games/${game.id}/install`), onSuccess: () => client.invalidateQueries({ queryKey: ['games'] }), onError: error => notify(error.message) });
  const cancel = useMutation({ mutationFn: () => mutate(`/games/${game.id}/install/cancel`), onSuccess: () => client.invalidateQueries({ queryKey: ['games'] }), onError: error => notify(error.message) });
  return <div className="game-install-action"><Button className="play-button" isDisabled={busy || install.isPending || !session.canAcquire && session.role !== 'admin'} onPress={() => install.mutate()}>{busy || install.isPending ? <LoaderCircle size={17} className="spin" /> : <ArrowDownToLine size={17} />}{busy ? `${game.installProgress ?? 0}% · Kuruluyor…` : install.isPending ? 'Kurulum hazırlanıyor…' : game.installStatus === 'failed' ? 'Kurulumu yeniden dene' : 'İndir ve kur'}</Button>{busy && <Button variant="ghost" isDisabled={cancel.isPending} onPress={() => cancel.mutate()}>İptal</Button>}</div>;
}
function GameCard({ game, onOpen }: { game: Game; onOpen: (path: string) => void }) {
  return <a className={`game-card game-card--${game.id}`} href={appUrl(gamePath(game.id))} data-home-focus={game.id} onClick={event => {
    if (event.button || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
    event.preventDefault(); event.currentTarget.focus({ preventScroll: true }); onOpen(gamePath(game.id));
  }} aria-label={`${game.title} ayrıntıları`}>
    <div className="game-card-art"><Artwork game={game} /><span className="game-kind"><Monitor size={13} />{game.kind === 'native' ? 'ROM' : 'Web'}</span>{game.playable !== false && <span className="game-card-play"><Play size={23} fill="currentColor" /></span>}{game.state.favorite && <span className="game-card-favorite"><Heart size={15} fill="currentColor" /></span>}</div>
    <div className="game-card-copy"><strong>{game.title}</strong><span>{game.kind === 'native' ? systemName(game.system) : game.genres.map(genre).join(' · ')}</span><small>{game.kind !== 'native' && <><Gamepad2 size={13} /> Gamepad<span>·</span></>}{game.playable === false ? 'Yayın kurulumu bekleniyor' : game.installed === false ? ['queued', 'running'].includes(game.installStatus ?? '') ? 'Kuruluyor…' : 'Kurulu değil' : hasGameCheckpoint(game) ? 'Kaydın var' : game.state.inLibrary ? 'Kütüphanende' : 'Oynamaya hazır'}</small></div>
  </a>;
}
export function GamesLibrary({ active, onOpen, notify }: { active: boolean; onOpen: (path: string) => void; notify: (message: string) => void }) {
  useUiLanguage();
  const [search,setSearch]=useState(''),[section,setSection]=useState<'roms'|'browser'>('roms'),[favorites,setFavorites]=useState(false),[group,setGroup]=useState<GameGrouping>('system'),[sort,setSort]=useState<GameSorting>('recent'),[importOpen,setImportOpen]=useState(false);const session=useSession();
  const scope = useRef<HTMLElement>(null), searchInput = useRef<HTMLInputElement>(null); const controller = useController();
  const collection=useQuery({queryKey:['games','library-settings'],queryFn:()=>request<GamesLibraryStatus>('/games/library/settings'),enabled:active,staleTime:20_000,refetchInterval:query=>active&&(query.state.data?.active||query.state.data?.options.automaticMetadata&&query.state.data.waiting)?8000:false});
  useControllerCommands(scope, action => { if (!active || action !== 'search') return false; controller?.edit(searchInput.current); return true; });
  const catalog=useQuery({queryKey:['games'],queryFn:()=>request<{items:Game[];serverRunnerAvailable:boolean}>('/games'),enabled:active,staleTime:15_000,refetchInterval:query=>active&&query.state.data?.items.some(game=>['queued','running'].includes(game.installStatus??'')||['queued','running'].includes(game.artworkStatus??''))?2500:false});
  const games=catalog.data?.items??[],roms=games.filter(game=>game.kind==='native'&&game.state.inLibrary),browser=games.filter(game=>game.kind!=='native');
  const filtered=filterGames(section==='roms'?roms:browser,search,favorites?'favorites':'all');const groups=romGroups(filtered,section==='roms'?group:'none',sort);
  const hero=roms.filter(game=>game.playable).sort((a,b)=>(b.state.lastPlayedAt??b.importedAt??'').localeCompare(a.state.lastPlayedAt??a.importedAt??''))[0];
  return <section ref={scope} className="games-page rom-library" data-home-position-scope={`games:${section}:${group}:${favorites}:${search}`}>
    <div className="page-heading games-heading"><div><p className="eyebrow">HOME · GAMES</p><h1>Oyunların.</h1><p className="page-subtitle">ROM’larını ekle, kütüphaneni düzenle ve kaldığın yerden devam et.</p></div><div className="games-heading-actions"><GamesSettingsButton/>{(session.role==='admin'||session.canAcquire)&&<Button className="play-button" onPress={()=>setImportOpen(true)}><Plus size={17}/> ROM ekle</Button>}</div></div>
    {!!collection.data?.active&&<p className="games-collection-status" role="status"><LoaderCircle className="spin" size={14}/>{t('Koleksiyon bilgileri ve kapakları arka planda tamamlanıyor.')} <span>{collection.data.complete}/{collection.data.total}</span></p>}
    <div className="rom-library-tabs" role="tablist" aria-label="Oyun bölümü"><button role="tab" aria-selected={section==='roms'} className={section==='roms'?'selected':''} onClick={()=>{setSection('roms');setSearch('');setFavorites(false);}}><Library size={17}/> ROM kütüphanem <span>{roms.length}</span></button><button role="tab" aria-selected={section==='browser'} className={section==='browser'?'selected':''} onClick={()=>{setSection('browser');setSearch('');setFavorites(false);}}><Globe size={17}/> Ücretsiz oyunlar</button></div>
    {catalog.isPending && <div className="games-hero-skeleton home-skeleton" />}
    {catalog.error && <div className="inline-error" role="alert"><CircleAlert size={18} /><span>{catalog.error.message}</span><Button variant="ghost" onPress={() => catalog.refetch()}>Yeniden dene</Button></div>}
    {section==='roms'&&hero&&!search&&!favorites&&<section className="rom-feature"><div className="rom-feature-background"><Artwork game={hero} backdrop/></div><div className="rom-feature-cover"><Artwork game={hero}/></div><div className="rom-feature-copy"><p className="eyebrow">{hasGameCheckpoint(hero)?'KALDIĞIN YERDEN':'KÜTÜPHANENDEN'}</p><h2>{hero.title}</h2><span>{systemName(hero.system)}</span><div className="game-detail-actions"><Button className="play-button" onPress={()=>onOpen(gamePath(hero.id)+'/play')}><Play size={17} fill="currentColor"/>{hasGameCheckpoint(hero)?'Devam et':'Oyna'}</Button><Button variant="ghost" onPress={()=>onOpen(gamePath(hero.id))}>Ayrıntılar <ArrowRight size={16}/></Button></div></div></section>}
    {section==='browser'&&<div className="free-games-intro"><h2>Kısa bir mola.</h2><p>ROM’un olmasa da oynayabileceğin açık kaynak tarayıcı oyunları. İstediğini seçip kur; ROM kütüphanenden ayrı kalır.</p></div>}
    <div className="games-toolbar rom-controls"><div className="search-box"><Search size={18}/><input ref={searchInput} value={search} onChange={event=>setSearch(event.target.value)} aria-label="Oyunlarda ara" placeholder="Kütüphanede ara…" data-controller-search/>{search&&<button aria-label="Aramayı temizle" onClick={()=>setSearch('')}><X size={16}/></button>}</div><div className="rom-control-selects"><button className={favorites?'rom-favorite-filter selected':'rom-favorite-filter'} aria-pressed={favorites} onClick={()=>setFavorites(value=>!value)}><Heart size={16}/> Favoriler</button>{section==='roms'&&<label><SlidersHorizontal size={15}/><select aria-label="Oyunları grupla" value={group} onChange={event=>setGroup(event.target.value as GameGrouping)}><option value="system">Cihaza göre</option><option value="category">Kategoriye göre</option><option value="alphabet">A–Z grupları</option><option value="none">Gruplama yok</option></select></label>}<select aria-label="Oyun sıralaması" value={sort} onChange={event=>setSort(event.target.value as GameSorting)}><option value="recent">Son kullanılan</option><option value="title">Ada göre</option><option value="played">Oynama süresi</option></select></div></div>
    {groups.map(item=><section key={item.id} className={section==='roms'&&group==='system'?'rom-shelf console-shelf':'rom-shelf'} style={group==='system'?consoleStyle(item.games[0]?.system):undefined}>
      {section==='roms'&&group==='system'?<header className="console-shelf-header"><div className="console-shelf-identity"><p className="eyebrow">{t('KONSOL KOLEKSİYONU')}</p><h2>{item.label}</h2><span>{t('{{v0}} oyun',{v0:item.games.length})}</span></div><ConsoleArt system={item.games[0]?.system}/></header>:section==='roms'&&group!=='none'?<div className="section-heading"><h2>{item.label||t('Kategorisiz')}</h2><span>{item.games.length}</span></div>:null}
      <div className="game-grid">{item.games.map(game=><GameCard key={game.id} game={game} onOpen={onOpen}/>)}</div></section>)}
    {!catalog.isPending&&!catalog.error&&!filtered.length&&<div className="rom-empty"><Library size={42} strokeWidth={1.2}/><h2>{roms.length||section==='browser'?'Bir oyun bulunamadı.':'Kütüphanen seni bekliyor.'}</h2><p>{roms.length||section==='browser'?'Aramayı veya filtreleri değiştir.':'Elindeki oyun ROM’unu ekle. Home dosyanın bir kopyasını saklar; kayıtların bu profilde kalır.'}</p>{section==='roms'&&<Button className="play-button" onPress={()=>setImportOpen(true)}><Plus size={16}/> ROM ekle</Button>}<Button variant="ghost" onPress={()=>{setSection('browser');setSearch('');setFavorites(false);}}>Ücretsiz oyunlara göz at <ArrowRight size={16}/></Button></div>}
    <NativeRomImportDialog open={importOpen} onClose={()=>setImportOpen(false)} onAdded={id=>{setSection('roms');notify(t('ROM kütüphanene eklendi.'));onOpen(gamePath(id));}}/>
  </section>;
}
export function GameDetails({ id, onBack, onOpen, notify }: { id: string; onBack: () => void; onOpen: (path: string) => void; notify: (message: string) => void }) {
  useUiLanguage(); const gameQuery = useQuery({ queryKey: ['games', id], queryFn: () => request<Game>(`/games/${id}`), refetchInterval: query => ['queued', 'running'].includes(query.state.data?.installStatus ?? '') ? 2000 : false }); const preference = useGamePreference(notify);
  const game = gameQuery.data;
  if (gameQuery.isPending) return <div className="connection-screen"><LoaderCircle className="spin" /><p>Oyunun hazırlanıyor…</p></div>;
  if (!game) return <div className="search-empty"><CircleAlert size={30} /><h1>Oyun açılamadı.</h1><p className="form-error">{gameQuery.error?.message}</p><Button onPress={onBack}>Oyunlara dön</Button></div>;
  if (game.kind === 'native') return <NativeGameDetails game={game} onBack={onBack} onOpen={onOpen} />;
  return <article className="rom-game-page browser-game-page games-surface" data-home-position-scope={`game:${id}`} style={consoleStyle('browser')}>
    <div className="rom-game-topbar"><Button variant="ghost" onPress={onBack}><ArrowLeft size={17} /> Oyunlara dön</Button><span>Web</span></div>
    <section className="rom-game-hero"><div className="rom-game-atmosphere" aria-hidden="true"><Artwork game={game} backdrop /></div><div className="rom-game-identity"><div className="rom-game-cover"><Artwork game={game}/></div><div className="rom-game-copy"><p className="eyebrow">TARAYICI OYUNU</p><h1>{game.title}</h1><p className="browser-game-description">{description(game)}</p><div className="rom-game-facts"><span>{game.publisher}</span><span>{game.genres.map(genre).join(' · ')}</span></div>
      <div className="rom-game-actions">{game.installed === false ? <InstallButton game={game} notify={notify} /> : <Button className="play-button" data-home-focus={`play-${game.id}`} onPress={() => onOpen(gamePath(id) + '/play')}><Play size={17} fill="currentColor" />{hasGameCheckpoint(game) ? 'Devam et' : 'Oyna'}</Button>}
        {game.installed !== false && <Button className="secondary-button" isDisabled={preference.isPending} onPress={() => preference.mutate({ game, inLibrary: !game.state.inLibrary })}>{game.state.inLibrary ? <Check size={17} /> : <Plus size={17} />}{game.state.inLibrary ? 'Kütüphanemde' : 'Kütüphaneme ekle'}</Button>}
        <Button isIconOnly className={game.state.favorite ? 'secondary-button game-heart active' : 'secondary-button game-heart'} aria-label={game.state.favorite ? 'Favorilerimden çıkar' : 'Favorilerime ekle'} aria-pressed={game.state.favorite} isDisabled={preference.isPending} onPress={() => preference.mutate({ game, favorite: !game.state.favorite })}><Heart size={18} fill={game.state.favorite ? 'currentColor' : 'none'} /></Button>
       </div><p className="games-inline-status"><Monitor size={15} /> Bu oyun bu cihazın tarayıcısında çalışır; GBA ROM’ları sunucuda çalıştırılır.</p></div></div></section>
    {game.installStatus === 'failed' && <p className="form-error" role="alert">{game.installMessage}</p>}
    <div className="browser-game-information"><section className="rom-game-info-card"><h2>Nasıl oynanır?</h2><p>{game.runtime === 'html' ? game.id === 'hexgl' ? 'Sol analog ile yön ver, A / ✕ ile hızlan. Klavyede yön tuşlarını kullanabilirsin.' : ['oh-hi', 'a-dark-room'].includes(id) ? 'D-pad veya sol analog ile oyun içi kontrolleri seç, A / ✕ ile onayla. Fare ve klavye de kullanılabilir.' : 'D-pad veya sol analog ile yön ver. Klavyede yön tuşları, gamepad’de A / ✕ onay için kullanılır.' : id === 'tiny-crate' ? 'Yön tuşları veya sol analog ile hareket et. A / ✕ ile zıpla, X / □ ile kasayı al veya at.' : 'Yön tuşları veya sol analog ile hareket et. A / ✕ ile zıpla; ekranın kenarından diğer tarafa geçerek şekerleri topla.'}</p><div className="game-control-badges"><span><Gamepad2 size={16} /> Gamepad</span><span><Keyboard size={16} /> Klavye</span></div><p className="muted">Home oyun menüsü: Start + Select veya Esc. Dokunmatik kontrol desteği henüz doğrulanmadı.</p></section>
      <section className="rom-game-info-card"><h2>Kayıt ve uyumluluk</h2><p>{game.runtime === 'html' ? 'Oyunun sakladığı tarayıcı ilerlemesi Home profiline eşitlenir. Bu bir emülatör anlık kaydı değildir.' : id === 'tiny-crate' ? 'Tamamlanan bölümler ve oyun içi kayıt yuvaları bu profilde saklanır. Bölümün ortasındaki anlık durum kaydedilmez.' : 'Bulunduğun bölüm bu profilde saklanır. Devam ettiğinde bölüm yeniden başlar; bu bir emülatör anlık kaydı değildir.'}</p><p className="muted">Gamepad için güvenilir HTTPS veya localhost bağlantısı kullan. HexGL ayrıca WebGL gerektirir; gerçek TV performansı cihazına bağlıdır.</p>{game.state.lastPlayedAt && <small>{date(game.state.lastPlayedAt)} · Son oynama</small>}</section>
      <section className="rom-game-info-card"><h2>Oyun hakkında</h2><dl><div><dt>Oyun motoru</dt><dd>{game.engine}</dd></div><div><dt>Paket</dt><dd>{bytes(game.sizeBytes)}</dd></div><div><dt>Lisans</dt><dd>{game.license}</dd></div><div><dt>Sınıflandırma</dt><dd>Yaş sınıflandırması bulunmuyor.</dd></div></dl><p className="muted">Home için uyarlanmış açık kaynak tarayıcı paketi. Geliştiricinin yeni veya mağaza sürümüyle aynı paket değildir.</p><a href={game.sourceUrl} target="_blank" rel="noopener noreferrer">Geliştiricinin sayfası <ExternalLink size={13} /></a></section>
    </div>
  </article>;
}
