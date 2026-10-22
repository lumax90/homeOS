import { useState } from 'react';
import { Button } from '@heroui/react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { RefreshCw, ShieldCheck, WandSparkles } from 'lucide-react';
import { date, mutate, request, type AutomationJob, type AutomationRule, type AutomationSettings, type BlockedRelease } from '../api';

export function AutomationPanel({ contentId }: { contentId: string }) {
  const rule = useQuery({ queryKey: ['automation-rule', contentId], queryFn: () => request<AutomationRule>(`/automation/${contentId}`) });
  const [open, setOpen] = useState(false);
  return <section className="automation-panel"><Button variant="ghost" onPress={() => setOpen(value => !value)} aria-expanded={open}><WandSparkles size={15} />{rule.data?.settings.enabled ? rule.data.settings.autoGrab ? 'Otomatik edinme etkin' : 'Otomatik arama etkin' : 'Otomasyonu ayarla'}</Button>
    {open && rule.data && <AutomationForm key={rule.data.revision} rule={rule.data} />}{rule.error && <p className="form-error">{rule.error.message}</p>}
  </section>;
}
function AutomationForm({ rule }: { rule: AutomationRule }) {
  const client = useQueryClient(); const [settings, setSettings] = useState(rule.settings);
  const set = <K extends keyof AutomationSettings>(key: K, value: AutomationSettings[K]) => setSettings(previous => ({ ...previous, [key]: value }));
  const save = useMutation({ mutationFn: () => mutate<AutomationRule>(`/automation/${rule.contentId}`, settings, 'PUT'), onSuccess: async result => {
    client.setQueryData(['automation-rule', rule.contentId], result); await client.invalidateQueries({ queryKey: ['automation'] });
  } });
  const search = useMutation({ mutationFn: () => mutate(`/automation/${rule.contentId}/search`), onSuccess: () => client.invalidateQueries({ queryKey: ['automation-jobs'] }) });
  return <div className="home-form automation-form">
    <label className="check-field"><input type="checkbox" checked={settings.enabled} onChange={event => set('enabled', event.target.checked)} /> Eksikleri kaynaklarda periyodik ara</label>
    <label className="check-field"><input type="checkbox" checked={settings.autoGrab} onChange={event => set('autoGrab', event.target.checked)} disabled={!settings.enabled} /> Uygun sürümü otomatik indir</label>
    <p className="field-hint">Edinme hesabı: {rule.budgetName ?? 'Kuralı kaydeden hesap'}. Otomatik aktarımlar bu hesabın indirme bütçesini kullanır; kural yeniden kaydedilirse kaydeden hesap sorumlu olur.</p>
    <p className="field-hint">Otomatik indirme kapalıysa uygun sürümler bildirim olarak gelir. Onaylı ve yayımlanmış istek kapsamı kullanılır.</p>
    <label className="check-field"><input type="checkbox" checked={settings.allowUpgrades} onChange={event => set('allowUpgrades', event.target.checked)} /> Daha yüksek çözünürlüklü sürüme yükselt</label>
    <label className="check-field"><input type="checkbox" checked={settings.sameQualityUpgrades ?? false} disabled={!settings.allowUpgrades} onChange={event => set('sameQualityUpgrades', event.target.checked)} /> Aynı çözünürlükte daha iyi kaynak/custom-format/PROPER sürümünü ara</label>
    <div className="form-columns"><label className="field"><span>Yükseltmenin duracağı kalite</span><select value={settings.cutoffResolution} onChange={event => set('cutoffResolution', Number(event.target.value))}>{[480, 576, 720, 1080, 2160].map(value => <option key={value} value={value}>{value}p</option>)}</select></label><label className="field"><span>Arama aralığı · dakika</span><input type="number" min={15} max={1440} value={settings.searchIntervalMinutes} onChange={event => set('searchIntervalMinutes', Number(event.target.value))} /></label></div>
    <div className="form-columns"><label className="field"><span>İlk görüldükten sonra bekle · dakika</span><input type="number" min={0} max={1440} value={settings.delayMinutes} onChange={event => set('delayMinutes', Number(event.target.value))} /></label><label className="field"><span>Minimum sürüm yaşı · saat</span><input type="number" min={0} max={720} value={settings.minimumAgeHours} onChange={event => set('minimumAgeHours', Number(event.target.value))} /></label></div>
    <label className="field"><span>Günlük en fazla otomatik edinme</span><input type="number" min={1} max={20} value={settings.maximumDailyGrabs} onChange={event => set('maximumDailyGrabs', Number(event.target.value))} /></label>
    <div className="content-actions"><Button className="secondary-button" onPress={() => save.mutate()} isDisabled={save.isPending}>Otomasyonu kaydet</Button><Button variant="ghost" onPress={() => search.mutate()} isDisabled={!rule.settings.enabled || search.isPending}><RefreshCw size={14} /> Şimdi değerlendir</Button></div>
    {(save.error || search.error || rule.lastError) && <p className="form-error">{save.error?.message || search.error?.message || rule.lastError}</p>}
    {rule.settings.enabled && <p className="field-hint">Son planlama: {rule.lastRun ? date(rule.lastRun) : 'Bekleniyor'} · Sonraki: {date(rule.nextRun)}</p>}
  </div>;
}

export function AutomationHistory() {
  const client = useQueryClient();
  const jobs = useQuery({ queryKey: ['automation-jobs'], queryFn: () => request<AutomationJob[]>('/automation/jobs'), refetchInterval: 10_000 });
  const rules = useQuery({ queryKey: ['automation'], queryFn: () => request<AutomationRule[]>('/automation') });
  const blocked = useQuery({ queryKey: ['automation-blocked'], queryFn: () => request<BlockedRelease[]>('/automation/blocked') });
  const unblock = useMutation({ mutationFn: (item: BlockedRelease) => mutate(`/automation/${item.contentId}/blocked/${item.fingerprint}`, undefined, 'DELETE'), onSuccess: () => client.invalidateQueries({ queryKey: ['automation-blocked'] }) });
  const labels: Record<string, string> = { queued: 'Sırada', running: 'Aranıyor', waiting: 'Bekleniyor', suggested: 'Sürüm önerildi', grabbed: 'Ediniliyor', failed: 'Sorun var', cancelled: 'İptal edildi', blocked: 'Kullanıcı kararı bekleniyor', satisfied: 'Hazır' };
  return <section><div className="section-heading"><div><h3>Otomasyon geçmişi</h3><p>İçerik ayrıntılarından etkinleştirdiğin kurallar ve kalıcı işler.</p></div></div>
    {!jobs.data?.length && <div className="search-empty"><WandSparkles size={28} /><h2>İlk otomasyonunu seç.</h2><p>Takip ettiğin bir içeriğin ayrıntılarında otomatik aramayı açabilirsin.</p></div>}
    {jobs.data?.map(job => <article className="request-card" key={job.id}><div className="request-summary"><strong>{rules.data?.find(rule => rule.contentId === job.contentId)?.title || 'İçerik'}{job.season === null ? '' : ` · S${job.season} E${job.episode}`}</strong><span className="episode-state">{labels[job.status]}</span></div><p className="request-note">{job.message || 'İş sırası bekleniyor.'}</p><p className="field-hint">{job.attempts} deneme · {date(job.createdAt)}{job.status === 'waiting' ? ` · Yeniden: ${date(job.dueAt)}` : ''}</p></article>)}
    {!!blocked.data?.length && <><div className="section-heading"><h3><ShieldCheck size={15} /> Engellenen sürümler</h3></div>{blocked.data.map(item => <article className="request-card" key={`${item.contentId}-${item.fingerprint}`}><strong>{item.title}</strong><p className="request-note">{item.reason}</p><Button size="sm" variant="ghost" onPress={() => unblock.mutate(item)} isDisabled={unblock.isPending}>Bu sürüme yeniden izin ver</Button></article>)}</>}
    {(jobs.error || blocked.error || unblock.error) && <p className="form-error">{jobs.error?.message || blocked.error?.message || unblock.error?.message}</p>}
  </section>;
}
