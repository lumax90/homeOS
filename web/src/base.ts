export const basePath = document.querySelector<HTMLMetaElement>('meta[name="home-base"]')?.content ?? '';
export function appUrl(path: string) {
  if (!path.startsWith('/') || path.startsWith('//')) return path;
  return basePath && (path === basePath || path.startsWith(`${basePath}/`)) ? path : `${basePath}${path}`;
}
