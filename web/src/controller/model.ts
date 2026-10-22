export const controllerActions = ['up', 'down', 'left', 'right', 'confirm', 'back', 'menu', 'search', 'previous', 'next', 'playpause', 'seekback', 'seekforward', 'scrollup', 'scrolldown', 'scrollleft', 'scrollright', 'focusplayer'] as const;
export type ControllerAction = typeof controllerActions[number];
export type Direction = 'up' | 'down' | 'left' | 'right';
export type ControllerMode = 'pointer' | 'keyboard' | 'controller';
export type ControllerSource = 'gamepad' | 'remote';
export type ButtonMapping = Partial<Record<ControllerAction, number>>;
export interface ControllerPreferences { enabled: boolean; deadzone: number; mappings: Record<string, ButtonMapping> }
export const defaultControllerPreferences: ControllerPreferences = { enabled: true, deadzone: .35, mappings: {} };
export const standardButtons: ButtonMapping = { confirm: 0, back: 1, playpause: 2, menu: 9, search: 3, previous: 4, next: 5, seekback: 6, seekforward: 7, up: 12, down: 13, left: 14, right: 15, focusplayer: 11 };
export function validPreferences(value: unknown): ControllerPreferences {
  const data = value && typeof value === 'object' ? value as Partial<ControllerPreferences> : {};
  const mappings: Record<string, ButtonMapping> = {};
  if (data.mappings && typeof data.mappings === 'object') for (const [id, entries] of Object.entries(data.mappings).slice(0, 16)) {
    if (!entries || typeof entries !== 'object' || id.length > 300) continue;
    const mapping: ButtonMapping = {};
    for (const action of controllerActions) { const button = entries[action]; if (typeof button === 'number' && Number.isInteger(button) && button >= 0 && button < 32) mapping[action] = button; }
    mappings[id] = mapping;
  }
  return { enabled: data.enabled !== false, deadzone: typeof data.deadzone === 'number' && Number.isFinite(data.deadzone) ? Math.max(.2, Math.min(.8, data.deadzone)) : .35, mappings };
}
export function completeMapping(mapping: ButtonMapping | undefined) { return !!mapping && ['up', 'down', 'left', 'right', 'confirm', 'back'].every(action => mapping[action as ControllerAction] !== undefined); }
export function isDirection(action: ControllerAction): action is Direction { return ['up', 'down', 'left', 'right'].includes(action); }
export interface PadSample { index: number; id: string; mapping: string; connected: boolean; buttons: readonly { pressed: boolean; value: number }[]; axes: readonly number[] }
interface PadState { signature: string; buttons: boolean[]; ready: boolean; direction?: Direction; held?: ControllerAction; repeatAt: number }
export interface PadIntent { index: number; action: ControllerAction; repeat: boolean }
/** Pure input reader: no DOM, media, network or browser event synthesis. */
export class GamepadReader {
  private states = new Map<number, PadState>();
  suspend() { for (const state of this.states.values()) { state.ready = false; state.held = undefined; state.direction = undefined; } }
  clear() { this.states.clear(); }
  sample(pads: readonly (PadSample | null)[], now: number, preferences: ControllerPreferences, rawPress?: (pad: PadSample, button: number) => boolean): PadIntent[] {
    const intents: PadIntent[] = []; const connected = new Set<number>();
    for (const pad of pads.slice(0, 16)) {
      if (!pad?.connected) continue; connected.add(pad.index);
      const buttons = pad.buttons.slice(0, 32).map(button => button.pressed || button.value >= .65);
      const signature = `${pad.id}:${pad.mapping}:${JSON.stringify(preferences.mappings[pad.id] ?? {})}`;
      let state = this.states.get(pad.index);
      if (!state || state.signature !== signature) {
        state = { signature, buttons, ready: false, repeatAt: 0 }; this.states.set(pad.index, state);
      }
      const rising = buttons.findIndex((pressed, index) => pressed && !state!.buttons[index]);
      if (rising >= 0 && rawPress?.(pad, rising)) { state.buttons = buttons; state.ready = false; continue; }
      const custom = preferences.mappings[pad.id]; const mapping = custom ?? (pad.mapping === 'standard' ? standardButtons : undefined);
      const x = pad.mapping === 'standard' && Number.isFinite(pad.axes[0]) ? pad.axes[0] : 0;
      const y = pad.mapping === 'standard' && Number.isFinite(pad.axes[1]) ? pad.axes[1] : 0;
      const sx = pad.mapping === 'standard' && Number.isFinite(pad.axes[2]) ? pad.axes[2] : 0, sy = pad.mapping === 'standard' && Number.isFinite(pad.axes[3]) ? pad.axes[3] : 0;
      const magnitude = Math.max(Math.abs(x), Math.abs(y));
      const neutral = !buttons.some(Boolean) && Math.max(magnitude, Math.abs(sx), Math.abs(sy)) < preferences.deadzone * .65;
      if (!state.ready) { if (neutral) state.ready = true; state.buttons = buttons; continue; }
      if (!preferences.enabled || !completeMapping(mapping)) { state.buttons = buttons; continue; }
      const pressed = (action: ControllerAction) => mapping![action] !== undefined && !!buttons[mapping![action]!];
      let direction: Direction | undefined;
      if (pressed('up') !== pressed('down')) direction = pressed('up') ? 'up' : 'down';
      else if (pressed('left') !== pressed('right')) direction = pressed('left') ? 'left' : 'right';
      else if (magnitude >= (state.direction ? preferences.deadzone * .75 : preferences.deadzone)) direction = Math.abs(x) > Math.abs(y) ? x < 0 ? 'left' : 'right' : y < 0 ? 'up' : 'down';
      const face = controllerActions.find(action => !isDirection(action) && pressed(action) && !state!.buttons[mapping![action]!]);
      const heldSeek = pressed('seekback') !== pressed('seekforward') ? pressed('seekback') ? 'seekback' : 'seekforward' : undefined;
      const scroll = Math.max(Math.abs(sx), Math.abs(sy)) >= preferences.deadzone ? Math.abs(sx) > Math.abs(sy) ? sx < 0 ? 'scrollleft' : 'scrollright' : sy < 0 ? 'scrollup' : 'scrolldown' : undefined;
      const action = face ?? direction ?? scroll ?? heldSeek;
      if (action && action !== state.held) { intents.push({ index: pad.index, action, repeat: false }); state.repeatAt = now + 380; }
      else if (action && !face && (isDirection(action) || action.startsWith('scroll') || action === 'seekback' || action === 'seekforward') && now >= state.repeatAt) {
        intents.push({ index: pad.index, action, repeat: true }); state.repeatAt = now + (isDirection(action) || action.startsWith('scroll') ? 125 : 300);
      }
      // Non-directional actions remain held until release, and never auto-repeat.
      state.held = action ?? controllerActions.find(item => !isDirection(item) && pressed(item));
      state.direction = direction; state.buttons = buttons;
    }
    for (const index of this.states.keys()) if (!connected.has(index)) this.states.delete(index);
    return intents;
  }
}
export interface FocusRect { left: number; top: number; right: number; bottom: number }
export function spatialTarget(rects: FocusRect[], origin: FocusRect, direction: Direction, preferredX?: number): number {
  const horizontal = direction === 'left' || direction === 'right'; const positive = direction === 'right' || direction === 'down';
  const ox = horizontal || preferredX === undefined ? (origin.left + origin.right) / 2 : preferredX; const oy = (origin.top + origin.bottom) / 2;
  let best = -1, score = Infinity;
  rects.forEach((rect, index) => {
    const x = (rect.left + rect.right) / 2, y = (rect.top + rect.bottom) / 2;
    const delta = horizontal ? x - ox : y - oy; if ((positive ? delta : -delta) <= 2) return;
    const across = Math.abs(horizontal ? y - oy : x - ox);
    const beam = horizontal ? rect.top < origin.bottom && rect.bottom > origin.top : rect.left <= ox && rect.right >= ox;
    if(!beam&&(horizontal?x>origin.left+2&&x<origin.right-2:y>origin.top+2&&y<origin.bottom-2))return;
    const gap = horizontal ? Math.max(0, rect.top-origin.bottom, origin.top-rect.bottom) : Math.max(0, rect.left-ox, ox-rect.right);
    // Prefer the current row/column, but not an item a screen away over a nearby control.
    const distance = Math.abs(delta) + across * .8 + (beam ? 0 : 48 + Math.min(192, gap * 2));
    if (distance < score) { score = distance; best = index; }
  });
  return best;
}
export function remoteAction(key: string, keyCode = 0): ControllerAction | undefined {
  const actions: Record<string, ControllerAction> = { ArrowUp: 'up', ArrowDown: 'down', ArrowLeft: 'left', ArrowRight: 'right', Enter: 'confirm', Escape: 'back', BrowserBack: 'back', GoBack: 'back', Backspace: 'back', ContextMenu: 'menu', MediaPlayPause: 'playpause', MediaRewind: 'seekback', MediaFastForward: 'seekforward', MediaTrackPrevious: 'previous', MediaTrackNext: 'next' };
  const legacy: Record<number, ControllerAction> = { 37: 'left', 38: 'up', 39: 'right', 40: 'down', 13: 'confirm', 27: 'back', 8: 'back', 93: 'menu', 10009: 'back', 461: 'back' };
  return actions[key] ?? legacy[keyCode];
}
