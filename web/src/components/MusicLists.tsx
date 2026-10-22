import { useState } from 'react';
import { Button } from '@heroui/react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Check, ListPlus, RefreshCw } from 'lucide-react';
import { date, mutate, request, type MusicListJob, type MusicListReceipt, type Playlist } from '../api';
import { MusicSwitch } from './MusicAutomation';

export function MusicListImports({ playlistId }: { playlistId?: string }) {
  const client = useQueryClient(); const [source, setSource] = useState(playlistId ? 'playlist' : 'collection'); const [id, setId] = useState(playlistId ?? ''); const [kind, setKind] = useState('artist'); const [selected, setSelected] = useState('');
  const lists = useQuery({ queryKey: ['playlists'], queryFn: () => request<Playlist[]>('/playlists') });
  const jobs = useQuery({ queryKey: ['music-list-imports'], queryFn: ({ signal }) => request<MusicListJob[]>('/music/list-imports', { signal }), refetchInterval: query => query.state.data?.some(job => ['queued', 'running'].includes(job.status)) ? 3000 : false });
  const detail = useQuery({ queryKey: ['music-list-import', selected], queryFn: ({ signal }) => request<MusicListJob>(`/music/list-imports/${selected}`, { signal }), enabled: !!selected, refetchInterval: query => query.state.data && ['queued', 'running'].includes(query.state.data.status) ? 3000 : false });
  const refresh = () => Promise.all(['music-list-imports', 'music-list-import', 'music-follows', 'album-follows', 'music-monitors'].map(key => client.invalidateQueries({ queryKey: [key] })));
  const begin = useMutation({ mutationFn: () => {
    let sourceId = id.trim(); if (source === 'collection' && sourceId.startsWith('https://')) { const url = new URL(sourceId); const match = /^\/collection\/([a-f0-9-]{36})\/?$/i.exec(url.pathname); if (url.hostname !== 'musicbrainz.org' || !match) throw new Error('MusicBrainz koleksiyon bağlantısı veya MBID gir.'); sourceId = match[1]; }
    return mutate<MusicListJob>('/music/list-imports', { source, id: sourceId, kind });
  }, onSuccess: value => { setSelected(value.id); void refresh(); } });
  const cancel = useMutation({ mutationFn: (job: MusicListJob) => mutate(`/music/list-imports/${job.id}/cancel`, { revision: job.revision, mbids: [] }), onSuccess: refresh });
  const labels: Record<string, string> = { queued: 'Sırada', running: 'Kimlikler okunuyor', ready: 'Seçim bekliyor', applied: 'Takibe eklendi', failed: 'İş tamamlanamadı', cancelled: 'İptal' };
  return <section className="music-list-imports"><h2>Listeden toplu takip</h2><p className="field-hint">Herkese açık MusicBrainz koleksiyonundan veya Home çalma listendeki kimliklerden sanatçı/albüm seç. Kaynak listesi tamamlandığında takip tercihlerini uygulayabilirsin.</p>
    <div className="home-form"><div className="form-columns"><label className="field"><span>Liste kaynağı</span><select value={source} onChange={event => { setSource(event.target.value); setId(''); }}><option value="collection">MusicBrainz koleksiyonu</option><option value="playlist">Home çalma listesi</option></select></label><label className="field"><span>Takip edilecekler</span><select value={kind} onChange={event => setKind(event.target.value)}><option value="artist">Sanatçılar ve konuklar</option><option value="album">Albümler</option></select></label></div>
      <label className="field"><span>{source === 'collection' ? 'Koleksiyon bağlantısı veya MBID' : 'Çalma listesi'}</span>{source === 'collection' ? <input value={id} maxLength={200} onChange={event => setId(event.target.value)} placeholder="https://musicbrainz.org/collection/…" /> : <select value={id} onChange={event => setId(event.target.value)}><option value="">Liste seç</option>{lists.data?.map(list => <option key={list.id} value={list.id}>{list.name} · {list.count} medya</option>)}</select>}</label>
      <Button className="secondary-button" onPress={() => begin.mutate()} isDisabled={!id.trim() || begin.isPending}><ListPlus size={16} /> Listeyi incelemeye al</Button></div>
    <div className="music-list-history">{jobs.data?.map(job => <button className={selected === job.id ? 'selected' : ''} key={job.id} onClick={() => setSelected(job.id)}><strong>{job.name}</strong><span>{labels[job.status]} · {job.cursor}{job.total !== null ? ` / ${job.total}` : ''}</span></button>)}</div>
    {detail.data?.source === 'collection' && <p className="provider-attribution">Kaynak: <a href={`https://musicbrainz.org/collection/${detail.data.sourceId}`} target="_blank" rel="noreferrer">MusicBrainz kullanıcı koleksiyonu</a> · Hesabına ait seçim kaydı.</p>}
    {detail.data && <article className="request-card"><div className="request-summary"><strong>{detail.data.name}</strong><span>{labels[detail.data.status]}</span></div><p className="field-hint">{detail.data.message} · {detail.data.kind === 'artist' ? 'Sanatçılar' : 'Albümler'}</p>{detail.data.skipped > 0 && <p className="field-hint">{detail.data.skipped} medya için doğrulanabilir MusicBrainz kimliği yok veya müzik parçası değil.</p>}
      {['queued', 'running'].includes(detail.data.status) && <p className="field-hint"><RefreshCw className="spin" size={13} /> İş sunucuda devam ediyor; bu ekranı kapatabilirsin.</p>}
      {detail.data.status === 'ready' && <MusicListReview key={detail.data.id + ':' + detail.data.revision} job={detail.data} onApplied={refresh} />}
      {detail.data.receipt && <p role="status"><Check size={15} /> {detail.data.receipt.followed} yeni takip · {detail.data.receipt.alreadyFollowed} kayıt zaten listendeydi · {detail.data.receipt.monitorsCreated} yeni yayın takibi</p>}
      {['queued', 'running', 'ready'].includes(detail.data.status) && <Button variant="ghost" size="sm" onPress={() => cancel.mutate(detail.data!)} isDisabled={cancel.isPending}>Planı iptal et</Button>}
    </article>}
    {(jobs.error || detail.error || lists.error || begin.error || cancel.error) && <p className="form-error">{jobs.error?.message || detail.error?.message || lists.error?.message || begin.error?.message || cancel.error?.message}</p>}
  </section>;
}
function MusicListReview({ job, onApplied }: { job: MusicListJob; onApplied: () => unknown }) {
  const [selected, setSelected] = useState<Set<string>>(new Set()); const [monitor, setMonitor] = useState(false); const [visible, setVisible] = useState(50);
  const apply = useMutation({ mutationFn: () => mutate<MusicListReceipt>(`/music/list-imports/${job.id}/apply`, { revision: job.revision, mbids: [...selected], monitor }), onSuccess: onApplied });
  return <div className="home-form"><p className="field-hint">{job.items.length} farklı kimlik · Önizleme sonu {date(job.expiresAt)}</p><Button variant="ghost" size="sm" onPress={() => setSelected(selected.size === job.items.length ? new Set() : new Set(job.items.map(item => item.mbid)))}>{selected.size === job.items.length ? 'Seçimi temizle' : 'Tümünü seç'}</Button>
    {job.items.slice(0, visible).map(item => <label className="music-list-choice" key={item.mbid}><input type="checkbox" checked={selected.has(item.mbid)} onChange={event => { const next = new Set(selected); if (event.target.checked) next.add(item.mbid); else next.delete(item.mbid); setSelected(next); }} /><span><strong>{item.name}</strong><small>{item.album?.artist ?? item.artist?.disambiguation ?? item.mbid}</small></span></label>)}
    {job.items.length > visible && <Button variant="ghost" size="sm" onPress={() => setVisible(value => value + 50)}>Diğer kayıtlar</Button>}
    <MusicSwitch value={monitor} onChange={setMonitor}>Yeni yayınları da takip et</MusicSwitch><p className="field-hint">Yeni yayın takibi bildirim üretir. Kaynak arama ve otomatik edinme, Takip ve otomasyon ekranındaki ayrı tercihlerdir. Mevcut takip profilleri korunur.</p>
    <Button className="primary-button" onPress={() => apply.mutate()} isDisabled={!selected.size || apply.isPending}>{selected.size} kaydı takip listeme ekle</Button>{apply.error && <p className="form-error">{apply.error.message}</p>}
  </div>;
}
