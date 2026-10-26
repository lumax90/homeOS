import { useRef, useState } from 'react';
import { Button } from '@heroui/react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Download, RefreshCw, RotateCcw, ShieldCheck } from 'lucide-react';
import { mutate, request } from '../api';
import {ContainerUpdates}from'./ContainerUpdates';

interface UpdateStatus { currentVersion: string; runtime: string; trusted: boolean; keyFingerprint: string | null; canApply: boolean;
  pending: { id: string; version: string; runtime: string; createdAt: string } | null; rollback: { version: string; backupId: string } | null }
interface AutomaticUpdateStatus { supported: boolean; configured: boolean; enabled: boolean; preparing: boolean; channel: string; revision: string | null; error: string | null;
  last?: { state: string; preparedAt: string; fromVersion: string; toVersion: string | null; backupId: string } | null }
export function Updates() {
  const client = useQueryClient(); const file = useRef<HTMLInputElement>(null); const [message, setMessage] = useState('');
  const status = useQuery({ queryKey: ['updates'], queryFn: () => request<UpdateStatus>('/operations/updates') });
  const automatic = useQuery({ queryKey: ['container-updates'], queryFn: () => request<AutomaticUpdateStatus>('/operations/container-updates'), refetchInterval: 15000 });
  const upload = useMutation({ mutationFn: (file: File) => request('/operations/updates', { method: 'POST', body: file, headers: { 'Content-Type': 'application/zip' } }),
    onSuccess: () => { setMessage('Paket imzası, platformu ve dosyaları doğrulandı.'); client.invalidateQueries({ queryKey: ['updates'] }); } });
  const action = useMutation({ mutationFn: (type: 'apply' | 'rollback' | 'cancel') => type === 'cancel' ? mutate('/operations/updates', undefined, 'DELETE') : mutate(`/operations/updates/${type}`),
    onSuccess: (_, type) => { setMessage(type === 'cancel' ? 'Güncelleme hazırlığı kaldırıldı.' : 'Home yeni sürümü/veri durumunu açıyor. Birkaç saniye sonra sayfayı yenile.'); if (type === 'cancel') client.invalidateQueries({ queryKey: ['updates'] }); } });
  return <section className="source-form"><div className="section-heading"><h3><ShieldCheck size={16} /> Sürüm ve güncelleme</h3></div><p className="field-hint">Home {status.data?.currentVersion ?? '…'} · {status.data?.runtime}</p>
    <ContainerUpdates/>
    {!automatic.data?.supported && (status.data?.trusted ? <><p className="field-hint">Güvenilir anahtar: <code>{status.data.keyFingerprint?.slice(0, 24)}…</code></p><Button className="secondary-button" onPress={() => file.current?.click()} isDisabled={upload.isPending || !!status.data.pending}><Download size={15} />{upload.isPending ? 'Paket doğrulanıyor…' : 'İmzalı güncelleme paketi seç'}</Button><input className="sr-only" ref={file} type="file" accept=".zip" aria-label="İmzalı Home güncelleme paketi" onChange={event => { const value = event.target.files?.[0]; if (value) upload.mutate(value); event.target.value = ''; }} /></> : <p className="field-hint">Bu kurulumda imzalı dağıtım anahtarı bulunmuyor. İlk güvenilir paket kurulduktan sonra uygulama içi güncelleme kullanılabilir.</p>)}
    {status.data?.pending && <div className="restore-pending"><strong>Home {status.data.pending.version} hazır</strong><p>Uygulamadan önce veritabanı ve anahtarların yedeği alınır. Çalışan paket yerinde değiştirilmez.</p><div className="content-actions"><Button className="primary-button" onPress={() => action.mutate('apply')} isDisabled={action.isPending || !status.data.canApply}><RefreshCw size={15} /> Yedekle ve güncelle</Button><Button variant="ghost" onPress={() => action.mutate('cancel')} isDisabled={action.isPending}>Hazırlığı kaldır</Button></div></div>}
    {status.data?.rollback && <div className="content-actions"><Button variant="ghost" onPress={() => action.mutate('rollback')} isDisabled={action.isPending || !status.data.canApply}><RotateCcw size={15} /> {status.data.rollback.version} sürümüne ve güncelleme öncesi veriye dön</Button></div>}
    {message && <p className="operation-message">{message}</p>}{(status.error || upload.error || action.error) && <p className="form-error">{status.error?.message || upload.error?.message || action.error?.message}</p>}
  </section>;
}
