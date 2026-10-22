/** The upstream reader uses its WHEP URL as a URL-constructor base, not only as a fetch target. */
export function nativeWhepUrl(path: string, pageHref: string): string {
  const page = new URL(pageHref);
  const endpoint = new URL(path, page);
  if (!['http:', 'https:'].includes(endpoint.protocol) || endpoint.origin !== page.origin || endpoint.username || endpoint.password || endpoint.search || endpoint.hash || !/\/game-native\/[a-f0-9]{32}\/whep$/.test(endpoint.pathname)) {
    throw new Error('Yerel yayın adresi geçersiz.');
  }
  return endpoint.href;
}
