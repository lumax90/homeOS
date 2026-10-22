import { useState } from 'react';
import { Button, Input, Label, Modal, TextField } from '@heroui/react';
import { useInfiniteQuery, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ArrowRight, Check, CircleAlert, Download, Link2, LoaderCircle, Plus, Radio, Search, Settings2, SlidersHorizontal, X } from 'lucide-react';
import { bytes, mutate, request, type Download as DownloadRecord, type EpisodePage, type IndexerInfo, type QualityProfile, type ReleaseSearchResult, type ReleaseTarget } from '../api';
import { Dialog } from './Dialogs';
import { useSession } from './SessionContext';
import { UsenetSettings } from './Usenet';
import { QualityFormats } from './QualityFormats';
import { SourceExtensions } from './SourceExtensions';
import { SourceCatalogPicker, SourceConnectionEditor } from './SourceCatalog';

export function SourcesDialog({ open, onClose, notify }: { open: boolean; onClose: () => void; notify: (message: string) => void }) {
  return <Dialog open={open} onClose={onClose} title="Kaynaklar ve kalite" className="detail-container"><div className="dialog-content"><p className="eyebrow">TEK YERDEN YÖNET</p><Modal.Heading className="dialog-title">Kaynaklar ve tercihler.</Modal.Heading>{open && <SourceSettings notify={notify} />}</div></Dialog>;
}

export function SourceSettings({ notify }: { notify: (message: string) => void }) {
  const client = useQueryClient();
  const sources = useQuery({ queryKey: ['indexers'], queryFn: () => request<IndexerInfo[]>('/indexers') });
  const profiles = useQuery({ queryKey: ['quality-profiles'], queryFn: () => request<QualityProfile[]>('/quality-profiles') });
  const [tab, setTab] = useState('sources'); const [editing, setEditing] = useState<IndexerInfo | 'new' | null>(null);
  const [profile, setProfile] = useState<QualityProfile | 'new' | null>(null);
  const [manual, setManual] = useState(false);
  const test = useMutation({ mutationFn: (id: string) => mutate<IndexerInfo>(`/indexers/${id}/test`), onSuccess: async () => { await client.invalidateQueries({ queryKey: ['indexers'] }); notify('Kaynak yetenekleri doğrulandı.'); }, onError: () => client.invalidateQueries({ queryKey: ['indexers'] }) });
  return <><div className="filter-pills product-subnav"><button className={tab === 'sources' ? 'filter-pill selected' : 'filter-pill'} onClick={() => setTab('sources')}><Radio size={14} /> Kaynaklar</button><button className={tab === 'quality' ? 'filter-pill selected' : 'filter-pill'} onClick={() => setTab('quality')}><SlidersHorizontal size={14} /> Kalite profilleri</button><button className={tab === 'usenet' ? 'filter-pill selected' : 'filter-pill'} onClick={() => setTab('usenet')}>Usenet hesabı</button><button className={tab === 'advanced' ? 'filter-pill selected' : 'filter-pill'} onClick={() => setTab('advanced')}><Settings2 size={14}/> Gelişmiş</button></div>
    {tab === 'usenet' && <UsenetSettings notify={notify} />}
    {tab==='advanced'&&<><p className="source-onboarding-note">Hazır listede olmayan Torznab/Newznab API bağlantıları, özel adaptörler ve ağ tercihleri.</p>{manual?<IndexerForm initial={null} onDone={()=>setManual(false)} notify={notify}/>:<Button variant="ghost" onPress={()=>setManual(true)}><Plus size={16}/> Özel API bağlantısı ekle</Button>}<SourceExtensions sources={sources.data??[]}/></>}
    {(tab === 'sources' || tab === 'quality') && <>
    {tab === 'sources' ? <>
      <p className="muted source-intro">TMDB film ve dizi bilgilerini getirir. Buradaki kaynaklar, isteklerin için indirilebilecek sürümleri bulur.</p>
      {sources.error && <p className="form-error">{sources.error.message}</p>}
      <div className="source-list">{sources.data?.map(source => <article className="source-row" key={source.id}>
        <div className="source-symbol"><Radio size={19} /></div><div><strong>{source.name}</strong><span>{source.protocol==='torznab'?'Torrent':'Usenet'} · {source.enabled ? 'Etkin' : 'Devre dışı'} · Öncelik {source.priority}</span><small>{source.lastError || (source.capabilities ? `${Object.keys(source.capabilities.searches).length} arama türü · ${Object.keys(source.capabilities.categories).length} kategori` : 'Bağlantı testi bekleniyor')}</small></div>
        <Button isIconOnly size="sm" variant="ghost" aria-label={`${source.name} test et`} onPress={() => test.mutate(source.id)} isDisabled={test.isPending}><Check size={15} /></Button><Button isIconOnly size="sm" variant="ghost" aria-label={`${source.name} düzenle`} onPress={() => setEditing(source)}><Settings2 size={15} /></Button>
      </article>)}</div>
      {!sources.isPending&&!sources.data?.length&&<p className="source-onboarding-note">Henüz kaynak bağlı değil. Bir site seçerek başlayabilirsin.</p>}
      {test.error && <p className="form-error">{test.error.message}</p>}
      {editing==='new'?<SourceCatalogPicker sources={sources.data??[]} onDone={()=>setEditing(null)} notify={notify} onUsenet={()=>setTab('usenet')}/>:editing?<SourceConnectionEditor id={editing.id} onDone={()=>setEditing(null)} notify={notify} onUsenet={()=>setTab('usenet')}/>:<Button className="primary-button" onPress={() => setEditing('new')}><Plus size={16} /> Site seçerek kaynak ekle</Button>}
    </> : <>{profiles.data?.map(value => <button className="profile-row" key={value.id} onClick={() => setProfile(value)}><SlidersHorizontal size={18} /><div><strong>{value.name}</strong><span>{value.rules.resolutions.map(r => `${r}p`).join(' → ')} · en fazla {value.rules.maxSizeGiB} GiB · {value.rules.minSeeders}+ seeder</span></div><Settings2 size={15} /></button>)}{profile ? <ProfileForm key={profile === 'new' ? 'new' : profile.id} initial={profile === 'new' ? null : profile} onDone={() => setProfile(null)} notify={notify} /> : <Button className="secondary-button" onPress={() => setProfile('new')}><Plus size={16} /> Kalite profili ekle</Button>}{profiles.error&&<p className="form-error">{profiles.error.message}</p>}</>}
    </>}
  </>;
}

function IndexerForm({ initial, onDone, notify }: { initial: IndexerInfo | null; onDone: () => void; notify: (message: string) => void }) {
  const client = useQueryClient();
  const [name, setName] = useState(initial?.name ?? ''); const [endpoint, setEndpoint] = useState(initial?.endpoint ?? '');
  const [protocol, setProtocol] = useState(initial?.protocol ?? 'torznab'); const [key, setKey] = useState('');
  const [priority, setPriority] = useState(String(initial?.priority ?? 25)); const [categories, setCategories] = useState(initial?.categories ?? '');
  const [hosts, setHosts] = useState(initial?.allowedHosts ?? ''); const [enabled, setEnabled] = useState(initial?.enabled ?? true);
  const save = useMutation({ mutationFn: async () => {
    const result = await mutate<IndexerInfo>(initial ? `/indexers/${initial.id}` : '/indexers', { name, endpoint, protocol, apiKey: key || null, priority: Number(priority), categories, allowedHosts: hosts, enabled }, initial ? 'PUT' : 'POST');
    await client.invalidateQueries({ queryKey: ['indexers'] });
    return result;
  }, onSuccess: () => { notify('Kaynak kaydedildi. Bağlantı testiyle yeteneklerini doğrulayabilirsin.'); onDone(); } });
  return <form className="home-form source-form" onSubmit={e => { e.preventDefault(); save.mutate(); }}><div className="settings-heading"><h3>{initial ? 'Kaynağı düzenle' : 'Yeni kaynak'}</h3><Button isIconOnly size="sm" variant="ghost" aria-label="Kaynak formunu kapat" onPress={onDone}><X size={15} /></Button></div><div className="form-columns"><TextField className="field" isRequired><Label>Kaynak adı</Label><Input value={name} onChange={e => setName(e.target.value)} placeholder="Kullandığın indexer" /></TextField><label className="field"><span>Protokol</span><select value={protocol} onChange={e => setProtocol(e.target.value)}><option value="torznab">Torznab · Torrent</option><option value="newznab">Newznab · Usenet arama</option></select></label></div>
    <TextField className="field" isRequired><Label>API adresi</Label><Input value={endpoint} onChange={e => setEndpoint(e.target.value)} placeholder="https://kaynak.example/api" spellCheck={false} /><span className="field-hint">Kaynağın kendi API adresi. Anahtarı URL'ye ekleme.</span></TextField><TextField className="field"><Label>API anahtarı</Label><Input type="password" value={key} onChange={e => setKey(e.target.value)} placeholder={initial?.configured ? 'Kayıtlı · değiştirmek için gir' : 'Gerekiyorsa kaynağın anahtarı'} autoComplete="off" /></TextField>
    <details className="advanced-fields"><summary>Gelişmiş tercihler</summary><div className="home-form"><div className="form-columns"><TextField className="field"><Label>Öncelik (1–50)</Label><Input type="number" value={priority} onChange={e => setPriority(e.target.value)} min={1} max={50} /></TextField><TextField className="field"><Label>Kategoriler</Label><Input value={categories} onChange={e => setCategories(e.target.value)} placeholder="2000,5000" /></TextField></div><TextField className="field"><Label>Ek indirme alan adları</Label><Input value={hosts} onChange={e => setHosts(e.target.value)} placeholder="cdn.kaynak.example" /><span className="field-hint">Kaynağın torrent linkleri farklı bir HTTPS alanına yönleniyorsa ekle.</span></TextField><label className="check-field"><input type="checkbox" checked={enabled} onChange={e => setEnabled(e.target.checked)} /> Aramalarda etkin</label></div></details>
    {save.error && <p className="form-error" role="alert">{save.error.message}</p>}<Button className="primary-button form-submit" type="submit" isDisabled={save.isPending}>{save.isPending ? 'Kaydediliyor…' : 'Kaynağı kaydet'}<ArrowRight size={16} /></Button>
  </form>;
}

function ProfileForm({ initial, onDone, notify }: { initial: QualityProfile | null; onDone: () => void; notify: (message: string) => void }) {
  const client = useQueryClient(); const [name, setName] = useState(initial?.name ?? '');
  const [resolutions, setResolutions] = useState(initial?.rules.resolutions.join(',') ?? '1080,720');
  const [size, setSize] = useState(String(initial?.rules.maxSizeGiB ?? 25)); const [seeders, setSeeders] = useState(String(initial?.rules.minSeeders ?? 1));
  const [minimum, setMinimum] = useState(String(initial?.rules.minSizeGiB ?? 0));
  const [languages, setLanguages] = useState(initial?.rules.languages?.join(',') ?? '');
  const [sources, setSources] = useState(initial?.rules.sources?.join(',') ?? '');
  const [required, setRequired] = useState(initial?.rules.requiredTerms.join(',') ?? ''); const [forbidden, setForbidden] = useState(initial?.rules.forbiddenTerms.join(',') ?? ''); const [preferred, setPreferred] = useState(initial?.rules.preferredTerms.join(',') ?? 'web-dl,bluray');
  const [unknown, setUnknown] = useState(initial?.rules.allowUnknownResolution ?? false);
  const [formats, setFormats] = useState(initial?.rules.customFormats ?? []);
  const [minimumScore, setMinimumScore] = useState(String(initial?.rules.minimumCustomScore ?? 0));
  const [protocols, setProtocols] = useState(initial?.rules.protocols?.join(',') ?? '');
  const [preferredProtocol, setPreferredProtocol] = useState(initial?.rules.preferredProtocol ?? 'any');
  const [maximumAge, setMaximumAge] = useState(String(initial?.rules.maximumAgeDays ?? 0));
  const terms = (value: string) => value.split(',').map(v => v.trim()).filter(Boolean);
  const save = useMutation({ mutationFn: () => mutate(initial ? `/quality-profiles/${initial.id}` : '/quality-profiles', {
    name, rules: { resolutions: terms(resolutions).map(Number), maxSizeGiB: Number(size), minSizeGiB: Number(minimum), minSeeders: Number(seeders),
      requiredTerms: terms(required), forbiddenTerms: terms(forbidden), preferredTerms: terms(preferred), allowUnknownResolution: unknown,
      languages: terms(languages), sources: terms(sources), customFormats: formats, minimumCustomScore: Number(minimumScore),
      protocols: terms(protocols), preferredProtocol, maximumAgeDays: Number(maximumAge) },
  }, initial ? 'PUT' : 'POST'), onSuccess: async () => { await client.invalidateQueries({ queryKey: ['quality-profiles'] }); notify('Kalite profili kaydedildi.'); onDone(); } });
  return <form className="home-form source-form" onSubmit={e => { e.preventDefault(); save.mutate(); }}>
    <div className="settings-heading"><h3>Kalite profili</h3><Button isIconOnly size="sm" variant="ghost" aria-label="Profil formunu kapat" onPress={onDone}><X size={15} /></Button></div>
    <TextField className="field" isRequired><Label>Profil adı</Label><Input value={name} onChange={e => setName(e.target.value)} /></TextField>
    <TextField className="field"><Label>Çözünürlük tercih sırası</Label><Input value={resolutions} onChange={e => setResolutions(e.target.value)} placeholder="1080,720" /><span className="field-hint">2160, 1080, 720, 576 veya 480. İlk değer önceliklidir.</span></TextField>
    <div className="form-columns">
      <TextField className="field"><Label>En az boyut (GiB)</Label><Input type="number" value={minimum} onChange={e => setMinimum(e.target.value)} min={0} step={0.1} /></TextField>
      <TextField className="field"><Label>En fazla boyut (GiB)</Label><Input type="number" value={size} onChange={e => setSize(e.target.value)} min={0.1} max={1000} step={0.1} /></TextField>
    </div>
    <TextField className="field"><Label>Minimum seeder</Label><Input type="number" value={seeders} onChange={e => setSeeders(e.target.value)} min={0} /></TextField>
    <TextField className="field"><Label>İstenen ses dilleri</Label><Input value={languages} onChange={e => setLanguages(e.target.value)} placeholder="tr,en" /><span className="field-hint">Boş bırakıldığında dil filtresi uygulanmaz. Seçili dillerden en az biri doğrulanmalı.</span></TextField>
    <TextField className="field"><Label>İzin verilen yayın kaynakları</Label><Input value={sources} onChange={e => setSources(e.target.value)} placeholder="web-dl,bluray,remux" /><span className="field-hint">web-dl, webrip, bluray, remux, hdtv, unknown. Boşsa kaynak filtresi uygulanmaz.</span></TextField>
    {([['Gerekli ifadeler', required, setRequired], ['İstenmeyen ifadeler', forbidden, setForbidden], ['Tercih edilen ifadeler', preferred, setPreferred]] as const).map(([label, value, set]) => <TextField className="field" key={label}><Label>{label}</Label><Input value={value} onChange={e => set(e.target.value)} placeholder="Virgülle ayır" /></TextField>)}
    <label className="check-field"><input type="checkbox" checked={unknown} onChange={e => setUnknown(e.target.checked)} /> Çözünürlüğü bilinmeyen sonuçlara izin ver</label>
    <div className="form-columns"><TextField className="field"><Label>İzinli protokoller · boşsa hepsi</Label><Input value={protocols} onChange={event => setProtocols(event.target.value)} placeholder="torrent,usenet" /></TextField><label className="field"><span>Tercih edilen protokol</span><select value={preferredProtocol} onChange={event => setPreferredProtocol(event.target.value)}><option value="any">Fark etmez</option><option value="torrent">Torrent</option><option value="usenet">Usenet</option></select></label></div>
    <div className="form-columns"><TextField className="field"><Label>En eski sürüm yaşı · gün · 0 sınırsız</Label><Input type="number" min={0} max={100000} value={maximumAge} onChange={event => setMaximumAge(event.target.value)} /></TextField><TextField className="field"><Label>Minimum custom-format puanı</Label><Input type="number" min={-20000} max={20000} value={minimumScore} onChange={event => setMinimumScore(event.target.value)} /></TextField></div>
    <QualityFormats formats={formats} onChange={setFormats} />
    {save.error && <p className="form-error">{save.error.message}</p>}<Button type="submit" className="primary-button" isDisabled={save.isPending}>Profili kaydet</Button>
  </form>;
}

export function ReleaseDialog({ target, onClose, onSources, onDownload, notify }: { target: ReleaseTarget | null; onClose: () => void; onSources: () => void; onDownload: () => void; notify: (message: string) => void }) {
  return <Dialog open={!!target} onClose={onClose} title="Kaynaklarda ara" className="release-container">{target && <ReleaseBody key={`${target.entry.id}-${target.season}-${target.episode}`} target={target} onSources={onSources} onDownload={onDownload} notify={notify} />}</Dialog>;
}

function ReleaseBody({ target, onSources, onDownload, notify }: { target: ReleaseTarget; onSources: () => void; onDownload: () => void; notify: (message: string) => void }) {
  const account = useSession();
  const profiles = useQuery({ queryKey: ['quality-profiles'], queryFn: () => request<QualityProfile[]>('/quality-profiles') });
  const [profile, setProfile] = useState(target.entry.qualityProfileId);
  const [scope, setScope] = useState({ season: target.season, episode: target.episode });
  const [generation, setGeneration] = useState(0);
  const [override, setOverride] = useState(false);
  const episodes = useQuery({ queryKey: ['release-seasons', target.entry.id], enabled: target.entry.metadata.kind === 'show',
    queryFn: () => request<EpisodePage>(`/content/${target.entry.id}/episodes?limit=1`) });
  const search = useInfiniteQuery({
    queryKey: ['release-search', target.entry.id, scope.season, scope.episode, profile, generation], initialPageParam: 0,
    queryFn: ({ pageParam, signal }) => request<ReleaseSearchResult>('/releases/search', { method: 'POST', signal,
      body: JSON.stringify({ contentId: target.entry.id, season: scope.season ?? null, episode: scope.episode ?? null, profileId: profile, page: pageParam }) }),
    getNextPageParam: (page, _pages, lastPage) => lastPage < 20 && page.sources.some(source => source.moreAvailable) ? lastPage + 1 : undefined,
    retry: false, refetchOnWindowFocus: false, staleTime: 0, gcTime: 60_000,
  });
  const items = [...new Map((search.data?.pages.flatMap(page => page.items) ?? []).map(item => [item.fingerprint || item.id, item])).values()];
  const sourceResults = search.data?.pages.at(-1)?.sources ?? [];
  const grab = useMutation({ mutationFn: (id: string) => mutate<DownloadRecord>(`/releases/${id}/download`, { overrideRejections: override }),
    onSuccess: record => { notify(record.desiredState === 'running' ? 'Seçtiğin sürüm indirme kuyruğunda.' : 'Bu sürüm zaten indirme geçmişinde. İşlemlerden devam edebilirsin.'); onDownload(); } });
  return <div className="dialog-content"><p className="eyebrow">KAYNAK SONUÇLARI</p><Modal.Heading className="dialog-title">{target.entry.metadata.title}{scope.season !== undefined ? ` · S${scope.season}` : ''}{scope.episode !== undefined ? ` E${scope.episode}` : ''}</Modal.Heading>
    <div className="release-controls">
      <select aria-label="Arama kalite profili" value={profile} onChange={e => { setProfile(e.target.value); setOverride(false); }}>{profiles.data?.map(p => <option key={p.id} value={p.id}>{p.name}</option>)}</select>
      {target.entry.metadata.kind === 'show' && <select aria-label="Arama sezonu" value={scope.season ?? ''} onChange={e => { setScope({ season: e.target.value === '' ? undefined : Number(e.target.value), episode: undefined }); setOverride(false); }}><option value="">Tüm dizi</option>{episodes.data?.seasons.map(season => <option key={season} value={season}>{season}. sezon</option>)}</select>}
      <Button className="secondary-button" size="sm" onPress={() => setGeneration(value => value + 1)} isDisabled={search.isFetching}>{search.isFetching ? <LoaderCircle className="spin" size={15} /> : <Search size={15} />} Ara</Button>
      {account.role === 'admin' && <Button variant="ghost" size="sm" onPress={onSources}><Settings2 size={15} /> Kaynaklar</Button>}
    </div>
    <p className="field-hint">Arama dosya indirmez. İndirme, aşağıdaki seçiminle başlar.</p>{search.error && <p className="form-error" role="alert">{search.error.message}</p>}
    <div className="search-source-status">{sourceResults.map(source => <span className={source.error ? 'source-failed' : ''} key={source.indexerId} title={source.error || ''}>{source.error ? <CircleAlert size={12} /> : <Check size={12} />}{source.name}: {source.error || `${source.results} sonuç`}</span>)}</div>
    {!search.isPending && !search.error && items.length === 0 && <div className="search-empty"><Search size={29} /><h3>Bir sürüm bulunamadı.</h3><p>Bağlı kaynakları ve arama kapsamını kontrol et.</p></div>}
    <div className="release-results">{items.map(result => <article className={`release-row ${result.evaluation.eligible ? 'eligible' : 'rejected'}`} key={result.id}><div className="release-row-body"><strong>{result.title}</strong><div className="release-row-meta"><span>{result.indexerName}</span><span>{result.protocol}</span><span>{result.sizeBytes == null ? 'Boyut bilinmiyor' : bytes(result.sizeBytes)}</span>{result.protocol === 'torrent' && <span>{result.seeders == null ? '? seeder' : `${result.seeders} seeder`}</span>}<span>Puan {result.evaluation.score}</span></div><div className="release-reasons">{result.evaluation.rejections.map(reason => <span key={reason}>{reason}</span>)}{result.evaluation.eligible && <span className="accepted-reason">Profile uygun · {result.evaluation.notes.join(' ')}</span>}</div></div><Button size="sm" className="secondary-button" isDisabled={grab.isPending || search.isFetching || (!result.evaluation.eligible && !override)} onPress={() => grab.mutate(result.id)}><Download size={14} /> İndir</Button></article>)}</div>
    {search.hasNextPage && <div className="load-more"><Button variant="ghost" onPress={() => search.fetchNextPage()} isDisabled={search.isFetching}>Daha fazla sonuç</Button></div>}{items.some(result => !result.evaluation.eligible) && <label className="check-field override-field"><input type="checkbox" checked={override} onChange={e => setOverride(e.target.checked)} /> Reddedilen sürümleri manuel seçmeme izin ver</label>}{items.some(result => result.protocol === 'usenet') && <p className="field-hint">NZB aktarımı için Kaynaklar → Usenet / NNTP bölümünden sunucunu bağla.</p>}{grab.error && <p className="form-error" role="alert">{grab.error.message}</p>}
  </div>;
}
