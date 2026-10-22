import { useEffect, useRef, useState } from 'react';
import { Button } from '@heroui/react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { ArrowLeft, CircleAlert, Gamepad2, LoaderCircle, Maximize, Pause, Play, ShieldCheck, X } from 'lucide-react';
import { ApiError, appUrl, mutate, request, randomId } from '../api';
import { useController, useControllerCommands } from '../controller/react';
import { runtimeText, translate as t, useUiLanguage } from '../i18n';
import { gamepadsNeutral, readGameMessage, type Game, type GameCheckpoint, type GameLaunch } from './model';
import { GameSaveQueue } from './saveQueue';
import { NativeGameDetails } from './NativeGames';
import { NativeGamePlayer } from './NativeGamePlayer';
import './games.css';

function WebGamePlayer({ id, onExit }: { id: string; onExit: () => void }) {
  useUiLanguage(); const client = useQueryClient(); const controller = useController();
  const game = useQuery({ queryKey: ['games', id], queryFn: () => request<Game>(`/games/${id}`) });
  const [launch, setLaunch] = useState<GameLaunch | null>(null), [ready, setReady] = useState(false), [paused, setPaused] = useState(true);
  const [progress, setProgress] = useState(0), [error, setError] = useState(''), [saveError, setSaveError] = useState('');
  const [saving, setSaving] = useState(false), [saved, setSaved] = useState(false), [exiting, setExiting] = useState(false), [waiting, setWaiting] = useState(false);
  const [notice, setNotice] = useState(''); const root = useRef<HTMLElement>(null), menu = useRef<HTMLDivElement>(null), frame = useRef<HTMLIFrameElement>(null);
  const launchRef = useRef<GameLaunch | null>(null), queue = useRef<GameSaveQueue | null>(null), pausedRef = useRef(true), readyRef = useRef(false);
  const alive = useRef(false), saveTimer = useRef(0), neutralFrame = useRef(0), saveAck = useRef<(() => void) | null>(null);
  const nonce = useRef(''), startIdentity = useRef(''); const flushing = useRef<Promise<boolean> | null>(null);
  const discardOnClose = useRef(false);
  const endRequested = useRef(false);
  pausedRef.current = paused; readyRef.current = ready;
  const send = (type: string, values: Record<string, unknown> = {}) => frame.current?.contentWindow?.postMessage({ protocol: 'home-game-v1', nonce: nonce.current, type, ...values }, '*');
  const pause = () => { send('pause'); setPaused(true); };
  useControllerCommands(menu, action => { if (action === 'back' || action === 'menu') { if (ready && !error && !exiting) resume(); return true; } return false; });
  useEffect(() => {
    if (paused || !ready || !controller) return;
    return controller.acquireGameplay();
  }, [controller, paused, ready]);
  useEffect(() => {
    alive.current = true; let disposed = false; let started: GameLaunch | null = null; let heartbeat = 0;
    endRequested.current = false;
    const end = () => {
      if (!started || endRequested.current) return;
      endRequested.current = true;
      // Dispatch immediately. Awaiting a flush then closing can strand a session when the document is destroyed.
      if (discardOnClose.current) void mutate(`/games/sessions/${started.id}`, undefined, 'DELETE', true).catch(() => {});
      else void mutate(`/games/sessions/${started.id}/end`, queue.current?.unloadPulse() ?? { running: false }, 'POST', true).catch(() => {});
    };
    queueMicrotask(() => {
      if (disposed) return; // StrictMode's probe must not open an abandoned game session.
      startIdentity.current = randomId(); nonce.current = [...crypto.getRandomValues(new Uint8Array(32))].map(byte => byte.toString(16).padStart(2, '0')).join('');
      void mutate<GameLaunch>(`/games/${id}/sessions`, { clientId: startIdentity.current, ticket: nonce.current }).then(result => {
        started = result;
        if (disposed) { void mutate(`/games/sessions/${result.id}`, undefined, 'DELETE', true).catch(() => {}); return; }
        launchRef.current = result;
        queue.current = new GameSaveQueue(result.checkpoint, pulse => mutate<GameCheckpoint>(`/games/sessions/${result.id}`, pulse, 'PUT', true));
        setLaunch(result); setSaved(Object.keys(result.checkpoint.files).length > 0);
        heartbeat = window.setInterval(() => { void persist(); }, 25_000);
      }).catch(reason => { if (!disposed) setError((reason as Error).message); });
    });
    const message = (event: MessageEvent) => {
      if (disposed || event.source !== frame.current?.contentWindow || event.origin !== 'null') return;
      const data = readGameMessage(event.data, nonce.current); if (!data) return;
      if (data.type === 'hello') send('init', { files: launchRef.current?.checkpoint.files ?? {} });
      if (data.type === 'loading') setProgress(data.progress ?? 0);
      if (data.type === 'ready') { setReady(true); readyRef.current = true; }
      if (data.type === 'paused' || data.type === 'menu') setPaused(true);
      if (data.type === 'flushed') { saveAck.current?.(); saveAck.current = null; }
      if (data.type === 'save' && data.files) { queue.current?.stage(data.files); clearTimeout(saveTimer.current); saveTimer.current = window.setTimeout(() => { void persist(); }, 650); }
      if (data.type === 'exit') { void exit(); }
      if (data.type === 'error') { setError(data.code === 'webgl-unavailable' ? t('Bu tarayıcı oyunun gerektirdiği WebGL desteğini sunmuyor.') : t('Oyun başlatılamadı. Yeniden deneyebilir veya oyunlara dönebilirsin.')); setPaused(true); }
    };
    const hidden = () => { if (document.hidden) pause(); };
    const stop = () => { pause(); setError(t('Hesap değişti; oyun oturumu kapatıldı.')); };
    const restored = (event: PageTransitionEvent) => { if (event.persisted) location.reload(); }; // A BFCache frame must not reuse its closed game ticket.
    window.addEventListener('message', message); document.addEventListener('visibilitychange', hidden); window.addEventListener('home-watch-stop', stop); window.addEventListener('pagehide', end); window.addEventListener('pageshow', restored);
    return () => {
      disposed = true; alive.current = false; clearInterval(heartbeat); clearTimeout(saveTimer.current); cancelAnimationFrame(neutralFrame.current);
      window.removeEventListener('message', message); document.removeEventListener('visibilitychange', hidden); window.removeEventListener('home-watch-stop', stop); window.removeEventListener('pagehide', end); window.removeEventListener('pageshow', restored);
      end();
      launchRef.current = null; queue.current = null; saveAck.current?.(); saveAck.current = null;
      void client.invalidateQueries({ queryKey: ['games'] });
    };
  }, [id, client]);
  useEffect(() => {
    if (paused || !ready) return;
    let chordHeld = true, handle = 0;
    const sample = () => {
      try {
        const pads = typeof navigator.getGamepads === 'function' ? Array.from(navigator.getGamepads()) : [];
        const chord = pads.some(pad => pad?.connected && pad.mapping === 'standard' && pad.buttons[8]?.pressed && pad.buttons[9]?.pressed);
        if (!chord) chordHeld = false;
        else if (!chordHeld) { pause(); return; }
      } catch { /* Keyboard and pointer remain available when the Gamepad API is blocked. */ }
      handle = requestAnimationFrame(sample);
    };
    handle = requestAnimationFrame(sample); return () => cancelAnimationFrame(handle);
  }, [paused, ready]);
  useEffect(() => {
    if (!launch || ready || error) return;
    const timer = window.setTimeout(() => { setError(t('Oyun yüklenemedi. Bu tarayıcıda destek veya bağlantı sorunu olabilir.')); setPaused(true); }, 90_000);
    return () => clearTimeout(timer);
  }, [launch, ready, error]);
  async function persist() {
    if (!queue.current) return false;
    if (alive.current) setSaving(true);
    try { await queue.current.flush(readyRef.current && !pausedRef.current && !document.hidden); if (alive.current) { setSaveError(''); setSaved(queue.current?.hasSavedData ?? false); } return true; }
    catch (reason) { if (alive.current) { setSaveError((reason as Error).message); if (reason instanceof ApiError && [401, 403, 409].includes(reason.status)) { pause(); setError(reason.message); } } return false; }
    finally { if (alive.current) setSaving(false); }
  }
  function resume() {
    if (!ready || error || exiting) return;
    cancelAnimationFrame(neutralFrame.current); setWaiting(true);
    const wait = () => {
      if (!alive.current || document.hidden) { setWaiting(false); return; }
      let neutral = true;
      try { neutral = typeof navigator.getGamepads !== 'function' || gamepadsNeutral(navigator.getGamepads()); } catch { /* A keyboard-only browser can still play. */ }
      if (!neutral) { neutralFrame.current = requestAnimationFrame(wait); return; }
      setWaiting(false); send('resume'); setPaused(false); frame.current?.focus();
    };
    neutralFrame.current = requestAnimationFrame(wait);
  }
  async function flushFrame() {
    if (!readyRef.current || !frame.current) return true;
    if (flushing.current) return flushing.current;
    flushing.current = new Promise<boolean>(resolve => {
      const timer = window.setTimeout(() => { saveAck.current = null; resolve(false); }, 3000);
      saveAck.current = () => { clearTimeout(timer); resolve(true); }; send('flush');
    }).finally(() => { flushing.current = null; });
    return flushing.current;
  }
  async function exit(discard = false) {
    if (exiting) return;
    pause(); setExiting(true); cancelAnimationFrame(neutralFrame.current); setWaiting(false);
    if (!discard && readyRef.current && (!await flushFrame() || !await persist())) { setExiting(false); setSaveError(t('Son kayıt doğrulanamadı. Yeniden deneyebilir veya kaydetmeden çıkabilirsin.')); return; }
    if (launchRef.current) { try { await mutate(`/games/sessions/${launchRef.current.id}`, undefined, 'DELETE'); } catch (reason) { if (!discard) { setExiting(false); setSaveError((reason as Error).message); return; } } }
    discardOnClose.current = discard; endRequested.current = true; onExit();
  }
  async function fullscreen() {
    try { if (document.fullscreenElement) await document.exitFullscreen(); else await root.current?.requestFullscreen(); }
    catch { setNotice(t('Tam ekran için oynatıcı düğmesine gerçek bir tıklama veya klavye ile basman gerekebilir.')); }
  }
  return <section ref={root} className="game-player" aria-label="Oyun oynatıcısı">
    <header className="game-player-header"><Button variant="ghost" onPress={() => void exit()} aria-label="Kaydet ve oyunlara dön"><ArrowLeft size={20} /></Button><div><strong>{game.data?.title ?? 'Oyun'}</strong><small>{game.data?.publisher ?? 'Home'}</small></div><span className="game-save-status" role="status">{saving ? <LoaderCircle size={14} className="spin" /> : <ShieldCheck size={14} />}{saveError ? 'Kayıt bekliyor' : saving ? 'Kaydediliyor…' : saved ? 'Sunucuda kayıtlı' : 'Profil kaydı'}</span><Button isIconOnly variant="ghost" aria-label="Oyun menüsünü aç" onPress={pause}><Pause size={19} /></Button>{document.fullscreenEnabled && <Button isIconOnly variant="ghost" aria-label="Tam ekran" onPress={() => void fullscreen()}><Maximize size={18} /></Button>}</header>
    <div className="game-stage" data-controller-exclude>{launch && <iframe ref={frame} className="game-frame" src={appUrl(launch.url) + '#' + nonce.current} title={game.data?.title ?? t('Oyun')} sandbox="allow-scripts" allow="gamepad *; fullscreen *; autoplay *" referrerPolicy="no-referrer" tabIndex={paused ? -1 : 0} />}</div>
    {(paused || !ready || error) && <div className="game-pause-backdrop"><div ref={menu} className="game-pause-menu" role="dialog" aria-modal="true" aria-label="Oyun menüsü" data-controller-scope="game-menu"><span className="game-pause-symbol">{error ? <CircleAlert size={30} /> : ready ? <Gamepad2 size={32} /> : <LoaderCircle size={28} className="spin" />}</span><p className="eyebrow">{error ? 'OYUN AÇILAMADI' : ready ? 'BİR NEFES AL' : 'OYUNUN HAZIRLANIYOR'}</p><h1>{game.data?.title ?? 'Oyun'}</h1>
      {!ready && !error && <><p>Oyun dosyaları Home sunucundan yükleniyor.</p><progress max={1} value={progress} aria-label="Oyun yükleme ilerlemesi" /></>}
      {error && <p className="form-error" role="alert">{runtimeText(error)}</p>}
      {ready && !error && <><p>Bu cihazda oynuyorsun. Home menüsüne dönmek için Start + Select veya Esc.</p><Button className="play-button" data-controller-default isDisabled={waiting || exiting} onPress={resume}><Play size={17} fill="currentColor" />{waiting ? 'Tuşları bırak…' : 'Oyuna dön'}</Button><small>Ses başlamazsa oyun alanına tıkla veya oyun alanında Enter tuşuna bas.</small></>}
      {saveError && <p className="form-error" role="alert">{runtimeText(saveError)}</p>}
      {notice && <p className="muted">{runtimeText(notice)}</p>}
      <Button className="secondary-button" isDisabled={exiting} onPress={() => void exit()}>{exiting ? <LoaderCircle size={16} className="spin" /> : <ArrowLeft size={16} />}{exiting ? 'Kaydın korunuyor…' : ready ? 'Kaydet ve çık' : 'Oyunlara dön'}</Button>
      {(saveError || error) && <Button variant="ghost" isDisabled={exiting} onPress={() => void exit(true)}><X size={16} /> Kaydetmeden çık</Button>}
    </div></div>}
  </section>;
}

export function GamePlayer({ id, onExit }: { id: string; onExit: () => void }) {
  useUiLanguage();
  const game = useQuery({ queryKey: ['games', id], queryFn: () => request<Game>(`/games/${id}`) });
  if (game.isPending) return <div className="connection-screen"><LoaderCircle className="spin" /><p>Oyunun hazırlanıyor…</p></div>;
  if (!game.data) return <div className="search-empty"><CircleAlert size={30} /><p className="form-error">{runtimeText(game.error?.message ?? '')}</p><Button onPress={onExit}>Oyunlara dön</Button></div>;
  if (game.data.kind === 'native') return game.data.playable ? <NativeGamePlayer game={game.data} onExit={onExit}/> : <NativeGameDetails game={game.data} onBack={onExit} />;
  return <WebGamePlayer id={id} onExit={onExit} />;
}
