import { basePath } from '../base.ts';
import { controllerScope, focusCandidates, focusIdentity, focusMenuItem, initialFocus, moveFocus, navigable, scrollController, textEntry } from './dom.ts';
import { completeMapping, defaultControllerPreferences, GamepadReader, isDirection, remoteAction, standardButtons, validPreferences, type ButtonMapping, type ControllerAction, type ControllerMode, type ControllerPreferences, type ControllerSource } from './model.ts';

export interface ControllerDevice { id: string; index: number; standard: boolean; ready: boolean }
export interface ControllerSnapshot { mode: ControllerMode; source: ControllerSource; preferences: ControllerPreferences; capability: 'available' | 'insecure' | 'unsupported' | 'blocked'; devices: ControllerDevice[]; keyboard: HTMLInputElement | HTMLTextAreaElement | null; binding: ControllerAction | null; gameplay: boolean }
export type CommandHandler = (action: ControllerAction) => boolean;
interface Registration { scope: () => HTMLElement | null; handler: CommandHandler; exclusive: boolean }
export class ControllerRuntime {
  private listeners = new Set<() => void>(); private registrations: Registration[] = []; private reader = new GamepadReader();
  private snapshot: ControllerSnapshot;
  private frame = 0; private timer = 0; private repairFrame = 0; private restoreFrame = 0; private bindingTimer = 0;
  private preferredX?: number; private remembered = new Map<string, string>(); private started = false; private owner?: number;
  private key = `home-controller-v1:${basePath || '/'}`; private mappingDevice = '';
  private sessionIdentity = ''; private multiCursor = new WeakMap<HTMLSelectElement, number>();
  private gameplayOwners = new Set<object>();
  private movingFocus=false;private dockReturn?:{element:HTMLElement;identity:string;route:string};private dockFocus?:HTMLElement;
  constructor() {
    let preferences = defaultControllerPreferences; try { preferences = validPreferences(JSON.parse(localStorage.getItem(this.key) ?? '{}')); } catch { /* Local storage can be unavailable. */ }
    this.snapshot = { mode: 'pointer', source: 'remote', preferences, capability: this.gamepadCapability(), devices: [], keyboard: null, binding: null, gameplay: false };
  }
  private gamepadCapability():ControllerSnapshot['capability'] {
    try { return typeof navigator.getGamepads === 'function' ? 'available' : window.isSecureContext ? 'unsupported' : 'insecure'; }
    catch { return window.isSecureContext ? 'blocked' : 'insecure'; }
  }
  getSnapshot = () => this.snapshot;
  subscribe = (listener: () => void) => { this.listeners.add(listener); return () => { this.listeners.delete(listener); }; };
  private update(change: Partial<ControllerSnapshot>) { this.snapshot = { ...this.snapshot, ...change }; for (const listener of this.listeners) listener(); }
  register(scope: () => HTMLElement | null, handler: CommandHandler, exclusive = false) { const item = { scope, handler, exclusive }; this.registrations.push(item); return () => { this.registrations = this.registrations.filter(value => value !== item); }; }
  acquireGameplay() {
    const owner = {}; this.gameplayOwners.add(owner); this.pause(); this.reader.suspend();
    cancelAnimationFrame(this.repairFrame); cancelAnimationFrame(this.restoreFrame); this.repairFrame = 0; this.restoreFrame = 0;
    this.update({ gameplay: true, keyboard: null });
    return () => { if (!this.gameplayOwners.delete(owner) || this.gameplayOwners.size) return; this.reader.suspend(); this.update({ gameplay: false }); this.resume(); };
  }
  setPreferences(change: Partial<ControllerPreferences>) {
    const preferences = validPreferences({ ...this.snapshot.preferences, ...change });
    try { localStorage.setItem(this.key, JSON.stringify(preferences)); } catch { /* Still works in memory. */ }
    this.reader.suspend(); this.update({ preferences }); if (!preferences.enabled) { this.cancelBinding(); this.closeKeyboard(); this.pointer(); } queueMicrotask(this.poll);
  }
  clearFocus = () => { this.remembered.clear(); this.reader.suspend(); this.owner = undefined; this.preferredX=undefined;this.dockReturn=undefined;this.dockFocus=undefined;cancelAnimationFrame(this.restoreFrame); if (this.snapshot.keyboard) this.update({ keyboard: null }); this.scheduleRepair(); };
  private routeKey() { return location.pathname; }
  private focus(element?: HTMLElement, keepColumn=false) {
    if (!element || !navigable(element)) return;
    if(!keepColumn)this.preferredX=undefined;
    this.movingFocus=true;try{element.focus({ preventScroll: true });}finally{this.movingFocus=false;}
    // Repeated D-pad input must measure final geometry, not a still-moving smooth scroll.
    if(!element.closest('[data-controller-player-dock]')){
      element.scrollIntoView({ block: 'nearest', inline: 'nearest', behavior:'instant' });
      this.remembered.set(this.routeKey(), focusIdentity(element)); if (this.remembered.size > 40) this.remembered.delete(this.remembered.keys().next().value!);
    }
  }
  private rememberDockReturn(element:HTMLElement){this.dockReturn={element,identity:focusIdentity(element),route:this.routeKey()};}
  private togglePlayer(scope:HTMLElement){
    if(scope!==document.body||document.fullscreenElement||this.snapshot.keyboard)return false;
    const dock=[...document.querySelectorAll<HTMLElement>('[data-controller-player-dock]')].reverse().find(navigable);if(!dock)return false;
    const active=document.activeElement as HTMLElement|null;
    if(active&&dock.contains(active)){
      this.dockFocus=active;const outside=focusCandidates(scope).filter(element=>!element.closest('[data-controller-player-dock]'));
      const saved=this.dockReturn?.route===this.routeKey()?this.dockReturn:undefined;
      const target=saved&&outside.includes(saved.element)?saved.element:saved&&outside.find(element=>focusIdentity(element)===saved.identity)||outside.find(element=>element.hasAttribute('data-home-focus'))||outside[0];
      if(!target)return false;this.focus(target);return true;
    }
    if(active&&navigable(active))this.rememberDockReturn(active);
    const candidates=focusCandidates(dock),target=this.dockFocus&&candidates.includes(this.dockFocus)?this.dockFocus:candidates.find(element=>element.hasAttribute('data-controller-player-primary'))??initialFocus(dock);
    if(!target)return false;this.focus(target);return true;
  }
  private scheduleRepair = () => {
    if (this.snapshot.gameplay || this.snapshot.mode !== 'controller' || this.repairFrame || document.documentElement.dataset.homeRestoringPosition === 'true') return;
    this.repairFrame = requestAnimationFrame(() => { this.repairFrame = 0;
      if (this.snapshot.keyboard && !this.snapshot.keyboard.isConnected) { this.closeKeyboard(); return; }
      const scope = controllerScope(); const active = document.activeElement as HTMLElement | null;
      if (!active || !scope.contains(active) || !navigable(active) || !focusCandidates(scope).includes(active)) this.focus(initialFocus(scope, scope === document.body ? this.remembered.get(this.routeKey()) : undefined));
    });
  };
  private pointer = () => { this.reader.suspend(); this.owner = undefined; this.preferredX=undefined;this.multiCursor = new WeakMap(); if (this.snapshot.mode !== 'pointer') this.update({ mode: 'pointer' }); };
  closeKeyboard = () => { const target = this.snapshot.keyboard; if (!target) return; this.update({ keyboard: null }); cancelAnimationFrame(this.restoreFrame); this.restoreFrame = requestAnimationFrame(() => { this.restoreFrame = 0; this.focus(target); }); };
  edit = (target: HTMLElement | null) => { if (target && navigable(target) && textEntry(target) && !target.readOnly) { this.focus(target); this.update({ keyboard: target }); } };
  startBinding(id: string, action: ControllerAction) { this.cancelBinding(); this.mappingDevice = id; this.reader.suspend(); this.update({ binding: action }); this.bindingTimer = window.setTimeout(this.cancelBinding, 15000); }
  cancelBinding = () => { clearTimeout(this.bindingTimer); this.mappingDevice = ''; if (this.snapshot.binding) this.update({ binding: null }); this.reader.suspend(); };
  resetMapping(id: string) { const mappings = { ...this.snapshot.preferences.mappings }; delete mappings[id]; this.setPreferences({ mappings }); }
  command(action: ControllerAction, source: ControllerSource = 'gamepad'): boolean {
    if (this.snapshot.gameplay || !this.snapshot.preferences.enabled || document.hidden || !document.hasFocus()) return false;
    window.dispatchEvent(new Event('home-navigation-interaction'));
    if (this.snapshot.binding) { if (action === 'back') this.cancelBinding(); return true; }
    if (this.snapshot.mode !== 'controller' || this.snapshot.source !== source) this.update({ mode: 'controller', source });
    const scope = controllerScope(); let active = document.activeElement as HTMLElement | null;
    if (!active || !scope.contains(active) || !navigable(active) || !focusCandidates(scope).includes(active)) { this.focus(initialFocus(scope, this.remembered.get(this.routeKey()))); active = document.activeElement as HTMLElement | null; }
    const exclusive = this.registrations.slice().reverse().find(item => { const element = item.scope(); return item.exclusive && !!element && navigable(element) && (element === scope || element.contains(scope) || !!active && element.contains(active)); });
    if (exclusive) { exclusive.handler(action); return true; }
    if (action.startsWith('scroll')) { scrollController(scope, action === 'scrollleft' ? -90 : action === 'scrollright' ? 90 : 0, action === 'scrollup' ? -90 : action === 'scrolldown' ? 90 : 0); return true; }
    if (scope.matches('.media-menu-popup')) {
      if (isDirection(action)) focusMenuItem(scope, action);
      else if (action === 'confirm') active?.click();
      else if (action === 'back' || action === 'menu') active?.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }));
      return true; // Video.js owns menu input; transport shortcuts never bypass it.
    }
    if (action === 'back' && document.fullscreenElement === scope) { void document.exitFullscreen().catch(() => {}); return true; }
    if (active instanceof HTMLSelectElement && active.multiple && this.multiCursor.has(active)) {
      const options = [...active.options].filter(option => !option.disabled);
      let index = Math.max(0, Math.min(options.length - 1, this.multiCursor.get(active)!));
      if (action === 'up' || action === 'down') { index = Math.max(0, Math.min(options.length - 1, index + (action === 'up' ? -1 : 1))); this.multiCursor.set(active, index); }
      else if (action === 'confirm' && options[index]) { options[index].selected = !options[index].selected; active.dispatchEvent(new Event('change', { bubbles: true })); }
      else if (action === 'back') { this.multiCursor.delete(active); for (const option of active.options) delete option.dataset.controllerCursor; return true; }
      else this.multiCursor.delete(active);
      if (action === 'up' || action === 'down' || action === 'confirm') { for (const option of active.options) delete option.dataset.controllerCursor; if (options[index]) options[index].dataset.controllerCursor = 'true'; return true; }
    }
    const registrations = this.registrations.filter(item => { const element = item.scope(); return !!element && navigable(element) && (element === scope || element.contains(scope) || !!active && element.contains(active)); }).reverse().sort((a, b) => {
      const left = a.scope()!, right = b.scope()!; return left === right ? 0 : left.contains(right) ? 1 : right.contains(left) ? -1 : 0;
    });
    for (const item of registrations) if (item.handler(action) || item.exclusive) return true;
    if(action==='focusplayer')return this.togglePlayer(scope);
    if (isDirection(action)) {
      if (scope.matches('.home-menu-surface,.media-menu-popup,[role=listbox],[role=menu]') && focusMenuItem(scope, action)) return true;
      if (active?.matches('input[type=range],[role=slider]') && (action === 'left' || action === 'right')) {
        if (active instanceof HTMLInputElement && active.type === 'range') {
          const step = Number(active.step) || 1, min = Number(active.min) || 0, max = active.max ? Number(active.max) : 100;
          const next = Math.max(min, Math.min(max, Number(active.value) + (action === 'left' ? -step : step)));
          const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set;
          if (setter) { setter.call(active, String(next)); active.dispatchEvent(new Event('input', { bubbles: true })); active.dispatchEvent(new Event('change', { bubbles: true })); }
        }
        active.dispatchEvent(new KeyboardEvent('keydown', { key: action === 'left' ? 'ArrowLeft' : 'ArrowRight', bubbles: true, cancelable: true }));
        active.dispatchEvent(new KeyboardEvent('keyup', { key: action === 'left' ? 'ArrowLeft' : 'ArrowRight', bubbles: true })); return true;
      }
      if (action === 'left' || action === 'right') this.preferredX = undefined;
      else if (this.preferredX === undefined && active) { const box = active.getBoundingClientRect(); this.preferredX = (box.left + box.right) / 2; }
      const next = moveFocus(scope, action, this.preferredX); if (next) {
        const previousDock=active?.closest('[data-controller-player-dock]'),nextDock=next.closest('[data-controller-player-dock]');
        if(scope===document.body&&active&&!previousDock&&nextDock)this.rememberDockReturn(active);
        this.focus(next,(action==='up'||action==='down')&&previousDock===nextDock);
      } return true;
    }
    if (action === 'confirm' && active) {
      if (active instanceof HTMLSelectElement && active.multiple) { const index = Math.max(0, [...active.options].filter(option => !option.disabled).findIndex(option => option.selected)); this.multiCursor.set(active, index); const option = [...active.options].filter(value => !value.disabled)[index]; if (option) option.dataset.controllerCursor = 'true'; return true; }
      if (textEntry(active) && !active.readOnly) { if (!this.snapshot.keyboard) this.edit(active); return true; }
      if (active.matches('input[type=file],input[type=color]')) return true; // OS pickers need trusted activation.
      active.click(); return true;
    }
    if (action === 'back') {
      if (scope !== document.body) { const close = scope.querySelector<HTMLElement>('.dialog-close,[data-controller-close]'); if (close && navigable(close)) { close.click(); return true; }
        active?.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true })); return true; }
      return true;
    }
    if (action === 'search') { const search = focusCandidates(scope).find(element => element.matches('[data-controller-search],.search-box input')); if (search) { this.focus(search); if (textEntry(search)) this.update({ keyboard: search }); return true; } }
    if (action === 'previous' || action === 'next') {
      const tabs = [...scope.querySelectorAll<HTMLElement>('.home-navigation .nav-item')].filter(navigable); if (!tabs.length) return false;
      const index = tabs.findIndex(element => element.getAttribute('aria-current') === 'page' || element.classList.contains('active'));
      const target = tabs[(Math.max(index, 0) + (action === 'next' ? 1 : tabs.length - 1)) % tabs.length]; this.focus(target); target.click(); return true;
    }
    if (action === 'menu') { const menu = scope.querySelector<HTMLElement>('.nav-more'); if (menu && navigable(menu)) { this.focus(menu); menu.click(); return true; } }
    if (action === 'seekback' || action === 'seekforward') { scrollController(scope, 0, action === 'seekback' ? -180 : 180); return true; }
    return false;
  }
  private keydown = (event: KeyboardEvent) => {
    if (this.snapshot.gameplay || !event.isTrusted || event.defaultPrevented || event.ctrlKey || event.metaKey || event.altKey || !this.snapshot.preferences.enabled) return;
    const target = event.target as HTMLElement; const action = remoteAction(event.key, event.keyCode);
    if (this.snapshot.mode !== 'controller') {
      if (action === 'back' && event.key === 'Escape') return;
      if (target?.closest('.home-video-skin')) return; // Preserve Video.js keyboard shortcuts after mouse/Tab use.
      if (action === 'confirm' && target?.matches('button,a[href],summary,[role=button]')) return;
    }
    if (this.snapshot.keyboard && event.key === 'Backspace') return;
    const navigatingField = this.snapshot.mode === 'controller' && action && (isDirection(action) || action === 'confirm' || action === 'back' && event.key !== 'Backspace');
    if (target && textEntry(target) && !target.readOnly && !navigatingField) { if (event.key.length === 1 && this.snapshot.mode !== 'keyboard') { this.reader.suspend(); this.update({ mode: 'keyboard' }); } return; }
    if (!action) { if (event.key === 'Tab') { this.reader.suspend(); this.update({ mode: 'keyboard' }); } return; }
    // Real keyboard events retain native slider/listbox/menu behavior; only page navigation is intercepted.
    if (target?.closest('[role=listbox],[role=menu],[role=tablist],.media-menu-popup') || target?.matches('input[type=range],[role=slider],select')) {
      if (this.snapshot.mode === 'controller' && this.snapshot.source !== 'remote') this.update({ source: 'remote' }); return;
    }
    if (event.repeat && !isDirection(action) && action !== 'seekback' && action !== 'seekforward') { event.preventDefault(); return; }
    if (this.command(action, 'remote')) { event.preventDefault(); event.stopPropagation(); }
  };
  private poll = () => {
    cancelAnimationFrame(this.frame); clearTimeout(this.timer); this.frame = 0; this.timer = 0;
    if (this.snapshot.gameplay || !this.started || !this.snapshot.preferences.enabled || this.snapshot.capability !== 'available' || document.hidden || !document.hasFocus()) return;
    try {
      const pads = Array.from(navigator.getGamepads()).slice(0, 16);
      const devices = pads.filter((pad): pad is Gamepad => !!pad?.connected).map(pad => ({ id: pad.id.slice(0, 300), index: pad.index, standard: pad.mapping === 'standard', ready: completeMapping(this.snapshot.preferences.mappings[pad.id] ?? (pad.mapping === 'standard' ? standardButtons : undefined)) }));
      if (JSON.stringify(devices) !== JSON.stringify(this.snapshot.devices)) { if (this.owner !== undefined && !devices.some(device => device.index === this.owner)) this.pointer(); this.update({ devices }); }
      const binding = !!this.snapshot.binding;
      const intents = this.reader.sample(pads, performance.now(), this.snapshot.preferences, (pad, button) => {
        if (!this.snapshot.binding || pad.id !== this.mappingDevice) return false;
        const mapping: ButtonMapping = { ...(this.snapshot.preferences.mappings[pad.id] ?? (pad.mapping === 'standard' ? standardButtons : {})) };
        for (const [action, index] of Object.entries(mapping)) if (index === button) delete mapping[action as ControllerAction];
        mapping[this.snapshot.binding] = button; this.setPreferences({ mappings: { ...this.snapshot.preferences.mappings, [pad.id]: mapping } }); this.cancelBinding(); return true;
      });
      const intention = intents.find(intent => !intent.repeat) ?? intents.find(intent => intent.index === this.owner);
      if (!binding && intention) { this.owner = intention.index; this.command(intention.action); }
      if (devices.length) this.frame = requestAnimationFrame(this.poll); else this.timer = window.setTimeout(this.poll, 1000);
    } catch { this.reader.clear(); this.pointer(); this.update({ capability: window.isSecureContext ? 'blocked' : 'insecure', devices: [] }); }
  };
  private pause = () => { this.reader.suspend(); cancelAnimationFrame(this.frame); clearTimeout(this.timer); this.frame = 0; this.timer = 0; };
  private resume = () => { this.reader.suspend(); const capability=this.gamepadCapability();if(capability!==this.snapshot.capability)this.update({capability});this.poll();this.scheduleRepair(); };
  private movement = (event: PointerEvent) => { if (event.isTrusted && (Math.abs(event.movementX) + Math.abs(event.movementY) > 2)) this.pointer(); };
  private pointerDown = (event: PointerEvent) => { if (event.isTrusted) this.pointer(); };
  private focusIn = () => { if(!this.movingFocus)this.preferredX=undefined;if (this.snapshot.mode === 'controller') { const target = document.activeElement as HTMLElement | null; if (target?.dataset.homeFocus) this.remembered.set(this.routeKey(), focusIdentity(target)); this.scheduleRepair(); } };
  private sessionState = (event: Event) => { const session = (event as CustomEvent<{ userId: string | null; sessionId: string | null }>).detail; const identity = `${session?.userId ?? ''}:${session?.sessionId ?? ''}`; if (this.sessionIdentity && this.sessionIdentity !== identity) this.clearFocus(); this.sessionIdentity = identity; };
  private storage = (event: StorageEvent) => { if (event.key !== this.key) return; let preferences = defaultControllerPreferences; try { preferences = validPreferences(JSON.parse(event.newValue ?? '{}')); } catch { /* Ignore malformed stored preferences. */ } this.reader.suspend(); this.update({ preferences }); if (!preferences.enabled) { this.cancelBinding(); this.closeKeyboard(); this.pointer(); } this.poll(); };
  start() {
    this.started = true; this.poll();
    window.addEventListener('keydown', this.keydown, true); window.addEventListener('pointermove', this.movement); window.addEventListener('pointerdown', this.pointerDown); window.addEventListener('wheel', this.pointer, { passive: true });
    window.addEventListener('blur', this.pause); window.addEventListener('focus', this.resume); document.addEventListener('visibilitychange', this.resume);
    window.addEventListener('gamepadconnected', this.resume); window.addEventListener('gamepaddisconnected', this.poll); window.addEventListener('home-session-expired', this.clearFocus); window.addEventListener('home-controller-route', this.scheduleRepair); document.addEventListener('focusin', this.focusIn);
    window.addEventListener('home-session-state', this.sessionState); window.addEventListener('storage', this.storage);
    const observer = new MutationObserver(this.scheduleRepair); observer.observe(document.body, { childList: true, subtree: true, attributes: true, attributeFilter: ['hidden', 'inert', 'aria-hidden', 'aria-expanded', 'data-exiting', 'data-ending-style'] });
    return () => { this.started = false; this.pause(); cancelAnimationFrame(this.repairFrame); cancelAnimationFrame(this.restoreFrame); clearTimeout(this.bindingTimer); observer.disconnect();
      this.repairFrame = 0; this.restoreFrame = 0;
      window.removeEventListener('keydown', this.keydown, true); window.removeEventListener('pointermove', this.movement); window.removeEventListener('pointerdown', this.pointerDown); window.removeEventListener('wheel', this.pointer); window.removeEventListener('blur', this.pause); window.removeEventListener('focus', this.resume); document.removeEventListener('visibilitychange', this.resume);
      window.removeEventListener('gamepadconnected', this.resume); window.removeEventListener('gamepaddisconnected', this.poll); window.removeEventListener('home-session-expired', this.clearFocus); window.removeEventListener('home-controller-route', this.scheduleRepair); document.removeEventListener('focusin', this.focusIn);
      window.removeEventListener('home-session-state', this.sessionState); window.removeEventListener('storage', this.storage);
    };
  }
}
