import { useState } from 'react';
import { Button, Input, Label, TextField } from '@heroui/react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { FolderHeart, Heart, Plus, Settings2, Star, Trash2 } from 'lucide-react';
import { mutate, request, type ContentCollection, type ContentEntry, type ContentPresentation, type PersonalContent, type ProviderContent } from '../api';
import { useSession } from './SessionContext';
import { CatalogFeeds } from './CatalogFeeds';

export function PersonalContentPanel({ metadata, contentId, onSaved, placement = 'body' }: { metadata: ProviderContent; contentId?: string; onSaved?: (value: ContentEntry) => void; placement?: 'body' | 'hero' }) {
  const session = useSession(); const client = useQueryClient(); const [localId, setLocalId] = useState(''); const [collection, setCollection] = useState(''); const [edit, setEdit] = useState(false);
  const id = contentId || localId;
  const personal = useQuery({ queryKey: ['personal-content', id], queryFn: () => request<PersonalContent>(`/content/${id}/personal`), enabled: !!id });
  const collections = useQuery({ queryKey: ['collections'], queryFn: () => request<ContentCollection[]>('/collections') });
  const ensure = async () => {
    if (id) return id;
    const saved = await mutate<ContentEntry>('/content/save', { provider: metadata.provider, kind: metadata.kind, providerId: metadata.providerId });
    setLocalId(saved.id); onSaved?.(saved); return saved.id;
  };
  const save = useMutation({ mutationFn: async (changes: Partial<PersonalContent>) => {
    const key = await ensure(); await mutate(`/content/${key}/personal`, { favorite: false, rating: null, ...personal.data, ...changes }, 'PUT');
  }, onSuccess: () => { client.invalidateQueries({ queryKey: ['personal-content'] }); client.invalidateQueries({ queryKey: ['favorites'] }); client.invalidateQueries({ queryKey: ['discovery-personal'] }); } });
  const add = useMutation({ mutationFn: async () => mutate(`/collections/${collection}/items`, { contentId: await ensure() }), onSuccess: () => { client.invalidateQueries({ queryKey: ['collections'] }); client.invalidateQueries({ queryKey: ['discovery-personal'] }); } });
  return <section className={`personal-content ${placement === 'hero' ? 'personal-content--hero' : ''}`} aria-label="Favori, puan ve koleksiyon">
    <div className="personal-actions"><Button variant="ghost" className="personal-favorite" aria-pressed={personal.data?.favorite ?? false} onPress={() => save.mutate({ favorite: !personal.data?.favorite })} isDisabled={save.isPending || !!id && personal.isPending}><Heart size={16} fill={personal.data?.favorite ? 'currentColor' : 'none'} /> Favorim</Button>
      <label className="personal-rating"><Star size={15} /><select aria-label="Kişisel puanın" value={personal.data?.rating ?? ''} onChange={event => save.mutate({ rating: event.target.value ? Number(event.target.value) : null })} disabled={save.isPending || !!id && personal.isPending}><option value="">Puan ver</option>{Array.from({ length: 10 }, (_, index) => index + 1).map(value => <option key={value} value={value}>{value} / 10</option>)}</select></label>
      <div className="personal-collection"><FolderHeart size={15} /><select className="collection-select" aria-label="Eklenecek koleksiyon" value={collection} onChange={event => setCollection(event.target.value)}><option value="">Koleksiyon seç</option>{collections.data?.filter(value => value.owned).map(value => <option key={value.id} value={value.id}>{value.name}</option>)}</select><Button variant="ghost" size="sm" aria-label="Koleksiyona ekle" onPress={() => add.mutate()} isDisabled={!collection || add.isPending}><Plus size={14} /> Ekle</Button></div>
      {session.role === 'admin' && id && <span title="Yerel bilgiler ve erişim"><Button variant="ghost" size="sm" className="personal-edit" aria-label="Yerel bilgiler ve erişim" onPress={() => setEdit(value => !value)}><Settings2 size={15} />{placement === 'body' && 'Yerel bilgiler ve erişim'}</Button></span>}
    </div>
    {placement === 'body' && metadata.ageLabel && <p className="field-hint">Sınıflandırma: {metadata.ageLabel}{metadata.minimumAge != null ? ` · ${metadata.minimumAge}+` : ' · yaş eşiği bilinmiyor'}</p>}
    {(personal.error || save.error || add.error) && <p className="form-error">{personal.error?.message || save.error?.message || add.error?.message}</p>}
    {edit && id && <PresentationEditor contentId={id} />}
  </section>;
}
function PresentationEditor({ contentId }: { contentId: string }) {
  const values = useQuery({ queryKey: ['presentation', contentId], queryFn: () => request<ContentPresentation>(`/content/${contentId}/presentation`) });
  return values.data ? <PresentationForm initial={values.data} contentId={contentId} /> : values.error ? <p className="form-error">{values.error.message}</p> : <p className="field-hint">Yerel bilgiler yükleniyor…</p>;
}
function PresentationForm({ initial, contentId }: { initial: ContentPresentation; contentId: string }) {
  const client = useQueryClient(); const [title, setTitle] = useState(initial.title ?? ''); const [overview, setOverview] = useState(initial.overview ?? '');
  const [age, setAge] = useState(initial.minimumAge == null ? '' : String(initial.minimumAge)); const [blocked, setBlocked] = useState(initial.blocked); const [tags, setTags] = useState(initial.tags.join(','));
  const save = useMutation({ mutationFn: () => mutate(`/content/${contentId}/presentation`, { title: title || null, overview: overview || null, minimumAge: age === '' ? null : Number(age), blocked, tags: tags.split(',').map(value => value.trim()).filter(Boolean) }, 'PUT'),
    onSuccess: () => Promise.all(['presentation', 'content', 'content-details', 'catalog', 'media', 'collections', 'collection-items', 'favorites'].map(key => client.invalidateQueries({ queryKey: [key] }))) });
  return <div className="home-form source-form"><p className="field-hint">Doldurduğun yerel alanlar sağlayıcı yenilemelerinde korunur. Boş alan sağlayıcı değerini kullanır.</p><TextField className="field"><Label>Yerel başlık</Label><Input value={title} onChange={event => setTitle(event.target.value)} maxLength={500} /></TextField><label className="field"><span>Yerel açıklama</span><textarea value={overview} onChange={event => setOverview(event.target.value)} maxLength={20000} /></label><div className="form-columns"><TextField className="field"><Label>Yerel minimum yaş · boşsa sağlayıcı</Label><Input type="number" min={0} max={21} value={age} onChange={event => setAge(event.target.value)} /></TextField><TextField className="field"><Label>Etiketler</Label><Input value={tags} onChange={event => setTags(event.target.value)} placeholder="aile,hafta sonu" /></TextField></div><label className="check-field"><input type="checkbox" checked={blocked} onChange={event => setBlocked(event.target.checked)} /> Yönetici dışındaki profillere kapat</label><Button className="secondary-button" onPress={() => save.mutate()} isDisabled={save.isPending}>Yerel tercihleri kaydet</Button>{save.error && <p className="form-error">{save.error.message}</p>}</div>;
}
export function MediaAccessEditor({ mediaId }: { mediaId: string }) {
  const client = useQueryClient(); const [open, setOpen] = useState(false); const [age, setAge] = useState(''); const [blocked, setBlocked] = useState(false); const [loaded, setLoaded] = useState(false);
  const [loadError, setLoadError] = useState('');
  const load = async () => { setOpen(value => !value); if (loaded) return; try { const data = await request<{ minimumAge: number | null; blocked: boolean }>(`/media/${mediaId}/access`); setAge(data.minimumAge == null ? '' : String(data.minimumAge)); setBlocked(data.blocked); setLoaded(true); setLoadError(''); } catch (error) { setLoadError((error as Error).message); } };
  const save = useMutation({ mutationFn: () => mutate(`/media/${mediaId}/access`, { minimumAge: age === '' ? null : Number(age), blocked }, 'PUT'), onSuccess: () => Promise.all(['catalog', 'media', 'stats'].map(key => client.invalidateQueries({ queryKey: [key] }))) });
  return <section className="personal-content"><Button variant="ghost" size="sm" onPress={load}><Settings2 size={14} /> Bu dosyanın erişimi</Button>{loadError && <p className="form-error">{loadError}</p>}{open && loaded && <div className="home-form"><TextField className="field"><Label>Dosya minimum yaşı · boşsa içerik</Label><Input type="number" min={0} max={21} value={age} onChange={event => setAge(event.target.value)} /></TextField><label className="check-field"><input type="checkbox" checked={blocked} onChange={event => setBlocked(event.target.checked)} /> Yönetici dışındaki profillere kapat</label><Button variant="ghost" onPress={() => save.mutate()} isDisabled={save.isPending}>Kaydet</Button>{save.error && <p className="form-error">{save.error.message}</p>}</div>}</section>;
}

export function CollectionsView({ onOpen }: { onOpen: (metadata: ProviderContent) => void }) {
  const session = useSession(); const client = useQueryClient(); const [selected, setSelected] = useState(''); const [name, setName] = useState(''); const [shared, setShared] = useState(false);
  const collections = useQuery({ queryKey: ['collections'], queryFn: () => request<ContentCollection[]>('/collections') });
  const favorites = useQuery({ queryKey: ['favorites'], queryFn: () => request<ContentEntry[]>('/favorites'), enabled: !selected });
  const items = useQuery({ queryKey: ['collection-items', selected], queryFn: () => request<ContentEntry[]>(`/collections/${selected}/items`), enabled: !!selected });
  const create = useMutation({ mutationFn: () => mutate<ContentCollection>('/collections', { name, shared }), onSuccess: value => { setName(''); setSelected(value.id); client.invalidateQueries({ queryKey: ['collections'] }); } });
  const remove = useMutation({ mutationFn: () => mutate(`/collections/${selected}`, undefined, 'DELETE'), onSuccess: () => { setSelected(''); client.invalidateQueries({ queryKey: ['collections'] }); } });
  const unlist = useMutation({ mutationFn: (id: string) => mutate(`/collections/${selected}/items/${id}`, undefined, 'DELETE'), onSuccess: () => { client.invalidateQueries({ queryKey: ['collection-items'] }); client.invalidateQueries({ queryKey: ['collections'] }); } });
  const active = collections.data?.find(value => value.id === selected); const rows = selected ? items.data : favorites.data;
  return <section><div className="collection-toolbar"><select aria-label="Koleksiyonunu seç" value={selected} onChange={event => setSelected(event.target.value)}><option value="">Favorilerim</option>{collections.data?.map(value => <option value={value.id} key={value.id}>{value.name} · {value.count}{value.shared ? ' · Paylaşılan' : ''}</option>)}</select>{active?.owned && <Button variant="ghost" onPress={() => remove.mutate()} isDisabled={remove.isPending}><Trash2 size={15} /> Koleksiyonu kaldır</Button>}</div>
    <div className="collection-create"><TextField className="field"><Label>Yeni koleksiyon</Label><Input value={name} onChange={event => setName(event.target.value)} maxLength={100} placeholder="Birlikte izlenecekler" /></TextField>{session.role === 'admin' && <label className="check-field"><input type="checkbox" checked={shared} onChange={event => setShared(event.target.checked)} /> Haneyle paylaş</label>}<Button className="secondary-button" onPress={() => create.mutate()} isDisabled={!name.trim() || create.isPending}><Plus size={15} /> Oluştur</Button></div>
    {active?.owned&&session.canDiscover&&<CatalogFeeds collectionId={active.id} onOpen={onOpen}/>}
    {!rows?.length && <div className="search-empty"><FolderHeart size={32} /><h2>Sevdiklerin burada biriksin.</h2><p>İçerik ayrıntılarından favorilerine veya koleksiyonlarına ekleyebilirsin.</p></div>}
    <div className="tracking-grid">{rows?.map(entry => <article className="tracking-card" key={entry.id}><button className="tracking-poster" onClick={() => onOpen(entry.metadata)}>{entry.metadata.posterUrl ? <img src={entry.metadata.posterUrl} alt="" loading="lazy" /> : <FolderHeart size={24} />}</button><div><button className="tracking-title" onClick={() => onOpen(entry.metadata)}>{entry.metadata.title}</button><p>{entry.metadata.releaseDate?.slice(0, 4)} · {entry.metadata.kind === 'movie' ? 'Film' : 'Dizi'}</p>{active?.owned && <Button variant="ghost" size="sm" onPress={() => unlist.mutate(entry.id)} isDisabled={unlist.isPending}>Koleksiyondan çıkar</Button>}</div></article>)}</div>
    {(collections.error || items.error || favorites.error || create.error || remove.error || unlist.error) && <p className="form-error">{collections.error?.message || items.error?.message || favorites.error?.message || create.error?.message || remove.error?.message || unlist.error?.message}</p>}
  </section>;
}
