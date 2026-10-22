import { lazy, Suspense, useEffect, useRef, useState, type CSSProperties, type KeyboardEvent } from 'react';
import { createPortal } from 'react-dom';
import { Button } from '@heroui/react';
import { useQuery } from '@tanstack/react-query';
import { motion } from 'motion/react';
import { ChevronDown, Disc3, Expand, Image, ListMusic, LoaderCircle, Pause, Play, Repeat, Repeat1, Settings2, Shuffle, SkipBack, SkipForward, Volume2, VolumeX, X } from 'lucide-react';
import { appUrl, clock, mutate, request, type MediaItem, type MusicPlaybackPreferences } from '../api';
import { GaplessEngine, type MusicAudioState } from '../audio/GaplessEngine';
import { createMusicAudioTransport } from '../audio/MusicAudioTransport';
import { PlaybackUsagePanel } from './PlaybackUsage';
import { browserPlayback } from '../playbackIdentity';
import { useSession } from './SessionContext';
import { MusicCover } from './MusicReleases';
import { MusicFavorite } from './MusicTools';
import { MusicPlaybackSettings } from '../music/MusicPlaybackSettings';
import { MusicVisualMode } from '../music/MusicVisualMode';
import { runtimeText, translate as t, useUiLanguage } from '../i18n';
import '../music/music-experience.css';
import { Lyrics } from './Lyrics';
import type { WatchController } from '../watch/WatchController';
import { useControllerCommands } from '../controller/react';

interface Props { item: MediaItem; queue: MediaItem[]; index: number; playbackKey: number; shuffle: boolean; repeat: string; onTrack: (index: number) => void; onSelect: (index: number) => void; onClose: () => void; onNext: () => void; onPrevious: () => void; onEnded: () => void; onShuffle: () => void; onRepeat: () => void; roomController?: WatchController }
const CompatiblePlayer = lazy(() => import('./Player').then(module => ({ default: module.Player })));
export function MusicPlayer(props: Props) {
  const [compatible, setCompatible] = useState(!('AudioContext' in window));
  return compatible ? <Suspense fallback={<div className="video-player-loading" role="status"><LoaderCircle className="spin" size={28}/></div>}><CompatiblePlayer {...props} queueLength={props.queue.length} /></Suspense> : <GaplessPlayer {...props} onCompatible={() => setCompatible(true)} />;
}
function GaplessPlayer(props: Props & { onCompatible: () => void }) {
  useUiLanguage(); const { item, queue, index, playbackKey, shuffle, repeat, onTrack, onSelect, onClose, onNext, onPrevious, onShuffle, onRepeat, onCompatible } = props;
  const session = useSession(); const panel = useRef<HTMLDivElement>(null); const engine = useRef<GaplessEngine | null>(null);
  const opener = useRef(document.activeElement as HTMLElement | null); const callbacks = useRef({ onTrack }); callbacks.current = { onTrack };
  const owner = useRef(session.userId); const clientId = useRef(browserPlayback.clientId);
  const [state, setState] = useState<MusicAudioState>({ index, position: 0, duration: item.durationSeconds ?? 0, playing: false, buffering: true, error: '', notice: '', info: null });
  const [minimized, setMinimized] = useState(true); const [side, setSide] = useState('queue'); const [visual, setVisual] = useState(false); const [volume, setVolume] = useState(1); const [rate, setRate] = useState(1); const [draft, setDraft] = useState<number | null>(null); const [saveError, setSaveError] = useState(false);
  const preferences = useQuery({ queryKey: ['music-playback-preferences'], queryFn: () => request<MusicPlaybackPreferences>('/music/playback-preferences') });
  const queueKey = queue.map(value => `${value.id}:${value.modifiedAt}:${value.sizeBytes}`).join('|');
  const current = useRef({ queue, index, item }); current.current = { queue, index, item };
  useEffect(() => {
    let live = true;
    const audio = new GaplessEngine({ state: value => { if (live) setState(value); }, track: value => callbacks.current.onTrack(value), external: value => callbacks.current.onTrack(value),
      progress: (id, positionSeconds, durationSeconds, keepalive) => { void mutate(`/media/${id}/progress`, { positionSeconds, durationSeconds, expectedUserId: owner.current, clientId: clientId.current, sequence: browserPlayback.nextProgress() }, 'PUT', keepalive)
        .then(() => { if (live) setSaveError(false); }).catch(() => { if (live) setSaveError(true); }); } }, createMusicAudioTransport(clientId.current, browserPlayback.nextRevision));
    engine.current = audio;
    const visibility = () => { if (document.hidden) audio.save(true); else audio.heartbeat(); }; document.addEventListener('visibilitychange', visibility);
    const pulse = () => audio.heartbeat(); window.addEventListener('home-playback-changed', pulse);
    return () => { live = false; document.removeEventListener('visibilitychange', visibility); window.removeEventListener('home-playback-changed', pulse); audio.dispose(); engine.current = null; };
  }, []);
  useEffect(() => { const value = current.current; engine.current?.load(value.queue, value.index, value.item.isWatched ? 0 : value.item.positionSeconds); }, [playbackKey]);
  useEffect(() => { engine.current?.appendQueue(current.current.queue); }, [queueKey]);
  useEffect(() => { engine.current?.setOrder(shuffle, repeat); }, [shuffle, repeat]);
  useEffect(() => { engine.current?.setPreferences(preferences.data ?? { replayGain: 'track', preampDb: 0, preventClipping: true }); }, [preferences.data]);
  useEffect(() => {
    const content = document.getElementById('app-content'); if (content) content.inert = !minimized;
    document.documentElement.classList.toggle('player-open', !minimized); if(!minimized)panel.current?.focus();
    return () => { if (content) content.inert = false; document.documentElement.classList.remove('player-open'); };
  }, [minimized]);
  useEffect(() => { setDraft(null); }, [item.id]);
  useEffect(() => { const open=()=>setVisual(true);window.addEventListener('home-music-visual',open);return()=>window.removeEventListener('home-music-visual',open); }, []);
  useEffect(() => {
    if (!('mediaSession' in navigator)) return;
    navigator.mediaSession.metadata = new MediaMetadata({ title: item.title, artist: state.info?.artist ?? '', artwork: state.info?.albumId ? [{ src: new URL(appUrl(`/api/music/albums/${state.info.albumId}/artwork`), window.location.href).href }] : [] });
    const actions: Partial<Record<MediaSessionAction, MediaSessionActionHandler>> = { play: () => { if (!state.playing) engine.current?.toggle(); }, pause: () => { if (state.playing) engine.current?.toggle(); }, nexttrack: onNext, previoustrack: onPrevious, seekto: value => { if (value.seekTime !== undefined) engine.current?.seek(value.seekTime); } };
    for (const [action, handler] of Object.entries(actions)) { try { navigator.mediaSession.setActionHandler(action as MediaSessionAction, handler); } catch { /* Device action is optional. */ } }
    navigator.mediaSession.playbackState = state.playing ? 'playing' : 'paused';
    return () => { for (const action of Object.keys(actions)) { try { navigator.mediaSession.setActionHandler(action as MediaSessionAction, null); } catch { /* Optional device action. */ } } };
  }, [item.id, item.title, state.info?.artist, state.info?.albumId, state.playing, onNext, onPrevious]);
  function close() { engine.current?.save(true); onClose(); requestAnimationFrame(() => { if (opener.current?.isConnected) opener.current.focus(); else document.querySelector<HTMLElement>('[aria-current="page"]')?.focus(); }); }
  useControllerCommands(panel, action => {
    if (action === 'back') { if (!minimized) setMinimized(true); else close(); return true; }
    if (action === 'menu') { setMinimized(false); setSide('settings'); return true; }
    if (action === 'playpause') { engine.current?.toggle(); return true; }
    if (action === 'seekback' || action === 'seekforward') { engine.current?.seek(Math.max(0, Math.min(state.duration, state.position + (action === 'seekback' ? -10 : 10)))); return true; }
    if (action === 'previous' || action === 'next') { (action === 'previous' ? onPrevious : onNext)(); return true; }
    return false;
  });
  function keydown(event: KeyboardEvent<HTMLDivElement>) {
    if (event.key === 'Escape') { event.preventDefault(); setMinimized(true); }
    if (event.key === ' ' && event.target === panel.current) { event.preventDefault(); engine.current?.toggle(); }
    if (!minimized && event.key === 'Tab' && panel.current) {
      const targets = [...panel.current.querySelectorAll<HTMLElement>('button:not([disabled]),input:not([disabled]),select:not([disabled])')].filter(element => element.getClientRects().length > 0); const first = targets[0], last = targets.at(-1);
      if (event.shiftKey && (document.activeElement === first || document.activeElement === panel.current)) { event.preventDefault(); last?.focus(); }
      else if (!event.shiftKey && (document.activeElement === last || document.activeElement === panel.current)) { event.preventDefault(); first?.focus(); }
    }
  }
  const position = draft ?? state.position; const seek = (value: string) => { engine.current?.seek(Number(value)); setDraft(null); };
  const changeVolume=(value:number)=>{setVolume(value);engine.current?.setVolume(value);};
  return createPortal(<>{!minimized&&<div className="music-now-backdrop"/>}<motion.div ref={panel} data-controller-player-dock={minimized?'music':undefined} tabIndex={-1} role={minimized?'region':'dialog'} aria-modal={!minimized||undefined} aria-label={item.title} onKeyDown={keydown} className={`music-now-player ${minimized?'music-dock is-mini':'is-expanded'}`} initial={{opacity:0,y:12}} animate={{opacity:1,y:0}} exit={{opacity:0,y:12}}>
    {!minimized&&<header className="music-now-heading"><Button variant="ghost" onPress={()=>setMinimized(true)}><ChevronDown size={21}/> Kütüphaneye dön</Button><span className="eyebrow">ŞİMDİ DİNLİYORSUN</span><div><Button variant="secondary" onPress={()=>setVisual(true)}><Image size={18}/> Görsel mod</Button><Button isIconOnly variant="ghost" aria-label="Oynatıcıyı kapat" onPress={close}><X size={21}/></Button></div></header>}
    <div className="music-now-main"><div className="music-now-record"><button className="music-now-cover" onClick={()=>{if(minimized)setMinimized(false);}} aria-label="Oynatıcıyı genişlet" tabIndex={minimized?0:-1}>{state.info?.albumId?<MusicCover url={appUrl(`/api/music/albums/${state.info.albumId}/artwork`)}/>:<Disc3 size={minimized?34:110} strokeWidth={.7}/>}</button><div className="music-dock-title"><strong title={item.title}>{item.title}</strong><span>{minimized&&state.error?runtimeText(state.error):state.info?.artist||t('Kesintisiz müzik')}</span></div>{!minimized&&<MusicFavorite mediaId={item.id}/>}</div>
    <div className="music-now-transport"><div className="music-now-buttons"><Button isIconOnly variant="ghost" className="music-order-button" aria-label="Karışık çal" aria-pressed={shuffle} onPress={onShuffle}><Shuffle size={18}/></Button><Button isIconOnly variant="ghost" className="music-previous-button" aria-label="Önceki parça" onPress={onPrevious}><SkipBack size={21}/></Button><Button isIconOnly data-controller-player-primary variant="primary" className="music-play-pause" aria-label={state.playing?'Duraklat':'Oynat'} onPress={()=>engine.current?.toggle()}>{state.buffering?<LoaderCircle size={22} className="spin"/>:state.playing?<Pause size={23} fill="currentColor"/>:<Play size={23} fill="currentColor"/>}</Button><Button isIconOnly variant="ghost" aria-label="Sonraki parça" onPress={onNext}><SkipForward size={21}/></Button><Button isIconOnly variant="ghost" className="music-order-button" aria-label={repeat==='off'?'Tekrar kapalı':repeat==='one'?'Tek parçayı tekrarla':'Listeyi tekrarla'} aria-pressed={repeat!=='off'} onPress={onRepeat}>{repeat==='one'?<Repeat1 size={18}/>:<Repeat size={18}/>}</Button></div>
      <div className="music-now-seek"><span>{clock(position)}</span><input type="range" aria-label="Oynatma konumu" min={0} max={state.duration||1} step={.1} value={Math.min(position,state.duration||1)} style={{'--played':`${state.duration?position/state.duration*100:0}%`} as CSSProperties} onChange={event=>setDraft(Number(event.target.value))} onPointerUp={event=>seek(event.currentTarget.value)} onPointerCancel={()=>setDraft(null)} onKeyUp={event=>{if(['ArrowLeft','ArrowRight','Home','End','PageUp','PageDown'].includes(event.key))seek(event.currentTarget.value);}}/><span>{clock(state.duration)}</span></div>
    </div>
    <div className="music-now-accessories"><div className="music-dock-volume"><Button isIconOnly variant="ghost" aria-label={volume?'Sesi kapat':'Sesi aç'} onPress={()=>changeVolume(volume?0:1)}>{volume?<Volume2 size={19}/>:<VolumeX size={19}/>}</Button><input type="range" aria-label="Ses seviyesi" min={0} max={1} step={.05} value={volume} onChange={event=>changeVolume(Number(event.target.value))}/></div>{minimized&&<><Button isIconOnly variant="ghost" aria-label="Çalma sırası" onPress={()=>{setSide('queue');setMinimized(false);}}><ListMusic size={19}/></Button><Button isIconOnly variant="ghost" aria-label="Görsel müzik modu" onPress={()=>setVisual(true)}><Image size={19}/></Button><Button isIconOnly variant="ghost" className="music-expand-button" aria-label="Oynatıcıyı genişlet" onPress={()=>setMinimized(false)}><Expand size={18}/></Button><Button isIconOnly variant="ghost" aria-label="Oynatıcıyı kapat" onPress={close}><X size={18}/></Button></>}</div>
    </div>
    {!minimized&&<aside className="music-now-inspector"><nav aria-label="Dinleme paneli"><Button variant={side==='queue'?'secondary':'ghost'} onPress={()=>setSide('queue')}><ListMusic size={17}/> Sıradaki</Button><Button variant={side==='lyrics'?'secondary':'ghost'} onPress={()=>setSide('lyrics')}>Sözler</Button><Button variant={side==='settings'?'secondary':'ghost'} onPress={()=>setSide('settings')}><Settings2 size={17}/> Ses</Button></nav><div className="music-now-panel">{side==='queue'&&<><p className="music-help">{index+1} / {queue.length}</p>{queue.map((entry,position)=><button className="music-queue-row" aria-current={position===index?'true':undefined} key={`${entry.id}:${position}`} disabled={!entry.available} onClick={()=>onSelect(position)}><span>{position+1}</span><strong>{entry.title}</strong><small>{clock(entry.durationSeconds??0)}</small>{position===index&&state.playing?<Volume2 size={16}/>:<Play size={16}/>}</button>)}</>}{side==='lyrics'&&<Lyrics key={item.id} mediaId={item.id} seconds={state.position} duration={state.duration} onSeek={value=>engine.current?.seek(value)}/>} {side==='settings'&&<><MusicPlaybackSettings/><label className="music-field"><span>Hız</span><select aria-label="Oynatma hızı" value={rate} onChange={event=>{const value=Number(event.target.value);setRate(value);engine.current?.setSpeed(value);}}>{[.5,.75,1,1.25,1.5,2].map(value=><option value={value} key={value}>{value}×</option>)}</select></label><Button variant="ghost" onPress={onCompatible}>Özgün oynatıcı · birlikte dinle / kumanda</Button></>}</div></aside>}
    {state.error&&<div className="music-now-error" role="alert"><span>{runtimeText(state.error)}</span><Button variant="ghost" onPress={()=>engine.current?.toggle()}>Yeniden dene</Button><Button variant="ghost" onPress={onCompatible}>Özgün oynatıcı</Button>{!minimized&&<PlaybackUsagePanel/>}</div>}
    {!minimized&&<footer className="music-now-footnote"><span>Kesintisiz · Stereo PCM 48 kHz</span><span>{runtimeText(state.notice||(saveError?'Dinleme konumu kaydedilemedi.':!state.playing&&!state.buffering&&state.position===0?'Başlamak için oynat düğmesine dokun.':'Konumun otomatik kaydedilir.'))}</span></footer>}
  </motion.div>{visual&&<MusicVisualMode title={item.title} subtitle={state.info?.artist??''} playing={state.playing} onToggle={()=>engine.current?.toggle()} onPrevious={onPrevious} onNext={onNext} volume={volume} onVolume={changeVolume} onClose={()=>setVisual(false)}/>}</>,document.body);
}
