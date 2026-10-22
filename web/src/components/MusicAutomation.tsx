import { useState, type ReactNode } from 'react';
import { Button, Label, Switch } from '@heroui/react';
import { useInfiniteQuery, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { RefreshCw, WandSparkles } from 'lucide-react';
import { date, mutate, request, type MonitoredMusicAlbum, type MusicAutomationJob, type MusicMonitor, type MusicMonitorSettings, type MusicPage, type ProviderAlbum } from '../api';
import { useSession } from './SessionContext';

const defaults: MusicMonitorSettings = { enabled: false, mode: 'future', primaryTypes: ['Album', 'EP', 'Single'], excludedSecondaryTypes: ['Live', 'Compilation', 'Remix', 'DJ-mix'], releaseStatuses: ['Official'], countries: [], formats: [], search: false, autoGrab: false, allowUpgrades: false, cutoff: 'lossless', cutoffBitrateKbps: 320, refreshHours: 12, searchIntervalMinutes: 360, delayMinutes: 0, maximumDailyGrabs: 2, quality: { losslessOnly: false, codecs: null, minimumBitrateKbps: 0, maximumSizeGiB: 5, minimumSeeders: 1, completeAlbum: true } };
const primaryNames: Record<string, string> = { Album: 'Albüm', EP: 'EP', Single: 'Tekli', Broadcast: 'Yayın', Other: 'Diğer' };
const secondary = ['Audio drama', 'Audiobook', 'Compilation', 'Demo', 'DJ-mix', 'Field recording', 'Interview', 'Live', 'Mixtape/Street', 'Remix', 'Soundtrack', 'Spokenword'];
export function MusicSwitch({ value, onChange, children, disabled }: { value: boolean; onChange: (value: boolean) => void; children: ReactNode; disabled?: boolean }) {
  return <Switch isSelected={value} onChange={onChange} isDisabled={disabled} size="sm"><Switch.Content><Switch.Control><Switch.Thumb /></Switch.Control><Label>{children}</Label></Switch.Content></Switch>;
}
export function MusicMonitorPanel({ kind, mbid, name }: { kind: 'artist' | 'album'; mbid: string; name: string }) {
  const [open, setOpen] = useState(false); const session = useSession();
  const monitors = useQuery({ queryKey: ['music-monitors'], queryFn: () => request<MusicMonitor[]>('/music/monitors'), enabled: session.canDiscover });
  const monitor = monitors.data?.find(item => item.kind === kind && item.mbid === mbid);
  return <section className="automation-panel"><Button variant="ghost" onPress={() => setOpen(value => !value)} aria-expanded={open}><WandSparkles size={15} />{monitor?.settings.enabled ? 'Müzik takibi etkin' : 'Takip ve otomasyon'}</Button>
    {open && monitors.data && <MonitorForm key={monitor?.revision ?? 0} kind={kind} mbid={mbid} name={name} monitor={monitor} />}{open && monitors.error && <p className="form-error">{monitors.error.message}</p>}
  </section>;
}
function MonitorForm({ kind, mbid, name, monitor }: { kind: string; mbid: string; name: string; monitor?: MusicMonitor }) {
  const session = useSession(); const client = useQueryClient(); const [settings, setSettings] = useState(monitor?.settings ?? defaults);
  const [countries, setCountries] = useState(settings.countries.join(', '));
  const set = <K extends keyof MusicMonitorSettings>(key: K, value: MusicMonitorSettings[K]) => setSettings(previous => ({ ...previous, [key]: value }));
  const toggle = (key: 'primaryTypes' | 'excludedSecondaryTypes' | 'releaseStatuses', value: string) => set(key, settings[key].includes(value) ? settings[key].filter(item => item !== value) : [...settings[key], value]);
  const save = useMutation({ mutationFn: () => mutate(`/music/monitors/${kind}/${mbid}`, { settings: { ...settings, countries: countries.split(',').map(value => value.trim().toUpperCase()).filter(Boolean) }, expectedRevision: monitor?.revision ?? 0 }, 'PUT'), onSuccess: () => client.invalidateQueries({ queryKey: ['music-monitors'] }) });
  return <div className="home-form automation-form"><h3>{name}</h3><MusicSwitch value={settings.enabled} onChange={value => set('enabled', value)}>Yeni yayınları düzenli takip et</MusicSwitch>
    {kind === 'artist' && <><label className="field"><span>Takip kapsamı</span><select value={settings.mode} onChange={event => set('mode', event.target.value)}><option value="future">Takibe başladığımdan sonraki yayınlar</option><option value="all">Geçmiş ve gelecek tüm yayınlar</option></select></label>
      <fieldset className="music-checks"><legend>Yayın türleri</legend>{Object.entries(primaryNames).map(([key, label]) => <label className="check-field" key={key}><input type="checkbox" checked={settings.primaryTypes.includes(key)} onChange={() => toggle('primaryTypes', key)} />{label}</label>)}</fieldset>
      <details><summary>İkincil tür istisnaları</summary><fieldset className="music-checks"><legend>Şu türleri otomatik takibe alma</legend>{secondary.map(value => <label className="check-field" key={value}><input type="checkbox" checked={settings.excludedSecondaryTypes.includes(value)} onChange={() => toggle('excludedSecondaryTypes', value)} />{value}</label>)}</fieldset></details></>}
    <label className="field"><span>Metadata yenileme · saat</span><input type="number" min={6} max={168} value={settings.refreshHours} onChange={event => set('refreshHours', Number(event.target.value))} /></label>
    {session.role === 'admin' && <><MusicSwitch value={settings.search} onChange={value => setSettings(previous => ({ ...previous, search: value, autoGrab: value && previous.autoGrab }))}>Eksik albümleri kaynaklarda ara</MusicSwitch>
      <MusicSwitch value={settings.autoGrab} disabled={!settings.search} onChange={value => set('autoGrab', value)}>Uygun albümü otomatik indir</MusicSwitch>
      <p className="field-hint">Arama tek başına bildirim üretir. Otomatik indirme ayrıca açıldığında, yayımlanmış albümler seçilen metadata ve kalite profiliyle edinilir.</p>
      {settings.search && <><MusicSwitch value={settings.allowUpgrades} onChange={value => set('allowUpgrades', value)}>Daha iyi ses kalitesine yükselt</MusicSwitch>
        <div className="form-columns"><label className="field"><span>Kalite hedefi</span><select value={settings.cutoff} onChange={event => set('cutoff', event.target.value)}><option value="lossy">Kayıplı · bitrate hedefi</option><option value="lossless">Kayıpsız</option><option value="hires">Yüksek çözünürlüklü kayıpsız</option></select></label><label className="field"><span>Kayıplı hedef · kbps</span><input type="number" min={64} max={1000} value={settings.cutoffBitrateKbps} onChange={event => set('cutoffBitrateKbps', Number(event.target.value))} /></label></div>
        <MusicSwitch value={settings.quality.losslessOnly} onChange={value => set('quality', { ...settings.quality, losslessOnly: value })}>Yalnız kayıpsız adayları kabul et</MusicSwitch>
        <div className="form-columns"><label className="field"><span>En az bitrate · kbps</span><input type="number" min={0} max={10000} value={settings.quality.minimumBitrateKbps} onChange={event => set('quality', { ...settings.quality, minimumBitrateKbps: Number(event.target.value) })} /></label><label className="field"><span>En fazla paket · GiB</span><input type="number" min={.01} max={1000} step={.1} value={settings.quality.maximumSizeGiB} onChange={event => set('quality', { ...settings.quality, maximumSizeGiB: Number(event.target.value) })} /></label></div>
        <fieldset className="music-checks"><legend>İzin verilen baskı durumu</legend>{['Official', 'Promotion', 'Bootleg', 'Pseudo-Release'].map(value => <label className="check-field" key={value}><input type="checkbox" checked={settings.releaseStatuses.includes(value)} onChange={() => toggle('releaseStatuses', value)} />{value}</label>)}</fieldset>
        <div className="form-columns"><label className="field"><span>Ülke kodları · boşsa tümü</span><input value={countries} onChange={event => setCountries(event.target.value)} placeholder="TR, GB, US" /></label><label className="field"><span>Baskı biçimi</span><select value={settings.formats[0] ?? ''} onChange={event => set('formats', event.target.value ? [event.target.value] : [])}><option value="">Tümü</option><option>Digital Media</option><option>CD</option><option>Vinyl</option></select></label></div>
        <div className="form-columns"><label className="field"><span>Kaynak aralığı · dakika</span><input type="number" min={30} max={10080} value={settings.searchIntervalMinutes} onChange={event => set('searchIntervalMinutes', Number(event.target.value))} /></label><label className="field"><span>Aday bekleme · dakika</span><input type="number" min={0} max={1440} value={settings.delayMinutes} onChange={event => set('delayMinutes', Number(event.target.value))} /></label></div>
        <label className="field"><span>Bu kuralın günlük hesap bütçesi · albüm</span><input type="number" min={1} max={20} value={settings.maximumDailyGrabs} onChange={event => set('maximumDailyGrabs', Number(event.target.value))} /></label><p className="field-hint">Hesabın bugün otomatik edindiği toplam albüm sayısı bu bütçeye ulaştığında bu kural bekler.</p>
      </>}
    </>}
    <Button className="secondary-button" onPress={() => save.mutate()} isDisabled={save.isPending}>Takibi kaydet</Button>{save.error && <p className="form-error">{save.error.message}</p>}
  </div>;
}
export function MusicMonitoringView({ onOpen }: { onOpen: (album: ProviderAlbum) => void }) {
  const session = useSession(); const client = useQueryClient();
  const monitors = useQuery({ queryKey: ['music-monitors'], queryFn: () => request<MusicMonitor[]>('/music/monitors') });
  const jobs = useQuery({ queryKey: ['music-automation-jobs'], queryFn: () => request<MusicAutomationJob[]>('/music/automation/jobs'), refetchInterval: 10000 });
  const blocked = useQuery({ queryKey: ['music-automation-blocked'], queryFn: () => request<{ groupId: string; fingerprint: string; reason: string }[]>('/music/automation/blocked'), enabled: session.role === 'admin' });
  const unblock = useMutation({ mutationFn: (item: { groupId: string; fingerprint: string }) => mutate(`/music/automation/blocked/${item.groupId}/${item.fingerprint}`, undefined, 'DELETE'), onSuccess: () => client.invalidateQueries({ queryKey: ['music-automation-blocked'] }) });
  const labels: Record<string, string> = { queued: 'Sırada', waiting: 'Bekleniyor', running: 'Değerlendiriliyor', suggested: 'Aday bulundu', grabbed: 'Ediniliyor', satisfied: 'Hazır', cancelled: 'İptal', blocked: 'Kararın bekleniyor', failed: 'Sorun var' };
  return <><p className="field-hint">Sanatçı keşfi veya albüm ayrıntısından takip ekleyebilirsin.</p>{monitors.data?.map(monitor => <MonitorCard key={monitor.id} monitor={monitor} onOpen={onOpen} />)}
    {!!jobs.data?.length && <h3 className="section-heading">Albüm edinme geçmişi</h3>}{jobs.data?.map(job => <article className="request-card" key={job.id}><div className="request-summary"><strong>{job.title}</strong><span>{labels[job.status] ?? job.status}</span></div><p className="request-note">{job.message}</p>{job.release && <p className="field-hint">{job.release.metadata.formats.join(' / ')} · {job.release.metadata.date} · {job.release.tracks.length} parça</p>}</article>)}
    {(monitors.error || jobs.error) && <p className="form-error">{monitors.error?.message || jobs.error?.message}</p>}
    {!!blocked.data?.length && <h3 className="section-heading">Engellenen müzik sürümleri</h3>}{blocked.data?.map(item => <article className="request-card" key={item.groupId + item.fingerprint}><p>{item.reason}</p><Button variant="ghost" size="sm" onPress={() => unblock.mutate(item)} isDisabled={unblock.isPending}>Bu sürüme yeniden izin ver</Button></article>)}{(blocked.error || unblock.error) && <p className="form-error">{blocked.error?.message || unblock.error?.message}</p>}
  </>;
}
function MonitorCard({ monitor, onOpen }: { monitor: MusicMonitor; onOpen: (album: ProviderAlbum) => void }) {
  const client = useQueryClient(); const [open, setOpen] = useState(false);
  const albums = useInfiniteQuery({ queryKey: ['music-monitor-albums', monitor.id], initialPageParam: 0, enabled: open,
    queryFn: ({ pageParam }) => request<MusicPage<MonitoredMusicAlbum>>(`/music/monitors/${monitor.id}/albums?offset=${pageParam}`), getNextPageParam: page => page.offset + page.items.length < page.total ? page.offset + page.items.length : undefined });
  const refresh = useMutation({ mutationFn: () => mutate(`/music/monitors/${monitor.id}/refresh`, { expectedRevision: monitor.revision }), onSuccess: () => client.invalidateQueries({ queryKey: ['music-monitors'] }) });
  const override = useMutation({ mutationFn: ({ group, wanted }: { group: string; wanted: boolean | null }) => mutate(`/music/monitors/${monitor.id}/albums/${group}`, { wanted, expectedRevision: monitor.revision }, 'PUT'), onSuccess: () => { client.invalidateQueries({ queryKey: ['music-monitors'] }); client.invalidateQueries({ queryKey: ['music-monitor-albums', monitor.id] }); } });
  return <article className="request-card"><div className="request-summary"><button className="tracking-title" onClick={() => setOpen(value => !value)} aria-expanded={open}>{monitor.name}</button><span>{monitor.settings.enabled ? monitor.settings.autoGrab ? 'Otomatik edinme' : 'Takip etkin' : 'Duraklatıldı'}</span></div>
    <p className="field-hint">{monitor.lastRun ? `Son yenileme ${date(monitor.lastRun)}` : 'İlk yenileme bekleniyor'}{monitor.cursor ? ` · ${monitor.cursor} albüm okundu` : ''}</p>
    <div className="content-actions"><MusicMonitorPanel kind={monitor.kind as 'artist' | 'album'} mbid={monitor.mbid} name={monitor.name} /><Button variant="ghost" size="sm" onPress={() => refresh.mutate()} isDisabled={refresh.isPending || !monitor.settings.enabled}><RefreshCw size={14} /> Yenile</Button><Button variant="ghost" size="sm" onPress={() => setOpen(value => !value)}>Albüm kapsamı</Button></div>
    {open && albums.data?.pages.flatMap(page => page.items).map(album => <div className="session-row" key={album.metadata.id}><div><button className="tracking-title" onClick={() => onOpen(album.metadata)}>{album.metadata.title}</button><span>{album.metadata.date} · {album.metadata.type}</span></div><label className="check-field"><input type="checkbox" checked={album.wanted} disabled={override.isPending} onChange={event => override.mutate({ group: album.metadata.id, wanted: event.target.checked })} /> Takip</label>{album.override !== null && <Button variant="ghost" size="sm" onPress={() => override.mutate({ group: album.metadata.id, wanted: null })}>Profil kuralı</Button>}</div>)}
    {open && albums.hasNextPage && <Button variant="ghost" onPress={() => albums.fetchNextPage()} isDisabled={albums.isFetching}>Diğer albümler</Button>}
    {(monitor.lastError || refresh.error || override.error || albums.error) && <p className="form-error">{refresh.error?.message || override.error?.message || albums.error?.message || monitor.lastError}</p>}
  </article>;
}
