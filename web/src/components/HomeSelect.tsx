import { Select, ListBox } from '@heroui/react';
import { useEffect, useRef, useState, type ReactNode, type SelectHTMLAttributes } from 'react';
import { homeSelectChange, homeSelectKey, homeSelectOptions, homeSelectValue } from '../selectModel';
import { useControllerCommands } from '../controller/react';
import { focusMenuItem } from '../controller/dom';
import { isDirection } from '../controller/model';

export interface HomeSelectProps extends Pick<SelectHTMLAttributes<HTMLSelectElement>, 'aria-label' | 'aria-labelledby' | 'aria-describedby' | 'className' | 'disabled' | 'required' | 'name' | 'id'> {
  children?: ReactNode;
  value?: string | number;
  onChange?: (change: { target: { value: string } }) => void;
}

/** Shared renderer for single-choice Home controls; React Aria owns interaction. */
export function HomeSelect({ children, value, onChange, disabled, required, className, name, ...accessibility }: HomeSelectProps) {
  const options = homeSelectOptions(children);
  const selected = homeSelectValue(options, value);
  const [open, setOpen] = useState(false);
  const root = useRef<HTMLDivElement>(null), popup = useRef<HTMLDivElement>(null), trigger = useRef<HTMLButtonElement>(null);
  const close = () => { setOpen(false); requestAnimationFrame(() => trigger.current?.focus({ preventScroll: true })); };
  useControllerCommands(root, action => {
    if (action === 'back' && open) { close(); return true; }
    if (action === 'confirm' && !open) { if (!disabled && options.length) setOpen(true); return true; }
    return false;
  });
  useControllerCommands(popup, action => {
    if (isDirection(action)) { if (popup.current) focusMenuItem(popup.current, action); return true; }
    if (action === 'back' || action === 'menu') { close(); return true; }
    if (action === 'confirm') {
      const focused = document.activeElement as HTMLElement | null;
      const value = focused?.dataset.controllerValue ?? selected?.value;
      const change = homeSelectChange(options, value === undefined ? null : homeSelectKey(value), disabled);
      if (change) { onChange?.(change); close(); } return true;
    }
    return true; // The open choice owns input; page/player shortcuts cannot leak through.
  });
  useEffect(() => {
    if (!open) return;
    const close = () => setOpen(false);
    document.addEventListener('fullscreenchange', close);
    return () => document.removeEventListener('fullscreenchange', close);
  }, [open]);
  return <Select {...accessibility} ref={root} className="home-select" data-home-control="select-v1" value={selected && (!required || selected.value !== '') ? homeSelectKey(selected.value) : null}
    isOpen={open} onOpenChange={setOpen}
    isDisabled={disabled || !options.length} isRequired={required} validationBehavior="native"
    onChange={key => { const change = homeSelectChange(options, key, disabled); if (change) onChange?.(change); }}>
    <Select.Trigger ref={trigger} className={`home-select-trigger ${className ?? ''}`}>
      <Select.Value className="home-select-value">{selected?.label ?? ''}</Select.Value><Select.Indicator className="home-select-indicator"/>
    </Select.Trigger>
    {name && <input type="hidden" name={name} value={selected?.value ?? ''} disabled={disabled || !options.length}/>}
    <Select.Popover ref={popup} className="home-menu-surface home-select-popover" data-controller-scope="select" placement="bottom start" offset={8} containerPadding={14}
      UNSTABLE_portalContainer={typeof document === 'undefined' ? undefined : document.fullscreenElement ?? undefined}>
      <ListBox className="home-select-list" items={options.map(option => ({ ...option, id: homeSelectKey(option.value) }))}>
        {option => <ListBox.Item id={option.id} textValue={option.label} isDisabled={option.disabled} data-controller-value={option.value} className="home-menu-item home-select-option">
          <span className="home-select-option-label">{option.label}</span><ListBox.ItemIndicator className="home-select-option-indicator"/>
        </ListBox.Item>}
      </ListBox>
    </Select.Popover>
  </Select>;
}
