export interface FocusLocation { key: string; region: string | null }
export interface PagePosition { x: number; y: number; focus: FocusLocation | null; anchor: { location: FocusLocation; top: number } | null; regions: { id: string; x: number; y: number }[] }
const finite = (value: unknown, fallback = 0) => typeof value === 'number' && Number.isFinite(value) ? value : fallback;
const coordinate = (value: unknown) => Math.max(0, Math.min(10_000_000, finite(value)));
function location(value: unknown): FocusLocation | null {
  if (!value || typeof value !== 'object') return null;
  const data = value as Partial<FocusLocation>;
  return typeof data.key === 'string' && data.key.length <= 500 ? { key: data.key, region: typeof data.region === 'string' && data.region.length <= 1200 ? data.region : null } : null;
}
export function readPagePosition(state: unknown): PagePosition {
  const data = state && typeof state === 'object' ? state as Record<string, unknown> : {};
  const saved = data.homePosition && typeof data.homePosition === 'object' ? data.homePosition as Partial<PagePosition> : {};
  const anchor = saved.anchor && typeof saved.anchor === 'object' ? saved.anchor : null;
  const anchorLocation = location(anchor?.location);
  return { x: coordinate(saved.x), y: coordinate(saved.y ?? data.homeScroll), focus: location(saved.focus) ?? (typeof data.homeFocus === 'string' ? location({ key: data.homeFocus }) : null),
    anchor: anchorLocation ? { location: anchorLocation, top: Math.max(-10000, Math.min(10000, finite(anchor?.top))) } : null,
    regions: Array.isArray(saved.regions) ? saved.regions.slice(0, 64).filter(value => value && typeof value.id === 'string' && value.id.length <= 1200).map(value => ({ id: value.id, x: coordinate(value.x), y: coordinate(value.y) })) : [] };
}
export function focusLocation(element: HTMLElement | null): FocusLocation | null {
  const key = element?.getAttribute('data-home-focus');
  return key ? { key, region: element!.closest<HTMLElement>('[data-home-scroll]')?.getAttribute('data-home-scroll') ?? null } : null;
}
export function findPositionTarget(target: FocusLocation): HTMLElement | undefined {
  const region = target.region ? [...document.querySelectorAll<HTMLElement>('[data-home-scroll]')].find(element => element.getAttribute('data-home-scroll') === target.region) : document;
  const candidates = region ? [...region.querySelectorAll<HTMLElement>('[data-home-focus]')].filter(element => element.getAttribute('data-home-focus') === target.key && element.getClientRects().length > 0) : [];
  if (!target.region) candidates.sort((a, b) => Math.abs(a.getBoundingClientRect().top - 80) - Math.abs(b.getBoundingClientRect().top - 80));
  return candidates[0];
}
export function capturePagePosition(): PagePosition {
  const focused = document.activeElement as HTMLElement | null;
  const visible = (element: HTMLElement) => { const box = element.getBoundingClientRect(); return !!element.getClientRects().length && box.bottom > 0 && box.top < innerHeight && box.right > 0 && box.left < innerWidth; };
  const cards = [...document.querySelectorAll<HTMLElement>('[data-home-focus]')].filter(visible);
  const anchor = focused && focusLocation(focused) && visible(focused) ? focused : cards.sort((a, b) => Math.abs(a.getBoundingClientRect().top - 80) - Math.abs(b.getBoundingClientRect().top - 80))[0];
  const target = focusLocation(anchor ?? null);
  return { x: scrollX, y: scrollY, focus: focusLocation(focused), anchor: anchor && target ? { location: target, top: anchor.getBoundingClientRect().top } : null,
    regions: [...document.querySelectorAll<HTMLElement>('[data-home-scroll]')].filter(element => element.getClientRects().length > 0).slice(0, 64).map(element => ({ id: element.getAttribute('data-home-scroll')!, x: element.scrollLeft, y: element.scrollTop })) };
}
export interface PositionHost {
  now(): number; frame(callback: () => void): number; cancelFrame(id: number): void;
  viewport(): { x: number; y: number; maxY: number }; scroll(x: number, y: number): void;
  restoreRegions(regions: PagePosition['regions']): void; anchorTop(target: FocusLocation): number | undefined;
  focus(target: FocusLocation | null): boolean; watch(callback: () => void): () => void;
}
/** Restore layout in several phases, not just one RAF before cached/lazy content settles. */
export function restorePagePosition(position: PagePosition, host: PositionHost, finished: () => void) {
  let frame = 0, stopped = false, focused = false, changed = host.now(); const started = changed;
  let lastDesired = -1, lastHeight = -1;
  const unwatch = host.watch(() => { changed = host.now(); });
  const step = () => {
    if (stopped) return;
    host.restoreRegions(position.regions);
    const viewport = host.viewport(); const anchor = position.anchor ? host.anchorTop(position.anchor.location) : undefined;
    const desired = Math.max(0, anchor === undefined ? position.y : viewport.y + anchor - position.anchor!.top);
    if (Math.abs(desired - lastDesired) > 1 || viewport.maxY !== lastHeight) { changed = host.now(); lastDesired = desired; lastHeight = viewport.maxY; }
    const y = Math.min(desired, viewport.maxY);
    if (Math.abs(viewport.y - y) > .5 || Math.abs(viewport.x - position.x) > .5) host.scroll(position.x, y);
    if (!focused) focused = host.focus(position.focus ?? position.anchor?.location ?? null);
    // Browsers that ignore preventScroll must not move the restored viewport to a duplicate/top card.
    const after = host.viewport(); if (Math.abs(after.y - y) > .5) host.scroll(position.x, y);
    const ready = viewport.maxY >= desired && (!position.anchor || anchor !== undefined) && (!position.focus || focused);
    if (host.now() - started >= 4000 || ready && host.now() - changed >= 300) { stop(); finished(); return; }
    frame = host.frame(step);
  };
  const stop = () => { if (!stopped) { stopped = true; host.cancelFrame(frame); unwatch(); } };
  frame = host.frame(step); return stop;
}
export function pagePositionHost(): PositionHost {
  return {
    now: () => performance.now(), frame: callback => window.requestAnimationFrame(callback), cancelFrame: id => window.cancelAnimationFrame(id),
    viewport: () => ({ x: scrollX, y: scrollY, maxY: Math.max(0, (document.scrollingElement?.scrollHeight ?? 0) - innerHeight) }),
    scroll: (x, y) => window.scrollTo({ left: x, top: y, behavior: 'instant' }),
    restoreRegions: regions => { const elements = [...document.querySelectorAll<HTMLElement>('[data-home-scroll]')]; for (const region of regions) { const element = elements.find(value => value.getAttribute('data-home-scroll') === region.id); if (element && element.getClientRects().length) { if (Math.abs(element.scrollLeft - region.x) > 1) element.scrollLeft = region.x; if (Math.abs(element.scrollTop - region.y) > 1) element.scrollTop = region.y; } } },
    anchorTop: target => findPositionTarget(target)?.getBoundingClientRect().top,
    focus: target => { const element = target ? findPositionTarget(target) : document.querySelector<HTMLElement>('[data-page-heading]'); if (!element) return false; element.focus({ preventScroll: true }); return true; },
    watch: callback => { const resize = typeof ResizeObserver === 'undefined' ? null : new ResizeObserver(callback); resize?.observe(document.body);
      const mutation = new MutationObserver(callback); mutation.observe(document.body, { childList: true, subtree: true, attributes: true, attributeFilter: ['hidden'] }); return () => { resize?.disconnect(); mutation.disconnect(); }; }
  };
}
