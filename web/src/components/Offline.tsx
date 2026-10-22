import { useEffect, useRef, useState, useSyncExternalStore } from 'react';
import { Button, Modal } from '@heroui/react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ArrowDownToLine, Film, HardDrive, LockKeyhole, Music2, Pause, Play, RefreshCw, Trash2, UnlockKeyhole, WifiOff } from 'lucide-react';
import { appUrl, bytes, clock, date, mutate, request, type MediaItem, type MediaTracks } from '../api';
import { offlineManager, type OfflineManager } from '../offline/manager';
import type { OfflinePolicy, OfflineView } from '../offline/types';
import { pushRegistration, pushWorkerMessage } from '../pushBridge';
import { Dialog } from './Dialogs';
import { useSession } from './SessionContext';
import { useControllerCommands } from '../controller/react';
import { controllerPlayback } from '../controller/playback';

const supported = () => window.isSecureContext && !!crypto.subtle && 'indexedDB' in window && 'serviceWorker' in navigator;
function useOffline(manager: OfflineManager) { return useSyncExternalStore(manager.subscribe, manager.snapshot); }
function OfflineWorkerStatus() {
  const [waiting, setWaiting] = useState<ServiceWorker>(); const [error, setError] = useState('');
  useEffect(() => {
    let active = true;
    void (async () => {
      const registration = await pushRegistration(); if (!active) return; setWaiting(registration?.waiting ?? undefined);
      if (!registration?.active) throw new Error('Cihaz oynatıcısı için Home’u üretim HTTPS adresinden aç.');
      const response = await pushWorkerMessage<{ version: number }>(registration.active, { type: 'home.offline.ping' });
      if (response.version !== 1) throw new Error('Cihaz oynatıcısı güncellenmeli.');
    })().catch(error => { if (active) setError(error.message); });
    return () => { active = false; };
  }, []);
  return <>{error && <p className="field-hint">{error}</p>}{waiting && <Button variant="ghost" size="sm" onPress={() => { navigator.serviceWorker.addEventListener('controllerchange', () => location.reload(), { once: true }); waiting.postMessage({ type: 'home.activate' }); }}>İstemciyi güncelle ve aç</Button>}</>;
}
export function OfflineLibrary({ standalone = false, onOnline }: { standalone?: boolean; onOnline?: () => void }) {
  if (!supported()) return <section className="offline-library"><h1>Cihaz arşivi</h1><p>Şifreli cihaz kopyaları için HTTPS, Web Crypto, IndexedDB ve service worker desteği gerekiyor.</p>{onOnline && <Button onPress={onOnline}>Sunucuya dön</Button>}</section>;
  return <OfflineLibraryBody manager={offlineManager()} standalone={standalone} onOnline={onOnline} />;
}
function OfflineLibraryBody({ manager, standalone, onOnline }: { manager: OfflineManager; standalone: boolean; onOnline?: () => void }) {
  const state = useOffline(manager); const [selected, setSelected] = useState(''); const [password, setPassword] = useState(''); const [confirmation, setConfirmation] = useState(''); const [create, setCreate] = useState(false);
  const [playing, setPlaying] = useState<string | null>(null); const [quota, setQuota] = useState('8'); const [persisted, setPersisted] = useState<boolean | null>(null);
  const action = useMutation({ mutationFn: (run: () => Promise<unknown>) => run() });
  useEffect(() => { void manager.initialize().catch(() => {}); void navigator.storage?.persisted?.().then(setPersisted); }, [manager]);
  useEffect(() => { setQuota(String(state.usage.quota / 1024 ** 3)); }, [state.usage.quota]);
  const current = state.items.find(item => item.row.id === playing); const vault = selected || state.vaults.find(vault => vault.userId === state.device?.userId && vault.deviceId === state.device?.deviceId)?.id || state.vaults[0]?.id || '';
  const labels: Record<string, string> = { queued: 'Sırada', preparing: 'Sunucuda hazırlanıyor', downloading: 'Cihaza indiriliyor', paused: 'Duraklatıldı', ready: 'Çevrimdışı hazır', failed: 'İlgilenmen gerekiyor', revoked: 'Erişim kaldırıldı' };
  function unlock() { const operation = create || !state.vaults.length ? () => manager.create(password) : () => manager.unlock(vault, password); action.mutate(async () => { await operation(); setPassword(''); setConfirmation(''); }); }
  return <section className={`offline-library ${standalone ? 'offline-library--standalone' : ''}`}>
    <header className="offline-heading"><div><p className="eyebrow">BU CİHAZDA</p><h1>Yanında taşı.</h1><p className="muted">{state.online === false ? 'Sunucuya ulaşılamıyor; şifreli cihaz kopyaların kullanılabilir.' : 'Medyanı cihazına al, bağlantı olmasa da devam et.'}</p></div><div className="content-actions">{onOnline && <Button variant="ghost" onPress={onOnline}><RefreshCw size={16} /> Sunucuya dön</Button>}{state.unlocked && <Button variant="ghost" onPress={() => action.mutate(() => manager.lock())}><LockKeyhole size={16} /> Arşivi kilitle</Button>}</div></header>
    <OfflineWorkerStatus />
    <div className="offline-storage"><HardDrive size={20} /><div><strong>{bytes(state.usage.stored)} cihazda</strong><span>{bytes(state.usage.reserved)} ayrıldı · kota {bytes(state.usage.quota)}</span></div><span>{persisted ? 'Kalıcı depolama izni var' : 'Tarayıcı depolaması'}</span></div>
    {!state.unlocked ? <div className="home-form offline-unlock">
      <h3><LockKeyhole size={18} /> {create || !state.vaults.length ? 'Şifreli cihaz arşivi oluştur' : 'Cihaz arşivini aç'}</h3>
      {state.vaults.length > 0 && <label className="field"><span>Arşiv</span><select value={create ? 'new' : vault} onChange={event => { setCreate(event.target.value === 'new'); if (event.target.value !== 'new') setSelected(event.target.value); }}>
        {state.vaults.map(vault => <option key={vault.id} value={vault.id}>{vault.label}</option>)}{state.device?.policy.enabled && <option value="new">Bu hesap için yeni arşiv</option>}
      </select></label>}
      {!state.vaults.length && !state.device && <p className="field-hint">İlk arşivi oluşturmak için Home sunucuna bağlanıp giriş yap.</p>}
      <label className="field"><span>Cihaz arşivi parolası</span><input type="password" autoComplete={create || !state.vaults.length ? 'new-password' : 'current-password'} minLength={8} maxLength={256} value={password} onChange={event => setPassword(event.target.value)} /></label>
      {(create || !state.vaults.length) && <><label className="field"><span>Parolayı tekrar yaz</span><input type="password" autoComplete="new-password" value={confirmation} onChange={event => setConfirmation(event.target.value)} /></label><p className="field-hint">Bu parola cihaz kopyalarını şifreler; Home sunucusuna gönderilmez. Unutursan cihaz arşivini kaldırıp kopyaları yeniden indirmen gerekir.</p></>}
      <Button className="secondary-button" onPress={unlock} isDisabled={action.isPending || state.busy || password.length < 8 || (create || !state.vaults.length) && (password !== confirmation || !state.device?.policy.enabled)}><UnlockKeyhole size={16} />{create || !state.vaults.length ? 'Arşivi oluştur' : 'Kilidi aç'}</Button>
      {vault && !create && <Button variant="ghost" size="sm" onPress={() => action.mutate(() => manager.forget(vault))} isDisabled={action.isPending}><Trash2 size={14} /> Seçili arşivi ve cihaz kopyalarını kaldır</Button>}
    </div> : <>
      <div className="section-heading"><h3>{state.vault?.label} · Cihaz kopyaları</h3><Button variant="ghost" size="sm" onPress={() => action.mutate(() => manager.maintain())} isDisabled={action.isPending}><RefreshCw size={15} /> Bağlantı ve konumları eşitle</Button></div>
      {!state.items.length && <div className="search-empty"><WifiOff size={30} /><h3>Henüz cihaz kopyası yok.</h3><p>Kütüphanede bir dosyanın ayrıntılarını açıp “Cihazıma indir” seçeneğini kullan.</p></div>}
      <div className="offline-grid">{state.items.map(item => {
        const total = item.metadata.manifest?.assets.reduce((sum, value) => sum + value.chunks, 0) ?? 0; const expired = item.metadata.manifest && Date.parse(item.metadata.manifest.expiresAt) <= Date.now();
        return <article className="offline-item" key={item.row.id}><div className="offline-item-icon">{item.metadata.media.kind === 'audio' ? <Music2 size={30} /> : <Film size={30} />}</div><div className="offline-item-body"><strong>{item.metadata.media.title}</strong><span>{labels[item.row.state]}{expired ? ' · Erişim süresi yenilenmeli' : ''}</span><small>{item.row.message}</small>
          {item.row.state !== 'ready' && <progress max={total || 1} value={item.row.nextChunk} aria-label="Cihaza aktarım ilerlemesi" />}
          <small>{bytes(item.row.storedBytes)} · {item.metadata.input.profile === 'original' ? 'Özgün dosya' : item.metadata.input.profile === 'compatible-1080' ? 'Uyumlu 1080p' : 'Uyumlu 720p / AAC'}{item.metadata.progress.pending ? ' · Konum eşitleme bekliyor' : ''}</small>
          <div className="content-actions">{item.row.state === 'ready' ? <Button className="secondary-button" size="sm" onPress={() => setPlaying(item.row.id)} isDisabled={!!expired}><Play size={14} /> Cihazdan oynat</Button> : ['queued', 'preparing', 'downloading'].includes(item.row.state) ? <Button variant="ghost" size="sm" onPress={() => action.mutate(() => manager.pause(item.row.id))} isDisabled={action.isPending}><Pause size={14} /> Duraklat</Button> : <Button variant="ghost" size="sm" onPress={() => action.mutate(() => manager.resume(item.row.id))} isDisabled={action.isPending || !state.online}><ArrowDownToLine size={14} /> Devam et</Button>}
            <Button variant="ghost" size="sm" isIconOnly aria-label={`${item.metadata.media.title} cihaz kopyasını kaldır`} onPress={() => action.mutate(() => manager.remove(item.row.id))} isDisabled={action.isPending}><Trash2 size={14} /></Button></div>
          {item.metadata.progress.conflict && <div className="offline-conflict"><p>Konum başka bir cihazda değişti. Bu cihaz: {clock(item.metadata.progress.position)} · Sunucu: {clock(item.metadata.progress.conflict.positionSeconds)}</p><Button variant="ghost" size="sm" onPress={() => action.mutate(() => manager.resolve(item.row.id, 'local'))}>Cihazdakini kullan</Button><Button variant="ghost" size="sm" onPress={() => action.mutate(() => manager.resolve(item.row.id, 'server'))}>Sunucudakini tut</Button></div>}
        </div></article>;
      })}</div>
    </>}
    <details className="advanced-fields offline-preferences"><summary>Cihaz alanı ve saklama</summary><div className="home-form"><label className="field"><span>Bu tarayıcı/PWA için kota · GiB</span><input type="number" min={.25} max={64} step={.25} value={quota} onChange={event => setQuota(event.target.value)} /></label><Button variant="ghost" onPress={() => action.mutate(async () => { await manager.store.setQuota(Math.floor(Number(quota) * 1024 ** 3)); await manager.refresh(); })}>Kotayı kaydet</Button>
      {navigator.storage?.persist && !persisted && <Button variant="ghost" onPress={() => action.mutate(async () => { setPersisted(await navigator.storage.persist()); })}>Kalıcı depolama izni iste</Button>}
      <p className="field-hint">İndirme Home açıkken sürer; kapatıp açınca tamamlanan parçalar korunur. Tarayıcı depolamasını temizlemek cihaz kopyalarını kaldırır. Çevrimdışı erişim, sunucunun verdiği süre içinde geçerlidir.</p>
    </div></details>
    {(action.error || state.message) && <p className={action.error ? 'form-error' : 'field-hint'} role="status">{action.error?.message ?? state.message}</p>}
    {state.unlocked && state.vault && current?.metadata.manifest && <OfflinePlayer key={current.row.id} item={current} vaultId={state.vault.id} manager={manager} onClose={() => setPlaying(null)} />}
  </section>;
}
function OfflinePlayer({ item, vaultId, manager, onClose }: { item: OfflineView; vaultId: string; manager: OfflineManager; onClose: () => void }) {
  const media = useRef<HTMLVideoElement & HTMLAudioElement>(null); const last = useRef(0); const loaded = useRef(false); const [error, setError] = useState(''); const manifest = item.metadata.manifest!;
  const url = (asset: string) => appUrl(`/api/offline/local/${vaultId}/${item.row.id}/${asset}`);
  const save = () => { if (loaded.current && media.current) { last.current = Date.now(); return manager.saveProgress(item.row.id, media.current.currentTime).catch(error => setError(error.message)); } return Promise.resolve(); };
  const metadata = () => { if (!loaded.current && media.current) { loaded.current = true; media.current.currentTime = Math.min(item.metadata.progress.position, Math.max(0, media.current.duration - .05)); void media.current.play().catch(() => {}); } };
  const changed = () => { if (Date.now() - last.current > 5000) void save(); };
  const close = () => { void save().finally(() => { void manager.sync().catch(() => {}); onClose(); }); };
  const panel = useRef<HTMLDivElement>(null);
  useControllerCommands(panel, action => { if (action === 'confirm' && document.activeElement === media.current) action = 'playpause'; return controllerPlayback(action, { read: () => ({ position: media.current?.currentTime ?? 0, duration: media.current?.duration ?? 0, ready: loaded.current, pending: false }), play: () => media.current?.play() ?? Promise.resolve(), pause: () => media.current?.pause(), seek: seconds => { if (media.current) media.current.currentTime = seconds; } }, media.current?.paused ?? true, true, () => setError('Tarayıcı oynatma için gerçek bir kumanda veya klavye etkileşimi isteyebilir.')); });
  return <Dialog open onClose={close} title={manifest.title} className="detail-container"><div ref={panel} className="dialog-content"><p className="eyebrow">CİHAZINDAN · ÇEVRİMDIŞI KOPYA</p><Modal.Heading className="dialog-title">{manifest.title}</Modal.Heading>
    {manifest.kind === 'audio' ? <audio ref={media} controls tabIndex={0} data-controller-default src={url('media')} onLoadedMetadata={metadata} onTimeUpdate={changed} onPause={() => { void save(); }} onEnded={() => { void save(); }} onError={() => setError('Cihaz kopyası açılamadı. Arşiv kilidini ve parça bütünlüğünü kontrol et.')} />
      : <video ref={media} className="offline-video" controls playsInline tabIndex={0} data-controller-default src={url('media')} onLoadedMetadata={metadata} onTimeUpdate={changed} onPause={() => { void save(); }} onEnded={() => { void save(); }} onError={() => setError('Cihaz kopyası açılamadı. Kopyayı yeniden indirmen veya uyumlu biçim seçmen gerekebilir.')}>
        {manifest.assets.filter(asset => asset.id === 'subtitle').map(asset => <track key={asset.id} kind="subtitles" src={url(asset.id)} srcLang={asset.language ?? 'und'} label={asset.title ?? 'Altyazı'} default />)}
      </video>}
    <p className="field-hint">İzleme konumu bu cihazda saklanır. Bağlantı geldiğinde kendi hesabınla eşitlenir. Erişim: {date(manifest.expiresAt)}</p>{error && <p className="form-error">{error}</p>}
  </div></Dialog>;
}
export function OfflineDownloadAction({ item, onOpen }: { item: MediaItem; onOpen: () => void }) {
  if (!supported()) return <p className="field-hint">Cihaza indirme HTTPS/PWA desteğiyle kullanılabilir.</p>;
  return <DownloadActionBody item={item} onOpen={onOpen} manager={offlineManager()} />;
}
function DownloadActionBody({ item, onOpen, manager }: { item: MediaItem; onOpen: () => void; manager: OfflineManager }) {
  const session = useSession(); const state = useOffline(manager); const [open, setOpen] = useState(false); const [profile, setProfile] = useState<'original' | 'compatible-720' | 'compatible-1080'>(() => document.createElement(item.kind === 'audio' ? 'audio' : 'video').canPlayType(item.mimeType) && (item.kind === 'audio' || item.videoCodec === 'h264') ? 'original' : 'compatible-720');
  const [audio, setAudio] = useState(''); const [subtitle, setSubtitle] = useState(''); const [burn, setBurn] = useState(false);
  const tracks = useQuery({ queryKey: ['media-tracks', item.id], queryFn: () => request<MediaTracks>(`/media/${item.id}/tracks`), enabled: open });
  const queue = useMutation({ mutationFn: () => manager.queue(item, { profile, audioStream: profile === 'original' || !audio ? null : Number(audio), subtitleId: subtitle || null, burnSubtitle: profile !== 'original' && burn }), onSuccess: () => setOpen(false) });
  const existing = state.items.find(value => value.metadata.media.id === item.id);
  return <section className="offline-download-action"><Button variant="ghost" onPress={() => { if (existing || !state.unlocked || state.vault?.userId !== session.userId) onOpen(); else setOpen(value => !value); }} isDisabled={!item.available}><ArrowDownToLine size={16} />{existing ? 'Cihaz kopyasını aç' : 'Cihazıma indir'}</Button>
    {open && <div className="home-form automation-form"><label className="field"><span>Cihaz kopyası</span><select value={profile} onChange={event => { setProfile(event.target.value as typeof profile); setAudio(''); setSubtitle(''); setBurn(false); }}><option value="original">Özgün dosya · {bytes(item.sizeBytes)}</option><option value="compatible-720">{item.kind === 'audio' ? 'Uyumlu AAC stereo' : 'Uyumlu MP4 · en fazla 720p'}</option>{item.kind === 'video' && <option value="compatible-1080">Uyumlu MP4 · en fazla 1080p</option>}</select></label>
      {profile !== 'original' && <label className="field"><span>Ses izi</span><select value={audio} onChange={event => setAudio(event.target.value)}><option value="">Varsayılan ses</option>{tracks.data?.streams.filter(stream => stream.kind === 'audio').map(stream => <option key={stream.index} value={stream.index}>{stream.language} · {stream.title || stream.codec}</option>)}</select></label>}
      {item.kind === 'video' && <><label className="field"><span>Çevrimdışı altyazı</span><select value={subtitle} onChange={event => setSubtitle(event.target.value)}><option value="">Altyazısız</option>{tracks.data?.subtitles.map(track => <option key={track.id} value={track.id} disabled={!track.playable && (profile === 'original' || !track.burnable)}>{track.language} · {track.title || track.format}{track.image ? ' · Görüntüye işlenir' : ''}</option>)}</select></label>{profile !== 'original' && subtitle && <label className="check-field"><input type="checkbox" checked={burn} onChange={event => setBurn(event.target.checked)} /> Metin altyazısı stilini de görüntüye işle</label>}</>}
      <p className="field-hint">Özgün dosyanın codec'i cihazında desteklenmeli. Uyumlu kopya sunucuda hazırlanır. İndirme, açık cihaz arşivine şifrelenerek yazılır.</p><Button className="secondary-button" onPress={() => queue.mutate()} isDisabled={queue.isPending}>Cihaza indirmeyi başlat</Button>{(queue.error || tracks.error) && <p className="form-error">{queue.error?.message || tracks.error?.message}</p>}
    </div>}
  </section>;
}
export function OfflinePolicyEditor({ userId }: { userId: string }) {
  const settings = useQuery({ queryKey: ['offline-policy', userId], queryFn: () => request<OfflinePolicy>(`/users/${userId}/offline-policy`) });
  return <>{settings.data && <OfflinePolicyForm key={settings.data.revision} userId={userId} initial={settings.data} />}{settings.error && <p className="form-error">{settings.error.message}</p>}</>;
}
function OfflinePolicyForm({ userId, initial }: { userId: string; initial: OfflinePolicy }) {
  const [value, setValue] = useState(initial); const client = useQueryClient();
  const save = useMutation({ mutationFn: () => mutate(`/users/${userId}/offline-policy`, value, 'PUT'), onSuccess: () => client.invalidateQueries({ queryKey: ['offline-policy', userId] }) });
  return <form className="home-form source-form" onSubmit={event => { event.preventDefault(); save.mutate(); }}><h3>Çevrimdışı cihaz kopyaları</h3><label className="check-field"><input type="checkbox" checked={value.enabled} onChange={event => setValue(current => ({ ...current, enabled: event.target.checked }))} /> Bu hesap cihazına medya indirebilsin</label>
    <div className="form-columns"><label className="field"><span>Dosya sınırı</span><input type="number" min={1} max={100} value={value.maximumItems} onChange={event => setValue(current => ({ ...current, maximumItems: Number(event.target.value) }))} /></label><label className="field"><span>Toplam sınır · GiB</span><input type="number" min={.25} max={256} step={.25} value={value.maximumBytes / 1024 ** 3} onChange={event => setValue(current => ({ ...current, maximumBytes: Math.floor(Number(event.target.value) * 1024 ** 3) }))} /></label></div>
    <label className="field"><span>Çevrimdışı kontrol aralığı · gün</span><input type="number" min={1} max={7} value={value.leaseDays} onChange={event => setValue(current => ({ ...current, leaseDays: Number(event.target.value) }))} /></label><p className="field-hint">Değişiklik eski kopya izinlerini kaldırır. Bağlantısız cihaz bunu yeniden bağlandığında veya kayıtlı erişim süresi dolduğunda uygular.</p><Button variant="ghost" type="submit" isDisabled={save.isPending}>Çevrimdışı sınırı kaydet</Button>{save.error && <p className="form-error">{save.error.message}</p>}
  </form>;
}
