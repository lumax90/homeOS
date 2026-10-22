import { useEffect, useRef, useState } from 'react';
import { Button, Modal } from '@heroui/react';
import { ArrowLeft, Check, Delete, Space, X } from 'lucide-react';
import { translate as t, useUiLanguage } from '../i18n';
import { writeText } from './dom';
import { useControllerCommands } from './react';
import type { ControllerRuntime } from './runtime';

const symbols = '0123456789@._-/:\\+!?#$%&*()[]=,;\'"T';
export function ControllerKeyboard({ target, controller }: { target: HTMLInputElement | HTMLTextAreaElement; controller: ControllerRuntime }) {
  const language = useUiLanguage(); const panel = useRef<HTMLDivElement>(null);
  const [draft, setDraft] = useState(target.value); const [upper, setUpper] = useState(false); const [error, setError] = useState('');
  const [numbers, setNumbers] = useState(target.inputMode === 'numeric' || target instanceof HTMLInputElement && ['number', 'date', 'time', 'datetime-local'].includes(target.type));
  const [turkish, setTurkish] = useState(language === 'tr');
  const secret = target instanceof HTMLInputElement && target.type === 'password';
  const label = target.getAttribute('aria-label') || target.labels?.[0]?.textContent?.trim().slice(0, 100) || t('Metin gir');
  const add = (text: string) => setDraft(old => target.maxLength < 0 || old.length + text.length <= target.maxLength ? old + text : old);
  const apply = () => { if (writeText(target, draft)) controller.closeKeyboard(); else setError(t('Bu alanın biçimini ve uzunluk sınırını kontrol et.')); };
  useControllerCommands(panel, action => {
    if (action === 'back') { controller.closeKeyboard(); return true; }
    if (action === 'menu') { apply(); return true; }
    if (action === 'previous' || action === 'next') { setNumbers(old => !old); return true; }
    return false;
  });
  useEffect(() => {
    const key = (event: KeyboardEvent) => {
      if (!event.isTrusted || event.ctrlKey || event.metaKey || event.altKey || event.defaultPrevented) return;
      if (event.key === 'Backspace') { event.preventDefault(); setDraft(old => [...old].slice(0, -1).join('')); }
      else if (event.key.length === 1) { event.preventDefault(); add(event.key); }
    };
    window.addEventListener('keydown', key); return () => window.removeEventListener('keydown', key);
  }, [target]);
  const letters = turkish ? 'qwertyuıopğüasdfghjklşizxcvbnmöç' : 'qwertyuiopasdfghjklzxcvbnm';
  const keys = [...(numbers ? symbols : upper ? letters.toLocaleUpperCase(turkish ? 'tr' : 'en') : letters)];
  return <Modal.Backdrop isOpen onOpenChange={open => { if (!open) controller.closeKeyboard(); }} className="home-backdrop controller-keyboard-backdrop">
    <Modal.Container className="controller-keyboard-container"><div ref={panel}><Modal.Dialog className="controller-keyboard" aria-label={t('Ekran klavyesi')} data-controller-scope="keyboard">
      <header><div><Modal.Heading>{t('Ekran klavyesi')}</Modal.Heading><p>{label}</p></div><Button isIconOnly variant="ghost" aria-label={t('Kapat')} onPress={controller.closeKeyboard}><X size={20}/></Button></header>
      <input className="controller-keyboard-preview" type={secret ? 'password' : 'text'} readOnly value={draft} aria-label={t('Metin taslağı')} autoComplete="off"/>
      <div className="controller-keyboard-keys">{keys.map((key, index) => <button type="button" key={key + index} data-controller-default={index === 0 || undefined} onClick={() => add(key)}>{key}</button>)}</div>
      <div className="controller-keyboard-tools"><button type="button" onClick={() => setUpper(old => !old)} aria-pressed={upper}>⇧</button><button type="button" onClick={() => setNumbers(old => !old)}>{numbers ? 'ABC' : '123 / #+='}</button><button type="button" onClick={() => setTurkish(old => !old)}>{turkish ? 'TR' : 'EN'}</button><button type="button" aria-label={t('Boşluk')} onClick={() => add(' ')}><Space size={18}/></button><button type="button" aria-label={t('Son karakteri sil')} onClick={() => setDraft(old => [...old].slice(0, -1).join(''))}><Delete size={18}/></button>{target instanceof HTMLTextAreaElement && <button type="button" aria-label={t('Yeni satır')} onClick={() => add('\n')}>↵</button>}</div>
      {error && <p className="form-error" role="alert">{error}</p>}
      <footer><Button variant="ghost" onPress={controller.closeKeyboard}><ArrowLeft size={16}/>{t('Vazgeç')}</Button><Button className="primary-button" onPress={apply}><Check size={16}/>{t('Metni uygula')}</Button></footer>
    </Modal.Dialog></div></Modal.Container>
  </Modal.Backdrop>;
}
