import { useEffect, useRef, useState } from 'react';
import { Button, Modal } from '@heroui/react';
import { ArrowDownToLine, Bell, Check, ChevronRight, Film, Folder, Info, Languages, Link2, LogOut, MonitorPlay, Play, Plus, Radio, Search, Server, ShieldCheck, Sparkles, Subtitles, Users, X } from 'lucide-react';
import { useQueryClient } from '@tanstack/react-query';
import { broadcastAuthChange, loadSession, mutate, type Library, type Session, type SystemInfo } from '../api';
import { findSettings, visibleSettings, type SettingsSection } from '../settingsModel';
import { translate as t, useUiLanguage } from '../i18n';
import { useControllerCommands } from '../controller/react';
import { Dialog } from './Dialogs';
import { PersonalAccount, HouseholdBody } from './Accounts';
import { LanguageSettings } from './LanguageSettings';
import { ControllerSettings } from './ControllerSettings';
import { DeviceLinkApprovalPanel } from './Receivers';
import { NotificationChannelsPanel,NotificationDeliveryHistory } from './NotificationChannels';
import { MetadataConnection } from './Metadata';
import { SourceSettings } from './SourceDialogs';
import { SubtitleSettings } from './SubtitleProviders';
import { LibraryList } from './LibraryManager';
import { OperationsBody } from './Operations';
import { SkipPreferences } from './PlaybackSegments';
import { HomeSelect } from './HomeSelect';
import './settings.css';

const icons = { account: ShieldCheck, interface: Languages, playback: Play, devices: Link2, downloads: ArrowDownToLine, notifications: Bell, libraries: Folder, sources: Radio, metadata: Film, subtitles: Subtitles, household: Users, operations: Server, about: Info };
export interface SettingsDialogProps { open: boolean; onClose: () => void; session: Session; system?: SystemInfo; libraries: Library[]; onAdd: () => void; notify: (message: string) => void; initialSection?: SettingsSection }
export function SettingsDialog(props: SettingsDialogProps) { return props.open ? <SettingsContents {...props}/> : null; }
function SettingsContents({ onClose, session, system, libraries, onAdd, notify, initialSection = 'account' }: SettingsDialogProps) {
  const client = useQueryClient(); useUiLanguage();
  const available = visibleSettings(session);
  const initial = available.some(section => section.id === initialSection) ? initialSection : 'account';
  const [active, setActive] = useState<SettingsSection>(initial); const [visited, setVisited] = useState<SettingsSection[]>([initial]); const [query, setQuery] = useState('');
  const nav = useRef<HTMLDivElement>(null), pane = useRef<HTMLDivElement>(null);
  const matches = findSettings(session, query, t); const selected = available.find(section => section.id === active) ?? available[0];
  useEffect(() => { if (!available.some(section => section.id === active)) setActive('account'); }, [session.role, session.canAcquire, active]);
  const choose = (id: SettingsSection) => { setActive(id); setVisited(old => old.includes(id) ? old : [...old, id]); };
  const focusTab = (id: SettingsSection) => nav.current?.querySelector<HTMLElement>(`[data-settings-tab="${id}"]`)?.focus({ preventScroll: true });
  useControllerCommands(nav, action => {
    const focused = document.activeElement as HTMLElement;
    if (!focused?.hasAttribute('data-settings-tab')) return false;
    if (action === 'up' || action === 'down') { const index = matches.findIndex(section => section.id === focused.dataset.settingsTab); const next = matches[Math.max(0, Math.min(matches.length - 1, index + (action === 'up' ? -1 : 1)))]; if (next) focusTab(next.id); return true; }
    return false;
  });
  useControllerCommands(pane, action => { if (action === 'back') { focusTab(selected.id); return true; } return false; });
  async function logout() {
    try { await mutate('/auth/logout'); client.setQueryData(['session'], await loadSession()); client.removeQueries({ predicate: query => query.queryKey[0] !== 'session' }); broadcastAuthChange(); onClose(); }
    catch (error) { notify((error as Error).message); }
  }
  const panel = (id: SettingsSection) => {
    switch (id) {
      case 'account': return <><div className="settings-identity"><div className="profile-avatar">{session.displayName?.slice(0, 1).toLocaleUpperCase()}</div><div><strong>{session.displayName}</strong><span>{t(session.role === 'admin' ? 'Yönetici' : session.role === 'member' ? 'Üye' : 'İzleyici')}</span></div></div><PersonalAccount notify={notify} section="security" expanded/><Button variant="ghost" onPress={logout}><LogOut size={16}/>{t('Oturumu kapat')}</Button></>;
      case 'interface': return <><LanguageSettings/><ControllerSettings/></>;
      case 'playback': return <><PersonalAccount notify={notify} section="playback"/><SkipPreferences/></>;
      case 'devices': return <DeviceLinkApprovalPanel/>;
      case 'downloads': return <PersonalAccount notify={notify} section="downloads"/>;
      case 'notifications': return <><PersonalAccount notify={notify} section="notifications"/>{session.role === 'admin' ? <NotificationChannelsPanel/>:<NotificationDeliveryHistory/>}</>;
      case 'libraries': return <><div className="settings-panel-toolbar"><span>{libraries.length} {t('kütüphane')}</span><Button className="secondary-button" onPress={onAdd}><Plus size={16}/>{t('Kütüphane ekle')}</Button></div><LibraryList notify={notify}/></>;
      case 'sources': return <SourceSettings notify={notify}/>;
      case 'metadata': return <MetadataConnection notify={notify}/>;
      case 'subtitles': return <SubtitleSettings/>;
      case 'household': return <HouseholdBody notify={notify}/>;
      case 'operations': return <OperationsBody/>;
      case 'about': return <><div className="capabilities"><div><MonitorPlay size={18}/><span>{t('Oynatma uyumluluğu')}<small>{t(system?.playbackMode === 'direct+remux+software-hls' ? 'Özgün dosya, HLS ve sunucuda doğrulanan kodlayıcı profilleri' : 'Cihazının desteklediği özgün dosyalar')}</small></span><Check size={16}/></div><div><Sparkles size={18}/><span>{t('Medya analizi ve önizlemeler')}<small>{t(system?.ffprobeAvailable && system.ffmpegAvailable ? 'ffprobe ve FFmpeg kullanılabilir' : 'Medya araçları henüz yapılandırılmadı')}</small></span></div></div>{system && <dl className="system-info"><div><dt>{t('Sürüm')}</dt><dd>Home {system.version}</dd></div><div><dt>{t('Çekirdek')}</dt><dd>{system.framework}</dd></div><div><dt>{t('Veri klasörü')}</dt><dd>{system.dataPath}</dd></div></dl>}</>;
    }
  };
  return <Dialog open onClose={onClose} title={t('Ayarlar')} className="settings-container" backdropClassName="settings-backdrop"><div className="settings-shell" data-settings="organized-v1">
    <aside className="settings-sidebar"><header><Modal.Heading>{t('Ayarlar')}</Modal.Heading><p>{session.displayName}</p></header>
      <div className="settings-search"><Search size={16}/><input aria-label={t('Ayarlarda ara')} placeholder={t('Bir ayar bul…')} value={query} maxLength={80} onChange={event => setQuery(event.target.value)}/>{query && <button type="button" aria-label={t('Aramayı temizle')} onClick={() => setQuery('')}><X size={14}/></button>}</div>
      <div className="settings-mobile-select"><HomeSelect aria-label={t('Ayar kategorileri')} value={selected.id} disabled={!matches.length} onChange={event => choose(event.target.value as SettingsSection)}>{(matches.some(section => section.id === selected.id) ? matches : [selected, ...matches]).map(section => <option key={section.id} value={section.id}>{t(section.label)}</option>)}</HomeSelect></div>
      <div ref={nav} role="tablist" aria-label={t('Ayar kategorileri')} aria-orientation="vertical" className="settings-navigation" onKeyDown={event => {
        if (!['ArrowUp', 'ArrowDown', 'Home', 'End'].includes(event.key) || !(event.target as HTMLElement).hasAttribute('data-settings-tab')) return;
        event.preventDefault(); const index = matches.findIndex(section => section.id === (event.target as HTMLElement).dataset.settingsTab); const next = event.key === 'Home' ? 0 : event.key === 'End' ? matches.length - 1 : Math.max(0, Math.min(matches.length - 1, index + (event.key === 'ArrowUp' ? -1 : 1))); if (matches[next]) focusTab(matches[next].id);
      }}>
        {(['personal', 'server', 'about'] as const).map(group => { const entries = matches.filter(section => section.group === group); return entries.length ? <section key={group}><h3>{t(group === 'personal' ? 'Kişisel tercihler' : group === 'server' ? 'Sunucu yönetimi' : 'Bilgi')}</h3>{entries.map(section => { const Icon = icons[section.id]; return <button key={section.id} type="button" role="tab" id={`settings-tab-${section.id}`} aria-selected={selected.id === section.id} aria-controls={`settings-panel-${section.id}`} tabIndex={(matches.some(item => item.id === selected.id) ? selected.id : matches[0]?.id) === section.id ? 0 : -1} data-settings-tab={section.id} onClick={() => choose(section.id)}><Icon size={17}/><span>{t(section.label)}</span><ChevronRight size={12}/></button>; })}</section> : null; })}
      </div>{!matches.length && <p className="settings-empty">{t('Bu ayarla eşleşen bir kategori yok.')}</p>}<footer><span>Home {system?.version ?? ''}</span><Button variant="ghost" onPress={logout}><LogOut size={15}/>{t('Oturumu kapat')}</Button></footer>
    </aside>
    <div ref={pane} className="settings-pane"><header className="settings-pane-heading"><p>{t(selected.group === 'server' ? 'SUNUCU' : 'KİŞİSEL ALANIN')}</p><h2>{t(selected.label)}</h2><span>{t(selected.description)}</span></header>
      {available.filter(section => visited.includes(section.id) || section.id === selected.id).map(section => <div key={section.id} id={`settings-panel-${section.id}`} role="tabpanel" aria-label={t(section.label)} hidden={section.id !== selected.id} className="settings-panel">{panel(section.id)}</div>)}
    </div>
  </div></Dialog>;
}
