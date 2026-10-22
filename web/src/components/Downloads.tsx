import { useRef, useState } from 'react';
import { Button, Input, Label, Modal, TextField } from '@heroui/react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ArrowDownToLine, ArrowRight, ArrowUpRight, Check, CircleAlert, Clock3, FileUp, FolderCheck, Link2, LoaderCircle, Pause, Play, Radio, RefreshCw, Upload, X } from 'lucide-react';
import { bytes, date, mutate, request, type Download, type QualityProfile } from '../api';
import { Dialog } from './Dialogs';
import { TransferHistoryAndLimits, TransferOptions } from './TransferOptions';
import { MusicImportReview } from './MusicAcquisition';
import { DownloadBudgetPanel, DownloadOwnerLabel } from './DownloadBudgets';
import { useSession } from './SessionContext';

export function AddDownloadDialog({ open, onClose, notify }: { open: boolean; onClose: () => void; notify: (message: string) => void }) {
  const client = useQueryClient();
  const [mode, setMode] = useState<'magnet' | 'torrent' | 'nzb'>('magnet');
  const [magnet, setMagnet] = useState('');
  const [file, setFile] = useState<File | null>(null);
  const input = useRef<HTMLInputElement>(null);
  const add = useMutation({ mutationFn: async () => {
    if (mode === 'magnet') return mutate<Download>('/downloads/magnet', { magnet });
    if (!file) throw new Error('Bir torrent veya NZB dosyası seç.');
    if (file.size > (mode === 'nzb' ? 8 : 4) * 1024 * 1024) throw new Error('Dosya boyutu sınırı aşıldı.');
    return request<Download>(`/downloads/${mode}`, { method: 'POST', headers: { 'Content-Type': mode === 'nzb' ? 'application/x-nzb' : 'application/x-bittorrent' }, body: file });
  }, onSuccess: async () => {
    await client.invalidateQueries({ queryKey: ['downloads'] });
    setMagnet(''); setFile(null); add.reset(); onClose(); notify('İndirme kuyruğa eklendi.');
  } });
  return <Dialog open={open} onClose={onClose} title="İndirme ekle"><div className="dialog-content">
    <div className="dialog-symbol"><ArrowDownToLine size={26} /></div><p className="eyebrow">KÜTÜPHANENE DOĞRU</p><Modal.Heading className="dialog-title">Yeni bir şey ekle.</Modal.Heading><p className="muted dialog-description">Magnet bağlantısı, torrent veya NZB dosyası ekle. Home aktarımı kendi içinde yönetir.</p>
    <div className="download-mode">{(['magnet', 'torrent', 'nzb'] as const).map(value => <button key={value} className={mode === value ? 'selected' : ''} onClick={() => { setMode(value); setFile(null); add.reset(); }}><FileUp size={15} />{value === 'magnet' ? 'Magnet' : value === 'torrent' ? 'Torrent' : 'NZB'}</button>)}</div>
    <form className="home-form" onSubmit={e => { e.preventDefault(); add.mutate(); }}>
      {mode === 'magnet' ? <TextField className="field"><Label>Magnet bağlantısı</Label><Input value={magnet} onChange={e => setMagnet(e.target.value)} placeholder="magnet:?xt=urn:btih:…" spellCheck={false} /></TextField> : <><input key={mode} ref={input} className="sr-only" type="file" accept={mode === 'nzb' ? '.nzb,application/x-nzb' : '.torrent,application/x-bittorrent'} aria-label={`${mode} dosyası seç`} onChange={e => setFile(e.target.files?.[0] ?? null)} /><button type="button" className="torrent-dropzone" onClick={() => input.current?.click()}><Upload size={26} strokeWidth={1.4} /><strong>{file?.name || `Bir .${mode} dosyası seç`}</strong><span>{file ? bytes(file.size) : mode === 'nzb' ? 'En fazla 8 MiB · NNTP hesabı gerekli' : 'En fazla 4 MiB'}</span></button></>}
      <div className="download-destination"><FolderCheck size={19} /><div><strong>İndirilenler kütüphanesi</strong><span>Tamamlanan medya otomatik olarak burada görünecek.</span></div></div>
      {add.error && <p className="form-error" role="alert">{add.error.message}</p>}
      <Button type="submit" className="primary-button form-submit" isDisabled={add.isPending || (mode === 'magnet' ? !magnet.trim() : !file)}>{add.isPending ? 'Ekleniyor…' : 'İndirmeyi başlat'}<ArrowRight size={17} /></Button>
    </form>
  </div></Dialog>;
}

export function DownloadsView({ downloads, loading, error, onAdd, onLibrary, notify }: {
  downloads: Download[]; loading: boolean; error?: Error | null;
  onAdd: () => void; onLibrary: (id: string) => void; notify: (message: string) => void;
}) {
  const client = useQueryClient();
  const session = useSession(); const [ownerFilter, setOwnerFilter] = useState('');
  const action = useMutation({ mutationFn: ({ id, action }: { id: string; action: string }) => mutate(`/downloads/${id}/control`, { action }), onSuccess: () => client.invalidateQueries({ queryKey: ['downloads'] }), onError: err => notify(err.message) });
  const relink = useMutation({ mutationFn: (id: string) => mutate(`/downloads/${id}/link`), onSuccess: () => client.invalidateQueries({ queryKey: ['downloads'] }), onError: err => notify(err.message) });
  const musicLink = useMutation({ mutationFn: (id: string) => mutate(`/music/downloads/${id}/link`), onSuccess: () => client.invalidateQueries({ queryKey: ['downloads'] }), onError: err => notify(err.message) });
  const active = downloads.filter(d => ['downloading', 'metadata', 'verifying', 'repairing', 'unpacking', 'importing', 'queued'].includes(d.status));
  const labels: Record<string, string> = { queued: 'Sırada', 'budget-waiting': 'Hesap bütçesi bekleniyor', metadata: 'Bilgiler alınıyor', verifying: 'Doğrulanıyor', repairing: 'PAR2 doğrulaması', unpacking: 'Arşiv açılıyor', downloading: 'İndiriliyor', importing: 'Kütüphaneye alınıyor', completed: 'Hazır', seeding: 'Paylaşılıyor', paused: 'Duraklatıldı', cancelled: 'İptal edildi', failed: 'İlgilenmen gerekiyor' };
  return <section className="downloads-view">
    <div className="transfer-summary"><span><ArrowDownToLine size={16} />{bytes(downloads.reduce((n, d) => n + d.downloadSpeed, 0))}/sn</span><span><ArrowUpRight size={16} />{bytes(downloads.reduce((n, d) => n + d.uploadSpeed, 0))}/sn</span><span>{active.length} aktif işlem</span><span className="engine-label"><span className="status-dot" /> Uygulama içi motor</span></div>
    <TransferHistoryAndLimits />
    {session.role === 'admin' && downloads.length > 0 && <label className="field budget-owner-filter"><span>Aktarım hesabı</span><select value={ownerFilter} onChange={event => setOwnerFilter(event.target.value)}><option value="">Tüm hesaplar</option><option value="legacy">Hesabı bilinmeyen eski kayıtlar</option>{[...new Map(downloads.filter(item => item.ownership?.userId).map(item => [item.ownership.userId!, item.ownership.displayName])).entries()].map(([id, name]) => <option key={id} value={id}>{name}</option>)}</select></label>}
    <DownloadBudgetPanel key={ownerFilter} userId={session.role === 'admin' && ownerFilter && ownerFilter !== 'legacy' ? ownerFilter : undefined} compact />
    {error && <p className="form-error" role="alert">{error.message}</p>}
    {loading && <div className="job-skeleton home-skeleton" />}
    {!loading && !downloads.length && <div className="downloads-empty"><div className="transfer-illustration"><div className="transfer-orbit" /><ArrowDownToLine size={39} strokeWidth={1.3} /></div><p className="eyebrow">YENİ HİKÂYELERE YER VAR</p><h2>Kütüphanenin bir sonraki durağı.</h2><p>İndirmeyi başlat, gerisini Home takip etsin.<br />Doğrulanan dosyalar kütüphanene ulaşır.</p><Button className="primary-button" onPress={onAdd}><Link2 size={17} /> İlk indirmeyi ekle</Button></div>}
    <div className="transfer-list">{downloads.filter(download => !ownerFilter || (download.ownership?.userId ?? 'legacy') === ownerFilter).map(download => {
      const progress = Math.max(0, Math.min(100, download.progress));
      const running = download.desiredState === 'running' && download.status !== 'failed';
      const ready = !!download.importedAt;
      return <article key={download.id} className={`transfer-card transfer-card--${download.status}`}>
        <div className="transfer-icon">{download.status === 'failed' ? <CircleAlert size={23} /> : download.status === 'paused' ? <Pause size={22} /> : download.status === 'seeding' ? <Radio size={22} /> : ready ? <Check size={23} /> : download.status === 'queued' ? <Clock3 size={22} /> : <ArrowDownToLine size={23} />}</div>
        <div className="transfer-body"><div className="transfer-heading"><h3>{download.name}</h3><span className="transfer-state">{labels[download.status] || download.status}</span></div><p>{download.message}</p>
          <DownloadOwnerLabel download={download} />
          {download.acquisition && <p className={download.acquisition.status === 'review' ? 'transfer-review' : ''}>{download.acquisition.title} · {download.acquisition.message || 'İçerik doğrulaması ve katalog bağlantısı bekleniyor.'}</p>}
          {download.acquisition && !ready && download.progress >= 100 && download.status === 'failed' && <ImportReview download={download} notify={notify} />}
          {download.sourceKind === 'nzb' && download.status === 'failed' && <ArchivePassword downloadId={download.id} notify={notify} />}
          {download.musicAcquisition && <p>{download.musicAcquisition.title} · {download.musicAcquisition.message || 'Albüm kimliği ve dosya doğrulaması bekleniyor.'}</p>}
          {download.musicAcquisition && !ready && download.progress >= 100 && download.status === 'failed' && <MusicImportReview downloadId={download.id} />}
          <div className={`transfer-progress ${download.status === 'metadata' ? 'indeterminate' : ''}`}><span style={{ width: `${progress}%` }} /></div><div className="transfer-meta"><span>{progress.toFixed(1)}%</span><span>{download.totalBytes ? bytes(download.totalBytes) : 'Boyut bekleniyor'}</span>{download.peers > 0 && <span>{download.peers} eş</span>}{download.downloadSpeed > 0 && <span>↓ {bytes(download.downloadSpeed)}/sn</span>}{download.uploadSpeed > 0 && <span>↑ {bytes(download.uploadSpeed)}/sn</span>}<span>{date(download.createdAt)}</span></div>
        </div>
        <div className="transfer-actions">
          <TransferOptions download={download} notify={notify} />
          {ready && download.musicAcquisition?.status === 'review' && <Button isIconOnly size="sm" variant="ghost" aria-label="Albüm bağlantısını yeniden dene" onPress={() => musicLink.mutate(download.id)} isDisabled={musicLink.isPending}><RefreshCw size={18} /></Button>}
          {ready && download.acquisition?.status === 'review' && <Button isIconOnly size="sm" variant="ghost" aria-label={`${download.name} katalog bağlantısını yeniden dene`} isDisabled={relink.isPending} onPress={() => relink.mutate(download.id)}><RefreshCw size={18} /></Button>}
          {ready && <Button isIconOnly size="sm" variant="ghost" aria-label={`${download.name} kütüphanesini aç`} onPress={() => onLibrary(download.importedLibraryId!)}><FolderCheck size={18} /></Button>}
          <Button isIconOnly size="sm" variant="ghost" aria-label={`${download.name} ${running ? 'duraklat' : 'devam et'}`} isDisabled={action.isPending} onPress={() => action.mutate({ id: download.id, action: running ? 'pause' : 'resume' })}>{running ? <Pause size={18} /> : <Play size={18} />}</Button>
          {download.desiredState !== 'cancelled' && <Button isIconOnly size="sm" variant="ghost" aria-label={`${download.name} iptal et`} isDisabled={action.isPending} onPress={() => action.mutate({ id: download.id, action: 'cancel' })}><X size={18} /></Button>}
        </div>
      </article>;
    })}</div>
    {!!downloads.length && <p className="transfer-footnote"><FolderCheck size={14} /> Tamamlanan dosyalar ayrı bir kütüphanede tutulur. İptal komutu kütüphanedeki medyayı silmez.</p>}
  </section>;
}

function ArchivePassword({ downloadId, notify }: { downloadId: string; notify: (message: string) => void }) {
  const client = useQueryClient(); const [password, setPassword] = useState('');
  const save = useMutation({ mutationFn: () => mutate(`/downloads/${downloadId}/archive-password`, { password }, 'PUT'), onSuccess: () => { setPassword(''); client.invalidateQueries({ queryKey: ['downloads'] }); notify('Arşiv parolası korumalı kaydedildi; işlem yeniden sırada.'); } });
  return <details className="advanced-fields"><summary>Arşiv parolasını gir / değiştir</summary><div className="home-form"><Input aria-label="Arşiv parolası" type="password" value={password} onChange={event => setPassword(event.target.value)} autoComplete="off" /><Button size="sm" variant="ghost" onPress={() => save.mutate()} isDisabled={save.isPending}>Parolayla yeniden dene</Button>{save.error && <p className="form-error">{save.error.message}</p>}</div></details>;
}

function ImportReview({ download, notify }: { download: Download; notify: (message: string) => void }) {
  const client = useQueryClient();
  const profiles = useQuery({ queryKey: ['quality-profiles'], queryFn: () => request<QualityProfile[]>('/quality-profiles') });
  const [profile, setProfile] = useState('balanced');
  const review = useMutation({ mutationFn: () => mutate(`/downloads/${download.id}/import-profile`, { qualityProfileId: profile }),
    onSuccess: () => { client.invalidateQueries({ queryKey: ['downloads'] }); notify('Dosya doğrulaması seçtiğin profille yeniden sıraya alındı.'); } });
  return <div className="import-review">
    <label><span>Dosyayı farklı kalite profiliyle değerlendir</span><select aria-label={`${download.name} doğrulama profili`} value={profile} onChange={e => setProfile(e.target.value)}>{profiles.data?.map(value => <option key={value.id} value={value.id}>{value.name}</option>)}</select></label>
    <Button variant="ghost" size="sm" isDisabled={review.isPending || !profiles.data?.length} onPress={() => review.mutate()}><RefreshCw size={14} /> Yeniden doğrula</Button>
    {(review.error || profiles.error) && <p className="form-error">{review.error?.message || profiles.error?.message}</p>}
  </div>;
}
