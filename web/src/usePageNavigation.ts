import { useCallback, useEffect, useRef, useState } from 'react';
import { appUrl, basePath } from './base';
import { parsePageRoute, type Section } from './pageRoutes';
import { capturePagePosition, pagePositionHost, readPagePosition, restorePagePosition, type PagePosition } from './pagePosition';

const marker = 'home-page-v2';
let entrySequence = 0;
const entryId = () => `home-${Date.now().toString(36)}-${++entrySequence}`;
const ownState = (state: unknown): Record<string, unknown> => state && typeof state === 'object' && !Array.isArray(state) ? state as Record<string, unknown> : {};
export function usePageNavigation(fallback: Section) {
  const [route, setRoute] = useState(() => parsePageRoute(location.pathname, basePath, fallback));
  const restoration = useRef<PagePosition | null>(readPagePosition(history.state));
  const positions = useRef(new Map<string, PagePosition>()); const currentEntry = useRef(''); const currentPath = useRef(location.pathname); const restoring = useRef(false);
  const cancelRestoration = useRef<() => void>(() => {});
  const save = useCallback(() => {
    if (restoring.current || [...document.querySelectorAll<HTMLElement>('[role=dialog]')].some(element => element.getClientRects().length > 0)) return;
    const position = capturePagePosition(), state = ownState(history.state); const id = typeof state.homeEntry === 'string' ? state.homeEntry : entryId();
    currentEntry.current = id; positions.current.set(id, position); positions.current.set(location.pathname, position);
    if (positions.current.size > 80) positions.current.delete(positions.current.keys().next().value!);
    history.replaceState({ ...state, homePage: marker, homeEntry: id, homePosition: position, homeScroll: position.y, homeFocus: position.focus?.key ?? null }, '', location.href);
  }, []);
  const visit = useCallback((path: string, replace = false) => {
    const next = parsePageRoute(appUrl(path), basePath, fallback);
    if (next.kind === 'not-found') return;
    const current = location.pathname;
    if (current === appUrl(path) && !replace) return;
    cancelRestoration.current(); window.dispatchEvent(new Event('home-navigation-interaction')); save();
    const position = replace ? positions.current.get(appUrl(path)) ?? readPagePosition(null) : readPagePosition(null);
    const state = { ...(replace ? ownState(history.state) : {}), homePage: marker, homeEntry: entryId(), homeReturn: replace ? ownState(history.state).homeReturn : current, homePosition: position, homeScroll: position.y, homeFocus: position.focus?.key ?? null };
    if (replace) history.replaceState(state, '', appUrl(path)); else history.pushState(state, '', appUrl(path));
    currentEntry.current = state.homeEntry; currentPath.current = location.pathname; restoration.current = position; setRoute(next);
  }, [fallback, save]);
  const back = useCallback((path: string) => {
    if (['home-page-v1', marker].includes(history.state?.homePage) && typeof history.state.homeReturn === 'string' && history.state.homeReturn !== location.pathname && parsePageRoute(history.state.homeReturn, basePath, fallback).kind !== 'not-found') history.back();
    else visit(path, true);
  }, [visit, fallback]);
  useEffect(() => {
    const previous = history.scrollRestoration; history.scrollRestoration = 'manual';
    const state = ownState(history.state); currentEntry.current = typeof state.homeEntry === 'string' ? state.homeEntry : entryId();
    history.replaceState({ ...state, homePage: marker, homeEntry: currentEntry.current }, '', location.href);
    let timer = 0;
    const scroll = () => { clearTimeout(timer); timer = window.setTimeout(save, 150); };
    const pop = () => {
      if (!restoring.current) { const leaving = capturePagePosition(); positions.current.set(currentEntry.current, leaving); positions.current.set(currentPath.current, leaving); }
      cancelRestoration.current();
      const target = ownState(history.state); currentEntry.current = typeof target.homeEntry === 'string' ? target.homeEntry : entryId(); currentPath.current = location.pathname;
      restoration.current = positions.current.get(currentEntry.current) ?? readPagePosition(target); setRoute(parsePageRoute(location.pathname, basePath, fallback));
    };
    window.addEventListener('popstate', pop);
    window.addEventListener('scroll', scroll, true);
    return () => { clearTimeout(timer); window.removeEventListener('popstate', pop); window.removeEventListener('scroll', scroll, true); history.scrollRestoration = previous; };
  }, [fallback, save]);
  useEffect(() => {
    const position = restoration.current ?? readPagePosition(history.state); restoring.current = true; document.documentElement.dataset.homeRestoringPosition = 'true';
    let stop = () => {};
    const release = () => { restoring.current = false; delete document.documentElement.dataset.homeRestoringPosition; };
    const finished = () => { restoration.current = null; release(); save(); window.dispatchEvent(new Event('home-controller-route')); };
    stop = restorePagePosition(position, pagePositionHost(), finished);
    cancelRestoration.current = () => { stop(); release(); };
    const interaction = (event: Event) => { if (event.type === 'home-navigation-interaction' || event.isTrusted) { stop(); restoration.current = null; release(); } };
    for (const type of ['wheel', 'pointerdown', 'touchstart', 'keydown', 'home-navigation-interaction']) window.addEventListener(type, interaction, { capture: true, passive: true });
    return () => { stop(); release(); for (const type of ['wheel', 'pointerdown', 'touchstart', 'keydown', 'home-navigation-interaction']) window.removeEventListener(type, interaction, true); };
  }, [route, save]);
  return { route, visit, back };
}
