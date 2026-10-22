import { useState } from 'react';
import { Button, Input, Label, Modal, TextField } from '@heroui/react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Check, Copy, KeyRound, Plus, Settings2, ShieldCheck, Users, X } from 'lucide-react';
import { appUrl, broadcastAuthChange, date, loadSession, mutate, request, type Account, type Library, type LoginSession } from '../api';
import { Dialog } from './Dialogs';
import { useSession } from './SessionContext';
import { DeviceProfiles, PersonalPin } from './Profiles';
import { PlaybackUsagePanel, UserPlaybackLimitsEditor } from './PlaybackUsage';
import { BrowserPushPanel } from './WebPush';
import { OfflinePolicyEditor } from './Offline';
import { DownloadBudgetPanel } from './DownloadBudgets';

export function PersonalAccount({ notify, section = 'all', expanded = false }: { notify: (message: string) => void; section?: 'all' | 'security' | 'playback' | 'downloads' | 'notifications'; expanded?: boolean }) {
  const client = useQueryClient();
  const [open, setOpen] = useState(expanded);
  const security = section === 'all' || section === 'security';
  const [current, setCurrent] = useState(''); const [next, setNext] = useState('');
  const sessions = useQuery({ queryKey: ['login-sessions'], queryFn: () => request<LoginSession[]>('/account/sessions'), enabled: security && open });
  const change = useMutation({ mutationFn: () => mutate('/account/password', { currentPassword: current, newPassword: next }, 'PUT'),
    onSuccess: async () => { setCurrent(''); setNext(''); client.setQueryData(['session'], await loadSession()); await client.invalidateQueries({ queryKey: ['login-sessions'] }); broadcastAuthChange(); notify('Parolan değişti; diğer oturumlar kapatıldı.'); } });
  const revoke = useMutation({ mutationFn: (id: string) => mutate(`/account/sessions/${id}`, undefined, 'DELETE'), onSuccess: async () => {
    client.setQueryData(['session'], await loadSession()); await client.invalidateQueries({ queryKey: ['login-sessions'] }); broadcastAuthChange();
  } });
  return <section className="account-security">{security && <>{expanded ? <h3>Parola ve oturumlar</h3> : <Button variant="ghost" onPress={() => setOpen(value => !value)} aria-expanded={open}><KeyRound size={16} /> Parola ve oturumlar</Button>}
    {open && <><form className="home-form" onSubmit={event => { event.preventDefault(); change.mutate(); }}>
      <TextField className="field" isRequired><Label>Mevcut parola</Label><Input type="password" autoComplete="current-password" value={current} onChange={event => setCurrent(event.target.value)} /></TextField>
      <TextField className="field" isRequired><Label>Yeni parola</Label><Input type="password" autoComplete="new-password" minLength={10} value={next} onChange={event => setNext(event.target.value)} /></TextField>
      {change.error && <p className="form-error">{change.error.message}</p>}<Button className="secondary-button" type="submit" isDisabled={change.isPending}>Parolayı değiştir</Button>
    </form><div className="session-list">{sessions.data?.map(session => <div className="session-row" key={session.id}><div><strong>{session.current ? 'Bu oturum' : 'Diğer oturum'}</strong><span title={session.device}>{session.device || 'Cihaz bilgisi yok'}</span><small>{date(session.createdAt)}</small></div><Button size="sm" variant="ghost" onPress={() => revoke.mutate(session.id)} isDisabled={revoke.isPending}>{session.current ? 'Çıkış yap' : 'Kapat'}</Button></div>)}</div>{(sessions.error || revoke.error) && <p className="form-error">{sessions.error?.message || revoke.error?.message}</p>}</>}
    <PersonalPin /></>}
    {(section === 'all' || section === 'playback') && <PlaybackUsagePanel />}
    {(section === 'all' || section === 'downloads') && <DownloadBudgetPanel />}
    {(section === 'all' || section === 'notifications') && <BrowserPushPanel />}
  </section>;
}

export function HouseholdDialog({ open, onClose, notify }: { open: boolean; onClose: () => void; notify: (message: string) => void }) {
  return <Dialog open={open} onClose={onClose} title="Hane ve hesaplar" className="detail-container"><div className="dialog-content"><p className="eyebrow">PAYLAŞILAN BİR EV</p><Modal.Heading className="dialog-title">Hane ve hesaplar.</Modal.Heading>{open && <HouseholdBody notify={notify} />}</div></Dialog>;
}
export function HouseholdBody({ notify }: { notify: (message: string) => void }) {
  const users = useQuery({ queryKey: ['users'], queryFn: () => request<Account[]>('/users') });
  const libraries = useQuery({ queryKey: ['libraries'], queryFn: () => request<Library[]>('/libraries') });
  const [editing, setEditing] = useState<Account | 'new' | null>(null);
  return <><p className="muted source-intro">Herkesin izleme geçmişi kendine ait. Kütüphaneleri ve yönetim yetkilerini buradan paylaş.</p>
    {users.error && <p className="form-error">{users.error.message}</p>}
    {users.data?.map(user => <div className="household-row" key={user.id}><div className="source-symbol">{user.isAdmin ? <ShieldCheck size={19} /> : <Users size={19} />}</div><div><strong>{user.displayName}</strong><span>{({ admin: 'Yönetici', member: 'Üye', viewer: 'İzleyici' })[user.role]} · {user.enabled ? 'Etkin' : 'Devre dışı'} · {user.allLibraries || user.isAdmin ? 'Tüm kütüphaneler' : `${user.libraryIds.length} kütüphane`}</span></div><Button isIconOnly variant="ghost" aria-label={`${user.displayName} hesabını düzenle`} onPress={() => setEditing(user)}><Settings2 size={16} /></Button></div>)}
    {editing ? <AccountForm key={editing === 'new' ? 'new' : editing.id} initial={editing === 'new' ? null : editing} libraries={libraries.data ?? []} onDone={() => setEditing(null)} notify={notify} />
      : <Button className="secondary-button" onPress={() => setEditing('new')}><Plus size={16} /> Hesap veya davet ekle</Button>}
    {editing && editing !== 'new' && <UserPlaybackLimitsEditor key={editing.id} userId={editing.id} />}
    {editing && editing !== 'new' && <OfflinePolicyEditor key={editing.id + '-offline'} userId={editing.id} />}
    {editing && editing !== 'new' && <DownloadBudgetPanel key={editing.id + '-downloads'} userId={editing.id} editable />}
    <PlaybackUsagePanel all />
    <DeviceProfiles accounts={users.data ?? []} />
  </>;
}
function AccountForm({ initial, libraries, onDone, notify }: { initial: Account | null; libraries: Library[]; onDone: () => void; notify: (message: string) => void }) {
  const session = useSession(); const client = useQueryClient();
  const [name, setName] = useState(initial?.displayName ?? '');
  const [role, setRole] = useState(initial?.role ?? 'member');
  const [enabled, setEnabled] = useState(initial?.enabled ?? true);
  const [all, setAll] = useState(initial?.allLibraries ?? true);
  const [ids, setIds] = useState(initial?.libraryIds ?? []);
  const [discover, setDiscover] = useState(initial?.canDiscover ?? true);
  const [acquire, setAcquire] = useState(initial?.canAcquire ?? false);
  const [approve, setApprove] = useState(initial?.autoApprove ?? false);
  const [quota, setQuota] = useState(String(initial?.monthlyRequestLimit ?? 20));
  const [password, setPassword] = useState(''); const [invite, setInvite] = useState('');
  const [age, setAge] = useState(initial?.maximumAge === null || initial?.maximumAge === undefined ? '' : String(initial.maximumAge));
  const [unrated, setUnrated] = useState(initial?.allowUnrated ?? true); const [pin, setPin] = useState('');
  const body = () => ({ displayName: name, role, enabled, allLibraries: all, libraryIds: ids, canDiscover: discover,
    canAcquire: role === 'viewer' || age !== '' && role !== 'admin' ? false : acquire, autoApprove: role === 'viewer' ? false : approve, monthlyRequestLimit: Number(quota), maximumAge: age === '' ? null : Number(age), allowUnrated: unrated });
  const save = useMutation({ mutationFn: () => initial ? mutate<Account>(`/users/${initial.id}`, body(), 'PUT') : mutate<Account>('/users', { account: body(), password }),
    onSuccess: async () => { await client.invalidateQueries({ queryKey: ['users'] }); if (initial?.id === session.userId) { client.setQueryData(['session'], await loadSession()); broadcastAuthChange(); } notify('Hane hesabı kaydedildi.'); onDone(); } });
  const invitation = useMutation({ mutationFn: () => mutate<{ token: string }>('/users/invitations', body()), onSuccess: value => setInvite(`${location.origin}${appUrl('/')}#invite=${value.token}`) });
  const reset = useMutation({ mutationFn: () => mutate(`/users/${initial!.id}/password`, { newPassword: password }), onSuccess: async () => {
    setPassword(''); if (initial?.id === session.userId) { client.setQueryData(['session'], await loadSession()); broadcastAuthChange(); } notify('Parola değiştirildi, eski oturumlar kapatıldı.');
  } });
  const pinReset = useMutation({ mutationFn: () => mutate(`/users/${initial!.id}/pin`, { pin: pin || null }, 'PUT'), onSuccess: async () => {
    setPin(''); await client.invalidateQueries({ queryKey: ['users'] }); await client.invalidateQueries({ queryKey: ['profiles'] });
    if (initial?.id === session.userId) { client.setQueryData(['session'], await loadSession()); broadcastAuthChange(); } notify('Profil PIN’i güncellendi.');
  } });
  return <form className="home-form source-form" onSubmit={event => { event.preventDefault(); save.mutate(); }}>
    <div className="settings-heading"><h3>{initial ? 'Hesabı düzenle' : 'Yeni hane hesabı'}</h3><Button isIconOnly variant="ghost" aria-label="Hesap formunu kapat" onPress={onDone}><X size={16} /></Button></div>
    <TextField className="field" isRequired><Label>Hesap adı</Label><Input value={name} onChange={event => setName(event.target.value)} maxLength={60} autoComplete="off" /></TextField>
    <label className="field"><span>Rol</span><select value={role} disabled={initial?.id === '1'} onChange={event => setRole(event.target.value as Account['role'])}><option value="admin">Yönetici</option><option value="member">Üye · izle ve iste</option><option value="viewer">İzleyici</option></select></label>
    {initial?.id !== '1' && <label className="check-field"><input type="checkbox" checked={enabled} onChange={event => setEnabled(event.target.checked)} /> Hesap etkin</label>}
    {role !== 'admin' && <><label className="check-field"><input type="checkbox" checked={all} onChange={event => setAll(event.target.checked)} /> Tüm kütüphanelere erişim</label>
      {!all && <div className="library-grants">{libraries.map(library => <label className="check-field" key={library.id}><input type="checkbox" checked={ids.includes(library.id)} onChange={event => setIds(current => event.target.checked ? [...current, library.id] : current.filter(id => id !== library.id))} />{library.name}</label>)}{!libraries.length && <p className="field-hint">Henüz bir kütüphane bağlı değil.</p>}</div>}
      <label className="check-field"><input type="checkbox" checked={discover} onChange={event => setDiscover(event.target.checked)} /> Gerçek katalogda keşif</label>
      <div className="form-columns"><label className="field"><span>Yaş filtresi</span><select value={age} onChange={event => { setAge(event.target.value); if (event.target.value !== '') { setAcquire(false); setUnrated(false); } }}><option value="">Sınır yok</option>{[0, 6, 7, 10, 12, 13, 15, 16, 17, 18].map(value => <option value={value} key={value}>{value} yaşa kadar sınıflandırılmış</option>)}</select></label><label className="check-field"><input type="checkbox" checked={unrated} onChange={event => setUnrated(event.target.checked)} /> Sınıflandırması bilinmeyen içeriklere izin ver</label></div>
      {role === 'member' && <><label className="check-field"><input type="checkbox" checked={approve} onChange={event => setApprove(event.target.checked)} /> İstekleri otomatik onayla</label><label className="check-field"><input type="checkbox" checked={acquire} disabled={age !== ''} onChange={event => setAcquire(event.target.checked)} /> Kaynak arama ve indirme yönetimi</label><TextField className="field"><Label>Aylık istek kotası · 0 sınırsız</Label><Input type="number" min={0} max={10000} value={quota} onChange={event => setQuota(event.target.value)} /></TextField></>}
    </>}
    <TextField className="field" isRequired={!initial}><Label>{initial ? 'Parolayı sıfırlamak için yeni parola' : 'Başlangıç parolası'}</Label><Input type="password" value={password} onChange={event => setPassword(event.target.value)} autoComplete="new-password" minLength={10} /></TextField>
    {(save.error || invitation.error || reset.error) && <p className="form-error">{save.error?.message || invitation.error?.message || reset.error?.message}</p>}
    <div className="content-actions"><Button type="submit" className="primary-button" isDisabled={save.isPending || !name.trim() || (!initial && password.length < 10)}><Check size={15} /> Kaydet</Button>
      {initial ? <Button variant="ghost" isDisabled={reset.isPending || password.length < 10} onPress={() => reset.mutate()}>Parolayı sıfırla</Button> : <Button variant="ghost" onPress={() => invitation.mutate()} isDisabled={invitation.isPending}>Davet bağlantısı oluştur</Button>}
    </div>
    {initial && <details className="advanced-fields"><summary>Profil PIN'i · {initial.pinEnabled ? 'Etkin' : 'Yok'}</summary><div className="home-form"><Input type="password" inputMode="numeric" aria-label="Yeni profil PIN'i" value={pin} onChange={event => setPin(event.target.value)} placeholder="6–12 rakam; boş bırakmak kaldırır" maxLength={12} /><Button variant="ghost" onPress={() => pinReset.mutate()} isDisabled={pinReset.isPending || pin.length > 0 && pin.length < 6}>PIN'i güncelle</Button>{pinReset.error && <p className="form-error">{pinReset.error.message}</p>}</div></details>}
    {invite && <div className="invite-link"><p>Bu bağlantı 48 saat ve tek kullanım için geçerli.</p><input aria-label="Davet bağlantısı" readOnly value={invite} onFocus={event => event.currentTarget.select()} /><Button variant="ghost" size="sm" onPress={() => navigator.clipboard.writeText(invite).then(() => notify('Davet bağlantısı kopyalandı.')).catch(() => notify('Bağlantıyı seçip kopyalayabilirsin.'))}><Copy size={14} /> Kopyala</Button></div>}
  </form>;
}
