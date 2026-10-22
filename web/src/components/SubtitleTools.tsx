import { useState } from 'react';
import { Button } from '@heroui/react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { AudioLines, History, RefreshCw, RotateCcw, SlidersHorizontal, Trash2 } from 'lucide-react';
import { date, mutate, request, type MediaTracks, type SubtitlePolicy, type SubtitleSyncJob, type SubtitleVersion } from '../api';
import { ProfileForm } from './SubtitleProviders';
import { SpeechGenerationPanel } from './SpeechGeneration';

function useSubtitleRefresh(mediaId: string) {
  const client = useQueryClient(); return () => Promise.all(['subtitle-policy', 'subtitle-versions', 'subtitle-sync', 'subtitle-wanted', 'media-tracks'].map(key => client.invalidateQueries({ queryKey: key === 'subtitle-wanted' ? [key] : [key, mediaId] })));
}
export function SubtitleMediaPanel({ mediaId, onSelected }: { mediaId: string; onSelected?: (id: string) => void }) {
  const [open, setOpen] = useState(false); const refresh = useSubtitleRefresh(mediaId);
  const policy = useQuery({ queryKey: ['subtitle-policy', mediaId], queryFn: () => request<SubtitlePolicy>(`/subtitles/media/${mediaId}/policy`), enabled: open });
  const versions = useQuery({ queryKey: ['subtitle-versions', mediaId], queryFn: () => request<SubtitleVersion[]>(`/subtitles/media/${mediaId}/versions`), enabled: open });
  const inherit = useMutation({ mutationFn: () => mutate(`/subtitles/media/${mediaId}/policy`, { profile: null, expectedRevision: policy.data!.stamp.overrideRevision }, 'PUT'), onSuccess: refresh });
  const wake = useMutation({ mutationFn: () => mutate(`/subtitles/media/${mediaId}/refresh`), onSuccess: refresh });
  const restore = useMutation({ mutationFn: (version: SubtitleVersion) => {
    const current = versions.data?.filter(item => !item.retired && item.language === version.language && item.forced === version.forced && item.hearingImpaired === version.hearingImpaired).at(-1);
    return mutate<SubtitleVersion>(`/subtitles/media/${mediaId}/versions/${version.id}/restore`, { replaceId: current?.id ?? null });
  }, onSuccess: async result => { await refresh(); onSelected?.('managed-' + result.id); } });
  const remove = useMutation({ mutationFn: (id: string) => mutate(`/subtitles/media/${mediaId}/versions/${id}`, undefined, 'DELETE'), onSuccess: refresh });
  const labels: Record<string, string> = { provider: 'OpenSubtitles', upload: 'Elle yüklendi', sync: 'Sesle eşlendi', generated:'Konuşmadan üretildi' };
  return <section className="subtitle-media-panel"><Button variant="ghost" size="sm" onPress={() => setOpen(value => !value)} aria-expanded={open}><SlidersHorizontal size={15} /> Bu dosyanın altyazı tercihleri</Button>{open && <div className="home-form automation-form">
    {policy.data && <><p className="field-hint">{policy.data.inherited ? 'Genel dil profili kullanılıyor.' : 'Bu dosyanın özel dil profili kullanılıyor.'}{policy.data.upgradesPaused ? ' Geri alma sonrası otomatik yükseltme duraklatıldı.' : ''}</p>
      <ProfileForm key={JSON.stringify(policy.data.stamp)} initial={policy.data.override ?? policy.data.profile} mediaId={mediaId} revision={policy.data.stamp.overrideRevision} />
      <div className="content-actions"><Button variant="ghost" size="sm" onPress={() => inherit.mutate()} isDisabled={inherit.isPending}><RotateCcw size={14} /> Genel tercihe dön</Button><Button variant="ghost" size="sm" onPress={() => wake.mutate()} isDisabled={wake.isPending || !policy.data.profile.enabled}><RefreshCw size={14} /> Şimdi değerlendir</Button></div></>}
    <h4><History size={15} /> Altyazı sürüm geçmişi</h4>{versions.data?.slice().reverse().map(version => <article className="subtitle-version" key={version.id}><div><strong>{version.title}</strong><span>{version.language} · {labels[version.origin] ?? version.origin} · {version.retired ? 'Geçmiş' : 'Etkin'}{version.score === null ? '' : ` · ${version.score} puan`}</span><small>{date(version.createdAt)}</small></div>
      {version.retired ? <Button variant="ghost" size="sm" onPress={() => restore.mutate(version)} isDisabled={restore.isPending}>Geri al</Button> : onSelected && <Button variant="ghost" size="sm" onPress={() => onSelected('managed-' + version.id)}>Seç</Button>}<Button variant="ghost" size="sm" isIconOnly aria-label={`${version.title} sürümünü sil`} onPress={() => remove.mutate(version.id)} isDisabled={remove.isPending}><Trash2 size={14} /></Button>
    </article>)}{!versions.data?.length && <p className="field-hint">Yüklediğin, sağlayıcıdan eklediğin veya senkronladığın sürümler burada görünür.</p>}
    {(policy.error || versions.error || inherit.error || wake.error || restore.error || remove.error) && <p className="form-error">{policy.error?.message || versions.error?.message || inherit.error?.message || wake.error?.message || restore.error?.message || remove.error?.message}</p>}
  </div>}</section>;
}
export function SubtitleSyncPanel({ mediaId, selectedTrackId, onSelected }: { mediaId: string; selectedTrackId?: string; onSelected?: (id: string) => void }) {
  const [open, setOpen] = useState(false); const [track, setTrack] = useState(''); const [audio, setAudio] = useState(''); const [range, setRange] = useState(30); const [rate, setRate] = useState(false); const [automatic, setAutomatic] = useState(false); const refresh = useSubtitleRefresh(mediaId);
  const [segmented,setSegmented]=useState(false);
  const tracks = useQuery({ queryKey: ['media-tracks', mediaId], queryFn: () => request<MediaTracks>(`/media/${mediaId}/tracks`), enabled: open });
  const capabilities = useQuery({ queryKey: ['subtitle-vad'], queryFn: () => request<{ available: boolean; model: string; runtime: string; error: string | null }>('/subtitles/sync/capabilities'), enabled: open, staleTime: 60000 });
  const jobs = useQuery({ queryKey: ['subtitle-sync', mediaId], queryFn: () => request<SubtitleSyncJob[]>(`/subtitles/sync/jobs?mediaId=${mediaId}`), enabled: open, refetchInterval: open ? 3000 : false });
  const source = track || selectedTrackId || '';
  const start = useMutation({ mutationFn: () => mutate(`/subtitles/media/${mediaId}/sync`, { trackId: source, audioStream: audio === '' ? null : Number(audio), maximumOffsetSeconds: range, correctFrameRate: rate, autoApply: automatic, segmented }), onSuccess: refresh });
  const cancel = useMutation({ mutationFn: (id: string) => mutate(`/subtitles/sync/jobs/${id}/cancel`), onSuccess: refresh });
  const apply = useMutation({ mutationFn: (job: SubtitleSyncJob) => mutate<SubtitleVersion>(`/subtitles/sync/jobs/${job.id}/apply`, { revision: job.revision }), onSuccess: async result => { await refresh(); onSelected?.('managed-' + result.id); } });
  const statuses: Record<string, string> = { queued: 'Sırada', running: 'Konuşma ölçülüyor', proposed: 'Öneri hazır', applying: 'Uygulanıyor', completed: 'Tamamlandı', failed: 'İlgilenmen gerekiyor', cancelled: 'İptal' };
  return <section className="subtitle-sync-panel"><Button variant="ghost" size="sm" onPress={() => setOpen(value => !value)} aria-expanded={open}><AudioLines size={15} /> Sesle altyazı zamanını eşleştir</Button>{open && <div className="home-form automation-form">
    <p className="field-hint">{capabilities.data?.model ?? 'Yerel konuşma etkinliği modeli hazırlanıyor…'} · Ses bu sunucuda analiz edilir.</p>
    <div className="form-columns"><label className="field"><span>Metin altyazısı</span><select value={source} onChange={event => setTrack(event.target.value)}><option value="">Bir metin izi seç</option>{tracks.data?.subtitles.filter(item => item.playable).map(item => <option key={item.id} value={item.id}>{item.language} · {item.title || item.format}</option>)}</select></label><label className="field"><span>Konuşmanın olduğu ses izi</span><select value={audio} onChange={event => setAudio(event.target.value)}><option value="">Varsayılan ses</option>{tracks.data?.streams.filter(stream => stream.kind === 'audio').map(stream => <option key={stream.index} value={stream.index}>{stream.language} · {stream.title || stream.codec}</option>)}</select></label></div>
    <label className="field"><span>Arama aralığı · ± saniye</span><input type="number" min={1} max={120} value={range} onChange={event => setRange(Number(event.target.value))} /></label>
    <label className="check-field"><input type="checkbox" checked={rate} onChange={event => setRate(event.target.checked)} /> 23,976 / 24 / 25 fps zaman ölçeği farkını da dene</label>
    <label className="check-field"><input type="checkbox" checked={segmented} onChange={event=>setSegmented(event.target.checked)}/> Bölgesel zaman eşlemesi · farklı kesimler ve değişen gecikme</label>
    <label className="check-field"><input type="checkbox" checked={automatic} onChange={event => setAutomatic(event.target.checked)} /> Belirgin eşleşmeyi otomatik uygula</label>
    <p className="field-hint">Sonuç yeni bir WebVTT sürümüdür. Konuşma zamanlarıyla metin cue'ları karşılaştırılır; diyalog metni üretilmez. Belirsiz sonuç uygulanmaz.</p>
    <Button className="secondary-button" onPress={() => start.mutate()} isDisabled={!source || !capabilities.data?.available || start.isPending}>Yerel ses analizini başlat</Button>
    {jobs.data?.map(job => <article className="subtitle-sync-job" key={job.id}><div><strong>{statuses[job.status] ?? job.status}</strong><span>{job.progress}%</span></div><p>{job.message}</p>{job.status === 'running' && <progress max={100} value={job.progress} aria-label="Ses analizi ilerlemesi" />}
      {job.result && <p className="field-hint">{job.result.offsetSeconds > 0 ? '+' : ''}{job.result.offsetSeconds.toFixed(1)} sn · zaman ölçeği {job.result.timeScale.toFixed(4)} · uyum {Math.round(job.result.score * 100)}/100 · {job.result.voiceSeconds.toFixed(1)} sn konuşma</p>}
      {job.status === 'proposed' && job.result?.canApply && <Button variant="ghost" size="sm" onPress={() => apply.mutate(job)} isDisabled={apply.isPending}>Öneriyi uygula</Button>}{['queued', 'running', 'proposed', 'applying'].includes(job.status) && <Button variant="ghost" size="sm" onPress={() => cancel.mutate(job.id)} isDisabled={cancel.isPending}>İptal et</Button>}
      {job.status === 'completed' && job.subtitleId && onSelected && <Button variant="ghost" size="sm" onPress={() => onSelected('managed-' + job.subtitleId)}>Senkron sürümünü seç</Button>}
    </article>)}
    {(capabilities.error || capabilities.data?.error || tracks.error || jobs.error || start.error || apply.error || cancel.error) && <p className="form-error">{capabilities.error?.message || capabilities.data?.error || tracks.error?.message || jobs.error?.message || start.error?.message || apply.error?.message || cancel.error?.message}</p>}
  </div>}<SpeechGenerationPanel mediaId={mediaId} onSelected={onSelected}/></section>;
}
