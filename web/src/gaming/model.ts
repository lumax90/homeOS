export interface GameState { gameId: string; inLibrary: boolean; favorite: boolean; lastPlayedAt: string | null; playedSeconds: number; saveVersion: string | null; saveRevision: number; hasSave: boolean }
export interface RomMetadataCandidate{id:number;name:string;platform:string;publisher:string;description:string|null;year:string|null;regions:string[];languages:string[];hashMatch:boolean;edition?:string|null;detailsPartial?:boolean}
export interface RomMetadataDocument{format:number;revision:number;romSha256:string;game:RomMetadataCandidate;matchMethod:string;fetchedAt:string;provider:string}
export interface Game { id: string; title: string; publisher: string; sourceUrl: string; sourceRepository: string; sourceCommit: string; version: string; engine: string; license: string; kind: 'web' | 'native'; aspect: '1/1' | '16/9'; genres: string[]; sizeBytes: number; state: GameState; installed?: boolean; inStore?: boolean; runtime?: 'godot' | 'html' | 'native'; installJobId?: string | null; installStatus?: string | null; installProgress?: number; installMessage?: string | null; playable?: boolean; system?: string | null; blockReason?: string | null; romSha256?: string | null;hasArtwork?:boolean;category?:string|null;importedAt?:string|null;artworkStatus?:string|null;coreId?:string|null;coreName?:string|null;systemLabel?:string|null;metadata?:RomMetadataDocument|null }
export interface GameCheckpoint { revision: number; files: Record<string, string>; version: string | null }
export interface GameLaunch { id: string; url: string; version: string; expiresAt: string; checkpoint: GameCheckpoint }
export type GameFilter = 'all' | 'library' | 'favorites';
export function filterGames(games: Game[], search: string, filter: GameFilter): Game[] {
  const key = (value: string) => value.normalize('NFKD').replace(/\p{M}/gu, '').toLowerCase();
  const query = key(search.trim());
  return games.filter(game => (!query || key(game.title + ' ' + game.publisher).includes(query)) && (filter !== 'library' || game.state.inLibrary) && (filter !== 'favorites' || game.state.favorite));
}
export const hasGameCheckpoint = (game: Game) => game.playable !== false && game.state.hasSave && game.state.saveVersion === game.version;
export interface GameMessage { protocol: 'home-game-v1'; nonce: string; type: string; files?: Record<string, string>; progress?: number; code?: string }
export function readGameMessage(data: unknown, nonce: string): GameMessage | null {
  if (!data || typeof data !== 'object') return null;
  const message = data as GameMessage;
  if (message.protocol !== 'home-game-v1' || message.nonce !== nonce || !['hello', 'ready', 'loading', 'save', 'paused', 'menu', 'flushed', 'error', 'exit'].includes(message.type)) return null;
  if (message.type === 'loading' && (typeof message.progress !== 'number' || !Number.isFinite(message.progress) || message.progress < 0 || message.progress > 1)) return null;
  if (message.type === 'save') {
    if (!message.files || typeof message.files !== 'object' || Array.isArray(message.files) || Object.keys(message.files).length > 8) return null;
    if (Object.entries(message.files).some(([key, value]) => !/^(?:home-progress\.json|web-profile\.json|save\/[0-5]\/box\.save)$/.test(key) || typeof value !== 'string' || value.length > 32768)) return null;
    if (new TextEncoder().encode(JSON.stringify(message.files)).length > 48 * 1024) return null;
  }
  return message;
}
/** Fresh neutral input before resuming gameplay: the menu-confirm button must not become a jump. */
export function gamepadsNeutral(pads: ArrayLike<Gamepad | null>): boolean {
  return Array.from(pads).filter((pad): pad is Gamepad => !!pad?.connected).every(pad => pad.buttons.every(button => !button.pressed && button.value < .15) && pad.axes.every(axis => Math.abs(axis) < .25));
}
