import { useState } from 'react';
import { Button } from '@heroui/react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Check, ChevronRight, Clock3, X } from 'lucide-react';
import { date, mutate, request, type ContentRequest, type ProviderContent, type RequestEvent } from '../api';
export {NotificationBell} from './NotificationsPopover';
import { useSession } from './SessionContext';

const labels: Record<string, string> = { pending: 'Onay bekliyor', approved: 'Takipte', rejected: 'Reddedildi', cancelled: 'İptal edildi', fulfilled: 'Hazır' };
export function RequestInbox({ onOpen }: { onOpen: (content: ProviderContent) => void }) {
  const session = useSession(); const client = useQueryClient(); const [status, setStatus] = useState(''); const [expanded, setExpanded] = useState('');
  const [notes, setNotes] = useState<Record<string, string>>({});
  const rows = useQuery({ queryKey: ['requests', status], queryFn: () => request<ContentRequest[]>(`/requests?status=${status}`), refetchInterval: 15_000 });
  const history = useQuery({ queryKey: ['request-history', expanded], queryFn: () => request<RequestEvent[]>(`/requests/${expanded}/history`), enabled: !!expanded });
  const decide = useMutation({ mutationFn: ({ id, state }: { id: string; state: string }) => mutate(`/requests/${id}/decision`, { status: state, note: notes[id] ?? '' }),
    onSuccess: async (_, value) => { setNotes(previous => ({ ...previous, [value.id]: '' })); await Promise.all(['requests', 'content', 'notifications', 'request-history'].map(key => client.invalidateQueries({ queryKey: [key] }))); } });
  return <section className="request-inbox"><div className="section-heading"><h3>{session.role === 'admin' ? 'Hanenin istekleri' : 'İsteklerin'}</h3><select aria-label="İstek durumu" value={status} onChange={event => setStatus(event.target.value)}><option value="">Tüm durumlar</option>{Object.entries(labels).map(([key, label]) => <option key={key} value={key}>{label}</option>)}</select></div>
    {(rows.error || decide.error) && <p className="form-error">{rows.error?.message || decide.error?.message}</p>}
    {!rows.isPending && !rows.data?.length && <div className="search-empty"><Clock3 size={30} /><h2>Bu listede henüz istek yok.</h2></div>}
    {rows.data?.map(row => <article className="request-card" key={row.id}><div className="request-summary"><div><button className="tracking-title" onClick={() => onOpen(row.metadata)}>{row.metadata.title}</button><p>{row.requesterName} · {row.scopeEpisodeCount !== null && row.scopeEpisodeCount !== undefined ? `${row.scopeEpisodeCount} bölüm · Kimlik korumalı kapsam` : row.season === null ? row.metadata.kind === 'movie' ? 'Film' : 'Tüm dizi' : `${row.season}. sezon${row.episode === null ? '' : ` · ${row.episode}. bölüm`}`} · {date(row.createdAt)}</p></div><span className={`episode-state ${row.status === 'fulfilled' ? 'available' : ''}`}>{labels[row.status]}</span></div>
      {row.note && <p className="request-note">{row.note}</p>}
      <div className="request-actions">{session.role === 'admin' && row.status === 'pending' && <><Button size="sm" className="secondary-button" isDisabled={decide.isPending} onPress={() => decide.mutate({ id: row.id, state: 'approved' })}><Check size={14} /> Onayla</Button><Button size="sm" variant="ghost" isDisabled={decide.isPending} onPress={() => decide.mutate({ id: row.id, state: 'rejected' })}><X size={14} /> Reddet</Button></>}
        {['pending', 'approved', 'fulfilled'].includes(row.status) && <Button size="sm" variant="ghost" isDisabled={decide.isPending} onPress={() => decide.mutate({ id: row.id, state: 'cancelled' })}>İsteği kaldır</Button>}
        <Button size="sm" variant="ghost" onPress={() => setExpanded(value => value === row.id ? '' : row.id)}>Geçmiş ve not <ChevronRight size={14} /></Button>
      </div>{expanded === row.id && <div className="request-history">{session.role === 'admin' && row.status === 'pending' && <label className="field"><span>Karar notu</span><textarea value={notes[row.id] ?? ''} onChange={event => setNotes(previous => ({ ...previous, [row.id]: event.target.value }))} maxLength={1000} /></label>}{history.data?.map(event => <p key={event.id}><strong>{labels[event.status] || event.status}</strong> · {event.actorName} · {date(event.createdAt)}<span>{event.note}</span></p>)}</div>}
    </article>)}
  </section>;
}
