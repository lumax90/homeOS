import { useEffect, useRef, useState } from 'react';
import { Dropdown, Header, Label } from '@heroui/react';
import { motion } from 'motion/react';
import { Activity, ArrowDownToLine, CheckCheck, ChevronDown, Ellipsis, Folder, Gamepad2, HardDrive, Layers3, Music2, Search, Tv, UsersRound } from 'lucide-react';
import type { Section } from '../pageRoutes';
import { secondarySelection, visibleNavigation, type NavigationAccess } from '../navigationModel';
import { Glass } from './Glass';
import './navigation.css';
import { useControllerCommands } from '../controller/react';
import { focusMenuItem } from '../controller/dom';
import { isDirection } from '../controller/model';

const icons = { discover: Search, library: Layers3, music: Music2, games: Gamepad2, live: Tv, shelves: Folder, tracking: CheckCheck,
  rooms: UsersRound, offline: HardDrive, downloads: ArrowDownToLine, activity: Activity, home: Layers3 };

export function MainNavigation({ view, pageKey, access, activeJobs, onNavigate }: { view: Section; pageKey: string; access: NavigationAccess; activeJobs: number; onNavigate: (view: Section) => void }) {
  const model = visibleNavigation(access);
  const selected = secondarySelection(view, model.groups);
  const [open, setOpen] = useState(false);
  const popup = useRef<HTMLDivElement>(null), trigger = useRef<HTMLButtonElement>(null);
  const close = () => { setOpen(false); requestAnimationFrame(() => trigger.current?.focus({ preventScroll: true })); };
  useControllerCommands(trigger, action => { if (action === 'back' && open) { close(); return true; } if (action === 'confirm' && !open) { setOpen(true); return true; } return false; });
  useControllerCommands(popup, action => {
    if (isDirection(action)) { if (popup.current) focusMenuItem(popup.current, action); return true; }
    if (action === 'back' || action === 'menu') { close(); return true; }
    if (action === 'confirm') { const id = (document.activeElement as HTMLElement | null)?.dataset.controllerNav; if (id) choose(id); else if (popup.current) focusMenuItem(popup.current, 'down'); return true; }
    return true;
  });
  useEffect(() => setOpen(false), [view, pageKey]);
  const choose = (key: string) => {
    const item = model.groups.flatMap(group => group.items).find(item => item.id === key);
    if (!item) return;
    setOpen(false); onNavigate(item.id);
  };
  return <Glass className="home-navigation" data-navigation="compact-v2"><nav aria-label="Ana gezinme" style={{ gridTemplateColumns: `repeat(${model.primary.length + 1}, minmax(0,1fr))` }}>
    {model.primary.map(({ id, label }) => {
      const Icon = icons[id];
      return <button key={id} onClick={() => onNavigate(id)} className={view === id ? 'nav-item active' : 'nav-item'} aria-current={view === id ? 'page' : undefined}>
        {view === id && <motion.span layoutId="nav-active" className="nav-active-material" />}<Icon size={17} strokeWidth={1.7}/><span>{label}</span>
      </button>;
    })}
    <Dropdown isOpen={open} onOpenChange={setOpen}>
      <Dropdown.Trigger ref={trigger} className={`nav-item nav-more ${selected ? 'active' : ''}`} aria-label={selected ? `Daha fazla · ${selected.label}` : 'Daha fazla'} aria-current={selected ? 'page' : undefined}>
        {selected && <motion.span layoutId="nav-active" className="nav-active-material" />}<Ellipsis size={18}/><span>Daha fazla</span><ChevronDown className="nav-more-chevron" size={12}/>
        {(selected || activeJobs > 0 && access.canAcquire) && <span className="nav-indicator" aria-hidden="true"/>}
      </Dropdown.Trigger>
      <Dropdown.Popover ref={popup} className="home-menu-surface home-navigation-popover" data-controller-scope="navigation" placement="bottom end" offset={12}>
        <Dropdown.Menu aria-label="Diğer ekranlar" className="home-navigation-menu" selectionMode="single" selectedKeys={selected ? [selected.id] : []} onAction={key => choose(String(key))}>
          {model.groups.map(group => <Dropdown.Section key={group.id} id={group.id} className="home-navigation-group"><Header className="home-navigation-group-title">{group.label}</Header>
            {group.items.map(item => { const Icon = icons[item.id]; return <Dropdown.Item key={item.id} id={item.id} textValue={item.label} data-controller-nav={item.id} className="home-menu-item home-navigation-menu-item"><Icon size={17}/><Label className="home-navigation-item-label">{item.label}</Label><Dropdown.ItemIndicator/></Dropdown.Item>; })}
          </Dropdown.Section>)}
        </Dropdown.Menu>
      </Dropdown.Popover>
    </Dropdown>
  </nav></Glass>;
}
