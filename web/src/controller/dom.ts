import { spatialTarget, type Direction } from './model.ts';

const interactive = 'button,a[href],input:not([type=hidden]),textarea,select,summary,[role=button],[role=option],[role^=menuitem],[role=slider],[tabindex="0"]';
export function navigable(element: HTMLElement): boolean {
  if (!element.isConnected || element.closest('[hidden],[inert],[aria-hidden="true"],[data-exiting],[data-ending-style],.media-menu-content[data-child-open]') || element.matches(':disabled,[aria-disabled="true"],input[type=hidden]')) return false;
  if (!element.getClientRects().length) return false;
  const style = getComputedStyle(element); if(style.display==='none'||style.visibility==='hidden'||style.opacity==='0')return false;
  if(typeof element.checkVisibility==='function')return element.checkVisibility({checkOpacity:true,checkVisibilityCSS:true});
  for(let parent=element.parentElement;parent;parent=parent.parentElement)if(getComputedStyle(parent).opacity==='0')return false;
  return true;
}
export function focusCandidates(scope: ParentNode): HTMLElement[] {
  return [...scope.querySelectorAll<HTMLElement>(interactive)].filter(element => navigable(element) && !element.closest('[data-controller-exclude]') && !element.matches('[data-controller-exclude]'));
}
export function controllerScope(): HTMLElement {
  const fullscreen = document.fullscreenElement as HTMLElement | null;
  const scopes = [...(fullscreen ?? document).querySelectorAll<HTMLElement>('[data-controller-scope],.home-menu-surface,.media-menu-popup,[role=dialog]')].filter(navigable);
  // Nested portals/virtual keyboard win over their owning dialog; visible modals win over the page.
  return scopes.at(-1) ?? fullscreen ?? document.body;
}
export function focusIdentity(element: HTMLElement): string {
  return element.dataset.homeFocus ? `media:${element.closest('[data-home-scroll]')?.getAttribute('data-home-scroll') ?? ''}:${element.dataset.homeFocus}` : element.id ? `id:${element.id}` : `control:${element.tagName}:${element.getAttribute('aria-label') ?? element.textContent?.trim().slice(0, 100) ?? ''}`;
}
const groups = '.discovery-rail,.poster-grid,.discovery-results-grid,.artist-grid,.profile-choices,.filter-pills,.content-actions,.home-navigation nav,[data-controller-group],[data-controller-player-dock]';
export function moveFocus(scope: HTMLElement, direction: Direction, preferredX?: number): HTMLElement | undefined {
  const current = document.activeElement as HTMLElement | null; let candidates = focusCandidates(scope);
  if (!current || !scope.contains(current) || !navigable(current) || !candidates.includes(current)) return initialFocus(scope);
  if (direction === 'left' || direction === 'right') {
    const group = current.closest<HTMLElement>(groups);
    if (group) {
      const local = candidates.filter(candidate => group.contains(candidate) && candidate !== current);
      const index = spatialTarget(local.map(element => element.getBoundingClientRect()), current.getBoundingClientRect(), direction);
       if (index >= 0) return local[index];
       if(group.hasAttribute('data-controller-player-dock'))return undefined;
    }
  }
  candidates = candidates.filter(candidate => candidate !== current);
  const index = spatialTarget(candidates.map(element => element.getBoundingClientRect()), current.getBoundingClientRect(), direction, preferredX);
  return index >= 0 ? candidates[index] : undefined;
}
export function initialFocus(scope: HTMLElement, remembered?: string): HTMLElement | undefined {
  const candidates = focusCandidates(scope);
  const old = remembered && candidates.find(element => focusIdentity(element) === remembered); if (old) return old;
  const withinScreen = (element: HTMLElement) => { const box = element.getBoundingClientRect(); return box.bottom > 0 && box.top < innerHeight && box.right > 0 && box.left < innerWidth; };
  return candidates.find(element => element.hasAttribute('data-controller-default') && withinScreen(element))
    ?? candidates.find(element => !!element.closest('.title-main-actions') && withinScreen(element))
    ?? candidates.find(element => element.hasAttribute('data-home-focus') && withinScreen(element))
    ?? candidates.find(withinScreen) ?? candidates[0];
}
export function textEntry(element: HTMLElement): element is HTMLInputElement | HTMLTextAreaElement {
  return element instanceof HTMLTextAreaElement || element instanceof HTMLInputElement && !['button', 'submit', 'reset', 'checkbox', 'radio', 'range', 'file', 'hidden', 'color'].includes(element.type);
}
/** Uses the native setter, so React's value tracker sees a real changed input. */
export function writeText(element: HTMLInputElement | HTMLTextAreaElement, value: string): boolean {
  if (!element.isConnected || element.disabled || element.readOnly) return false;
  if (element.maxLength >= 0 && value.length > element.maxLength) return false;
  const prototype = element instanceof HTMLTextAreaElement ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype;
  const setter = Object.getOwnPropertyDescriptor(prototype, 'value')?.set; if (!setter) return false;
  const old = element.value; setter.call(element, value);
  if (element.value !== value) { setter.call(element, old); return false; }
  element.dispatchEvent(new Event('input', { bubbles: true })); element.dispatchEvent(new Event('change', { bubbles: true })); return true;
}
export function focusMenuItem(scope: HTMLElement, direction: Direction): boolean {
  const options = focusCandidates(scope).filter(element => element.matches('[role=option],[role^=menuitem]'));
  if (!options.length) return false;
  const index = options.indexOf(document.activeElement as HTMLElement);
  const delta = direction === 'up' || direction === 'left' ? -1 : 1;
  const next = options[Math.max(0, Math.min(options.length - 1, index < 0 ? 0 : index + delta))]; next.focus({ preventScroll: true }); next.scrollIntoView({ block: 'nearest', inline: 'nearest' }); return true;
}
export function scrollController(scope: HTMLElement, left: number, top: number) {
  const options: ScrollToOptions = { left, top, behavior: matchMedia('(prefers-reduced-motion: reduce)').matches ? 'instant' : 'smooth' };
  for (let element = document.activeElement as HTMLElement | null; element && scope.contains(element); element = element.parentElement) {
    const style = getComputedStyle(element);
    if (top && element.scrollHeight > element.clientHeight && /auto|scroll/.test(style.overflowY) || left && element.scrollWidth > element.clientWidth && /auto|scroll/.test(style.overflowX)) { element.scrollBy(options); return; }
    if (element === scope) break;
  }
  if (scope === document.body) window.scrollBy(options); else scope.scrollBy(options);
}
