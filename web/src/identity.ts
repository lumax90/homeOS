/** Cryptographically random command IDs also work on a plain-HTTP LAN origin. */
export function randomId(): string {
  return [...crypto.getRandomValues(new Uint8Array(16))].map(value => value.toString(16).padStart(2, '0')).join('');
}
