import { useEffect, useState } from 'react';
import { Button } from '@heroui/react';
import { useInfiniteQuery, useMutation, useQuery } from '@tanstack/react-query';
import { Check, Clock3, GitCompareArrows, ListOrdered, RefreshCw, Scissors, X } from 'lucide-react';
import { clock, date, mutate, request, type CatalogEpisodeOrder, type CatalogIdentity, type ContentEntry, type EpisodeBoundaryJob, type EpisodeCatalogPage, type EpisodeNumbering, type EpisodeRefreshPreview, type MediaItem, type ProviderOrderDetails } from '../api';
import { useRefreshCatalog } from '../catalogQueries';

export function EpisodeRefreshPanel({ content }: { content: ContentEntry }) {
  const refresh = useRefreshCatalog(); const [plan, setPlan] = useState<EpisodeRefreshPreview | null>(null); const [choices, setChoices] = useState<Record<number, string>>({}); const [dirty, setDirty] = useState(false); const [visible, setVisible] = useState(40);
  const episodes = useInfiniteQuery({ queryKey: ['episode-catalog', content.id, true], enabled: !!plan, initialPageParam: 0,
    queryFn: ({ pageParam, signal }) => request<EpisodeCatalogPage>(`/content/${content.id}/episode-catalog?retired=true&offset=${pageParam}&limit=200`, { signal }), getNextPageParam: page => page.offset + page.items.length < page.total ? page.offset + page.items.length : undefined });
  const body = () => ({ planId: plan!.planId, expectedRevision: plan!.catalogRevision, choices: Object.entries(choices).map(([providerId, choice]) => ({ providerId: Number(providerId), existingEpisodeId: choice === 'new' ? null : choice })) });
  const begin = useMutation({ mutationFn: () => mutate<EpisodeRefreshPreview>(`/content/${content.id}/episode-refresh`), onSuccess: value => { setPlan(value); setChoices({}); setDirty(false); setVisible(40); } });
  const inspect = useMutation({ mutationFn: () => mutate<EpisodeRefreshPreview>('/episode-refresh/preview', body()), onSuccess: value => { setPlan(value); setDirty(false); } });
  const apply = useMutation({ mutationFn: () => mutate('/episode-refresh/apply', body()), onSuccess: () => { setPlan(null); void refresh(); } });
  const labels: Record<string, string> = { new: 'Yeni bölüm', metadata: 'Kimlik korunuyor', renumber: 'Numarası değişiyor', 'replace-id': 'Sağlayıcı kimliği yenileniyor', restore: 'Kataloğa dönüyor', 'identity-conflict': 'Kimlik seçimi gerekli' };
  return <section className="episode-catalog-panel"><Button variant="ghost" onPress={() => begin.mutate()} isDisabled={begin.isPending}><GitCompareArrows size={15} />{begin.isPending ? 'Sağlayıcı kataloğu okunuyor…' : 'Sağlayıcı bölüm değişikliklerini incele'}</Button>
    {plan && <div className="home-form"><p className="field-hint">{plan.changes.length} sağlayıcı bölümü · {plan.retired.length} arşivlenen bölüm · Plan sonu: {date(plan.expiresAt)}</p>
      {plan.changes.slice(0, visible).map(change => <div className="episode-change-row" key={change.incoming.providerId}><div><strong>{change.incoming.title}</strong><small>{change.previousSeason !== null ? `S${change.previousSeason} E${change.previousEpisode} → ` : ''}S{change.incoming.seasonNumber} E{change.incoming.episodeNumber} · {labels[change.action]}</small></div>
        {['identity-conflict', 'new', 'replace-id'].includes(change.action) && <select aria-label={`${change.incoming.title} kalıcı kimliği`} value={choices[change.incoming.providerId] ?? (change.needsChoice ? '' : change.existingId ?? 'new')} onChange={event => { if (!event.target.value) return; setChoices(old => ({ ...old, [change.incoming.providerId]: event.target.value })); setDirty(true); }}><option value="">Karşılık seç</option><option value="new">Yeni bölüm oluştur · eski kimliği koru</option>{episodes.data?.pages.flatMap(page => page.items).map(episode => <option key={episode.id} value={episode.id}>S{episode.metadata.seasonNumber} E{episode.metadata.episodeNumber} · {episode.metadata.title}{episode.retired ? ' · Arşiv' : ''}</option>)}</select>}
      </div>)}
      {plan.changes.length > visible && <Button variant="ghost" size="sm" onPress={() => setVisible(value => value + 40)}>Diğer değişiklikler</Button>}
      {episodes.hasNextPage && <Button variant="ghost" size="sm" onPress={() => episodes.fetchNextPage()} isDisabled={episodes.isFetchingNextPage}>Diğer eski kimlik seçenekleri</Button>}
      {!!plan.retired.length && <details className="advanced-fields"><summary>Sağlayıcıda görünmeyen bölümler</summary>{plan.retired.slice(0, 100).map(episode => <p className="field-hint" key={episode.id}>S{episode.metadata.seasonNumber} E{episode.metadata.episodeNumber} · {episode.metadata.title}</p>)}</details>}
      {plan.notes.map(note => <p className="field-hint" key={note}>{note}</p>)}{plan.conflicts.map(conflict => <p className="form-error" key={conflict}>{conflict}</p>)}
      <div className="content-actions">{dirty && <Button variant="ghost" onPress={() => inspect.mutate()} isDisabled={inspect.isPending}>Seçimleri denetle</Button>}<Button className="secondary-button" onPress={() => apply.mutate()} isDisabled={!plan.canApply || dirty || apply.isPending}><Check size={15} /> Bölüm değişikliklerini uygula</Button><Button variant="ghost" onPress={() => setPlan(null)}>Kapat</Button></div>
    </div>}
    {(begin.error || inspect.error || apply.error || episodes.error) && <p className="form-error">{begin.error?.message || inspect.error?.message || apply.error?.message || episodes.error?.message}</p>}
  </section>;
}

const orderNames: Record<string, string> = { dvd: 'DVD', digital: 'Dijital', air: 'Yayın', story: 'Hikâye', production: 'Yapım', tv: 'TV', custom: 'Özel' };
export function AlternativeOrdersPanel({ content, numbering }: { content: ContentEntry; numbering: EpisodeNumbering }) {
  const [open, setOpen] = useState(false); const [editing, setEditing] = useState<CatalogEpisodeOrder | 'new' | null>(null); const refresh = useRefreshCatalog();
  const orders = useQuery({ queryKey: ['episode-orders', content.id], queryFn: ({ signal }) => request<CatalogEpisodeOrder[]>(`/content/${content.id}/episode-orders`, { signal }), enabled: open });
  const select = useMutation({ mutationFn: (orderId: string | null) => mutate(`/content/${content.id}/episode-orders/selection`, { orderId, expectedRevision: numbering.revision }, 'PUT'), onSuccess: refresh });
  const remove = useMutation({ mutationFn: (order: CatalogEpisodeOrder) => mutate(`/episode-orders/${order.id}?revision=${order.revision}`, undefined, 'DELETE'), onSuccess: refresh });
  return <section className="episode-catalog-panel"><Button variant="ghost" onPress={() => setOpen(value => !value)} aria-expanded={open}><ListOrdered size={15} /> DVD, dijital ve alternatif sıralar</Button>
    {open && <div className="home-form"><p className="field-hint">Dosya adları ve kaynak aramaları seçili sıranın sezon/bölüm numaralarını kullanır. Katalog kimlikleri kanonik kalır.</p>{!numbering.valid && <p className="form-error">Etkin sırada arşivlenmiş veya taşınmış kimlik var; karşılıklarını düzenle.</p>}
      {orders.data?.map(order => <div className="episode-order-card" key={order.id}><div><strong>{order.name}</strong><small>{orderNames[order.kind]} · {order.items.length} kaynak bölümü · {order.source}{order.id === numbering.orderId ? ' · Etkin' : ''}</small></div><div className="content-actions"><Button size="sm" variant="ghost" onPress={() => select.mutate(order.id)} isDisabled={select.isPending || order.id === numbering.orderId}>Bu sırayı kullan</Button><Button size="sm" variant="ghost" onPress={() => setEditing(order)}>Düzenle</Button><Button size="sm" variant="ghost" isIconOnly aria-label={`${order.name} sırasını kaldır`} onPress={() => remove.mutate(order)} isDisabled={remove.isPending || order.id === numbering.orderId}><X size={14} /></Button></div></div>)}
      {numbering.orderId && <Button variant="ghost" size="sm" onPress={() => select.mutate(null)} isDisabled={select.isPending}>Kanonik sezon/bölüm sırasını kullan</Button>}
      <Button variant="ghost" onPress={() => setEditing('new')}>Elle alternatif sıra oluştur</Button>
      {editing && <OrderForm key={editing === 'new' ? 'new' : editing.id + ':' + editing.revision} content={content} order={editing === 'new' ? undefined : editing} onDone={() => setEditing(null)} />}
      <ImportOrderForm content={content} />
      {(orders.error || select.error || remove.error) && <p className="form-error">{orders.error?.message || select.error?.message || remove.error?.message}</p>}
    </div>}
  </section>;
}
function OrderForm({ content, order, onDone }: { content: ContentEntry; order?: CatalogEpisodeOrder; onDone: () => void }) {
  const [name, setName] = useState(order?.name ?? ''); const [kind, setKind] = useState(order?.kind ?? 'custom'); const [text, setText] = useState(order?.items.map(item => `${item.season}:${item.episode}=${item.coordinates.length === item.episodeIds.length ? item.coordinates.map(episode => `${episode.season}:${episode.episode}`).join(',') : item.episodeIds.map(id => '#' + id).join(',')}`).join('\n') ?? ''); const refresh = useRefreshCatalog();
  const save = useMutation({ mutationFn: () => {
    const items = text.split('\n').map(line => line.trim()).filter(Boolean).map(line => {
      const match = /^(\d+)\s*:\s*(\d+)\s*=\s*(.+)$/.exec(line); if (!match) throw new Error('Her satır kaynakSezon:bölüm=kanonikSezon:bölüm olmalı.');
      if (match[3].includes('#')) { const ids = match[3].split(',').map(value => value.trim()); if (ids.some(value => !/^#[a-f0-9]{32}$/.test(value))) throw new Error('Eski kimlik satırındaki karşılıkları etkin sezon:bölüm değerleriyle yeniden seç.'); return { season: Number(match[1]), episode: Number(match[2]), episodeIds: ids.map(value => value.slice(1)), coordinates: null }; }
      const coordinates = match[3].split(',').map(value => { const target = /^\s*(\d+)\s*:\s*(\d+)\s*$/.exec(value); if (!target) throw new Error('Kanonik karşılıkları virgülle ayır.'); return { season: Number(target[1]), episode: Number(target[2]) }; });
      return { season: Number(match[1]), episode: Number(match[2]), episodeIds: [], coordinates };
    });
    return mutate(`/content/${content.id}/episode-orders`, { id: order?.id ?? null, expectedRevision: order?.revision ?? 0, expectedCatalogRevision: content.catalogRevision, name, kind, items }, 'PUT');
  }, onSuccess: () => { void refresh(); onDone(); } });
  return <div className="home-form episode-order-form"><div className="form-columns"><label className="field"><span>Sıra adı</span><input value={name} onChange={event => setName(event.target.value)} maxLength={100} /></label><label className="field"><span>Tür</span><select value={kind} onChange={event => setKind(event.target.value)}>{Object.entries(orderNames).map(([id, label]) => <option key={id} value={id}>{label}</option>)}</select></label></div>
    <label className="field"><span>Kaynak sezon:bölüm = kanonik sezon:bölüm</span><textarea rows={7} value={text} maxLength={1000000} onChange={event => setText(event.target.value)} placeholder={'1:1=1:2\n1:2=1:1\n1:3=1:3,1:4'} /></label><p className="field-hint">Bir kaynak dosyası birkaç kanonik bölüm içeriyorsa karşılıkları virgülle yaz. # ile gösterilen eski kimlikler için etkin sezon:bölüm karşılığını açıkça seç.</p>
    <div className="content-actions"><Button className="secondary-button" onPress={() => save.mutate()} isDisabled={!name.trim() || !text.trim() || save.isPending}>Sırayı kaydet</Button><Button variant="ghost" onPress={onDone}>Kapat</Button></div>{save.error && <p className="form-error">{save.error.message}</p>}
  </div>;
}
function ImportOrderForm({ content }: { content: ContentEntry }) {
  const [open, setOpen] = useState(false); const [group, setGroup] = useState(''); const [seasons, setSeasons] = useState<Record<string, string>>({}); const refresh = useRefreshCatalog();
  const groups = useQuery({ queryKey: ['provider-episode-orders', content.id], queryFn: ({ signal }) => request<{ id: string; name: string; type: number; episodeCount: number }[]>(`/content/${content.id}/episode-orders/tmdb`, { signal }), enabled: open });
  const details = useQuery({ queryKey: ['provider-order-details', content.id, group], queryFn: ({ signal }) => request<ProviderOrderDetails>(`/content/${content.id}/episode-orders/tmdb/${group}`, { signal }), enabled: open && !!group });
  useEffect(() => { if (details.data) { const minimum = Math.min(...details.data.groups.map(group => group.position)); setSeasons(Object.fromEntries(details.data.groups.map(group => [group.id, String(group.position + (minimum === 0 ? 1 : 0))]))); } }, [details.data]);
  const save = useMutation({ mutationFn: () => mutate(`/content/${content.id}/episode-orders/import`, { provider: 'tmdb', orderId: group, seasons: Object.fromEntries(Object.entries(seasons).map(([id, value]) => [id, Number(value)])), expectedCatalogRevision: content.catalogRevision, expectedSourceFingerprint: details.data?.fingerprint }), onSuccess: () => { void refresh(); setOpen(false); } });
  return <div><Button variant="ghost" onPress={() => setOpen(value => !value)}><RefreshCw size={14} /> TMDB bölüm grubunu aktar</Button>{open && <div className="home-form"><label className="field"><span>Sağlayıcının sırası</span><select value={group} onChange={event => setGroup(event.target.value)}><option value="">Grup seç</option>{groups.data?.filter(group => group.type !== 2).map(group => <option value={group.id} key={group.id}>{group.name} · {group.episodeCount} bölüm</option>)}</select></label>
    {details.data && <><p className="field-hint">Her gruba, dosya adlarında kullanılan sezon numarasını ata. Özel bölümler için 0 seçebilirsin.</p>{details.data.groups.map(group => <label className="catalog-map-row" key={group.id}><span>{group.name} · {group.episodes.length} bölüm</span><input aria-label={`${group.name} kaynak sezonu`} type="number" min={0} max={1000} value={seasons[group.id] ?? ''} onChange={event => setSeasons(old => ({ ...old, [group.id]: event.target.value }))} /></label>)}
      <Button className="secondary-button" onPress={() => save.mutate()} isDisabled={save.isPending || details.isFetching || Object.values(seasons).some(value => value === '')}>Sağlayıcı sırasını kaydet</Button></>}
    {(groups.error || details.error || save.error) && <p className="form-error">{groups.error?.message || details.error?.message || save.error?.message}</p>}
  </div>}</div>;
}

export function EpisodeAliasPanel({ identity }: { identity: CatalogIdentity }) {
  const sources = identity.aliases.filter(alias => alias.provider !== identity.content.metadata.provider); const [open, setOpen] = useState(false);
  const [source, setSource] = useState(sources[0] ? `${sources[0].provider}:${sources[0].providerId}` : ''); const [season, setSeason] = useState('1'); const [episode, setEpisode] = useState('1'); const [text, setText] = useState(''); const refresh = useRefreshCatalog();
  const sourceKeys = sources.map(alias => `${alias.provider}:${alias.providerId}`).join(',');
  useEffect(() => { const keys = sourceKeys.split(',').filter(Boolean); if (!keys.includes(source)) setSource(keys[0] ?? ''); }, [sourceKeys, source]);
  const save = useMutation({ mutationFn: () => {
    const [provider, providerId] = source.split(':'); const targets = text.split('\n').map(value => value.trim()).filter(Boolean).map(value => {
      const match = /^(\d+):(\d+)(?:\s*@\s*(\d+(?:\.\d+)?)\s*-\s*(\d+(?:\.\d+)?))?$/.exec(value); if (!match) throw new Error('Karşılığı sezon:bölüm; zaman varsa sezon:bölüm @ başlangıç-bitiş biçiminde yaz.');
      return { episodeId: '', season: Number(match[1]), episode: Number(match[2]), startSeconds: match[3] ? Number(match[3]) : null, endSeconds: match[4] ? Number(match[4]) : null };
    });
    return mutate(`/content/${identity.content.id}/episode-map`, { provider, providerId: Number(providerId), season: Number(season), episode: Number(episode), targets, expectedRevision: identity.content.catalogRevision }, 'PUT');
  }, onSuccess: () => { void refresh(); setText(''); } });
  if (!sources.length) return null;
  return <section className="episode-catalog-panel"><Button variant="ghost" onPress={() => setOpen(value => !value)}><GitCompareArrows size={15} /> Bağlı sağlayıcının bölüm karşılıkları</Button>{open && <div className="home-form"><label className="field"><span>Kaynak sağlayıcı</span><select value={source} onChange={event => setSource(event.target.value)}>{sources.map(alias => <option key={alias.provider + alias.providerId} value={`${alias.provider}:${alias.providerId}`}>{alias.provider.toUpperCase()} #{alias.providerId}</option>)}</select></label>
    <div className="form-columns"><label className="field"><span>Kaynak sezon</span><input type="number" min={0} max={1000} value={season} onChange={event => setSeason(event.target.value)} /></label><label className="field"><span>Kaynak bölüm</span><input type="number" min={1} max={10000} value={episode} onChange={event => setEpisode(event.target.value)} /></label></div>
    <label className="field"><span>Kanonik karşılıklar · sırayla, her satıra bir bölüm</span><textarea rows={4} value={text} onChange={event => setText(event.target.value)} maxLength={4000} placeholder={'1:1 @ 0-1320\n1:2 @ 1320-2640'} /></label><p className="field-hint">Zamanlar isteğe bağlı ve kaynak bölümüne göre saniyedir. Yeni dosya eşleştirmeleri bu karşılıkları kullanır; mevcut dosyanın kapsamını dosya ayrıntısından düzenleyebilirsin.</p>
    <Button variant="ghost" onPress={() => save.mutate()} isDisabled={!text.trim() || !source || save.isPending}>Bölüm karşılığını kaydet</Button>{save.error && <p className="form-error">{save.error.message}</p>}
  </div>}</section>;
}
export function EpisodeBoundaryPanel({ item }: { item: MediaItem }) {
  const refresh = useRefreshCatalog(); const jobs = useQuery({ queryKey: ['episode-boundaries', item.id], queryFn: ({ signal }) => request<EpisodeBoundaryJob[]>(`/media/${item.id}/episode-boundaries`, { signal }), enabled: item.episodes.length > 1, refetchInterval: query => query.state.data?.some(job => ['queued', 'running'].includes(job.status)) ? 2000 : false });
  const start = useMutation({ mutationFn: () => mutate(`/media/${item.id}/episode-boundaries`, { expectedMatchRevision: item.matchRevision }), onSuccess: refresh });
  const command = useMutation({ mutationFn: ({ job, action }: { job: EpisodeBoundaryJob; action: string }) => mutate(`/episode-boundaries/${job.id}/${action}`, { revision: job.revision }), onSuccess: refresh });
  if (item.episodes.length < 2) return null;
  return <section className="episode-catalog-panel"><Button variant="ghost" onPress={() => start.mutate()} isDisabled={start.isPending || jobs.data?.some(job => ['queued', 'running'].includes(job.status))}><Scissors size={15} /> Dosyadan bölüm sınırlarını öner</Button>
    <p className="field-hint">Sahne işaretleri veya siyah görüntü/sessizlik incelenir. Öneriyi kontrol ederek uygula; belirsiz sınırları aşağıdan elle düzenleyebilirsin.</p>
    {jobs.data?.slice(0, 3).map(job => <div className="episode-boundary-result" key={job.id}><p><Clock3 size={13} /> {job.message}</p>{['queued', 'running'].includes(job.status) && <><progress max={100} value={job.progress} aria-label="Bölüm sınırı analiz ilerlemesi" /><Button size="sm" variant="ghost" onPress={() => command.mutate({ job, action: 'cancel' })} isDisabled={command.isPending}>İptal et</Button></>}
      {job.result && <>{job.result.segments.map((segment, index) => <p className="field-hint" key={segment.episodeId}>{index + 1}. bölüm · {clock(segment.startSeconds)} – {clock(segment.endSeconds)}</p>)}{job.result.evidence.map(evidence => <p className="field-hint" key={evidence}>{evidence}</p>)}{job.result.warnings.map(warning => <p className="field-hint" key={warning}>{warning}</p>)}
        {job.status === 'completed' && job.result.canApply && <Button size="sm" className="secondary-button" onPress={() => command.mutate({ job, action: 'apply' })} isDisabled={job.matchRevision !== item.matchRevision || command.isPending}>Bu bölüm zamanlarını uygula</Button>}</>}
    </div>)}{(jobs.error || start.error || command.error) && <p className="form-error">{jobs.error?.message || start.error?.message || command.error?.message}</p>}
  </section>;
}
