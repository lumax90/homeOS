import { useState } from 'react';
import { Button } from '@heroui/react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ArrowDownToLine, ChevronLeft, ChevronRight, Gauge, UserRound } from 'lucide-react';
import { bytes, date, mutate, request, type Account, type Download, type DownloadBudgetHistory, type DownloadBudgetPolicy, type DownloadBudgetUsage } from '../api';
import { useSession } from './SessionContext';

export function DownloadBudgetPanel({ userId, editable = false, compact = false }: { userId?: string; editable?: boolean; compact?: boolean }) {
  const [open, setOpen] = useState(compact || editable); const [offset, setOffset] = useState(0);
  const root = userId ? `/users/${userId}` : '/account';
  const usage = useQuery({ queryKey: ['download-budget-usage', userId ?? 'self'], queryFn: ({ signal }) => request<DownloadBudgetUsage>(root + '/download-usage', { signal }), enabled: open, refetchInterval: open ? 5000 : false });
  const history = useQuery({ queryKey: ['download-budget-history', userId ?? 'self', offset], queryFn: ({ signal }) => request<DownloadBudgetHistory>(`${root}/download-history?offset=${offset}&limit=20`, { signal }), enabled: open });
  const value = usage.data; const labels: Record<string, string> = { admission: 'Yeni edinme', payload: 'Paket boyutu ayrıldı', policy: 'Bütçe düzenlendi' };
  return <section className="download-budget-panel">
    <Button variant="ghost" onPress={() => setOpen(value => !value)} aria-expanded={open}><Gauge size={16} />{userId ? 'Hesap indirme bütçesi' : 'İndirme bütçem ve kullanımım'}</Button>
    {open && <div className="download-budget-content">
      {usage.isPending && <p className="field-hint" role="status">Bütçe okunuyor…</p>}
      {value && <><div className="budget-heading"><strong>{value.displayName}</strong><span>{value.period} · UTC ayı{!value.policy.enabled ? ' · Edinme kapalı' : ''}</span></div>
        <div className="budget-counters"><div><span>Yeni edinme</span><strong>{value.acquisitions} <small>/ {value.policy.monthlyAcquisitions ?? 'Sınırsız'}</small></strong></div>
          <div><span>Başlatılan paket boyutu</span><strong>{bytes(value.committedBytes)}</strong><small>{value.policy.monthlyPayloadBytes === null ? 'Aylık boyut sınırı yok' : `${bytes(value.policy.monthlyPayloadBytes)} aylık sınır`}{value.reservedBytes > 0 ? ` · ${bytes(value.reservedBytes)} hazırlıkta ayrıldı` : ''}</small></div>
          <div><span>Eşzamanlı aktarım</span><strong>{value.activeTransfers} <small>/ {value.policy.maximumConcurrent}</small></strong>{value.waitingTransfers > 0 && <small>{value.waitingTransfers} aktarım bütçe bekliyor</small>}</div></div>
        {value.policy.monthlyPayloadBytes !== null && value.policy.monthlyPayloadBytes > 0 && <progress max={value.policy.monthlyPayloadBytes} value={Math.min(value.policy.monthlyPayloadBytes, value.committedBytes + value.reservedBytes)} aria-label="Aylık paket boyutu bütçesi" />}
        <p className="field-hint">Yeni dönem: {date(value.resetsAt)} · Motorun bu ay raporladığı medya verisi: ↓ {bytes(value.receivedBytes)} / ↑ {bytes(value.uploadedBytes)}</p>
        <details className="advanced-fields"><summary>Boyut kotası nasıl işler?</summary><p className="field-hint">Seçili torrent dosyalarının doğrulanmış toplamı veya NZB parça beyanlarının toplamı aktarım başlamadan bütçeye yazılır. Duraklat/devam aynı paketi tekrar borçlandırmaz. Staging temizlenip yeniden indirilirse yeni kullanım oluşur. Bu değer internet sağlayıcısının trafik sayacı değildir; tekrar iletim ve protokol trafiği farklıdır.</p></details>
        {editable && userId && <BudgetForm key={value.policy.revision} userId={userId} initial={value.policy} />}
      </>}
      {!!history.data?.items.length && <details className="advanced-fields"><summary>Kullanım geçmişi · {history.data.total} kayıt</summary><div className="budget-history">{history.data.items.map(event => <div key={event.id}><div><strong>{event.title}</strong><span>{labels[event.action] ?? event.action} · {date(event.createdAt)}</span></div>{event.bytes > 0 && <span>{bytes(event.bytes)}</span>}</div>)}</div>
        <div className="content-actions"><Button size="sm" variant="ghost" onPress={() => setOffset(value => Math.max(0, value - 20))} isDisabled={offset === 0}><ChevronLeft size={14} /> Önceki</Button><span className="field-hint">{offset + 1}–{offset + history.data.items.length}</span><Button size="sm" variant="ghost" onPress={() => setOffset(value => value + 20)} isDisabled={offset + 20 >= history.data.total}>Sonraki <ChevronRight size={14} /></Button></div>
      </details>}
      {(usage.error || history.error) && <p className="form-error" role="alert">{usage.error?.message ?? history.error?.message}</p>}
    </div>}
  </section>;
}
function BudgetForm({ userId, initial }: { userId: string; initial: DownloadBudgetPolicy }) {
  const [value, setValue] = useState(initial); const client = useQueryClient();
  const save = useMutation({ mutationFn: () => mutate<DownloadBudgetPolicy>(`/users/${userId}/download-budget`, value, 'PUT'), onSuccess: () => {
    void client.invalidateQueries({ queryKey: ['download-budget-usage'] }); void client.invalidateQueries({ queryKey: ['download-budget-history'] }); void client.invalidateQueries({ queryKey: ['downloads'] });
  } });
  const optionalNumber = (input: string) => input === '' ? null : Number(input); const gib = (input: string) => input === '' ? null : Math.round(Number(input) * 1024 ** 3);
  return <form className="home-form budget-form" onSubmit={event => { event.preventDefault(); save.mutate(); }}>
    <label className="check-field"><input type="checkbox" checked={value.enabled} onChange={event => setValue(old => ({ ...old, enabled: event.target.checked }))} /> Bu hesabın indirmeleri çalışabilsin</label>
    <div className="form-columns"><label className="field"><span>Aylık yeni edinme · boş sınırsız</span><input type="number" min={0} max={100000} value={value.monthlyAcquisitions ?? ''} placeholder="Sınırsız" onChange={event => setValue(old => ({ ...old, monthlyAcquisitions: optionalNumber(event.target.value) }))} /></label>
      <label className="field"><span>Eşzamanlı indirme</span><input type="number" min={0} max={10} value={value.maximumConcurrent} onChange={event => setValue(old => ({ ...old, maximumConcurrent: Number(event.target.value) }))} /></label></div>
    <div className="form-columns"><label className="field"><span>Aylık paket boyutu · GiB, boş sınırsız</span><input type="number" min={0} max={65536} step={.25} value={value.monthlyPayloadBytes === null ? '' : value.monthlyPayloadBytes / 1024 ** 3} placeholder="Sınırsız" onChange={event => setValue(old => ({ ...old, monthlyPayloadBytes: gib(event.target.value) }))} /></label>
      <label className="field"><span>Tek paket · GiB, boş sınırsız</span><input type="number" min={0} max={4096} step={.25} value={value.maximumPayloadBytes === null ? '' : value.maximumPayloadBytes / 1024 ** 3} placeholder="Sınırsız" onChange={event => setValue(old => ({ ...old, maximumPayloadBytes: gib(event.target.value) }))} /></label></div>
    <p className="field-hint">0 ilgili işlemi kapatır. Değişiklik çalışan aktarımlara uygulanır; durdurulan işler doğrulanmış parçalarını korur.</p>
    <Button type="submit" variant="ghost" isDisabled={save.isPending}><ArrowDownToLine size={15} /> İndirme bütçesini kaydet</Button>{save.error && <p className="form-error">{save.error.message}</p>}
  </form>;
}
export function DownloadOwnerLabel({ download }: { download: Download }) {
  const session = useSession(); const [open, setOpen] = useState(false); const [user, setUser] = useState(''); const client = useQueryClient();
  const users = useQuery({ queryKey: ['users'], queryFn: () => request<Account[]>('/users'), enabled: open && session.role === 'admin' });
  const assign = useMutation({ mutationFn: () => mutate(`/downloads/${download.id}/owner`, { userId: user, revision: download.revision }, 'PUT'), onSuccess: () => { setOpen(false); void client.invalidateQueries({ queryKey: ['downloads'] }); void client.invalidateQueries({ queryKey: ['download-budget-usage'] }); } });
  const origin = download.ownership?.origin; const automated = origin === 'video-automation' || origin === 'music-automation';
  return <div className="download-owner"><span><UserRound size={12} />{download.ownership?.displayName ?? 'Eski kayıt · hesap bilinmiyor'}{automated ? ' · Otomasyon' : ''}{download.ownership?.chargedBytes > 0 ? ` · ${bytes(download.ownership.chargedBytes)} bütçeye yazıldı` : ''}</span>
    {session.role === 'admin' && !download.ownership?.userId && <Button size="sm" variant="ghost" onPress={() => setOpen(value => !value)}>Hesap ata</Button>}
    {open && <div className="home-form"><p className="field-hint">Önce aktarımı duraklat. Atama yeni hesabın bu ayki edinme sayısına yazılır; geçmiş trafik o hesaba yüklenmez.</p><select aria-label="Eski aktarımın sahibi" value={user} onChange={event => setUser(event.target.value)}><option value="">Hesap seç</option>{users.data?.filter(account => account.enabled && (account.isAdmin || account.canAcquire)).map(account => <option key={account.id} value={account.id}>{account.displayName}</option>)}</select><Button size="sm" variant="ghost" onPress={() => assign.mutate()} isDisabled={!user || download.desiredState === 'running' || assign.isPending}>Hesabı ata</Button>{(assign.error || users.error) && <p className="form-error">{assign.error?.message ?? users.error?.message}</p>}</div>}
  </div>;
}
