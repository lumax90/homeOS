import { useEffect, useState } from 'react';
import { Button } from '@heroui/react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { BellRing, RefreshCw, Smartphone, TestTube2 } from 'lucide-react';
import { date, mutate, request, type BrowserPushDevice, type BrowserPushRegistration, type BrowserPushState, type PushServerConfiguration } from '../api';
import { clearLocalPush, pushKeyString, pushRegistration, pushWorkerMessage, subscribeBrowserPush, syncLocalPush, withPushLock } from '../pushBridge';
import { useSession } from './SessionContext';

const topics: Record<string, string> = { request: 'İstek durumu', approval: 'Onay bekleyen istek', available: 'Oynatmaya hazır', automation: 'Otomasyon', music: 'Müzik', subtitle: 'Altyazı', system: 'Yedekleme' };
function useRefreshPush() {
  const client = useQueryClient(); return () => Promise.all(['push-device', 'push-configuration', 'notification-channels', 'notification-deliveries'].map(key => client.invalidateQueries({ queryKey: [key] })));
}
export function PushServerEditor() {
  const settings = useQuery({ queryKey: ['push-configuration'], queryFn: () => request<PushServerConfiguration>('/push/configuration') });
  return <>{settings.data && <PushServerForm key={settings.data.revision} initial={settings.data} />}{settings.error && <p className="form-error">{settings.error.message}</p>}</>;
}
function PushServerForm({ initial }: { initial: PushServerConfiguration }) {
  const [enabled, setEnabled] = useState(initial.enabled); const [subject, setSubject] = useState(initial.subject); const [rotate, setRotate] = useState(false); const refresh = useRefreshPush();
  const save = useMutation({ mutationFn: () => mutate('/push/configuration', { enabled, subject, revision: initial.revision, rotateKey: rotate }, 'PUT'), onSuccess: refresh });
  return <form className="home-form source-form" onSubmit={event => { event.preventDefault(); save.mutate(); }}><h4>Web/PWA push sunucusu</h4>
    <label className="field"><span>VAPID iletişim adresi</span><input value={subject} onChange={event => setSubject(event.target.value)} placeholder="mailto:sen@alanadi.com" maxLength={500} required={enabled} /></label>
    <p className="field-hint">Push servisinin sunucu yöneticisine ulaşabileceği mailto: veya HTTPS adresi. İleti ve abonelik anahtarları şifrelenir; imza özel anahtarı bu sunucuda saklanır.</p>
    <label className="check-field"><input type="checkbox" checked={enabled} onChange={event => setEnabled(event.target.checked)} /> Hane kullanıcıları cihazlarında push açabilsin</label>
    {initial.publicKey && <><p className="field-hint">İmza anahtarı sürümü {initial.keyRevision}. Kullanıcılar <strong>Bu cihazda push</strong> bölümünden kendi aboneliklerini açar.</p><label className="check-field"><input type="checkbox" checked={rotate} onChange={event => setRotate(event.target.checked)} /> Anahtarı yenile ve eski cihaz aboneliklerini kapat</label></>}
    <Button type="submit" variant="ghost" size="sm" isDisabled={save.isPending}>Push ayarını kaydet</Button>{save.error && <p className="form-error">{save.error.message}</p>}
  </form>;
}
export function BrowserPushPanel() {
  const session = useSession(); const [open, setOpen] = useState(false); const refresh = useRefreshPush();
  const state = useQuery({ queryKey: ['push-device'], queryFn: () => request<BrowserPushState>('/push/device'), enabled: open, refetchOnWindowFocus: true });
  return <section><Button variant="ghost" size="sm" onPress={() => setOpen(value => !value)} aria-expanded={open}><BellRing size={15} /> Bu cihazda push</Button>{open && <div className="home-form automation-form">
    {state.data && <BrowserPushForm key={`${session.sessionId}:${state.data.current?.revision ?? 0}:${state.data.configuration.revision}`} state={state.data} refresh={refresh} />}
    {state.isLoading && <p className="field-hint" role="status">Cihaz bildirimleri yükleniyor…</p>}{state.error && <p className="form-error">{state.error.message}</p>}
  </div>}</section>;
}
function BrowserPushForm({ state, refresh }: { state: BrowserPushState; refresh: () => Promise<unknown> }) {
  const session = useSession(); const [name, setName] = useState(state.current?.name ?? 'Bu cihaz'); const [events, setEvents] = useState(state.current?.events ?? ['request', 'available']);
  const [registration, setRegistration] = useState<ServiceWorkerRegistration>(); const [workerReady, setWorkerReady] = useState(false); const [workerError, setWorkerError] = useState('');
  const [waiting, setWaiting] = useState<ServiceWorker>();
  const supported = window.isSecureContext && 'Notification' in window && 'PushManager' in window && 'serviceWorker' in navigator;
  useEffect(() => {
    let active = true; let stopWatching = () => {};
    void (async () => {
      if (!supported) return;
      const value = await pushRegistration(); if (!active) return; setRegistration(value);
      if (!value?.active) throw new Error('PWA istemcisi henüz hazır değil. Home’u üretim adresinden yeniden aç.');
      let installing = value.installing;
      const changed = () => { if (active) setWaiting(value.waiting ?? undefined); };
      const found = () => { installing?.removeEventListener('statechange', changed); installing = value.installing; installing?.addEventListener('statechange', changed); changed(); };
      value.addEventListener('updatefound', found); installing?.addEventListener('statechange', changed); changed();
      stopWatching = () => { value.removeEventListener('updatefound', found); installing?.removeEventListener('statechange', changed); };
      const pong = await pushWorkerMessage<{ version: number }>(value.active, { type: 'home.push.ping' });
      if (active) { setWorkerReady(pong.version === 1); if (pong.version !== 1) setWorkerError('Home istemcisi güncellenmeli.'); }
    })().catch(error => { if (active) setWorkerError(error.message); });
    return () => { active = false; stopWatching(); };
  }, [supported]);
  const enable = useMutation({ mutationFn: async (permission: Promise<NotificationPermission>) => {
    if (await permission !== 'granted') throw new Error('Bildirim izni verilmedi. Tarayıcı veya cihaz ayarından izin verebilirsin.');
    return withPushLock(async () => {
      if (!registration?.active || !state.configuration.publicKey) throw new Error('Push sunucusu veya PWA istemcisi hazır değil.');
      const current = await request<BrowserPushState>('/push/device', { signal: AbortSignal.timeout(15000) });
      if (current.userId !== session.userId || current.sessionId !== session.sessionId) throw new Error('Hesap değişti; bu cihazın push ayarını yeniden aç.');
      let subscription = await registration.pushManager.getSubscription();
      if (subscription && (!subscription.options.applicationServerKey || pushKeyString(subscription.options.applicationServerKey) !== state.configuration.publicKey)) { await subscription.unsubscribe(); subscription = null; }
      subscription ??= await subscribeBrowserPush(registration, state.configuration.publicKey);
      const result = await request<BrowserPushRegistration>('/push/device', { method: 'POST', signal: AbortSignal.timeout(15000), body: JSON.stringify({ ...subscription.toJSON(),
        name, events, applicationServerKey: state.configuration.publicKey, keyRevision: state.configuration.keyRevision,
        expectedUserId: session.userId, expectedSessionId: session.sessionId, expectedRevision: state.current?.revision ?? null }) });
      await syncLocalPush(session); return result;
    });
  }, onSuccess: refresh, onError: refresh });
  const remove = useMutation({ mutationFn: (device: BrowserPushDevice) => withPushLock(async () => {
    await request(`/push/devices/${device.channelId}?revision=${device.revision}`, { method: 'DELETE', signal: AbortSignal.timeout(15000) });
    if (device.current) {
      const current = await request<BrowserPushState>('/push/device', { signal: AbortSignal.timeout(15000) });
      if (current.userId === session.userId && current.sessionId === session.sessionId && !current.current) {
        await (await registration?.pushManager.getSubscription())?.unsubscribe(); await clearLocalPush(session);
      }
    }
  }), onSuccess: refresh, onError: refresh });
  const test = useMutation({ mutationFn: () => mutate('/push/device/test', { revision: state.current!.revision }), onSuccess: refresh });
  const busy = enable.isPending || remove.isPending;
  function updateClient() {
    if (!waiting) return;
    navigator.serviceWorker.addEventListener('controllerchange', () => location.reload(), { once: true }); waiting.postMessage({ type: 'home.activate' });
  }
  return <><p className="field-hint">{state.current ? `Bu girişte etkin · ${date(state.current.expiresAt)} tarihine kadar` : 'Bu girişte push kapalı.'} Çıkış veya profil değişikliği aboneliği kapatır.</p>
    {!supported && <p className="field-hint">Push için destekleyen bir tarayıcı ve HTTPS gerekir. iPhone/iPad’de Home’u ana ekrana ekleyerek aç.</p>}
    {!state.configuration.enabled && <p className="field-hint">Yönetici, Bildirim kanalları bölümünde push sunucusunu etkinleştirmeli.</p>}
    {workerError && <p className="field-hint">{workerError}</p>}{waiting && <Button variant="ghost" size="sm" onPress={updateClient}><RefreshCw size={14} /> İstemciyi güncelle ve aç</Button>}
    <label className="field"><span>Cihaz bildirimi adı</span><input value={name} maxLength={80} onChange={event => setName(event.target.value)} /></label>
    <fieldset className="notification-event-fields"><legend>Bu cihazın olayları</legend>{Object.entries(topics).map(([key, label]) => <label className="check-field" key={key}><input type="checkbox" checked={events.includes(key)} onChange={event => setEvents(values => event.target.checked ? [...values, key] : values.filter(value => value !== key))} />{label}</label>)}</fieldset>
    <p className="field-hint">Kilit ekranında genel Home bildirimi görünür. Ayrıntılar uygulamadaki hesabınla açılır. Push, tarayıcının Apple/Google/Mozilla/Microsoft servisini kullanır.</p>
    <div className="content-actions"><Button className="secondary-button" isDisabled={!supported || !workerReady || !state.configuration.enabled || busy || !name.trim() || !events.length} onPress={() => {
      const permission = Notification.permission === 'granted' ? Promise.resolve<NotificationPermission>('granted') : Notification.requestPermission(); enable.mutate(permission);
    }}>{state.current ? 'Push tercihini kaydet' : 'Bu cihazda etkinleştir'}</Button>
      {state.current && <Button variant="ghost" size="sm" isDisabled={test.isPending || busy} onPress={() => test.mutate()}><TestTube2 size={14} /> Deneme gönder</Button>}</div>
    {test.isSuccess && <p className="field-hint" role="status">Deneme sıraya alındı. Cihaz bildirimlerini ve dış gönderim geçmişini kontrol et.</p>}
    {state.devices.map(device => <div className="session-row" key={device.channelId}><Smartphone size={17} /><div><strong>{device.name}{device.current ? ' · Bu giriş' : ''}</strong><span title={device.deviceName}>{device.deviceName}</span></div><Button variant="ghost" size="sm" isDisabled={busy} onPress={() => remove.mutate(device)}>Push’u kapat</Button></div>)}
    {(enable.error || remove.error || test.error) && <p className="form-error">{enable.error?.message || remove.error?.message || test.error?.message}</p>}
  </>;
}
