import { useEffect, useMemo, useState, useSyncExternalStore } from 'react';
import { Button } from '@heroui/react';
import { useMutation, useQuery } from '@tanstack/react-query';
import { Check, CirclePause, Crown, DoorOpen, Film, MonitorPlay, Pause, Play, Radio, RefreshCw, Send, UsersRound, X } from 'lucide-react';
import { clock, request, type CatalogPage, type Session } from '../api';
import { browserPlayback } from '../playbackIdentity';
import { WatchController } from '../watch/WatchController';
import { watchTransport } from '../watch/transport';
import type { WatchCommand, WatchSummary } from '../watch/types';

export function useWatchController(session: Session) {
  const controller = useMemo(() => new WatchController(watchTransport(session), browserPlayback.clientId, browserPlayback.nextRevision), [session.userId, session.sessionId]);
  useEffect(() => {
    controller.start();
    const pulse = () => { void controller.pulse(); }; const leave = () => { void controller.leave(); };
    window.addEventListener('home-rooms-changed', pulse); window.addEventListener('online', pulse); window.addEventListener('home-watch-stop', leave);
    window.addEventListener('home-session-expired', leave); window.addEventListener('pagehide', leave);
    const visible = () => { if (!document.hidden) pulse(); }; document.addEventListener('visibilitychange', visible);
    return () => { controller.stop(); window.removeEventListener('home-rooms-changed', pulse); window.removeEventListener('online', pulse); window.removeEventListener('home-watch-stop', leave); window.removeEventListener('home-session-expired', leave); window.removeEventListener('pagehide', leave); document.removeEventListener('visibilitychange', visible); };
  }, [controller]);
  return controller;
}
export function useWatchState(controller: WatchController) { return useSyncExternalStore(controller.subscribe, controller.snapshot); }
export function WatchRoomsView({ controller, onJoin }: { controller: WatchController; onJoin: (room: string, role: 'player' | 'controller') => Promise<void> }) {
  const state = useWatchState(controller);
  const rooms = useQuery({ queryKey: ['watch-rooms'], queryFn: ({ signal }) => request<WatchSummary[]>('/watch/rooms', { signal }), refetchInterval: 10000 });
  const join = useMutation({ mutationFn: ({ id, role }: { id: string; role: 'player' | 'controller' }) => onJoin(id, role) });
  return <section className="watch-library">
    <header className="watch-heading"><div><p className="eyebrow">AYNI HİKÂYE, AYNI AN</p><h1>Birlikte daha güzel.</h1><p className="muted">Davet edildiğin odalar ve kendi açık oynatıcıların.</p></div><Button variant="ghost" onPress={() => { void rooms.refetch(); }}><RefreshCw size={16} /> Yenile</Button></header>
    {state.connection && <WatchRoomPanel controller={controller} full />}
    {rooms.isPending && <p className="field-hint" role="status">Odalar yükleniyor…</p>}
    {!rooms.isPending && !rooms.data?.length && <div className="search-empty"><UsersRound size={32} /><h3>Birlikte bir şey açalım.</h3><p>Kütüphaneden bir dosyayı oynat. Oynatıcıdaki “Birlikte” düğmesinden oda kurabilir veya o cihazın kumandasını açabilirsin.</p></div>}
    <div className="watch-grid">{rooms.data?.map(room => <article className="watch-card" key={room.id}>
      <div className="watch-card-icon">{room.mode === 'remote' ? <MonitorPlay size={28} /> : <Film size={28} />}</div>
      <div><span className="eyebrow">{room.mode === 'remote' ? 'KENDİ OYNATICIN' : room.owned ? 'SENİN ODAN' : 'ODA DAVETİ'}</span><h3>{room.title}</h3><p>{room.mode === 'remote' ? room.device : `${room.ownerName} · ${room.players} oynatıcı`}</p>
        <div className="content-actions">{state.connection?.room.id === room.id ? <span className="watch-connected"><Check size={15} /> Bu sekmede bağlı</span> : <>
          {room.mode === 'together' && <Button className="secondary-button" size="sm" onPress={() => join.mutate({ id: room.id, role: 'player' })} isDisabled={join.isPending || state.busy}><Play size={14} /> Katıl ve oynat</Button>}
          <Button variant="ghost" size="sm" onPress={() => join.mutate({ id: room.id, role: 'controller' })} isDisabled={join.isPending || state.busy}><Radio size={15} /> {room.mode === 'remote' ? 'Kumandayı aç' : 'Yalnız oda ekranı'}</Button>
        </>}</div></div>
    </article>)}</div>
    {(rooms.error || join.error) && <p className="form-error" role="alert">{rooms.error?.message ?? join.error?.message}</p>}
    {!state.connection && state.message && <p className="field-hint" role="status">{state.message}</p>}
  </section>;
}
export function WatchPlayerPanel({ controller, ready, onPrepare }: { controller: WatchController; ready: boolean; onPrepare: () => Promise<boolean> }) {
  const state = useWatchState(controller); const create = useMutation({ mutationFn: (mode: 'together' | 'remote') => controller.create(mode) });
  if (state.connection) return <WatchRoomPanel controller={controller} onPrepare={onPrepare} />;
  return <section className="watch-panel"><div className="watch-panel-title"><UsersRound size={18} /><h3>Bu oynatıcıyla paylaş</h3></div><p className="field-hint">Odaya katılan herkes kendi hesabının erişimini ve oynatma kotasını kullanır.</p><div className="content-actions">
    <Button variant="ghost" onPress={() => create.mutate('together')} isDisabled={!ready || create.isPending}><UsersRound size={16} /> Birlikte izle / dinle</Button>
    <Button variant="ghost" onPress={() => create.mutate('remote')} isDisabled={!ready || create.isPending}><Radio size={16} /> Uzak kumandayı aç</Button>
  </div>{(create.error || state.message) && <p className="form-error" role="status">{create.error?.message ?? state.message}</p>}</section>;
}
export function WatchRoomPanel({ controller, full = false, onPrepare }: { controller: WatchController; full?: boolean; onPrepare?: () => Promise<boolean> }) {
  const state = useWatchState(controller); const connection = state.connection; const room = connection?.room;
  const [name, setName] = useState(''); const [search, setSearch] = useState(''); const [selecting, setSelecting] = useState(false); const [draft, setDraft] = useState<number | null>(null); const [position, setPosition] = useState(controller.position());
  const action = useMutation({ mutationFn: (input: WatchCommand) => controller.command(input), onSuccess: (_, input) => { if (input.action === 'invite') setName(''); if (input.action === 'media') { setSearch(''); setSelecting(false); } } });
  const prepare = useMutation({ mutationFn: async () => { if (await onPrepare?.()) await controller.pulse(); } });
  const files = useQuery({ queryKey: ['watch-media', search], queryFn: ({ signal }) => request<CatalogPage>(`/catalog?q=${encodeURIComponent(search)}&limit=12`, { signal }), enabled: selecting && !!room?.canManage });
  const history = useQuery({ queryKey: ['watch-events', room?.id], queryFn: ({ signal }) => request<{ action: string; actor: string; revision: number; createdAt: string }[]>(`/watch/rooms/${room!.id}/events`, { signal }), enabled: full && !!room, refetchInterval: 5000 });
  useEffect(() => { setPosition(controller.position()); const timer = setInterval(() => setPosition(controller.position()), 500); return () => clearInterval(timer); }, [controller]);
  if (!room || !connection) return null;
  const current = room.participants.find(member => member.id === connection.memberId);
  const paused = room.state === 'paused'; const disabled = state.busy || action.isPending || !room.canControl;
  const commands: Record<string, string> = { play: 'Oynat', pause: 'Duraklat', seek: 'Konum', rate: 'Hız', volume: 'Ses', invite: 'Davet', remove: 'Katılım kaldırıldı', control: 'Kumanda izni', media: 'Dosya değişimi', close: 'Oda kapandı' };
  const seek = (value: string) => { setDraft(null); action.mutate({ action: 'seek', positionSeconds: Number(value) }); };
  return <section className="watch-panel" aria-label={room.mode === 'remote' ? 'Uzak oynatıcı kumandası' : 'Birlikte izleme odası'}>
    <div className="watch-panel-title">{room.mode === 'remote' ? <Radio size={18} /> : <UsersRound size={18} />}<div><h3>{room.mode === 'remote' ? 'Kendi cihazına kumanda' : 'Birlikte izleme odası'}</h3><span>{room.ownerName} · {state.connected ? room.state === 'waiting' ? 'Hazır olma bekleniyor' : paused ? 'Duraklatıldı' : 'Birlikte oynuyor' : 'Bağlantı bekleniyor'}</span></div>
      <Button variant="ghost" size="sm" onPress={() => { void controller.leave(); }} aria-label={room.canManage && connection.role === 'player' ? 'Bu oynatıcıdaki odayı kapat' : 'Odadan ayrıl'}><DoorOpen size={15} /> Ayrıl</Button></div>
    {full && <h2 className="watch-title">{room.title}</h2>}
    <div className="watch-members">{room.participants.map(member => <div className={`watch-member ${member.connected ? '' : 'watch-member--away'}`} key={member.id}><span className={member.ready || member.role === 'controller' ? 'status-dot' : 'status-dot offline'} /><div><strong>{member.name}{member.owner && <Crown size={11} />}{member.id === connection.memberId && <small>Sen</small>}</strong><small>{member.role === 'controller' ? 'Kumanda ekranı' : !member.connected ? 'Bağlantı kesildi' : member.state === 'gesture' ? 'Cihazda oynatma izni bekliyor' : member.state === 'failed' ? 'Oynatma hatası' : member.ready ? 'Hazır' : 'Hazırlanıyor'} · {member.device}</small></div></div>)}</div>
    {onPrepare && connection.role === 'player' && current?.state === 'gesture' && <Button className="secondary-button" onPress={() => prepare.mutate()} isDisabled={prepare.isPending}><Play size={15} /> Bu cihazda oynatmaya izin ver</Button>}
    {full && <div className="watch-transport"><Button isIconOnly aria-label={paused ? 'Odada oynat' : 'Odayı duraklat'} className="transport-button" onPress={() => action.mutate({ action: paused ? 'play' : 'pause' })} isDisabled={disabled}>{paused ? <Play size={19} /> : <Pause size={19} />}</Button>
      <span>{clock(draft ?? position)}</span><input aria-label="Ortak oynatma konumu" type="range" min={0} max={room.durationSeconds} step={.1} value={draft ?? position} disabled={disabled}
        onChange={event => setDraft(Number(event.target.value))} onPointerUp={event => seek(event.currentTarget.value)} onPointerCancel={() => setDraft(null)} onKeyUp={event => { if (['ArrowLeft', 'ArrowRight', 'Home', 'End', 'Enter'].includes(event.key)) seek(event.currentTarget.value); }} /><span>{clock(room.durationSeconds)}</span></div>}
    {full && <div className="form-columns"><label className="field"><span>Ortak hız</span><select value={room.rate} onChange={event => action.mutate({ action: 'rate', rate: Number(event.target.value) })} disabled={disabled}>{[.5, .75, 1, 1.25, 1.5, 2].map(rate => <option key={rate} value={rate}>{rate}×</option>)}</select></label>
      {room.mode === 'remote' && <label className="field"><span>Uzaktaki ses seviyesi</span><select value={room.muted ? 'muted' : String(Math.round(room.volume * 100))} disabled={disabled} onChange={event => action.mutate({ action: 'volume', volume: event.target.value === 'muted' ? room.volume : Number(event.target.value) / 100, muted: event.target.value === 'muted' })}><option value="muted">Sessiz</option>{Array.from(new Set([0, 10, 20, 30, 40, 50, 60, 70, 80, 90, 100, Math.round(room.volume * 100)])).sort((a, b) => a - b).map(volume => <option key={volume} value={volume}>{volume}%</option>)}</select></label>}</div>}
    {!room.canControl && <p className="field-hint"><CirclePause size={13} /> Ortak oynatma kumandası ev sahibinde. Ses, altyazı ve kalite bu cihazda sana ait.</p>}
    {room.canManage && room.mode === 'together' && <div className="home-form watch-invites"><form onSubmit={event => { event.preventDefault(); action.mutate({ action: 'invite', name: name.trim() }); }}><label className="field"><span>Hane hesabını davet et</span><input value={name} maxLength={60} onChange={event => setName(event.target.value)} placeholder="Home hesap adı" /></label><Button type="submit" variant="ghost" isDisabled={!name.trim() || state.busy}><Send size={15} /> Davet et</Button></form>
      <label className="check-field"><input type="checkbox" checked={room.guestControl} disabled={state.busy} onChange={event => action.mutate({ action: 'control', enabled: event.target.checked })} /> Katılımcılar da ortak oynatma/konumu yönetebilsin</label>
      {room.invitations.filter(invite => !invite.removed).map(invite => <div className="watch-invitation" key={invite.userId}><span>{invite.name}</span><Button isIconOnly size="sm" variant="ghost" aria-label={`${invite.name} davetini ve oda erişimini kaldır`} onPress={() => action.mutate({ action: 'remove', userId: invite.userId })} isDisabled={state.busy}><X size={14} /></Button></div>)}
    </div>}
    {room.canManage && <div className="content-actions"><Button variant="ghost" size="sm" onPress={() => setSelecting(value => !value)}><Film size={14} /> Paylaşılan dosyayı değiştir</Button><Button variant="ghost" size="sm" onPress={() => action.mutate({ action: 'close' })} isDisabled={state.busy}>Odayı kapat</Button></div>}
    {selecting && room.canManage && <div className="home-form"><label className="field"><span>Ortak kütüphaneden seç</span><input value={search} onChange={event => setSearch(event.target.value)} placeholder="Dosya ara…" /></label><p className="field-hint">Yeni dosya tüm davetli hesaplara açık olmalı.</p><div className="watch-media-results">{files.data?.items.filter(item => item.available).map(item => <Button variant="ghost" size="sm" key={item.id} onPress={() => action.mutate({ action: 'media', mediaId: item.id })} isDisabled={state.busy || item.id === room.mediaId}>{item.title}</Button>)}</div>{files.error && <p className="form-error">{files.error.message}</p>}</div>}
    <p className={action.error ? 'form-error' : 'field-hint'} role="status">{action.error?.message || state.message || room.message}</p>
    {full && !!history.data?.length && <details className="advanced-fields"><summary>Oda işlemleri</summary><div className="watch-history">{history.data.slice(0, 10).map(event => <p key={event.revision}><strong>{commands[event.action] ?? event.action}</strong> · {event.actor} · {new Date(event.createdAt).toLocaleTimeString('tr')}</p>)}</div></details>}
  </section>;
}
