import { useState } from 'react';
import { Button } from '@heroui/react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { BellRing, History, Mail, Plus, Settings2, TestTube2, Trash2, Webhook, X } from 'lucide-react';
import { date, mutate, request, type Account, type NotificationAttempt, type NotificationChannel, type NotificationDelivery } from '../api';
import { useSession } from './SessionContext';
import { PushServerEditor } from './WebPush';

const topics: Record<string, string> = { request: 'İstek durumu', approval: 'Onay bekleyen istek', available: 'Oynatmaya hazır', automation: 'Otomasyon sonucu', music: 'Müzik ve yeni yayın', subtitle: 'Altyazı sonucu', system: 'Yedekleme ve sunucu' };
const states: Record<string, string> = { queued: 'Sırada', sending: 'Gönderiliyor', sent: 'Hedef kabul etti', failed: 'Gönderilemedi', cancelled: 'İptal edildi' };
const channelBody = (value: NotificationChannel) => ({ userId: value.userId, name: value.name, kind: value.kind, enabled: value.enabled, events: value.events, maximumHourlyAttempts: value.maximumHourlyAttempts, revision: value.revision });
function useRefresh() {
  const client = useQueryClient(); return () => Promise.all(['notification-channels', 'notification-deliveries', 'notification-attempts'].map(key => client.invalidateQueries({ queryKey: [key] })));
}
export function NotificationChannelsPanel() {
  const [open, setOpen] = useState(false); const [editing, setEditing] = useState<NotificationChannel | 'new' | null>(null); const refresh = useRefresh();
  const channels = useQuery({ queryKey: ['notification-channels'], queryFn: () => request<NotificationChannel[]>('/notification-channels'), enabled: open });
  const accounts = useQuery({ queryKey: ['users'], queryFn: () => request<Account[]>('/users'), enabled: open });
  const toggle = useMutation({ mutationFn: (value: NotificationChannel) => mutate(`/notification-channels/${value.id}`, { ...channelBody(value), enabled: !value.enabled }, 'PUT'), onSuccess: refresh });
  const test = useMutation({ mutationFn: (value: NotificationChannel) => mutate(`/notification-channels/${value.id}/test`, { revision: value.revision }), onSuccess: refresh });
  const remove = useMutation({ mutationFn: (value: NotificationChannel) => mutate(`/notification-channels/${value.id}?revision=${value.revision}`, undefined, 'DELETE'), onSuccess: refresh });
  return <section className="notification-channels"><Button className="secondary-button" aria-expanded={open} onPress={() => setOpen(value => !value)}><BellRing size={16} /> Bildirim kanalları</Button>{open && <div className="home-form automation-form">
    <p className="field-hint">Seçtiğin hesabın yeni bildirimlerini webhook veya e-postayla ilet. Geçmiş bildirimler kanal açıldığında yeniden gönderilmez.</p>
    {channels.data?.map(channel => <article className="notification-channel" key={channel.id}><div className="settings-heading"><h4>{channel.kind === 'smtp' ? <Mail size={15} /> : channel.kind === 'push' ? <BellRing size={15} /> : <Webhook size={15} />}{channel.name}</h4>{channel.kind !== 'push' && <Button isIconOnly variant="ghost" size="sm" aria-label={`${channel.name} kanalını düzenle`} onPress={() => setEditing(channel)}><Settings2 size={15} /></Button>}</div>
      <p>{accounts.data?.find(account => account.id === channel.userId)?.displayName ?? 'Hane hesabı'} · {channel.enabled ? 'Etkin' : 'Duraklatıldı'}</p><small>{channel.target} · saatte {channel.maximumHourlyAttempts} deneme</small>
      <div className="content-actions">{channel.kind !== 'push' && <Button variant="ghost" size="sm" isDisabled={toggle.isPending} onPress={() => toggle.mutate(channel)}>{channel.enabled ? 'Duraklat' : 'Etkinleştir'}</Button>}<Button variant="ghost" size="sm" isDisabled={test.isPending} onPress={() => test.mutate(channel)}><TestTube2 size={14} /> Deneme gönder</Button><Button isIconOnly variant="ghost" size="sm" aria-label={`${channel.name} kanalını sil`} isDisabled={remove.isPending} onPress={() => remove.mutate(channel)}><Trash2 size={14} /></Button></div>
      {channel.dropped > 0 && <p className="field-hint">Kuyruk sınırında {channel.dropped} dış gönderim atlandı; uygulama içi bildirimler korundu.</p>}
    </article>)}
    {editing ? <ChannelForm key={editing === 'new' ? 'new' : `${editing.id}:${editing.revision}`} initial={editing === 'new' ? null : editing} accounts={accounts.data ?? []} onDone={() => setEditing(null)} /> : <Button variant="ghost" onPress={() => setEditing('new')}><Plus size={16} /> Kanal ekle</Button>}
    {(channels.error || accounts.error || toggle.error || test.error || remove.error) && <p className="form-error">{channels.error?.message || accounts.error?.message || toggle.error?.message || test.error?.message || remove.error?.message}</p>}
    {test.isSuccess && <p className="field-hint" role="status">Deneme kuyruğa alındı. Sonuç ve tekrarlar gönderim geçmişinde görünür.</p>}
    <NotificationDeliveryHistory all />
    <PushServerEditor />
  </div>}</section>;
}
function ChannelForm({ initial, accounts, onDone }: { initial: NotificationChannel | null; accounts: Account[]; onDone: () => void }) {
  const session = useSession(); const refresh = useRefresh();
  const [name, setName] = useState(initial?.name ?? ''); const [kind, setKind] = useState(initial?.kind ?? 'webhook'); const [userId, setUserId] = useState(initial?.userId ?? session.userId ?? '');
  const [enabled, setEnabled] = useState(initial?.enabled ?? false); const [events, setEvents] = useState(initial?.events ?? ['request', 'available']); const [maximum, setMaximum] = useState(initial?.maximumHourlyAttempts ?? 60);
  const [fields, setFields] = useState<Record<string, string>>({}); const [clearSignature, setClearSignature] = useState(false); const [clearLogin, setClearLogin] = useState(false); const [clearPin, setClearPin] = useState(false);
  const field = (key: string, label: string, type = 'text', required = false) => <label className="field"><span>{label}</span><input type={type} required={required && !initial} maxLength={key === 'password' ? 2048 : key === 'url' ? 2048 : 400} autoComplete="off" value={fields[key] ?? ''} onChange={event => setFields(current => ({ ...current, [key]: event.target.value }))} /></label>;
  const save = useMutation({ mutationFn: () => mutate(initial ? `/notification-channels/${initial.id}` : '/notification-channels', {
    userId, name, kind, enabled, events, maximumHourlyAttempts: maximum, revision: initial?.revision ?? 0,
    url: fields.url || null, signingSecret: clearSignature ? '' : fields.signingSecret || null,
    host: fields.host || null, port: fields.port ? Number(fields.port) : null, security: fields.security || null,
    username: clearLogin ? '' : fields.username || null, password: clearLogin ? '' : fields.password || null,
    from: fields.from || null, to: fields.to || null, certificateSha256: clearPin ? '' : fields.certificateSha256 || null,
  }, initial ? 'PUT' : 'POST'), onSuccess: async () => { await refresh(); onDone(); } });
  return <form className="home-form source-form" onSubmit={event => { event.preventDefault(); save.mutate(); }}><div className="settings-heading"><h4>{initial ? 'Kanalı düzenle' : 'Yeni bildirim kanalı'}</h4><Button isIconOnly variant="ghost" size="sm" aria-label="Kanal düzenlemeyi kapat" onPress={onDone}><X size={15} /></Button></div>
    <label className="field"><span>Kanal adı</span><input required maxLength={80} value={name} onChange={event => setName(event.target.value)} /></label>
    <div className="form-columns"><label className="field"><span>Bildirimleri iletilecek hesap</span><select required disabled={!!initial} value={userId} onChange={event => setUserId(event.target.value)}>{accounts.filter(account => account.enabled).map(account => <option key={account.id} value={account.id}>{account.displayName}</option>)}</select></label><label className="field"><span>Kanal türü</span><select value={kind} disabled={!!initial} onChange={event => setKind(event.target.value as NotificationChannel['kind'])}><option value="webhook">Webhook</option><option value="smtp">SMTP e-posta</option></select></label></div>
    {initial && <p className="field-hint">Kayıtlı hedef: {initial.target}. Boş bağlantı alanları saklı değerleri korur; sırlar bu ekrana geri gönderilmez.</p>}
    {kind === 'webhook' ? <>{field('url', 'Tam HTTP(S) webhook adresi', 'password', true)}{field('signingSecret', 'HMAC-SHA256 imza sırrı · isteğe bağlı, en az 16 karakter', 'password')}{initial && <label className="check-field"><input type="checkbox" checked={clearSignature} onChange={event => setClearSignature(event.target.checked)} /> Saklı imza sırrını kaldır</label>}</>
      : <>{field('host', 'SMTP sunucusu', 'text', true)}<div className="form-columns">{field('port', 'Port · varsayılan 587', 'number')}<label className="field"><span>Bağlantı güvenliği</span><select value={fields.security ?? ''} onChange={event => setFields(current => ({ ...current, security: event.target.value }))}><option value="">{initial ? 'Kayıtlı tercihi koru' : 'STARTTLS'}</option><option value="starttls">Zorunlu STARTTLS</option><option value="ssl">Doğrudan TLS</option><option value="none">Şifresiz · yalnız loopback</option></select></label></div>
        {field('from', 'Gönderen adresi', 'text', true)}{field('to', 'Alıcı adresi', 'text', true)}{field('username', 'SMTP kullanıcı adı', 'text')}{field('password', 'SMTP parolası / uygulama parolası', 'password')}
        <details className="advanced-fields"><summary>Özel SMTP bağlantısı</summary><div className="home-form">{field('certificateSha256', 'Sunucu sertifikası SHA-256 pin · isteğe bağlı')}{initial && <><label className="check-field"><input type="checkbox" checked={clearPin} onChange={event => setClearPin(event.target.checked)} /> Sertifika pin'ini kaldır, sistem güvenini kullan</label><label className="check-field"><input type="checkbox" checked={clearLogin} onChange={event => setClearLogin(event.target.checked)} /> Saklı SMTP kullanıcı/parolasını kaldır</label></>}</div></details>
      </>}
    <fieldset className="notification-event-fields"><legend>Gönderilecek olaylar</legend>{Object.entries(topics).map(([key, label]) => <label className="check-field" key={key}><input type="checkbox" checked={events.includes(key)} onChange={event => setEvents(current => event.target.checked ? [...current, key] : current.filter(value => value !== key))} />{label}</label>)}</fieldset>
    <label className="field"><span>Saatlik gönderim denemesi · tekrarlar dahil</span><input type="number" min={1} max={1000} required value={maximum} onChange={event => setMaximum(Number(event.target.value))} /></label><label className="check-field"><input type="checkbox" checked={enabled} onChange={event => setEnabled(event.target.checked)} /> Yeni olayları otomatik ilet</label>
    <p className="field-hint">Kaydetmek eski bekleyen gönderimleri iptal eder. Deneme düğmesi, otomatik iletim kapalıyken de seçtiğin hedefe gerçek bir deneme iletisi gönderir.</p>
    <Button type="submit" className="secondary-button" isDisabled={save.isPending || !events.length}>Kanalı kaydet</Button>{save.error && <p className="form-error">{save.error.message}</p>}
  </form>;
}
export function NotificationDeliveryHistory({ all = false }: { all?: boolean }) {
  const [open, setOpen] = useState(false); const [details, setDetails] = useState<string | null>(null); const refresh = useRefresh();
  const jobs = useQuery({ queryKey: ['notification-deliveries', all], queryFn: () => request<NotificationDelivery[]>(`/notification-deliveries?all=${all}`), enabled: open, refetchInterval: open ? 5000 : false });
  const attempts = useQuery({ queryKey: ['notification-attempts', details], queryFn: () => request<NotificationAttempt[]>(`/notification-deliveries/${details}/attempts`), enabled: open && !!details, refetchInterval: open && details ? 5000 : false });
  const action = useMutation({ mutationFn: ({ job, command }: { job: NotificationDelivery; command: string }) => mutate(`/notification-deliveries/${job.id}/${command}`, { revision: job.revision }), onSuccess: refresh });
  return <section className="notification-delivery-history"><Button variant="ghost" size="sm" aria-expanded={open} onPress={() => setOpen(value => !value)}><History size={15} /> Dış gönderim geçmişi</Button>{open && <div className="home-form automation-form">
    {jobs.data?.map(job => <article className="notification-channel" key={job.id}><strong>{job.title}</strong><p>{job.channelName} · {states[job.status] ?? job.status} · {job.attempts} deneme{job.isTest ? ' · Kullanıcı denemesi' : ''}</p><small>{job.result}</small><small>{date(job.finishedAt ?? job.createdAt)}{job.status === 'queued' ? ` · Sonraki deneme ${date(job.nextAttemptAt)}` : ''}</small>
      <div className="content-actions"><Button variant="ghost" size="sm" onPress={() => setDetails(value => value === job.id ? null : job.id)}>Denemeler</Button>{['queued', 'sending'].includes(job.status) && <Button variant="ghost" size="sm" isDisabled={action.isPending} onPress={() => action.mutate({ job, command: 'cancel' })}>İptal et</Button>}{job.status === 'failed' && job.attempts < 16 && <Button variant="ghost" size="sm" isDisabled={action.isPending} onPress={() => action.mutate({ job, command: 'retry' })}>Yeniden dene</Button>}</div>
      {details === job.id && <ul className="notification-attempts">{attempts.data?.map(attempt => <li key={attempt.number}>{attempt.number}. {date(attempt.startedAt)} · {attempt.result}</li>)}</ul>}
    </article>)}{!jobs.data?.length && <p className="field-hint">Henüz dış gönderim yok.</p>}{(jobs.error || attempts.error || action.error) && <p className="form-error">{jobs.error?.message || attempts.error?.message || action.error?.message}</p>}
  </div>}</section>;
}
