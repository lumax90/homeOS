import { useState } from 'react';
import { Button } from '@heroui/react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { MonitorPlay, Save } from 'lucide-react';
import { clock, date, mutate, request, type PlaybackActivity, type PlaybackDevice, type PlaybackLimits, type PlaybackUsage, type UserPlaybackLimits } from '../api';

function useRefreshPlayback() {
  const client = useQueryClient();
  return () => Promise.all(['playback-usage', 'playback-devices', 'user-playback-limits'].map(key => client.invalidateQueries({ queryKey: [key] })));
}
const modes: Record<string, string> = { preparing: 'Hazırlanıyor', direct: 'Doğrudan', remux: 'HLS', 'audio-transcode': 'HLS · ses dönüşümü', 'video-transcode': 'HLS · video dönüşümü', pcm: 'Kesintisiz müzik' };
const reasons: Record<string, string> = { closed: 'Kapatıldı', replaced: 'Yeni oynatma komutu', expired: 'Bağlantı süresi doldu', 'paused-timeout': '15 dakika duraklatıldı', 'access-revoked': 'Erişim iptal edildi', 'source-changed': 'Dosya veya eşleşme değişti', 'limits-changed': 'Sınırlar değişti', 'server-restarted': 'Sunucu yeniden başladı', 'server-stopped': 'Sunucu durdu', administrator: 'Yönetici kapattı', failed: 'Hazırlık başarısız', cancelled: 'İptal edildi' };

export function PlaybackUsagePanel({ all = false }: { all?: boolean }) {
  const [open, setOpen] = useState(false); const [history, setHistory] = useState(false); const refresh = useRefreshPlayback();
  const usage = useQuery({ queryKey: ['playback-usage', all], queryFn: () => request<PlaybackUsage>(`/account/playback?all=${all}`), enabled: open, refetchInterval: open ? 10000 : false });
  const devices = useQuery({ queryKey: ['playback-devices'], queryFn: () => request<PlaybackDevice[]>('/playback/devices'), enabled: open && all });
  const close = useMutation({ mutationFn: (id: string) => mutate(`/account/playback/${id}`, undefined, 'DELETE'), onSuccess: refresh });
  function activity(item: PlaybackActivity) {
    return <article className="playback-activity" key={item.id}><div><strong>{item.title}</strong><span>{all ? `${item.displayName} · ` : ''}{modes[item.mode] ?? item.mode} · {clock(item.positionSeconds)}{item.status === 'starting' ? ' · Hazırlanıyor' : item.paused && !item.endedAt ? ' · Duraklatıldı' : ''}</span>
      <small title={item.deviceName}>{item.currentDevice ? 'Bu cihaz · ' : ''}{item.deviceName}</small><small>{date(item.endedAt ?? item.startedAt)}{item.endReason ? ` · ${reasons[item.endReason] ?? item.endReason}` : ''}</small></div>
      {!item.endedAt && <Button variant="ghost" size="sm" isDisabled={close.isPending} onPress={() => close.mutate(item.id)}>Oynatmayı kapat</Button>}
    </article>;
  }
  return <section className="playback-usage"><Button variant="ghost" size="sm" aria-expanded={open} onPress={() => setOpen(value => !value)}><MonitorPlay size={16} />{all ? 'Sunucudaki oynatmalar ve sınırlar' : 'Açık oynatmalarım'}</Button>
    {open && <div className="home-form automation-form">{usage.data && <>
      {!all && <p className="field-hint">Hesabın: {usage.data.user.maximumStreams} eşzamanlı oynatma · {usage.data.user.maximumDevices} etkin cihaz. Bu cihaz: {usage.data.deviceMaximumStreams} oynatma.</p>}
      <p className="field-hint">{usage.data.active.length} açık oynatma. Video, HLS ve kesintisiz müzik aynı sınırı kullanır. Bağlantısız oynatma 2 dakika, duraklatılmış oynatma 15 dakika sonra kapanır.</p>
      {usage.data.active.map(activity)}{!usage.data.active.length && <p className="muted">Açık oynatma yok.</p>}
      <Button size="sm" variant="ghost" aria-expanded={history} onPress={() => setHistory(value => !value)}>Oynatma oturum geçmişi</Button>
      {history && <div>{usage.data.history.map(activity)}{!usage.data.history.length && <p className="field-hint">Henüz bir oturum geçmişi yok.</p>}<p className="field-hint">Son 100 oturum gösterilir. Sunucu genelinde en fazla 90 gün / 10.000 kapanmış oturum tutulur.</p></div>}
      {all && <><ServerLimits key={usage.data.server.revision} initial={usage.data.server} /><h4>Cihaz sınırları</h4><p className="field-hint">Cihaz, sunucunun imzalı tarayıcı kaydıdır. Aynı tarayıcıdaki sekmeler ve profil geçişleri ortak sınırı kullanır.</p>{devices.data?.map(device => <DeviceLimits key={device.id + ':' + device.revision} initial={device} />)}</>}
    </>}{usage.isLoading && <p className="field-hint" role="status">Oynatmalar yükleniyor…</p>}{(usage.error || devices.error || close.error) && <p className="form-error" role="alert">{usage.error?.message || devices.error?.message || close.error?.message}</p>}</div>}
  </section>;
}
function ServerLimits({ initial }: { initial: PlaybackLimits }) {
  const [value, setValue] = useState(initial); const refresh = useRefreshPlayback();
  const save = useMutation({ mutationFn: () => mutate('/playback/limits', value, 'PUT'), onSuccess: refresh });
  return <form className="home-form playback-limits" onSubmit={event => { event.preventDefault(); save.mutate(); }}><h4>Sunucu sınırları</h4><div className="form-columns">
    <label className="field"><span>Toplam eşzamanlı oynatma</span><input type="number" min={1} max={64} required value={value.maximumStreams} onChange={event => setValue(current => ({ ...current, maximumStreams: Number(event.target.value) }))} /></label>
    <label className="field"><span>Varsayılan cihaz sınırı</span><input type="number" min={1} max={16} required value={value.maximumStreamsPerDevice} onChange={event => setValue(current => ({ ...current, maximumStreamsPerDevice: Number(event.target.value) }))} /></label>
    </div><p className="field-hint">Kaydettiğinde açık oynatmalar yeni sınırlarla yeniden başlatılmalı. FFmpeg'in ayrı dönüştürme ve disk sınırları da geçerlidir.</p><Button type="submit" variant="ghost" size="sm" isDisabled={save.isPending}><Save size={14} /> Sınırları kaydet</Button>{save.error && <p className="form-error">{save.error.message}</p>}</form>;
}
function DeviceLimits({ initial }: { initial: PlaybackDevice }) {
  const [name, setName] = useState(initial.name); const [maximum, setMaximum] = useState(initial.maximumStreams === null ? '' : String(initial.maximumStreams)); const refresh = useRefreshPlayback();
  const save = useMutation({ mutationFn: () => mutate(`/playback/devices/${initial.id}`, { name, maximumStreams: maximum === '' ? null : Number(maximum), revision: initial.revision }, 'PUT'), onSuccess: refresh });
  return <form className="home-form playback-limits" onSubmit={event => { event.preventDefault(); save.mutate(); }}><div className="form-columns"><label className="field"><span>{initial.current ? 'Bu cihazın adı' : 'Cihaz adı'}</span><input required maxLength={160} value={name} onChange={event => setName(event.target.value)} /></label><label className="field"><span>Oynatma sınırı · 0 kapalı</span><input type="number" min={0} max={16} value={maximum} placeholder="Genel cihaz sınırı" onChange={event => setMaximum(event.target.value)} /></label></div>
    <p className="field-hint">Boş değer genel cihaz sınırını kullanır. Kaydetmek bu cihazın açık oynatmalarını kapatır.</p><Button type="submit" variant="ghost" size="sm" isDisabled={save.isPending}>Cihazı kaydet</Button>{save.error && <p className="form-error">{save.error.message}</p>}</form>;
}
export function UserPlaybackLimitsEditor({ userId }: { userId: string }) {
  const settings = useQuery({ queryKey: ['user-playback-limits', userId], queryFn: () => request<UserPlaybackLimits>(`/users/${userId}/playback-limits`) });
  return <>{settings.data && <UserLimitsForm key={settings.data.revision} userId={userId} initial={settings.data} />}{settings.error && <p className="form-error">{settings.error.message}</p>}</>;
}
function UserLimitsForm({ userId, initial }: { userId: string; initial: UserPlaybackLimits }) {
  const [value, setValue] = useState(initial); const refresh = useRefreshPlayback();
  const save = useMutation({ mutationFn: () => mutate(`/users/${userId}/playback-limits`, value, 'PUT'), onSuccess: refresh });
  return <form className="home-form source-form" onSubmit={event => { event.preventDefault(); save.mutate(); }}><h3>Hesabın oynatma sınırları</h3><div className="form-columns">
    <label className="field"><span>Eşzamanlı oynatma · 0 kapalı</span><input type="number" min={0} max={16} required value={value.maximumStreams} onChange={event => setValue(current => ({ ...current, maximumStreams: Number(event.target.value) }))} /></label>
    <label className="field"><span>Aynı anda oynatan cihaz</span><input type="number" min={1} max={16} required value={value.maximumDevices} onChange={event => setValue(current => ({ ...current, maximumDevices: Number(event.target.value) }))} /></label>
    </div><p className="field-hint">Sunucu ve cihaz sınırları birlikte uygulanır. Kaydetmek bu hesabın açık oynatmalarını kapatır.</p><Button type="submit" variant="ghost" size="sm" isDisabled={save.isPending}>Oynatma sınırlarını kaydet</Button>{save.error && <p className="form-error">{save.error.message}</p>}</form>;
}
