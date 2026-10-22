import { useRef, useState } from 'react';
import { Button, Modal } from '@heroui/react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Archive, Download, RefreshCw, Upload } from 'lucide-react';
import { appUrl, bytes, date, mutate, request } from '../api';
import { Dialog } from './Dialogs';
import { NetworkSettings } from './Network';
import { Updates } from './Updates';
import { AdvancedOperationsPanel } from './AdvancedOperations';

interface Backup { id: string; createdAt: string; sizeBytes: number; schema: number }
interface Job { id: string; kind: string; status: string; message: string; createdAt: string }
export function OperationsDialog({ open, onClose }: { open: boolean; onClose: () => void }) {
  return <Dialog open={open} onClose={onClose} title="Yedekleme ve sunucu" className="detail-container"><div className="dialog-content"><p className="eyebrow">EVİNİN GÜVENCESİ</p><Modal.Heading className="dialog-title">Yedekleme ve sunucu.</Modal.Heading>{open && <OperationsBody />}</div></Dialog>;
}
export function OperationsBody() {
  const client = useQueryClient(); const input = useRef<HTMLInputElement>(null); const [message, setMessage] = useState('');
  const [tab, setTab] = useState('backups');
  const [visited, setVisited] = useState(['backups']);
  const chooseTab = (id: string) => { setTab(id); setVisited(old => old.includes(id) ? old : [...old, id]); };
  const backups = useQuery({ queryKey: ['backups'], queryFn: () => request<Backup[]>('/operations/backups'), enabled: tab === 'backups' });
  const jobs = useQuery({ queryKey: ['system-jobs'], queryFn: () => request<Job[]>('/operations/jobs'), enabled: tab === 'backups', refetchInterval: tab === 'backups' ? 3000 : false });
  const restore = useQuery({ queryKey: ['restore'], queryFn: () => request<{ pending: { id: string; preparedAt: string } | null }>('/operations/restore'), enabled: tab === 'backups' });
  const runtime = useQuery({ queryKey: ['operations-runtime'], queryFn: () => request<{ canRestart: boolean; statePath: string }>('/operations/runtime'), enabled: tab === 'backups' });
  const encoding = useQuery({ queryKey: ['encoding'], queryFn: () => request<{ nvenc: boolean; toneMapping: boolean; preference: string; checkedAt: string; note: string }>('/operations/encoding'), enabled: tab === 'encoding' });
  const preference = useMutation({ mutationFn: (value: string) => mutate(`/operations/encoding/${value}`, undefined, 'PUT'), onSuccess: () => client.invalidateQueries({ queryKey: ['encoding'] }) });
  const create = useMutation({ mutationFn: () => mutate('/operations/backups'), onSuccess: () => client.invalidateQueries({ queryKey: ['system-jobs'] }) });
  const prepare = useMutation({ mutationFn: (file: File) => request<{ message: string }>('/operations/restore', { method: 'POST', body: file, headers: { 'Content-Type': 'application/zip' } }), onSuccess: value => { setMessage(value.message); client.invalidateQueries({ queryKey: ['restore'] }); } });
  const cancel = useMutation({ mutationFn: () => mutate('/operations/restore', undefined, 'DELETE'), onSuccess: () => { setMessage('Geri yükleme planı kaldırıldı.'); client.invalidateQueries({ queryKey: ['restore'] }); } });
  const restart = useMutation({ mutationFn: () => mutate('/operations/restart'), onSuccess: () => setMessage('Home yeniden başlatılıyor. Birkaç saniye sonra sayfayı yenile.') });
  return <><div className="filter-pills product-subnav">{[['backups','Yedekler'],['network','Ağ ve HTTPS'],['updates','Güncellemeler'],['encoding','Kodlayıcı'],['advanced','Gelişmiş']].map(([id,label])=><button className={`filter-pill ${tab===id?'selected':''}`} key={id} onClick={()=>chooseTab(id)} aria-pressed={tab===id}>{label}</button>)}</div>
    {visited.includes('advanced')&&<div hidden={tab!=='advanced'}><AdvancedOperationsPanel/></div>}{visited.includes('network')&&<div hidden={tab!=='network'}><NetworkSettings/></div>}{visited.includes('updates')&&<div hidden={tab!=='updates'}><Updates/></div>}
    {tab==='encoding'&&<section className="encoding-settings"><div className="section-heading"><h3>Oynatma kodlayıcısı</h3></div>{encoding.data ? <><label className="field"><span>Tercih</span><select value={encoding.data.preference} onChange={event => preference.mutate(event.target.value)} disabled={preference.isPending}><option value="auto">Otomatik · doğrulanmış GPU / yazılım</option><option value="software">Yazılım · H.264</option><option value="nvenc">NVIDIA NVENC · hatada yazılıma geç</option></select></label><p className="field-hint">NVENC: {encoding.data.nvenc ? 'Kodlama denemesi geçti' : 'Kullanılabilir değil'} · HDR10/HLG → SDR: {encoding.data.toneMapping ? 'Filtre denemesi geçti' : 'Kullanılabilir değil'}</p><p className="field-hint">{encoding.data.note}</p></> : <p className="field-hint">Medya araçları doğrulanıyor…</p>}{(encoding.error || preference.error) && <p className="form-error">{encoding.error?.message || preference.error?.message}</p>}</section>}
    {tab==='backups'&&<><p className="muted source-intro">Yedek; uygulama veritabanını, hesapları, anahtarları ve yüklenmiş altyazıları içerir. Medya klasörlerinin yedeği ayrıca tutulur.</p>
    <div className="content-actions"><Button className="primary-button" onPress={() => create.mutate()} isDisabled={create.isPending || jobs.data?.some(job => ['queued', 'running'].includes(job.status))}><Archive size={16} /> Yedek oluştur</Button><Button variant="ghost" onPress={() => input.current?.click()} isDisabled={prepare.isPending || !!restore.data?.pending}><Upload size={16} />{prepare.isPending ? 'Yedek doğrulanıyor…' : 'Yedekten geri yükle'}</Button></div>
    <input ref={input} type="file" className="sr-only" aria-label="Home ZIP yedeğini seç" accept=".zip" onChange={event => { const file = event.target.files?.[0]; if (file) prepare.mutate(file); event.target.value = ''; }} />
    {message && <p className="operation-message">{message}</p>}
    {restore.data?.pending && <div className="restore-pending"><strong>Doğrulanmış geri yükleme hazır.</strong><p>Sonraki başlangıçta DB, anahtarlar ve altyazılar birlikte etkinleştirilir. Önceki state korunur. Yeniden giriş gerekir; indirmeler duraklatılır ve otomasyon kapatılır.</p><div className="content-actions"><Button variant="ghost" onPress={() => cancel.mutate()} isDisabled={cancel.isPending}>Planı iptal et</Button>{runtime.data?.canRestart && <Button className="secondary-button" onPress={() => restart.mutate()} isDisabled={restart.isPending}><RefreshCw size={15} /> Geri yüklemeyi uygula ve yeniden başlat</Button>}</div>{!runtime.data?.canRestart && <p>Home'u süreç/container yöneticisinden yeniden başlat.</p>}</div>}
    <div className="section-heading"><h3>Yedekler</h3><Button size="sm" variant="ghost" onPress={() => backups.refetch()}>Yenile</Button></div>
    {backups.data?.map(backup => <div className="session-row" key={backup.id}><div><strong>{date(backup.createdAt)}</strong><span>{bytes(backup.sizeBytes)} · şema {backup.schema}</span></div><a className="backup-download" href={appUrl(`/api/operations/backups/${backup.id}`)} download><Download size={15} /> İndir</a></div>)}
    {!backups.data?.length && <p className="field-hint">Henüz oluşturulmuş bir uygulama yedeği yok.</p>}
    {jobs.data?.slice(0, 5).map(job => <div className="operation-job" key={job.id}><strong>{job.status === 'completed' ? 'Tamamlandı' : job.status === 'failed' ? 'Sorun var' : 'Hazırlanıyor'}</strong><p>{job.message}</p></div>)}
    {!restore.data?.pending && runtime.data?.canRestart && <Button variant="ghost" onPress={() => restart.mutate()} isDisabled={restart.isPending}><RefreshCw size={15} /> Sunucuyu yeniden başlat</Button>}
    {(create.error || prepare.error || cancel.error || restart.error || backups.error) && <p className="form-error">{create.error?.message || prepare.error?.message || cancel.error?.message || restart.error?.message || backups.error?.message}</p>}
    </>}
  </>;
}
