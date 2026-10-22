import { useState } from 'react';
import { Button, Input, Label, Modal, TextField } from '@heroui/react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ArrowUp, Folder, FolderOpen, RefreshCw, Settings2 } from 'lucide-react';
import { date, mutate, request, type Library, type LibraryConfiguration, type LocalMetadata } from '../api';
import { Dialog } from './Dialogs';
import { useSession } from './SessionContext';

export function FolderBrowser({ open, initialPath, onClose, onSelect }: { open: boolean; initialPath: string; onClose: () => void; onSelect: (path: string) => void }) {
  return <Dialog open={open} onClose={onClose} title="Sunucudan klasör seç"><div className="dialog-content"><p className="eyebrow">SUNUCUNDAKİ KLASÖRLER</p><Modal.Heading className="dialog-title">Bir klasör seç.</Modal.Heading>{open && <FolderList key={initialPath} initialPath={initialPath} onSelect={onSelect} />}</div></Dialog>;
}
function FolderList({ initialPath, onSelect }: { initialPath: string; onSelect: (path: string) => void }) {
  const [path, setPath] = useState(initialPath); const [typed, setTyped] = useState(initialPath);
  const data = useQuery({ queryKey: ['directories', path], queryFn: () => request<{ path: string; parent: string | null; directories: { name: string; path: string }[]; truncated: boolean }>(`/filesystem/directories?path=${encodeURIComponent(path)}`), retry: false });
  return <div className="home-form"><form className="folder-path" onSubmit={event => { event.preventDefault(); setPath(typed); }}><Input aria-label="Sunucudaki klasör yolu" value={typed} onChange={event => setTyped(event.target.value)} /><Button type="submit" variant="ghost" size="sm">Git</Button></form><div className="content-actions"><Button variant="ghost" size="sm" onPress={() => { const next = data.data?.parent ?? ''; setPath(next); setTyped(next); }}><ArrowUp size={14} /> Üst dizin</Button><Button className="secondary-button" onPress={() => data.data && onSelect(data.data.path)} isDisabled={!data.data?.path}>Bu klasörü seç</Button></div>
    <div className="folder-list">{data.data?.directories.map(directory => <button key={directory.path} onClick={() => { setPath(directory.path); setTyped(directory.path); }}><Folder size={16} /><span>{directory.name}</span></button>)}</div>{data.data?.truncated && <p className="field-hint">İlk 300 klasör gösteriliyor; yol alanıyla daha dar bir dizine gidebilirsin.</p>}{data.error && <p className="form-error">{data.error.message}</p>}
  </div>;
}
export function LibraryManager({ open, onClose, notify }: { open: boolean; onClose: () => void; notify: (message: string) => void }) {
  return <Dialog open={open} onClose={onClose} title="Kütüphaneleri yönet" className="detail-container"><div className="dialog-content"><p className="eyebrow">KOLEKSİYONUNUN KÖKLERİ</p><Modal.Heading className="dialog-title">Kütüphanelerin.</Modal.Heading>{open && <LibraryList notify={notify} />}</div></Dialog>;
}
export function LibraryList({ notify }: { notify: (message: string) => void }) {
  const data = useQuery({ queryKey: ['libraries', 'manage'], queryFn: () => request<Library[]>('/libraries/manage') });
  const [selected, setSelected] = useState<Library | null>(null);
  const jobs = useQuery({ queryKey: ['auto-match-jobs'], queryFn: () => request<{ mediaId: string; status: string; message: string }[]>('/library/auto-matches'), refetchInterval: 10000 });
  return <>{data.data?.map(library => <div className="session-row" key={library.id}><Folder size={20} /><div><strong>{library.name}</strong><span>{library.isDemo ? 'Sentetik içerik arşivi' : library.enabled ? 'Etkin' : 'Arşivde'} · {library.itemCount} kayıt</span><small>{library.path}</small></div>{!library.isDemo && <Button isIconOnly variant="ghost" aria-label={`${library.name} düzenle`} onPress={() => setSelected(library)}><Settings2 size={16} /></Button>}</div>)}{data.error && <p className="form-error">{data.error.message}</p>}
    {selected && <LibraryEditor key={selected.id} library={selected} onDone={() => setSelected(null)} notify={notify} />}
    {!!jobs.data?.length && <details className="advanced-fields source-form"><summary>Otomatik eşleştirme işleri</summary>{jobs.data.map(job => <p className="field-hint" key={job.mediaId}>{job.status} · {job.message || 'İşlem sırası bekleniyor.'}</p>)}</details>}
  </>;
}
function LibraryEditor({ library, onDone, notify }: { library: Library; onDone: () => void; notify: (message: string) => void }) {
  const config = useQuery({ queryKey: ['library-configuration', library.id], queryFn: () => request<LibraryConfiguration>(`/libraries/${library.id}/configuration`) });
  return config.data ? <LibraryForm initial={config.data} library={library} onDone={onDone} notify={notify} /> : config.error ? <p className="form-error">{config.error.message}</p> : <p className="field-hint">Tercihler yükleniyor…</p>;
}
function LibraryForm({ library, initial, onDone, notify }: { library: Library; initial: LibraryConfiguration; onDone: () => void; notify: (message: string) => void }) {
  const client = useQueryClient(); const [name, setName] = useState(library.name); const [path, setPath] = useState(library.path); const [enabled, setEnabled] = useState(library.enabled);
  const [options, setOptions] = useState(initial.options); const [excluded, setExcluded] = useState((initial.options.excludedPaths ?? []).join('\n')); const [browser, setBrowser] = useState(false);
  const save = useMutation({ mutationFn: () => mutate(`/libraries/${library.id}`, { name, path, enabled, options: { ...options, excludedPaths: excluded.split('\n').map(value => value.trim().replace(/[\\/]+$/, '')).filter(Boolean) } }, 'PUT'),
    onSuccess: () => { ['libraries', 'library-configuration', 'catalog', 'stats', 'jobs'].forEach(key => client.invalidateQueries({ queryKey: [key] })); notify('Kütüphane tercihleri kaydedildi.'); onDone(); } });
  const scan = useMutation({ mutationFn: () => mutate(`/libraries/${library.id}/scan`), onSuccess: () => { client.invalidateQueries({ queryKey: ['jobs'] }); notify('Tarama sıraya alındı.'); } });
  return <div className="home-form source-form"><TextField className="field"><Label>Kütüphane adı</Label><Input value={name} onChange={event => setName(event.target.value)} /></TextField><TextField className="field"><Label>Sunucudaki kök yol · taşınan kökü yeniden bağla</Label><Input value={path} onChange={event => setPath(event.target.value)} /></TextField><Button variant="ghost" size="sm" onPress={() => setBrowser(true)}><FolderOpen size={15} /> Klasörlerden seç</Button><label className="check-field"><input type="checkbox" checked={enabled} onChange={event => setEnabled(event.target.checked)} /> Kütüphane etkin · kapalıysa kayıtlarıyla arşivlenir</label>
    <label className="field"><span>Dışlanan göreli klasör/dosyalar · her satıra bir yol</span><textarea value={excluded} onChange={event => setExcluded(event.target.value)} placeholder={'extras\nsamples\nözel/klasör'} /></label><TextField className="field"><Label>Periyodik tarama · dakika · 0 kapalı</Label><Input type="number" min={0} max={10080} value={String(options.scanIntervalMinutes)} onChange={event => setOptions(value => ({ ...value, scanIntervalMinutes: Number(event.target.value) }))} /></TextField>
    <label className="check-field"><input type="checkbox" checked={options.watchChanges} onChange={event => setOptions(value => ({ ...value, watchChanges: event.target.checked }))} /> Klasör değişikliklerini izle</label><label className="check-field"><input type="checkbox" checked={options.readLocalMetadata} onChange={event => setOptions(value => ({ ...value, readLocalMetadata: event.target.checked }))} /> Yerel NFO/afiş bilgilerini oku</label><label className="check-field"><input type="checkbox" checked={options.autoMatch} onChange={event => setOptions(value => ({ ...value, autoMatch: event.target.checked }))} /> Tekil sağlayıcı kimliği veya tam başlık/yıl ile otomatik eşleştir</label>
    <p className="field-hint">Son tamamlanan tarama: {initial.lastScan ? date(initial.lastScan) : 'Yok'}. Ayar değişikliği etkin taramayı iptal eder; yeni kökü tarayarak doğrulayabilirsin.</p><div className="content-actions"><Button className="primary-button" onPress={() => save.mutate()} isDisabled={save.isPending}>Kaydet</Button><Button variant="ghost" onPress={() => scan.mutate()} isDisabled={scan.isPending || !library.enabled}><RefreshCw size={15} /> Şimdi tara</Button><Button variant="ghost" onPress={onDone}>Kapat</Button></div>{(save.error || scan.error) && <p className="form-error">{save.error?.message || scan.error?.message}</p>}
    <FolderBrowser open={browser} initialPath={path} onClose={() => setBrowser(false)} onSelect={path => { setPath(path); setBrowser(false); }} />
  </div>;
}
export function LocalMetadataDetails({ mediaId }: { mediaId: string }) {
  const account = useSession(); const client = useQueryClient();
  const local = useQuery({ queryKey: ['local-metadata', mediaId], queryFn: () => request<{ metadata: LocalMetadata | null; error: string | null }>(`/media/${mediaId}/local-metadata`) });
  const match = useMutation({ mutationFn: () => mutate(`/media/${mediaId}/auto-match`), onSuccess: () => client.invalidateQueries({ queryKey: ['auto-match-jobs'] }) });
  const data = local.data?.metadata;
  return <section className="local-metadata">{data && <><p className="field-hint">Yerel kaynak: {data.source}{data.year ? ` · ${data.year}` : ''}</p>{data.overview && <p>{data.overview}</p>}{!!data.tags.length && <p className="field-hint">{data.tags.join(' · ')}</p>}</>}{account.role === 'admin' && <Button variant="ghost" size="sm" onPress={() => match.mutate()} isDisabled={match.isPending}>Otomatik eşleştirmeyi yeniden değerlendir</Button>}{(local.data?.error || local.error || match.error) && <p className="form-error">{local.data?.error || local.error?.message || match.error?.message}</p>}</section>;
}
