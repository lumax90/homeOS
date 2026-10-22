import { useEffect, useRef, useState, type FormEvent } from 'react';
import { Button, Input, Label, TextField } from '@heroui/react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ArrowLeft, Check, ExternalLink, Film, KeyRound, LoaderCircle, Search, Sparkles, Tv, X } from 'lucide-react';
import { mutate, request, type FilenameHint, type LocatedEpisodes, type MediaItem, type MetadataStatus, type ProviderContent } from '../api';

export function MetadataConnection({ notify }: { notify: (message: string) => void }) {
  const client = useQueryClient();
  const status = useQuery({ queryKey: ['metadata-settings'], queryFn: () => request<MetadataStatus>('/metadata/settings') });
  const [token, setToken] = useState('');
  const [draftLanguage, setLanguage] = useState<string | null>(null);
  const language = draftLanguage ?? status.data?.language ?? 'tr-TR';
  const save = useMutation({ mutationFn: (clear: boolean) => mutate('/metadata/settings', { tmdbToken: token || null, clearToken: clear, language }, 'PUT'), onSuccess: async () => {
    setToken(''); await client.invalidateQueries({ queryKey: ['metadata-settings'] }); notify('Metadata bağlantısı kaydedildi.');
  } });
  const check = useMutation({ mutationFn: () => mutate('/metadata/test'), onSuccess: () => notify('TMDB bağlantısı doğrulandı.') });
  return <section className="metadata-connection">
    <div className="settings-heading"><h3>İçerik bilgileri</h3><Sparkles size={16} /></div>
    <div className="provider-summary"><div className="provider-monogram">tv</div><div><strong>TVmaze</strong><span>Dizi ve bölüm bilgileri · Bağlantıya hazır</span></div><Check size={15} /></div>
    <div className="provider-summary"><div className="provider-monogram tmdb">tm</div><div><strong>TMDB</strong><span>Film ve dizi bilgileri · {status.data?.tmdbConfigured ? 'Token kayıtlı' : 'Token gerekli'}</span></div>{status.data?.tmdbConfigured ? <Check size={15} /> : <KeyRound size={15} />}</div>
    <form onSubmit={(event: FormEvent) => { event.preventDefault(); save.mutate(false); }} className="home-form metadata-settings-form">
      <TextField className="field"><Label>TMDB okuma token'ı</Label><Input type="password" autoComplete="off" value={token} onChange={e => setToken(e.target.value)} placeholder={status.data?.tmdbConfigured ? 'Kayıtlı · değiştirmek için yeni token gir' : 'API Read Access Token'} /><span className="field-hint">Sunucuda korumalı olarak saklanır. <a href="https://www.themoviedb.org/settings/api" target="_blank" rel="noreferrer">TMDB hesabından al <ExternalLink size={10} /></a></span></TextField>
      <label className="field"><span>Tercih edilen TMDB dili</span><select value={language} onChange={e => setLanguage(e.target.value)}><option value="tr-TR">Türkçe</option><option value="en-US">English</option></select></label>
      {(status.error || save.error || check.error) && <p className="form-error" role="alert">{status.error?.message || save.error?.message || check.error?.message}</p>}
      <div className="metadata-setting-actions"><Button type="submit" size="sm" className="secondary-button" isDisabled={save.isPending}>Kaydet</Button><Button size="sm" variant="ghost" onPress={() => check.mutate()} isDisabled={!status.data?.tmdbConfigured || check.isPending}>{check.isPending ? <LoaderCircle className="spin" size={14} /> : <Check size={14} />} Bağlantıyı dene</Button>{status.data?.tmdbConfigured && <Button isIconOnly size="sm" variant="ghost" aria-label="TMDB bağlantısını kaldır" onPress={() => save.mutate(true)} isDisabled={save.isPending}><X size={14} /></Button>}</div>
    </form>
    <p className="provider-attribution"><a href="https://www.tvmaze.com/api" target="_blank" rel="noreferrer">TVmaze</a> verileri CC BY-SA lisanslıdır. Bu ürün TMDB API'sini kullanır; TMDB tarafından onaylanmış veya sertifikalandırılmış değildir.</p>
  </section>;
}

export function MetadataMatchPanel({ item, onDone, onBack, onSettings }: {
  item: MediaItem; onDone: (item: MediaItem) => void; onBack: () => void; onSettings: () => void;
}) {
  const client = useQueryClient();
  const status = useQuery({ queryKey: ['metadata-settings'], queryFn: () => request<MetadataStatus>('/metadata/settings') });
  const hint = useQuery({ queryKey: ['match-hint', item.id], queryFn: () => request<FilenameHint>(`/media/${item.id}/match-hint`) });
  const [provider, setProvider] = useState('tvmaze');
  const [kind, setKind] = useState('show');
  const [q, setQ] = useState('');
  const [season, setSeason] = useState('');
  const [episode, setEpisode] = useState('');
  const [numbering, setNumbering] = useState('seasonal'); const [airDate, setAirDate] = useState(''); const [absolute, setAbsolute] = useState(''); const [absoluteEnd, setAbsoluteEnd] = useState('');
  const [edition, setEdition] = useState(item.content?.edition ?? '');
  const [selected, setSelected] = useState<ProviderContent | null>(null);
  const initialized = useRef(false);
  useEffect(() => {
    if (hint.data && !initialized.current) {
      initialized.current = true;
      setQ(value => value || item.content?.title || hint.data.query);
      setSeason(value => value || String(item.content?.seasonNumber ?? hint.data.seasonNumber ?? ''));
      setEpisode(value => value || (item.episodes?.length ? item.episodes.map(episode => episode.number).join(',') : hint.data.episodeNumbers?.length ? hint.data.episodeNumbers.join(',') : String(hint.data.episodeNumber ?? '')));
      if (!item.content && hint.data.airDate) { setNumbering('daily'); setAirDate(hint.data.airDate); }
      if (!item.content && hint.data.absoluteNumber !== null) { setNumbering('absolute'); setAbsolute(String(hint.data.absoluteNumber)); setAbsoluteEnd(hint.data.endAbsoluteNumber !== hint.data.absoluteNumber ? String(hint.data.endAbsoluteNumber ?? '') : ''); }
    }
  }, [hint.data, item.id]);
  const search = useMutation({ mutationFn: () => request<ProviderContent[]>(`/metadata/search?${new URLSearchParams({ provider, kind, q })}`) });
  const apply = useMutation({ mutationFn: async () => {
    if (selected!.kind === 'movie') return mutate<MediaItem>(`/media/${item.id}/match`, { provider: selected!.provider, kind: selected!.kind, providerId: selected!.providerId, edition }, 'PUT');
    const numbers = numbering === 'seasonal' || numbering === 'alternative' ? parseEpisodeList(episode) : [];
    const located = await mutate<LocatedEpisodes>('/metadata/episode-selection', { provider: selected!.provider, providerId: selected!.providerId,
      locator: { mode: numbering, season: Number(season), episode: numbers[0] ?? null, numbers: numbers.length ? numbers : null, airDate: airDate || null, absoluteNumber: absolute ? Number(absolute) : null, endEpisode: numbering === 'absolute' && absoluteEnd ? Number(absoluteEnd) : null } });
    return mutate<MediaItem>(`/media/${item.id}/episodes`, { provider: located.provider ?? selected!.provider, providerId: located.providerId ?? selected!.providerId, edition, expectedRevision: item.matchRevision,
      episodes: located.episodes.map(episode => ({ season: episode.seasonNumber, episode: episode.episodeNumber })) }, 'PUT');
  }, onSuccess: async updated => {
    await client.invalidateQueries({ predicate: query => ['catalog', 'versions', 'continue', 'media'].includes(String(query.queryKey[0])) });
    onDone(updated);
  } });
  const needsToken = provider === 'tmdb' && !status.data?.tmdbConfigured;
  return <div className="match-panel">
    <Button className="back-link" variant="ghost" size="sm" onPress={onBack}><ArrowLeft size={14} /> İçeriğe dön</Button>
    <div className="match-heading"><span className="eyebrow">DOĞRU HİKÂYEYİ BUL</span><h3>Bu dosya hangi içerik?</h3><p>{item.sourceTitle || item.title}{hint.data?.year ? ` · ${hint.data.year}` : ''}</p></div>
    <form className="match-search" onSubmit={event => { event.preventDefault(); setSelected(null); apply.reset(); search.mutate(); }}>
      <div className="match-selects"><label className="field"><span>Kaynak</span><select aria-label="Metadata kaynağı" value={provider} onChange={e => { setProvider(e.target.value); if (e.target.value === 'tvmaze') setKind('show'); setSelected(null); search.reset(); }}><option value="tvmaze">TVmaze · Dizi</option><option value="tmdb">TMDB · Film / Dizi</option></select></label><label className="field"><span>İçerik</span><select aria-label="Eşleştirme içerik türü" value={kind} disabled={provider === 'tvmaze'} onChange={e => { setKind(e.target.value); setSelected(null); search.reset(); }}><option value="show">Dizi</option><option value="movie">Film</option></select></label></div>
      <TextField className="field"><Label>İçerik adı</Label><Input value={q} onChange={e => setQ(e.target.value)} placeholder="Film veya dizi adı" /></TextField>
      {needsToken ? <div className="provider-setup-hint"><KeyRound size={17} /><span>TMDB için okuma token'ını bağla.</span><Button size="sm" variant="ghost" onPress={onSettings}>Ayarları aç <ExternalLink size={13} /></Button></div> : <Button type="submit" className="secondary-button" isDisabled={search.isPending || q.trim().length < 2}>{search.isPending ? <LoaderCircle className="spin" size={16} /> : <Search size={16} />} Eşleşme ara</Button>}
    </form>
    {search.error && <p className="form-error" role="alert">{search.error.message}</p>}
    {search.data?.length === 0 && <p className="match-no-results">Bir eşleşme bulunamadı. Adı değiştirerek tekrar deneyebilirsin.</p>}
    <div className="match-results">{search.data?.map(result => <button key={`${result.provider}-${result.kind}-${result.providerId}`} className={`match-result ${selected?.providerId === result.providerId ? 'selected' : ''}`} onClick={() => { setSelected(result); apply.reset(); }} aria-pressed={selected?.providerId === result.providerId}>
      <div className="match-result-art">{result.posterUrl ? <img src={result.posterUrl} alt="" loading="lazy" /> : result.kind === 'movie' ? <Film size={24} /> : <Tv size={24} />}</div><div><strong>{result.title}</strong><span>{result.releaseDate?.slice(0, 4) || 'Tarih bilinmiyor'} · {result.provider === 'tvmaze' ? 'TVmaze' : 'TMDB'}</span><p>{result.overview || result.originalTitle}</p></div>{selected?.providerId === result.providerId && <Check size={17} />}
    </button>)}</div>
    {selected && <div className="match-selection"><div className="selection-title"><Sparkles size={17} /><strong>{selected.title}</strong><a href={selected.providerUrl} target="_blank" rel="noreferrer" aria-label="Kaynak sayfasını aç"><ExternalLink size={14} /></a></div>
      {selected.kind === 'show' && <><label className="field"><span>Dosya numaralandırması</span><select value={numbering} onChange={event => setNumbering(event.target.value)}><option value="seasonal">Sezon / bölüm</option><option value="daily">Yayın tarihi</option><option value="absolute">Mutlak bölüm numarası</option><option value="alternative">Etkin DVD / dijital / özel sıra</option></select></label>
        {(numbering === 'seasonal' || numbering === 'alternative') && <div className="episode-fields"><TextField className="field"><Label>Sezon</Label><Input aria-label="Sezon" type="number" min={0} value={season} onChange={e => setSeason(e.target.value)} placeholder="1" /></TextField><TextField className="field"><Label>Bölümler · 1,2 veya 1-3</Label><Input aria-label="Bölümler" value={episode} onChange={e => setEpisode(e.target.value)} placeholder="1" maxLength={150} /></TextField></div>}
        {numbering === 'daily' && <label className="field"><span>Yayın tarihi</span><input type="date" value={airDate} onChange={event => setAirDate(event.target.value)} /></label>}
        {numbering === 'absolute' && <><div className="form-columns"><label className="field"><span>Mutlak bölüm</span><input type="number" min={1} max={20000} value={absolute} onChange={event => setAbsolute(event.target.value)} /></label><label className="field"><span>Son bölüm · isteğe bağlı</span><input type="number" min={1} max={20000} value={absoluteEnd} onChange={event => setAbsoluteEnd(event.target.value)} /></label></div><p className="field-hint">Dizinin katalog ayrıntısında mutlak bölüm listesini önce kaydet.</p></>}
      </>}
      <TextField className="field"><Label>Sürüm etiketi (isteğe bağlı)</Label><Input value={edition} onChange={e => setEdition(e.target.value)} placeholder="Örneğin, Sinema sürümü" maxLength={80} /></TextField>
      {apply.error && <p className="form-error" role="alert">{apply.error.message}</p>}
      <Button className="primary-button" onPress={() => apply.mutate()} isDisabled={apply.isPending || selected.kind === 'show' && (numbering === 'seasonal' || numbering === 'alternative' ? season === '' || episode === '' || Number(season) < 0 : numbering === 'daily' ? !airDate : !absolute)}>{apply.isPending ? <LoaderCircle className="spin" size={16} /> : <Check size={16} />} Bilgileri kullan</Button>
    </div>}
    <p className="detail-source">Seçilen kimlik dosyaya bağlanır; sonraki taramalar eşleştirmeyi korur.</p>
  </div>;
}
function parseEpisodeList(value: string): number[] {
  const result: number[] = [];
  for (const part of value.split(',')) {
    const match = /^\s*(\d{1,5})(?:\s*-\s*(\d{1,5}))?\s*$/.exec(part);
    if (!match) throw new Error('Bölümleri 1,2 veya 1-3 biçiminde gir.');
    const start = Number(match[1]), end = Number(match[2] ?? match[1]);
    if (start < 1 || end > 10000 || end < start || end - start + result.length >= 20) throw new Error('En fazla 20 farklı bölüm seç.');
    for (let episode = start; episode <= end; episode++) result.push(episode);
  }
  if (new Set(result).size !== result.length || result.some((number, index) => index > 0 && number <= result[index - 1])) throw new Error('Bölüm numaraları tekrarsız ve artan sırada olmalı.');
  return result;
}
