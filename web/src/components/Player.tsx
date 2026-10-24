import { useCallback, useEffect, useLayoutEffect, useRef, useState, useSyncExternalStore, type RefObject, type VideoHTMLAttributes } from 'react';
import { Button } from '@heroui/react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ArrowLeft, ChevronDown, Expand, Image, LoaderCircle, Settings2, SkipBack, SkipForward, Trash2, Upload, UsersRound, X } from 'lucide-react';
import { VideoPlayer, VideoSkin } from '@videojs/react/video';
import { I18nProvider, useMediaAttach } from '@videojs/react';
import '@videojs/react/i18n/locales/tr/register';
import '@videojs/react/video/skin.css';
import { appUrl, bytes, clock, mutate, request, thumbnail, type MediaItem, type MediaTracks } from '../api';
import { useUiLanguage, uiLocale, translate as t } from '../i18n';
import { createHomeMediaBridge, type HomeMediaPort } from '../homeMediaBridge';
import { usePlayback } from './usePlayback';
import { useSession } from './SessionContext';
import { Lyrics } from './Lyrics';
import { SubtitleSearchPanel } from './SubtitleProviders';
import { SubtitleMediaPanel, SubtitleSyncPanel } from './SubtitleTools';
import { SkipSegments, SkipPreferences, SegmentEditor } from './PlaybackSegments';
import { ReceiverPanel } from './Receivers';
import { PlaybackUsagePanel } from './PlaybackUsage';
import { WatchPlayerPanel } from './WatchRooms';
import type { WatchController, WatchState } from '../watch/WatchController';
import type { WatchCommand, WatchPlayer } from '../watch/types';
import './watch-page.css';
import { usePageTitle } from '../usePageTitle';
import { useControllerCommands } from '../controller/react';
import { controllerPlayback } from '../controller/playback';
import { MusicVisualMode } from '../music/MusicVisualMode';
import { playbackBuffer } from '../playbackDiagnostics';

function language(code: string) { try { return code === 'und' ? t('Dil belirtilmemiş') : new Intl.DisplayNames([uiLocale()], { type: 'language' }).of(code) || code; } catch { return code; } }
const noRoom = () => null;
const noSubscription = () => () => {};

function HomeVideo({ video, port, children, ...props }: VideoHTMLAttributes<HTMLVideoElement> & { video: RefObject<HTMLVideoElement | null>; port: RefObject<HomeMediaPort> }) {
  // This exported attachment hook is pinned to the installed Video.js version.
  // The native element remains owned by Home; Video.js receives its timeline/command facade.
  const attach = useMediaAttach();
  const bridge = useRef<ReturnType<typeof createHomeMediaBridge> | null>(null);
  const ref = useCallback((element: HTMLVideoElement | null) => {
    video.current = element;
    bridge.current = element ? createHomeMediaBridge(element, new Proxy({} as HomeMediaPort, { get: (_, key) => {
      const value = port.current[key as keyof HomeMediaPort]; return typeof value === 'function' ? value.bind(port.current) : value;
    } })) : null;
    attach?.(bridge.current?.media ?? null);
  }, [attach, video, port]);
  useLayoutEffect(() => { bridge.current?.refresh(); });
  return <video {...props} ref={ref}>{children}</video>;
}

export function Player({ item, onClose, onNext, onPrevious, onEnded, queueLength = 1, shuffle = false, repeat = 'off', onShuffle, onRepeat,
  playbackKey = 0, roomController, page: controlledPage = true, onMinimize, onExpand }: {
  item: MediaItem; onClose: () => void; onNext?: () => void; onPrevious?: () => void; onEnded?: () => void;
  queueLength?: number; shuffle?: boolean; repeat?: string; onShuffle?: () => void; onRepeat?: () => void;
  playbackKey?: number; roomController?: WatchController; page?: boolean; onMinimize?: () => void; onExpand?: () => void;
}) {
  const [locallyMinimized, setLocallyMinimized] = useState(false);
  const [visual, setVisual] = useState(false);
  const page = controlledPage && !locallyMinimized;
  const minimize = () => { if (onMinimize) onMinimize(); else setLocallyMinimized(true); };
  const expand = () => { setLocallyMinimized(false); onExpand?.(); };
  useEffect(() => {
    if (onMinimize) return;
    const content = document.getElementById('app-content'); if (content) content.inert = page;
    return () => { if (content) content.inert = false; };
  }, [page, onMinimize]);
  const locale = useUiLanguage(); const account = useSession(); const client = useQueryClient();
  usePageTitle(item.title, page);
  const pageHeading = useRef<HTMLHeadingElement>(null);
  useEffect(() => { if (page) pageHeading.current?.focus({ preventScroll: true }); }, [page]);
  const roomState = useSyncExternalStore<WatchState | null>(roomController?.subscribe ?? noSubscription, roomController?.snapshot ?? noRoom);
  const shared = roomState?.connection?.role === 'player'; const room = shared ? roomState.connection!.room : null;
  const playback = usePlayback(item, playbackKey, { managed: !!shared, position: roomController?.position(), binding: room && roomState?.connection
    ? { roomId: room.id, memberId: roomState.connection.memberId, clientRevision: roomState.connection.clientRevision, epoch: room.epoch } : undefined });
  const watchPort = useRef(playback.shared); watchPort.current = playback.shared;
  useEffect(() => {
    if (!roomController) return;
    const adapter: WatchPlayer = { read: () => watchPort.current.read(), pause: () => watchPort.current.pause(), play: () => watchPort.current.play(),
      seek: seconds => watchPort.current.seek(seconds), speed: rate => watchPort.current.speed(rate), volume: (value, muted) => watchPort.current.volume(value, muted),
      restart: seconds => watchPort.current.restart(seconds), stop: message => watchPort.current.stop(message) };
    return roomController.attach(adapter);
  }, [roomController]);
  const command = (input: WatchCommand) => roomController!.command(input).catch(error => { playback.setNotice(error.message); throw error; });
  const seek = (position: number) => { if (room) { if (room.canControl) void command({ action: 'seek', positionSeconds: position }).catch(() => {}); } else playback.seek(position); };
  const [settings, setSettings] = useState(false); const [roomPanel, setRoomPanel] = useState(false);
  const [subtitle, setSubtitleValue] = useState(''); const subtitleRef = useRef('');
  const setSubtitle = (id: string) => { subtitleRef.current = id; setSubtitleValue(id); };
  const [delay, setDelay] = useState(0); const [burnText, setBurnText] = useState(false); const [uploadLanguage, setUploadLanguage] = useState('tr');
  const fileInput = useRef<HTMLInputElement>(null);
  const tracks = useQuery({ queryKey: ['media-tracks', item.id], queryFn: ({ signal }) => request<MediaTracks>(`/media/${item.id}/tracks`, { signal }), retry: false });
  const selectedSubtitle = tracks.data?.subtitles.find(track => track.id === subtitle);
  const burnSubtitle = !!subtitle && (!!selectedSubtitle?.image || burnText);
  const configure = useRef(playback.configure); configure.current = playback.configure;
  useEffect(() => { setSubtitle(''); setDelay(0); setBurnText(false); }, [item.id, playbackKey]);
  useEffect(() => {
    if (subtitle && !selectedSubtitle) return;
    const id = burnSubtitle ? subtitle : null; const seconds = burnSubtitle ? delay : 0;
    if (playback.options.subtitleId === id && playback.options.burnSubtitle === burnSubtitle && playback.options.subtitleDelaySeconds === seconds) return;
    const timer = setTimeout(() => { void configure.current({ subtitleId: id, burnSubtitle, subtitleDelaySeconds: seconds }); }, playback.options.subtitleId === id ? 350 : 0);
    return () => clearTimeout(timer);
  }, [subtitle, !!selectedSubtitle, burnSubtitle, delay, playback.options.subtitleId, playback.options.burnSubtitle, playback.options.subtitleDelaySeconds]);
  const addSubtitle = useMutation({ mutationFn: (file: File) => {
    if (file.size > 2 * 1024 * 1024) throw new Error('Altyazı en fazla 2 MB olabilir.');
    const extension = file.name.split('.').at(-1)?.toLowerCase() || '';
    return request<{ id: string }>(`/media/${item.id}/subtitles?${new URLSearchParams({ language: uploadLanguage, title: file.name, extension })}`,
      { method: 'POST', headers: { 'Content-Type': 'application/octet-stream' }, body: file });
  }, onSuccess: async result => { await client.invalidateQueries({ queryKey: ['media-tracks', item.id] }); setSubtitle('managed-' + result.id); } });
  const removeSubtitle = useMutation({ mutationFn: (id: string) => mutate(`/media/${item.id}/subtitles/managed/${id.replace('managed-', '')}`, undefined, 'DELETE'),
    onSuccess: async () => { setSubtitle(''); await client.invalidateQueries({ queryKey: ['media-tracks', item.id] }); } });
  const { video } = playback;
  const mediaPort = useRef<HomeMediaPort>(null!);
  mediaPort.current = {
    read: () => ({ ...playback.timeline(), error: playback.error, subtitle: subtitleRef.current,
      audio: playback.options.audioStream ?? playback.session?.audioStream ?? null, quality: playback.options.maxHeight ? String(playback.options.maxHeight) : 'auto' }),
    play: async () => {
      if (room) { if (room.canControl) await command({ action: 'play' }); return; }
      await playback.requestPlay();
    },
    pause: () => { if (room) { if (room.canControl) void command({ action: 'pause' }).catch(() => {}); } else playback.requestPause(); },
    seek, retry: () => { void playback.retry(); },
    rate: value => { if (room) { if (room.canControl) void command({ action: 'rate', rate: value }).catch(() => {}); } else playback.shared.speed(value); },
    volume: (value, muted) => { if (room?.mode === 'remote') { if (room.canControl) void command({ action: 'volume', volume: value, muted }).catch(() => {}); }
      else playback.shared.volume(value, muted); },
    subtitle: value => { setSubtitle(value); setDelay(0); },
    audio: index => { if (index !== (playback.options.audioStream ?? playback.session?.audioStream)) void playback.configure({ audioStream: index }); },
    quality: value => { void playback.configure(value === 'original' ? { maxHeight: null, maxBitrate: null, forceCompatible: false }
      : { maxHeight: Number(value), maxBitrate: Number(value) >= 1080 ? 8_000_000 : Number(value) >= 720 ? 4_000_000 : 1_500_000, forceCompatible: false }); },
    audioOptions: () => (tracks.data?.streams.filter(stream => stream.kind === 'audio') ?? []).map(stream => ({ id: String(stream.index), language: stream.language,
      label: `${language(stream.language)} · ${stream.title || stream.codec.toUpperCase()}${stream.channels ? ` · ${stream.channels} kanal` : ''}` })),
    subtitleOptions: () => (tracks.data?.subtitles ?? []).filter(track => track.playable || track.burnable).map(track => ({ id: track.id, language: track.language,
      label: `${language(track.language)} · ${track.title || track.format.toUpperCase()}${track.forced ? ' · Forced' : ''}${track.hearingImpaired ? ' · SDH' : ''}${track.image ? ' · Görüntüye işlenir' : ''}` })),
    chapters: () => (tracks.data?.chapters ?? []).map((chapter, index, rows) => ({ startTime: chapter.startSeconds,
      endTime: rows[index + 1]?.startSeconds ?? playback.length, text: chapter.title || `${index + 1}` }))
  };
  const subtitleUrl = subtitle && !burnSubtitle && !playback.session?.subtitleBurned ? appUrl(`/api/media/${item.id}/subtitles/${encodeURIComponent(subtitle)}?${new URLSearchParams({ start: String(playback.session?.startSeconds ?? 0), delay: String(delay) })}`) : undefined;
  const mode = playback.session?.mode === 'direct' ? 'Doğrudan oynatma' : playback.session?.mode === 'remux' ? 'Yalnız kapsayıcı değişiyor'
    : playback.session?.mode === 'audio-transcode' ? 'Yalnız ses dönüştürülüyor' : playback.session?.encoder === 'h264_nvenc' ? 'GPU video uyarlaması · NVENC' : 'Görüntü dönüştürülüyor';
  const sourceVideo = tracks.data?.streams.find(stream => stream.kind === 'video');
  const sourceAudio = tracks.data?.streams.find(stream => stream.kind === 'audio' && stream.index === playback.session?.audioStream);
  const close = () => { playback.save(true); onClose(); };
  const controllerPanel = useRef<HTMLElement>(null), controllerStage = useRef<HTMLDivElement>(null);
  useControllerCommands(controllerPanel, action => {
    if (action === 'back') { if (settings || roomPanel) { setSettings(false); setRoomPanel(false); } else close(); return true; }
    if (action === 'menu') { setSettings(value => !value); return true; }
    if (action === 'confirm' && document.activeElement === controllerStage.current) action = 'playpause';
    if (controllerPlayback(action, mediaPort.current, !playback.playing, !room || room.canControl, () => playback.setNotice(t('Tarayıcı oynatma için gerçek bir kumanda veya klavye etkileşimi isteyebilir.')))) return true;
    if (action === 'previous' || action === 'next') { if (!room) (action === 'previous' ? onPrevious : onNext)?.(); return true; }
    return false;
  });
  return <><section ref={controllerPanel} className={`watch-player ${!page ? 'watch-player--mini' : ''}`} aria-label={item.title} data-controller-scope={page ? 'player' : undefined} data-testid="media-player" data-player="videojs-10" data-minimized={!page}>
    <header className="watch-header"><Button variant="ghost" onPress={close}><ArrowLeft size={19} /> Ayrıntılara dön</Button><div><span>{item.content?.episodeTitle || item.libraryName}</span><h1 ref={pageHeading} data-page-heading tabIndex={-1}>{item.title}</h1></div>
       <div className="watch-header-actions">{item.kind==='audio'&&<Button isIconOnly variant="ghost" aria-label="Görsel müzik modu" onPress={()=>setVisual(true)}><Image size={20}/></Button>}<Button variant="ghost" className="watch-settings-button" aria-label="Ses, altyazı ve kalite" aria-expanded={settings} onPress={() => setSettings(value => !value)}><Settings2 size={20} /><span>Ses, altyazı ve kalite</span></Button>
        {roomController && <Button isIconOnly variant="ghost" aria-label="Birlikte izleme ve uzak kumanda" aria-expanded={roomPanel} onPress={() => setRoomPanel(value => !value)}><UsersRound size={20} /></Button>}
        <Button isIconOnly variant="ghost" aria-label={page ? 'Oynatıcıyı küçült' : 'Oynatıcıyı genişlet'} onPress={page ? minimize : expand}>{page ? <ChevronDown size={22} /> : <Expand size={20} />}</Button>
        {!page && <Button isIconOnly variant="ghost" aria-label="Oynatıcıyı kapat" onPress={close}><X size={20} /></Button>}</div></header>
    <div className={`watch-layout ${settings || roomPanel || room ? 'watch-layout--panel' : ''}`}>
      <div ref={controllerStage} className="watch-stage" tabIndex={0} aria-label={t('Oynat / duraklat')} data-controller-default onKeyDown={event => { if (event.key === ' ' && event.target === controllerStage.current) { event.preventDefault(); controllerPlayback('playpause', mediaPort.current, !playback.playing, !room || room.canControl, () => playback.setNotice(t('Tarayıcı oynatma için gerçek bir kumanda veya klavye etkileşimi isteyebilir.'))); } }}><VideoPlayer title={item.title} poster={item.hasThumbnail ? thumbnail(item) : undefined}>
        <I18nProvider locale={locale} translations={{ menu: { auto: t('Özgün / otomatik uyum'), autoWithLabel: t('Özgün / otomatik uyum') } }}>
          <VideoSkin className="home-video-skin"><HomeVideo video={video} port={mediaPort} playsInline preload="metadata" {...playback.events}
            onEnded={() => { playback.events.onEnded(); if (room) return; if (repeat === 'one' || repeat === 'all' && queueLength === 1) void playback.restart(); else onEnded?.(); }}>
            {subtitleUrl && <track key={subtitleUrl} src={subtitleUrl} kind="subtitles" label={selectedSubtitle?.title || language(selectedSubtitle?.language || 'und')} default
              onLoad={event => { event.currentTarget.track.mode = 'showing'; }} onError={() => playback.setNotice('Altyazı açılamadı. Dosya biçimini veya seçilen izi kontrol et.')} />}
          </HomeVideo>
            {item.kind === 'video' && <SkipSegments mediaId={item.id} position={playback.position} onSeek={seek} canControl={!room || room.canControl} />}
          </VideoSkin>
        </I18nProvider>
      </VideoPlayer>
      {playback.busy && !playback.error && <div className="watch-preparing" role="status"><LoaderCircle className="spin" size={22} /> Oynatman hazırlanıyor…</div>}
      {playback.error && <div className="watch-failure" role="alert"><h2>Oynatmaya ara verdik.</h2><p>{playback.error}</p><Button className="secondary-button" onPress={() => playback.retry()}>Kaldığım yerden yeniden dene</Button><PlaybackUsagePanel /></div>}
      </div>
      {page && (settings || roomPanel || room) && <aside className="watch-inspector" aria-label="Oynatma tercihleri"><header><h2>Oynatma tercihleri</h2><Button isIconOnly variant="ghost" aria-label="Kapat" onPress={() => { setSettings(false); setRoomPanel(false); }}><X size={18} /></Button></header>
        {roomController && (roomPanel || room) && <WatchPlayerPanel controller={roomController} ready={!!playback.session && !playback.busy} onPrepare={playback.arm} />}
        {settings && <div className="home-form">
          {playback.session && <section aria-label="Oynatma bilgisi"><h3>{mode}</h3>
            {sourceVideo && <p className="field-hint">{t('Görüntü')}: {sourceVideo.codec.toUpperCase()} → {playback.session.videoCodec?.toUpperCase() ?? sourceVideo.codec.toUpperCase()}{video.current?.videoHeight ? ` · ${video.current.videoWidth} × ${video.current.videoHeight}` : ''}</p>}
            {sourceAudio && <p className="field-hint">{t('Ses')}: {sourceAudio.codec.toUpperCase()} → {playback.session.audioCodec?.toUpperCase() ?? sourceAudio.codec.toUpperCase()}</p>}
            <p className="field-hint">{t('Tarayıcı tamponu')}: {clock(playbackBuffer(video.current))}{playback.session.container ? ` · ${playback.session.container.toUpperCase()}` : ''}</p>
            {playback.session.telemetry && <><p className="field-hint">{t('Geçici akış alanı')}: {bytes(playback.session.telemetry.cacheBytes)} · {playback.session.state === 'completed' ? t('Akış hazır.') : playback.session.telemetry.throttled ? t('Tampon hazır; üretim bekliyor.') : t('Akış üretimi sürüyor.')}</p>
              {playback.session.telemetry.speed != null && <p className="field-hint">{t('Üretim hızı')}: {playback.session.telemetry.speed.toFixed(2)}×</p>}</>}
          </section>}
          <p className="field-hint">Bu seçimler videodaki ayarlar menüsüyle aynıdır. Değişiklikler kaldığın konumu korur.</p>
          <section className="watch-track-settings" aria-label="Ses, altyazı ve kalite">
            <label className="field"><span>Ses / dublaj</span><select aria-label="Ses / dublaj" value={playback.options.audioStream ?? playback.session?.audioStream ?? ''} disabled={tracks.isPending || !tracks.data?.streams.some(stream => stream.kind === 'audio')} onChange={event => { if (event.target.value === '') void playback.configure({ audioStream: null }); else mediaPort.current.audio(Number(event.target.value)); }}><option value="">Varsayılan</option>{mediaPort.current.audioOptions().map(option => <option key={option.id} value={option.id}>{option.label}</option>)}</select></label>
            {tracks.data?.streams.filter(stream => stream.kind === 'audio').length === 1 && <p className="field-hint">Bu dosyada tek ses izi var; başka dublaj seçeneği bulunmuyor.</p>}
            <label className="field"><span>Altyazı</span><select aria-label="Altyazı" value={subtitle} disabled={tracks.isPending || !mediaPort.current.subtitleOptions().length} onChange={event => mediaPort.current.subtitle(event.target.value)}><option value="">Kapalı</option>{mediaPort.current.subtitleOptions().map(option => <option key={option.id} value={option.id}>{option.label}</option>)}</select></label>
            {!tracks.isPending && !mediaPort.current.subtitleOptions().length && <p className="field-hint">Bu dosyada altyazı yok. Yönetici altyazı ekleyebilir veya sağlayıcıda arayabilir.</p>}
            <label className="field"><span>Kalite</span><select aria-label="Kalite" value={playback.options.maxHeight ? String(playback.options.maxHeight) : 'original'} onChange={event => mediaPort.current.quality(event.target.value)}><option value="original">Özgün / otomatik uyum</option><option value="1080">1080p · en fazla 8 Mbps</option><option value="720">720p · en fazla 4 Mbps</option><option value="480">480p · en fazla 1,5 Mbps</option></select></label>
          </section>
          <h3 className="watch-advanced-heading">Gelişmiş Home ayarları</h3>
          <label className="check-field"><input type="checkbox" checked={playback.options.forceCompatible} onChange={event => playback.configure({ forceCompatible: event.target.checked })} /> H.264 uyumluluk modu</label>
          {selectedSubtitle?.playable && selectedSubtitle.burnable && <label className="check-field"><input type="checkbox" checked={burnText} onChange={event => setBurnText(event.target.checked)} /> Altyazı stilini görüntüye işle</label>}
          {subtitle && <label className="field"><span>Altyazı gecikmesi (sn)</span><input type="number" min={-600} max={600} step={.5} value={delay} onChange={event => setDelay(Number(event.target.value))} /></label>}
          {!!tracks.data?.chapters.length && <label className="field"><span>Sahne işaretleri</span><select value="" disabled={!!room && !room.canControl} onChange={event => { if (event.target.value) seek(Number(event.target.value)); }}><option value="">Bir sahneye git</option>{tracks.data.chapters.map(chapter => <option key={chapter.index} value={chapter.startSeconds}>{clock(chapter.startSeconds)} · {chapter.title}</option>)}</select></label>}
          {item.episodes?.length > 1 && <label className="field"><span>Dosyadaki bölümler</span><select value="" disabled={!!room && !room.canControl} onChange={event => { if (event.target.value) seek(Number(event.target.value)); }}><option value="">Bir bölüme git</option>{item.episodes.map(episode => <option key={episode.episodeId} value={episode.startSeconds ?? ''} disabled={episode.startSeconds === null}>S{episode.season} E{episode.number} · {episode.title}</option>)}</select></label>}
          {item.kind === 'video' && account.role === 'admin' && <><label className="field"><span>Yüklenecek altyazının dili</span><select value={uploadLanguage} onChange={event => setUploadLanguage(event.target.value)}>{['tr', 'en', 'de', 'fr', 'es', 'ja', 'ko', 'ar', 'und'].map(code => <option key={code} value={code}>{language(code)}</option>)}</select></label>
            <input ref={fileInput} type="file" accept=".srt,.vtt,.ass,.ssa" className="sr-only" aria-label="Altyazı dosyası" onChange={event => { const file = event.target.files?.[0]; if (file) addSubtitle.mutate(file); event.target.value = ''; }} />
            <Button variant="ghost" isDisabled={addSubtitle.isPending} onPress={() => fileInput.current?.click()}><Upload size={14} /> Altyazı ekle</Button>
            {subtitle.startsWith('managed-') && <Button variant="ghost" isDisabled={removeSubtitle.isPending} onPress={() => removeSubtitle.mutate(subtitle)}><Trash2 size={14} /> Yüklenmiş altyazıyı kaldır</Button>}
            <SubtitleSearchPanel key={item.id} mediaId={item.id} onAdded={setSubtitle} /><SubtitleMediaPanel key={item.id + '-policy'} mediaId={item.id} onSelected={setSubtitle} /><SubtitleSyncPanel key={item.id + '-sync'} mediaId={item.id} selectedTrackId={selectedSubtitle?.playable ? subtitle : undefined} onSelected={setSubtitle} />
            <SegmentEditor item={item} /></>}
          {item.kind === 'video' && <SkipPreferences />}
          <ReceiverPanel item={item} position={playback.position} subtitleId={subtitle || undefined} onPauseLocal={() => video.current?.pause()} />
          {item.kind === 'audio' && <Lyrics key={item.id} mediaId={item.id} seconds={playback.position} duration={playback.length} onSeek={room && !room.canControl ? undefined : seek} />}
          {(tracks.error || addSubtitle.error || removeSubtitle.error) && <p className="form-error">{tracks.error?.message || addSubtitle.error?.message || removeSubtitle.error?.message}</p>}
        </div>}</aside>}
    </div>
    {page && <footer className="watch-footer"><div><span>{mode}</span><span>{playback.notice || (playback.saveError ? 'İzleme konumu kaydedilemedi.' : 'Kaldığın yer otomatik kaydedilir.')}</span></div>
      {queueLength > 1 && !room && <div className="content-actions"><Button variant="ghost" onPress={onPrevious}><SkipBack size={16} /> Önceki</Button><Button variant="ghost" onPress={onNext}>Sonraki <SkipForward size={16} /></Button><Button variant="ghost" onPress={onShuffle} aria-pressed={shuffle}>Karışık çal</Button><Button variant="ghost" onPress={onRepeat}>{repeat === 'off' ? 'Tekrar kapalı' : repeat === 'one' ? 'Tek parçayı tekrarla' : 'Listeyi tekrarla'}</Button></div>}
      {!!playback.session?.reasons.length && settings && <p className="field-hint">{playback.session.reasons.join(' ')}</p>}
    </footer>}
   </section>{visual&&item.kind==='audio'&&<MusicVisualMode title={item.title} subtitle={item.libraryName} playing={playback.playing} onToggle={()=>{if(playback.playing)mediaPort.current.pause();else void mediaPort.current.play().catch(error=>playback.setNotice((error as Error).message));}} onNext={room?undefined:onNext} onPrevious={room?undefined:onPrevious} onClose={()=>setVisual(false)}/>}</>;
}
