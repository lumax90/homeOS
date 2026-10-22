import { useState } from 'react';
import { Button, Input, Label, TextField } from '@heroui/react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Globe2, ShieldCheck } from 'lucide-react';
import { mutate, request } from '../api';

interface Preferences { pathBase: string; publicUrl: string | null; trustedProxies: string[] | null; requireHttps: boolean; bindAddress: string; httpPort: number | null; httpsPort: number | null }
interface NetworkInfo { current: Preferences; pending: Preferences | null; certificateConfigured: boolean; effectiveHttps: boolean; effectiveHost: string; remoteAddress: string | null }
export function NetworkSettings() {
  const info = useQuery({ queryKey: ['network'], queryFn: () => request<NetworkInfo>('/operations/network') });
  const [open, setOpen] = useState(false);
  return <section className="source-form"><Button variant="ghost" onPress={() => setOpen(value => !value)} aria-expanded={open}><Globe2 size={16} /> Ağ ve HTTPS</Button>{open && info.data && <NetworkForm initial={info.data} />}{info.error && <p className="form-error">{info.error.message}</p>}</section>;
}
function NetworkForm({ initial }: { initial: NetworkInfo }) {
  const client = useQueryClient(); const [value, setValue] = useState(initial.pending ?? initial.current); const [proxies, setProxies] = useState((value.trustedProxies ?? []).join(','));
  const [certificate, setCertificate] = useState<File | null>(null); const [password, setPassword] = useState(''); const [message, setMessage] = useState('');
  const save = useMutation({ mutationFn: () => mutate('/operations/network', { ...value, publicUrl: value.publicUrl || null, trustedProxies: proxies.split(',').map(value => value.trim()).filter(Boolean) }, 'PUT'),
    onSuccess: () => { setMessage('Ağ ayarları kaydedildi. Yeniden başlattığında uygulanacak.'); client.invalidateQueries({ queryKey: ['network'] }); } });
  const upload = useMutation({ mutationFn: async () => {
    if (!certificate || certificate.size > 128 * 1024) throw new Error('En fazla 128 KiB PFX/P12 sertifikası seç.');
    const bytes = new Uint8Array(await certificate.arrayBuffer()); let raw = ''; for (const byte of bytes) raw += String.fromCharCode(byte);
    return mutate('/operations/network/certificate', { base64: btoa(raw), password });
  }, onSuccess: () => { setPassword(''); setMessage('TLS sertifikası sunucuda korumalı kaydedildi.'); client.invalidateQueries({ queryKey: ['network'] }); } });
  return <div className="home-form"><p className="field-hint"><ShieldCheck size={13} /> Bu bağlantı: {initial.effectiveHttps ? 'HTTPS' : 'HTTP'} · {initial.effectiveHost}</p>
    <TextField className="field"><Label>Genel adres</Label><Input value={value.publicUrl ?? ''} onChange={event => setValue(value => ({ ...value, publicUrl: event.target.value }))} placeholder="https://home.example.com" /></TextField>
    <div className="form-columns"><TextField className="field"><Label>Alt yol · boş veya /home</Label><Input value={value.pathBase} onChange={event => setValue(value => ({ ...value, pathBase: event.target.value }))} /></TextField><TextField className="field"><Label>Dinlenecek IP</Label><Input value={value.bindAddress} onChange={event => setValue(value => ({ ...value, bindAddress: event.target.value }))} placeholder="127.0.0.1 veya 0.0.0.0" /></TextField></div>
    <div className="form-columns"><TextField className="field"><Label>HTTP portu · boşsa başlangıç ayarı</Label><Input type="number" min={1} max={65535} value={value.httpPort ?? ''} onChange={event => setValue(value => ({ ...value, httpPort: event.target.value ? Number(event.target.value) : null }))} /></TextField><TextField className="field"><Label>HTTPS portu</Label><Input type="number" min={1} max={65535} value={value.httpsPort ?? ''} onChange={event => setValue(value => ({ ...value, httpsPort: event.target.value ? Number(event.target.value) : null }))} /></TextField></div>
    <TextField className="field"><Label>Güvenilen reverse proxy IP'leri</Label><Input value={proxies} onChange={event => setProxies(event.target.value)} placeholder="127.0.0.1" /></TextField><label className="check-field"><input type="checkbox" checked={value.requireHttps} onChange={event => setValue(value => ({ ...value, requireHttps: event.target.checked }))} /> HTTPS zorunlu olsun</label>
    <Button className="secondary-button" onPress={() => save.mutate()} isDisabled={save.isPending}>Ağ ayarlarını kaydet</Button>
    <details className="advanced-fields"><summary>TLS sertifikası · {initial.certificateConfigured ? 'Kayıtlı' : 'Henüz yüklenmedi'}</summary><div className="home-form"><input type="file" accept=".pfx,.p12" aria-label="PFX/P12 sertifikası" onChange={event => setCertificate(event.target.files?.[0] ?? null)} /><TextField className="field"><Label>Sertifika parolası</Label><Input type="password" autoComplete="off" value={password} onChange={event => setPassword(event.target.value)} /></TextField><Button className="secondary-button" onPress={() => upload.mutate()} isDisabled={!certificate || upload.isPending}>Sertifikayı yükle</Button></div></details>
    {message && <p className="field-hint">{message}</p>}{(save.error || upload.error) && <p className="form-error">{save.error?.message || upload.error?.message}</p>}
  </div>;
}
