import { useState } from 'react';
import { Button } from '@heroui/react';
import { useInfiniteQuery, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ArrowDown, ArrowUp, Check, Layers3, Link2, Plus, RefreshCw, Search, X } from 'lucide-react';
import { clock, mutate, request, type CatalogIdentity, type CatalogMergePreview, type ContentEntry, type EpisodeNumbering, type EpisodePage, type MediaItem, type MediaTracks, type MovieEdition, type ProviderContent } from '../api';
import { useRefreshCatalog } from '../catalogQueries';
import { AlternativeOrdersPanel, EpisodeRefreshPanel, EpisodeAliasPanel, EpisodeBoundaryPanel } from './EpisodeCatalog';

export function CatalogSourceLinks({ metadata }: { metadata: ProviderContent }) {
  const aliases = metadata.aliases?.filter(alias => ['tmdb', 'tvmaze'].includes(alias.provider) && (alias.provider !== metadata.provider || alias.providerId !== metadata.providerId)) ?? [];
  return aliases.length ? <p className="detail-source">Diğer kimlik kaynakları: {aliases.map(alias => <a key={alias.provider + alias.providerId} href={alias.provider === 'tvmaze' ? `https://www.tvmaze.com/shows/${alias.providerId}` : `https://www.themoviedb.org/${alias.kind === 'show' ? 'tv' : 'movie'}/${alias.providerId}`} target="_blank" rel="noreferrer">{alias.provider === 'tvmaze' ? 'TVmaze · CC BY-SA' : 'TMDB'} #{alias.providerId}{' '}</a>)}</p> : null;
}
export function CatalogIdentityPanel({ content }: { content: ContentEntry }) {
  const [open, setOpen] = useState(false); const refreshCatalog = useRefreshCatalog();
  const identity = useQuery({ queryKey: ['catalog-identity', content.id], queryFn: () => request<CatalogIdentity>(`/content/${content.id}/identity`), enabled: open });
  const refresh = useMutation({ mutationFn: () => mutate(`/content/${content.id}/identity/refresh`), onSuccess: refreshCatalog });
  return <section className="automation-panel"><Button variant="ghost" onPress={() => setOpen(value => !value)} aria-expanded={open}><Link2 size={15} /> Katalog kimlikleri ve alternatif adlar</Button>
    {open && <div className="home-form automation-form"><div className="content-actions"><Button variant="ghost" size="sm" onPress={() => refresh.mutate()} isDisabled={refresh.isPending}><RefreshCw size={14} /> {refresh.isPending ? 'Katalog yenileniyor…' : 'Bölüm ve alternatif adları yenile'}</Button></div>
      {identity.data && <><IdentityForm key={identity.data.content.catalogRevision} identity={identity.data} />{content.metadata.kind === 'show' && <><EpisodeRefreshPanel content={identity.data.content} /><EpisodeAliasPanel identity={identity.data} /></>}</>}{(identity.error || refresh.error) && <p className="form-error">{identity.error?.message || refresh.error?.message}</p>}
    </div>}
  </section>;
}
function IdentityForm({ identity }: { identity: CatalogIdentity }) {
  const refresh = useRefreshCatalog(); const [names, setNames] = useState(identity.localNames.join('\n')); const [provider, setProvider] = useState(identity.content.metadata.provider === 'tmdb' ? 'tvmaze' : 'tmdb');
  const [query, setQuery] = useState(identity.content.metadata.title); const [candidate, setCandidate] = useState<ProviderContent | null>(null); const [maps, setMaps] = useState<Record<string, string>>({}); const [dirty, setDirty] = useState(false); const [visible, setVisible] = useState(40);
  const saveNames = useMutation({ mutationFn: () => mutate(`/content/${identity.content.id}/identity/names`, { names: names.split('\n').map(value => value.trim()).filter(Boolean) }, 'PUT'), onSuccess: refresh });
  const search = useMutation({ mutationFn: () => request<ProviderContent[]>(`/metadata/search?${new URLSearchParams({ provider, kind: identity.content.metadata.kind, q: query })}`) });
  const body = () => ({ provider: candidate!.provider, kind: candidate!.kind, providerId: candidate!.providerId, episodes: Object.entries(maps).flatMap(([sourceEpisodeId, targets]) => targets.split(',').filter(Boolean).map(targetEpisodeId => ({ sourceEpisodeId, targetEpisodeId }))) });
  const preview = useMutation({ mutationFn: () => mutate<CatalogMergePreview>(`/content/${identity.content.id}/identity/preview`, body()), onSuccess: () => setDirty(false) });
  const merge = useMutation({ mutationFn: () => mutate(`/content/${identity.content.id}/identity/merge`, { ...body(), targetRevision: preview.data!.targetRevision, sourceRevision: preview.data!.sourceRevision }), onSuccess: refresh });
  const targets = useInfiniteQuery({ queryKey: ['merge-target-episodes', identity.content.id], enabled: !!preview.data?.episodes.length, initialPageParam: 0,
    queryFn: ({ pageParam }) => request<EpisodePage>(`/content/${identity.content.id}/episodes?offset=${pageParam}&limit=200`), getNextPageParam: page => page.offset + page.items.length < page.total ? page.offset + page.items.length : undefined });
  const unresolved = preview.data?.episodes ?? [];
  return <><div className="catalog-aliases">{identity.aliases.map(alias => <span key={alias.provider + alias.providerId}>{alias.provider.toUpperCase()} #{alias.providerId} · {alias.title}</span>)}</div>
    {!!identity.names.length && <p className="field-hint">{identity.names.join(' · ')}</p>}
    <label className="field"><span>Yerel alternatif adlar · her satıra bir ad</span><textarea rows={3} value={names} onChange={event => setNames(event.target.value)} maxLength={60000} /></label><Button variant="ghost" size="sm" onPress={() => saveNames.mutate()} isDisabled={saveNames.isPending}>Alternatif adları kaydet</Button>
    {identity.content.metadata.kind === 'show' && <><h4>Diğer sağlayıcı kaydını bağla</h4><div className="form-columns"><label className="field"><span>Sağlayıcı</span><select value={provider} onChange={event => { setProvider(event.target.value); setCandidate(null); preview.reset(); }}><option value="tmdb">TMDB</option><option value="tvmaze">TVmaze</option></select></label><label className="field"><span>Yapım adı</span><input value={query} onChange={event => setQuery(event.target.value)} maxLength={120} /></label></div>
      <Button variant="ghost" onPress={() => search.mutate()} isDisabled={search.isPending || query.trim().length < 2}><Search size={14} /> Kayıt ara</Button>
      <div className="catalog-candidates">{search.data?.map(value => <button key={value.provider + value.providerId} className={candidate?.providerId === value.providerId ? 'selected' : ''} onClick={() => { setCandidate(value); setMaps({}); preview.reset(); setVisible(40); }}><strong>{value.title}</strong><small>{value.releaseDate?.slice(0, 4)} · {value.provider.toUpperCase()} #{value.providerId}</small></button>)}</div>
      {candidate && <Button className="secondary-button" onPress={() => preview.mutate()} isDisabled={preview.isPending}><Link2 size={15} /> Birleştirmeyi incele</Button>}
      {preview.data && <><p className="field-hint">{preview.data.files} dosya · {preview.data.requests} istek. Ana kayıt: {preview.data.target.title}.</p>{preview.data.evidence.map(value => <p className="field-hint" key={value}><Check size={12} /> {value}</p>)}
        <p className="field-hint">Bir kaynak bölümü için birden fazla kanonik bölüm seçebilirsin. Bölünmüş dosyanın başlangıç zamanlarını sonra dosya ayrıntısından eşleştir.</p>
        {unresolved.slice(0, visible).map(episode => <label className="catalog-map-row" key={episode.id}><span>S{episode.season} E{episode.number} · {episode.title}{episode.automatic ? ' · Önerilen karşılık' : ''}</span><select multiple size={3} aria-label={`${episode.title} kanonik karşılıkları`} value={(maps[episode.id] ?? episode.targets?.map(map => map.targetEpisodeId).join(',') ?? episode.targetId ?? '').split(',').filter(Boolean)} onChange={event => { const ids = [...event.target.selectedOptions].map(option => option.value).join(','); setMaps(previous => ({ ...previous, [episode.id]: ids })); setDirty(true); }}>{targets.data?.pages.flatMap(page => page.items).map(target => <option key={target.id} value={target.id}>S{target.seasonNumber} E{target.episodeNumber} · {target.title}</option>)}</select></label>)}
        {unresolved.length > visible && <Button variant="ghost" size="sm" onPress={() => setVisible(value => value + 40)}>Diğer eşlemeler</Button>}{targets.hasNextPage && <Button variant="ghost" size="sm" onPress={() => targets.fetchNextPage()} isDisabled={targets.isFetching}>Hedef bölüm seçeneklerini yükle</Button>}
        {preview.data.conflicts.map(value => <p className="form-error" key={value}>{value}</p>)}{dirty && <Button variant="ghost" onPress={() => preview.mutate()} isDisabled={preview.isPending}>Seçilen karşılıkları denetle</Button>}
        <p className="field-hint">Dosyalar, istek geçmişi, favoriler, koleksiyonlar ve kişisel konumlar ana kayda bağlanır. Birleştirme sonrasında otomasyon tercihlerini yeniden etkinleştirebilirsin.</p>
        <Button className="primary-button" onPress={() => merge.mutate()} isDisabled={!preview.data.canApply || dirty || merge.isPending}>Kimlikleri birleştir</Button>
      </>}
    </>}
    {(saveNames.error || search.error || preview.error || merge.error || targets.error) && <p className="form-error">{saveNames.error?.message || search.error?.message || preview.error?.message || merge.error?.message || targets.error?.message}</p>}
  </>;
}
export function EpisodeNumberingPanel({ content }: { content: ContentEntry }) {
  const [open, setOpen] = useState(false); const data = useQuery({ queryKey: ['episode-numbering', content.id], queryFn: () => request<EpisodeNumbering>(`/content/${content.id}/numbering`), enabled: open });
  return <section className="automation-panel"><Button variant="ghost" onPress={() => setOpen(value => !value)} aria-expanded={open}>Bölüm numaralandırması</Button>{open && data.data && <><NumberingForm key={data.data.revision} content={content} numbering={data.data} /><AlternativeOrdersPanel content={content} numbering={data.data} /></>}{data.error && <p className="form-error">{data.error.message}</p>}</section>;
}
function NumberingForm({ content, numbering }: { content: ContentEntry; numbering: EpisodeNumbering }) {
  const refresh = useRefreshCatalog(); const [mode, setMode] = useState(numbering.mode); const [entries, setEntries] = useState(numbering.entries.map(entry => `${entry.number}=${entry.season}:${entry.episode}`).join('\n')); const [group, setGroup] = useState('');
  const groups = useQuery({ queryKey: ['episode-order-groups', content.id], queryFn: () => request<{ id: string; name: string; type: number; episodeCount: number }[]>(`/content/${content.id}/numbering/groups`), enabled: content.metadata.provider === 'tmdb' && mode === 'absolute' });
  const save = useMutation({ mutationFn: () => {
    const values = mode !== 'absolute' ? [] : entries.split('\n').map(line => line.trim()).filter(Boolean).map(line => { const match = /^(\d+)\s*=\s*(\d+)\s*:\s*(\d+)$/.exec(line); if (!match) throw new Error('Her satırı mutlak=sezon:bölüm biçiminde gir.'); return { number: Number(match[1]), season: Number(match[2]), episode: Number(match[3]) }; });
    return mutate(`/content/${content.id}/numbering`, { mode, source: 'manual', entries: values, expectedRevision: numbering.revision }, 'PUT');
  }, onSuccess: refresh });
  const load = useMutation({ mutationFn: () => mutate(`/content/${content.id}/numbering/import`, { orderId: group, expectedRevision: numbering.revision }), onSuccess: refresh });
  return <div className="home-form automation-form"><label className="field"><span>Dosya ve kaynak sürümü numaraları</span><select value={mode} onChange={event => setMode(event.target.value)}><option value="seasonal">Sezon / bölüm</option><option value="daily">Günlük yayın tarihi</option><option value="absolute">Mutlak numara · anime</option>{numbering.mode === 'alternative' && <option value="alternative" disabled>Etkin alternatif sıra · aşağıdan yönet</option>}</select></label><p className="field-hint">Etkin kaynak: {numbering.source}. Bölüm kataloğu ana sağlayıcının sezon/bölüm kimliğini korur.</p>
    {mode === 'daily' && <p className="field-hint">YYYY-AA-GG dosya adı tam yayın tarihiyle eşlenir. Aynı gün birden fazla bölüm varsa açık sezon/bölüm seçimi gerekir.</p>}
    {mode === 'absolute' && <>{content.metadata.provider === 'tmdb' && <><label className="field"><span>TMDB mutlak bölüm grubu</span><select value={group} onChange={event => setGroup(event.target.value)}><option value="">Bir mutlak sıra seç</option>{groups.data?.filter(value => value.type === 2).map(value => <option key={value.id} value={value.id}>{value.name} · {value.episodeCount} bölüm</option>)}</select></label><Button variant="ghost" onPress={() => load.mutate()} isDisabled={!group || load.isPending}>Sağlayıcı sırasını aktar</Button></>}
      <label className="field"><span>Elle eşleme · mutlak=sezon:bölüm</span><textarea rows={7} value={entries} maxLength={600000} onChange={event => setEntries(event.target.value)} placeholder={'1=1:1\n2=1:2'} /></label><p className="field-hint">Önce kimlikler bölümündeki katalog yenilemesini tamamla. Mutlak sıra yayın tarihinden tahmin edilmez.</p></>}
    <Button className="secondary-button" onPress={() => save.mutate()} isDisabled={save.isPending || mode === 'alternative'}>Numaralandırmayı kaydet</Button>{(save.error || load.error || groups.error) && <p className="form-error">{save.error?.message || load.error?.message || groups.error?.message}</p>}
  </div>;
}
export function MoviePartsPanel({ content, files }: { content: ContentEntry; files: MediaItem[] }) {
  const [open, setOpen] = useState(false); const [editing, setEditing] = useState<string | null>(null); const refresh = useRefreshCatalog();
  const editions = useQuery({ queryKey: ['movie-editions', content.id], queryFn: () => request<MovieEdition[]>(`/content/${content.id}/movie-editions`), enabled: open });
  const remove = useMutation({ mutationFn: (edition: MovieEdition) => mutate(`/movie-editions/${edition.id}?revision=${edition.revision}`, undefined, 'DELETE'), onSuccess: refresh });
  const selected = editions.data?.find(edition => edition.id === editing);
  return <section className="automation-panel"><Button variant="ghost" onPress={() => setOpen(value => !value)} aria-expanded={open}><Layers3 size={15} /> Çok parçalı film sürümleri</Button>{open && <div className="home-form automation-form">
    {editions.data?.map(edition => <div className="request-card" key={edition.id}><strong>{edition.name}</strong><p className="field-hint">{edition.partCount} parça · {edition.complete ? 'Oynatmaya hazır' : 'Eksik veya erişilemeyen parça var'}</p><Button size="sm" variant="ghost" onPress={() => setEditing(edition.id)}>Sırayı düzenle</Button><Button size="sm" variant="ghost" onPress={() => remove.mutate(edition)} isDisabled={remove.isPending}>Parça eşleştirmesini kaldır</Button></div>)}
    <Button variant="ghost" size="sm" onPress={() => setEditing('new')}><Plus size={14} /> Parçalı sürüm oluştur</Button>
    {editing && <MoviePartsForm key={(selected?.id ?? 'new') + ':' + (selected?.revision ?? 0)} contentId={content.id} files={files} edition={selected} onDone={() => setEditing(null)} />}
    {(editions.error || remove.error) && <p className="form-error">{editions.error?.message || remove.error?.message}</p>}
  </div>}</section>;
}
function MoviePartsForm({ contentId, files, edition, onDone }: { contentId: string; files: MediaItem[]; edition?: MovieEdition; onDone: () => void }) {
  const refresh = useRefreshCatalog(); const [name, setName] = useState(edition?.name ?? ''); const [ids, setIds] = useState(edition?.files.map(file => file.id) ?? []);
  const save = useMutation({ mutationFn: () => mutate(`/content/${contentId}/movie-editions`, { name, mediaIds: ids, editionId: edition?.id ?? null, expectedRevision: edition?.revision ?? 0 }, 'PUT'), onSuccess: () => { refresh(); onDone(); } });
  const move = (index: number, delta: number) => setIds(previous => { const next = [...previous]; [next[index], next[index + delta]] = [next[index + delta], next[index]]; return next; });
  return <><label className="field"><span>Sürüm adı</span><input value={name} onChange={event => setName(event.target.value)} maxLength={80} placeholder="Sinema sürümü" /></label>
    {files.filter(file => !file.moviePart || file.moviePart.editionId === edition?.id).map(file => <label className="check-field" key={file.id}><input type="checkbox" checked={ids.includes(file.id)} onChange={event => setIds(previous => event.target.checked ? [...previous, file.id] : previous.filter(id => id !== file.id))} />{file.relativePath} · {clock(file.durationSeconds ?? 0)}</label>)}
    {ids.map((id, index) => <div className="catalog-order-row" key={id}><span>{index + 1}. parça · {files.find(file => file.id === id)?.relativePath}</span><Button variant="ghost" isIconOnly size="sm" aria-label="Parçayı yukarı taşı" isDisabled={index === 0} onPress={() => move(index, -1)}><ArrowUp size={14} /></Button><Button variant="ghost" isIconOnly size="sm" aria-label="Parçayı aşağı taşı" isDisabled={index === ids.length - 1} onPress={() => move(index, 1)}><ArrowDown size={14} /></Button></div>)}
    <Button className="secondary-button" onPress={() => save.mutate()} isDisabled={!name.trim() || ids.length < 2 || ids.length > 20 || save.isPending}>Sıralı parçaları kaydet</Button>{save.error && <p className="form-error">{save.error.message}</p>}
  </>;
}
export function EpisodeFileEditor({ item }: { item: MediaItem }) {
  const [open, setOpen] = useState(false);
  return item.content?.kind === 'show' ? <section className="automation-panel"><Button variant="ghost" onPress={() => setOpen(value => !value)} aria-expanded={open}>Dosyanın bölüm kapsamı ve zamanları</Button>{open && <><EpisodeBoundaryPanel item={item} /><EpisodeFileForm key={item.id + ':' + item.matchRevision} item={item} /></>}</section> : null;
}
function EpisodeFileForm({ item }: { item: MediaItem }) {
  const refresh = useRefreshCatalog(); const [timed, setTimed] = useState(item.episodes.length > 1 && item.episodes.every(episode => episode.startSeconds !== null));
  const [rows, setRows] = useState(item.episodes.map(episode => ({ season: episode.season, episode: episode.number, start: episode.startSeconds?.toString() ?? '', end: episode.endSeconds?.toString() ?? '' })));
  const tracks = useQuery({ queryKey: ['media-tracks', item.id], queryFn: () => request<MediaTracks>(`/media/${item.id}/tracks`) });
  const save = useMutation({ mutationFn: () => mutate(`/media/${item.id}/episodes`, { provider: item.content!.provider, providerId: item.content!.providerId, edition: item.content!.edition, expectedRevision: item.matchRevision,
    episodes: rows.map(row => ({ season: row.season, episode: row.episode, startSeconds: timed && row.start !== '' ? Number(row.start) : null, endSeconds: timed && row.end !== '' ? Number(row.end) : null })) }, 'PUT'), onSuccess: refresh });
  const change = (index: number, changes: Partial<typeof rows[number]>) => setRows(previous => previous.map((row, current) => current === index ? { ...row, ...changes } : row));
  return <div className="home-form automation-form"><label className="check-field"><input type="checkbox" checked={timed} onChange={event => setTimed(event.target.checked)} /> Bölüm başlangıç/bitiş zamanlarını eşleştir</label><p className="field-hint">Zamanlar bilinmiyorsa dosya tamamlanınca bütün bölümler izlenmiş sayılır. Zaman eşlemesiyle her bölümün konumu ayrı kaydedilir.</p>
    {rows.map((row, index) => <div className="catalog-segment-row" key={index}><label><span>Sezon</span><input type="number" min={0} max={1000} value={row.season} onChange={event => change(index, { season: Number(event.target.value) })} /></label><label><span>Bölüm</span><input type="number" min={1} max={10000} value={row.episode} onChange={event => change(index, { episode: Number(event.target.value) })} /></label>
      {timed && <><label><span>Başlangıç · sn</span><input type="number" min={0} max={item.durationSeconds ?? undefined} step={.1} value={row.start} onChange={event => change(index, { start: event.target.value })} /></label><label><span>Bitiş · sn</span><input type="number" min={0} max={item.durationSeconds ?? undefined} step={.1} value={row.end} onChange={event => change(index, { end: event.target.value })} placeholder="Dosya sonu" /></label>{!!tracks.data?.chapters.length && <select aria-label={`${index + 1}. bölüm sahne başlangıcı`} value="" onChange={event => { if (event.target.value) change(index, { start: event.target.value }); }}><option value="">Sahneden seç</option>{tracks.data.chapters.map(chapter => <option key={chapter.index} value={chapter.startSeconds}>{clock(chapter.startSeconds)} · {chapter.title}</option>)}</select>}</>}
      <Button variant="ghost" isIconOnly size="sm" aria-label="Bu bölümü kapsamdan çıkar" isDisabled={rows.length === 1} onPress={() => setRows(previous => previous.filter((_, current) => current !== index))}><X size={14} /></Button>
    </div>)}<Button variant="ghost" size="sm" isDisabled={rows.length >= 20} onPress={() => setRows(previous => [...previous, { season: previous.at(-1)?.season ?? 1, episode: (previous.at(-1)?.episode ?? 0) + 1, start: '', end: '' }])}><Plus size={14} /> Bölüm ekle</Button>
    <Button className="secondary-button" onPress={() => save.mutate()} isDisabled={save.isPending}>Bölüm kapsamını kaydet</Button>{(save.error || tracks.error) && <p className="form-error">{save.error?.message || tracks.error?.message}</p>}
  </div>;
}
