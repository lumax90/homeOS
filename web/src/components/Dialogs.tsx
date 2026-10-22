import { useEffect, useRef, useState, type FormEvent, type ReactNode } from 'react';
import { Button, Input, Label, Modal, TextField } from '@heroui/react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { ArrowRight, FolderPlus, HardDrive, X } from 'lucide-react';
import { mutate, type Library, type SystemInfo } from '../api';
import { FolderBrowser } from './LibraryManager';
import { useControllerCommands } from '../controller/react';
export { SettingsDialog } from './SettingsDialog';

export function Dialog({ open, onClose, title, children, className = '', backdropClassName = '' }: {
  open: boolean; onClose: () => void; title: string; children: ReactNode; className?: string; backdropClassName?: string;
}) {
  const panel = useRef<HTMLDivElement>(null);
  useControllerCommands(panel, action => { if (action === 'back') { onClose(); return true; } return false; });
  return (
    <Modal.Backdrop isOpen={open} onOpenChange={value => { if (!value) onClose(); }} className={`home-backdrop ${backdropClassName}`} UNSTABLE_portalContainer={typeof document==='undefined'?undefined:document.fullscreenElement??undefined}>
      <Modal.Container className={`home-modal-container ${className}`}><div ref={panel} className="home-dialog-frame">
        <Modal.Dialog className="home-dialog" aria-label={title}>
          <Modal.CloseTrigger className="dialog-close" aria-label="Kapat"><X size={18} /></Modal.CloseTrigger>
          {children}
        </Modal.Dialog></div>
      </Modal.Container>
    </Modal.Backdrop>
  );
}

export function AddLibraryDialog({ open, onClose, system, onAdded, initialKind='movies' }: {
  open: boolean; onClose: () => void; system?: SystemInfo; onAdded: () => void; initialKind?:Library['kind'];
}) {
  const [name, setName] = useState('');
  const [path, setPath] = useState('');
  const [kind, setKind] = useState<Library['kind']>('movies');
  useEffect(()=>{if(open)setKind(initialKind);},[open,initialKind]);
  const [browse, setBrowse] = useState(false);
  const queryClient = useQueryClient();
  const mutation = useMutation({
    mutationFn: () => mutate('/libraries', { name, path, kind }),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ predicate: q => ['libraries', 'catalog', 'stats', 'jobs'].includes(String(q.queryKey[0])) });
      setName(''); setPath(''); mutation.reset(); onClose(); onAdded();
    },
  });
  function submit(event: FormEvent) { event.preventDefault(); mutation.mutate(); }
  return (
    <Dialog open={open} onClose={onClose} title="Kütüphane ekle">
      <div className="dialog-content">
        <div className="dialog-symbol"><FolderPlus size={27} strokeWidth={1.5} /></div>
        <p className="eyebrow">YENİ BİR YER</p>
        <Modal.Heading className="dialog-title">Kütüphanene yer aç.</Modal.Heading>
        <p className="muted dialog-description">Sunucundaki bir medya klasörünü bağla. Home dosyalarını okuyup kütüphanende bir araya getirir.</p>
        <form onSubmit={submit} className="home-form">
          <TextField isRequired name="libraryName" className="field">
            <Label>Kütüphane adı</Label>
            <Input autoFocus value={name} onChange={event => setName(event.target.value)} placeholder="Örneğin, Film geceleri" maxLength={80} />
          </TextField>
          <TextField isRequired name="libraryPath" className="field">
            <Label>Sunucudaki klasör yolu</Label>
            <Input value={path} onChange={event => setPath(event.target.value)} placeholder={system?.os.includes('Windows') ? 'C:\\Medya\\Filmler' : '/media/filmler'} spellCheck={false} />
            <span className="field-hint">Bu yol, Home sunucusunun çalıştığı cihazdaki bir klasör olmalı.</span>
          </TextField>
          <label className="field">
            <Button variant="ghost" size="sm" onPress={() => setBrowse(true)}><FolderPlus size={15} /> Sunucu klasörlerinden seç</Button>
            <span>İçerik türü</span>
            <select value={kind} onChange={event => setKind(event.target.value as Library['kind'])}>
              <option value="movies">Filmler ve videolar</option><option value="shows">Diziler</option><option value="music">Müzik</option><option value="photos">Fotoğraflar ve albümler</option><option value="books">Kitaplar ve çizgi roman</option><option value="audiobooks">Sesli kitaplar</option>
            </select>
          </label>
          {mutation.error && <p className="form-error" role="alert">{mutation.error.message}</p>}
          <Button type="submit" className="primary-button form-submit" isDisabled={mutation.isPending || !name.trim() || !path.trim()}>
            {mutation.isPending ? 'Kütüphane ekleniyor…' : 'Kütüphaneyi ekle'}<ArrowRight size={17} />
          </Button>
          <p className="form-note"><HardDrive size={13} /> Dosyalar kendi depolama alanında kalır.</p>
        </form>
        <FolderBrowser open={browse} initialPath={path} onClose={() => setBrowse(false)} onSelect={value => { setPath(value); setBrowse(false); }} />
      </div>
    </Dialog>
  );
}
