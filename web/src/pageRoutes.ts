export const sections = ['home', 'discover', 'library', 'music', 'games', 'tracking', 'downloads', 'activity', 'offline', 'rooms', 'live', 'shelves'] as const;
export type Section = typeof sections[number];
export type PageRoute = { kind: 'section'; section: Section } | { kind: 'title'; provider: string; contentKind: 'movie' | 'show'; id: number }
  | { kind: 'media' | 'watch' | 'series' | 'game' | 'game-play'; id: string } | { kind: 'not-found' };

export function parsePageRoute(pathname: string, basePath = '', fallback: Section = 'discover'): PageRoute {
  if (basePath && pathname !== basePath && !pathname.startsWith(basePath + '/')) return { kind: 'not-found' };
  const path = (basePath ? pathname.slice(basePath.length) : pathname).replace(/\/$/, '') || '/';
  if (path === '/') return { kind: 'section', section: fallback };
  const parts = path.split('/').slice(1);
  if (parts.length === 1 && sections.includes(parts[0] as Section)) return { kind: 'section', section: parts[0] as Section };
  if (parts[0] === 'games' && /^[a-z0-9][a-z0-9-]{0,59}$/.test(parts[1] ?? '')) {
    if (parts.length === 2) return { kind: 'game', id: parts[1] };
    if (parts.length === 3 && parts[2] === 'play') return { kind: 'game-play', id: parts[1] };
  }
  if (parts.length === 2 && ['media', 'watch','series'].includes(parts[0]) && /^[a-f0-9]{32}$/.test(parts[1])) return { kind: parts[0] as 'media' | 'watch'|'series', id: parts[1] };
  if (parts.length === 4 && parts[0] === 'title' && ['tmdb', 'tvmaze'].includes(parts[1]) && ['movie', 'show'].includes(parts[2]) && /^\d{1,12}$/.test(parts[3]) && Number(parts[3]) > 0 && Number(parts[3]) <= 2147483647)
    return { kind: 'title', provider: parts[1], contentKind: parts[2] as 'movie' | 'show', id: Number(parts[3]) };
  return { kind: 'not-found' };
}
export function pagePath(route: Exclude<PageRoute, { kind: 'not-found' }>): string {
  return route.kind === 'section' ? '/' + route.section : route.kind === 'title' ? `/title/${route.provider}/${route.contentKind}/${route.id}` : route.kind === 'game' || route.kind === 'game-play' ? `/games/${route.id}${route.kind === 'game-play' ? '/play' : ''}` : `/${route.kind}/${route.id}`;
}
export function detailsPath(item: { id: string; kind: string; content?: { provider: string; kind: string; providerId: number } | null;catalogGroup?:{kind:string}|null }): string {
  if(item.catalogGroup?.kind==='show')return `/series/${item.id}`;
  const content = item.content;
  return item.kind === 'video' && content && ['tmdb', 'tvmaze'].includes(content.provider) && ['movie', 'show'].includes(content.kind)
    ? `/title/${content.provider}/${content.kind}/${content.providerId}` : `/media/${item.id}`;
}
