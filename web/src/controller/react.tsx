import { createContext, useContext, useEffect, useRef, useState, useSyncExternalStore, type ReactNode, type RefObject } from 'react';
import { createPortal } from 'react-dom';
import { Gamepad2 } from 'lucide-react';
import { useUiLanguage, translate as t } from '../i18n';
import { ControllerRuntime, type CommandHandler } from './runtime';
import { ControllerKeyboard } from './keyboard';
import {standardButtons} from './model';

const ControllerContext = createContext<ControllerRuntime | null>(null);
export function useController() { return useContext(ControllerContext); }
export function useControllerCommands(scope: RefObject<HTMLElement | null>, handler: CommandHandler, exclusive = false) {
  const controller = useController(); const current = useRef(handler); current.current = handler;
  useEffect(() => controller?.register(() => scope.current, action => current.current(action), exclusive), [controller, scope, exclusive]);
}
export function ControllerProvider({ children }: { children: ReactNode }) {
  const [controller] = useState(() => new ControllerRuntime());
  const state = useSyncExternalStore(controller.subscribe, controller.getSnapshot);
  useUiLanguage();
  useEffect(() => controller.start(), [controller]);
  useEffect(() => {
    const root = document.documentElement;
    if (state.mode === 'controller') root.dataset.homeInput = 'controller'; else delete root.dataset.homeInput;
    return () => { delete root.dataset.homeInput; };
  }, [state.mode]);
  return <ControllerContext value={controller}>{children}
    {state.keyboard && <ControllerKeyboard target={state.keyboard} controller={controller}/>}
    {state.mode === 'controller' && !state.keyboard && !state.gameplay && createPortal(<aside className="controller-hints" data-controller-exclude aria-hidden="true"><Gamepad2 size={17}/>
      <span>{state.source === 'gamepad' ? 'A / ✕' : 'OK'} <small>{t('Seç')}</small></span><span>{state.source === 'gamepad' ? 'B / ○' : '↩'} <small>{t('Geri')}</small></span><span>{state.source === 'gamepad' ? 'Start' : '☰'} <small>{t('Menü')}</small></span>
      {state.source==='gamepad'&&state.devices.some(device=>device.standard&&(state.preferences.mappings[device.id]??standardButtons).focusplayer===11)&&<span className="controller-player-hint">R3 <small>{t('Oynatıcı / geri')}</small></span>}
    </aside>, document.fullscreenElement ?? document.body)}
  </ControllerContext>;
}
