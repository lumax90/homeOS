import type { Section } from './pageRoutes';

export interface NavigationItem { id: Section; label: string; access?: 'discover' | 'acquire' | 'admin' }
export interface NavigationGroup { id: string; label: string; items: NavigationItem[] }
export interface NavigationAccess { canDiscover: boolean; canAcquire: boolean; role: string | null }

const primaryItems: NavigationItem[] = [
  { id: 'discover', label: 'Keşfet', access: 'discover' },
  { id: 'library', label: 'Kütüphane' },
  { id: 'music', label: 'Müzik' },
  { id: 'games', label: 'Oyunlar' }
];
const secondaryGroups: NavigationGroup[] = [
  { id: 'media', label: 'Medya', items: [{ id: 'live', label: 'Canlı TV' }, { id: 'shelves', label: 'Raflar' }] },
  { id: 'personal', label: 'Kişisel alan', items: [{ id: 'tracking', label: 'Takip listem' }, { id: 'rooms', label: 'Birlikte' }, { id: 'offline', label: 'Cihazım' }] },
  { id: 'operations', label: 'Sunucu', items: [{ id: 'downloads', label: 'İndirmeler', access: 'acquire' }, { id: 'activity', label: 'Aktivite', access: 'admin' }] }
];
function allowed(item: NavigationItem, access: NavigationAccess) {
  return !item.access || item.access === 'discover' && access.canDiscover || item.access === 'acquire' && access.canAcquire || item.access === 'admin' && access.role === 'admin';
}
export function visibleNavigation(access: NavigationAccess) {
  return { primary: primaryItems.filter(item => allowed(item, access)), groups: secondaryGroups
    .map(group => ({ ...group, items: group.items.filter(item => allowed(item, access)) })).filter(group => group.items.length > 0) };
}
export function secondarySelection(view: Section, groups: NavigationGroup[]) {
  return groups.flatMap(group => group.items).find(item => item.id === view) ?? null;
}
