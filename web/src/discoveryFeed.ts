export interface CatalogIdentity { provider: string; kind: string; providerId: number }
export function discoveryKey(item: CatalogIdentity) { return `${item.provider}:${item.kind}:${item.providerId}`; }
export function discoveryRegion(input: { provider: string; kind: string; collection: string; genre?: number }, search = '') {
  return `discovery:${input.provider}:${input.kind}:${input.collection}:${input.genre ?? 'all'}:${encodeURIComponent(search)}`;
}
export function uniqueDiscovery<T extends CatalogIdentity>(items: T[]): T[] { return [...new Map(items.map(item => [discoveryKey(item), item])).values()]; }
export function discoveryColumns(width: number) { return Math.max(2, Math.min(10, Math.floor((Math.max(0, width) + 16) / 174))); }
export function discoveryNext(page: { page: number; hasMore: boolean; nextPage?: number | null }) {
  const next = page.nextPage ?? page.page + 1;
  return page.hasMore && next > page.page && next <= 500 ? next : undefined;
}
export function personalNext(page: { offset: number; limit: number; total: number }) {
  const next = page.offset + page.limit; return next < page.total && next <= 5000 ? next : undefined;
}
export function pausedEmptyPages(pages: { items: unknown[] }[] | undefined) {
  return !!pages && pages.length >= 3 && pages.slice(-3).every(page => page.items.length === 0);
}
