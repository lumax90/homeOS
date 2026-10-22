import { useState } from 'react';
import { Button, Input, Label, Modal, TextField } from '@heroui/react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { KeyRound, Users } from 'lucide-react';
import { broadcastAuthChange, loadSession, mutate, request, type Account } from '../api';
import { Dialog } from './Dialogs';

interface Profiles { enabled: boolean; profiles: { id: string; displayName: string; role: string }[] }
export function ProfileSwitcher() {
  const data = useQuery({ queryKey: ['profiles'], queryFn: () => request<Profiles>('/profiles') });
  const [open, setOpen] = useState(false); const [selected, setSelected] = useState(''); const [pin, setPin] = useState('');
  const change = useMutation({ mutationFn: () => mutate('/profiles/switch', { userId: selected, pin }), onSuccess: () => { broadcastAuthChange(); location.reload(); } });
  if (!data.data?.enabled || !data.data.profiles.length) return null;
  return <><Button isIconOnly variant="ghost" aria-label="Hane profilini değiştir" onPress={() => setOpen(true)}><Users size={19} /></Button>
    <Dialog open={open} onClose={() => { setOpen(false); setPin(''); change.reset(); }} title="Profilini seç"><div className="dialog-content"><p className="eyebrow">EVDE KİM VAR?</p><Modal.Heading className="dialog-title">Profilini seç.</Modal.Heading>
      <div className="profile-choices">{data.data.profiles.map(profile => <button key={profile.id} className={selected === profile.id ? 'selected' : ''} onClick={() => { setSelected(profile.id); setPin(''); change.reset(); }}><span>{profile.displayName.slice(0, 1)}</span><strong>{profile.displayName}</strong></button>)}</div>
      {selected && <form className="home-form" onSubmit={event => { event.preventDefault(); change.mutate(); }}><TextField className="field" isRequired><Label>Profil PIN'i</Label><Input type="password" inputMode="numeric" autoComplete="off" pattern="[0-9]{6,12}" minLength={6} maxLength={12} value={pin} onChange={event => setPin(event.target.value)} /></TextField>{change.error && <p className="form-error">{change.error.message}</p>}<Button className="primary-button" type="submit" isDisabled={change.isPending || pin.length < 6}>Profile geç</Button></form>}
    </div></Dialog></>;
}
export function DeviceProfiles({ accounts }: { accounts: Account[] }) {
  const client = useQueryClient(); const [selected, setSelected] = useState<string[]>([]);
  const current = useQuery({ queryKey: ['profiles'], queryFn: () => request<Profiles>('/profiles') });
  const trust = useMutation({ mutationFn: () => mutate('/profiles/device', { userIds: selected }), onSuccess: () => client.invalidateQueries({ queryKey: ['profiles'] }) });
  const forget = useMutation({ mutationFn: () => mutate('/profiles/device', undefined, 'DELETE'), onSuccess: () => { broadcastAuthChange(); location.reload(); } });
  return <details className="advanced-fields source-form"><summary>Bu cihazda PIN ile profil geçişi</summary><div className="home-form"><p className="field-hint">PIN'i olan hesaplardan bu cihazda kullanılacakları seç. Yetki 30 gün geçerlidir; hesap/PIN değişikliği eski izni geçersiz kılar.</p>
    {accounts.filter(account => account.enabled).map(account => <label className="check-field" key={account.id}><input type="checkbox" disabled={!account.pinEnabled} checked={selected.includes(account.id)} onChange={event => setSelected(values => event.target.checked ? [...values, account.id] : values.filter(id => id !== account.id))} />{account.displayName}{!account.pinEnabled && ' · Önce PIN ayarla'}</label>)}
    <Button className="secondary-button" onPress={() => trust.mutate()} isDisabled={!selected.length || trust.isPending}>Seçilen profilleri bu cihazda etkinleştir</Button>
    {current.data?.enabled && <><p className="field-hint">Etkin: {current.data.profiles.map(profile => profile.displayName).join(', ') || 'Hesap revizyonları nedeniyle yeniden seçilmeli'}</p><Button variant="ghost" onPress={() => forget.mutate()} isDisabled={forget.isPending}>Bu cihazın profil yetkisini kaldır</Button></>}
    {(trust.error || forget.error) && <p className="form-error">{trust.error?.message || forget.error?.message}</p>}
  </div></details>;
}
export function PersonalPin() {
  const client = useQueryClient(); const [password, setPassword] = useState(''); const [pin, setPin] = useState('');
  const save = useMutation({ mutationFn: (clear: boolean) => mutate('/account/pin', { currentPassword: password, pin: clear ? null : pin }, 'PUT'),
    onSuccess: async () => { setPassword(''); setPin(''); client.setQueryData(['session'], await loadSession()); await client.invalidateQueries({ queryKey: ['profiles'] }); broadcastAuthChange(); } });
  return <details className="advanced-fields"><summary><KeyRound size={14} /> Profil PIN'i</summary><div className="home-form"><TextField className="field"><Label>Hesap parolan</Label><Input type="password" autoComplete="current-password" value={password} onChange={event => setPassword(event.target.value)} /></TextField><TextField className="field"><Label>Yeni PIN · 6–12 rakam</Label><Input type="password" inputMode="numeric" autoComplete="off" value={pin} onChange={event => setPin(event.target.value)} minLength={6} maxLength={12} /></TextField><div className="content-actions"><Button variant="ghost" onPress={() => save.mutate(false)} isDisabled={!password || pin.length < 6 || save.isPending}>PIN'i kaydet</Button><Button variant="ghost" onPress={() => save.mutate(true)} isDisabled={!password || save.isPending}>PIN'i kaldır</Button></div>{save.error && <p className="form-error">{save.error.message}</p>}</div></details>;
}
