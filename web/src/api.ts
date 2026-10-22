import { appUrl } from './base';
import { clearLocalPush, syncLocalPush } from './pushBridge';
export { appUrl } from './base';
export { randomId } from './identity';

export interface Session {
  configured: boolean;
  authenticated: boolean;
  preview: boolean;
  displayName: string | null;
  csrfToken: string;
  userId: string | null;
  role: 'admin' | 'member' | 'viewer' | null;
  canDiscover: boolean;
  canAcquire: boolean;
  canRequest: boolean;
  sessionId: string | null;
}

export interface Library {
  id: string;
  name: string;
  path: string;
  kind: 'movies' | 'shows' | 'music' | 'photos' | 'books' | 'audiobooks';
  isDemo: boolean;
  createdAt: string;
  itemCount: number;
  enabled: boolean;
}
export interface LibraryOptions { excludedPaths: string[] | null; scanIntervalMinutes: number; watchChanges: boolean; readLocalMetadata: boolean; autoMatch: boolean }
export interface LibraryConfiguration { libraryId: string; revision: number; options: LibraryOptions; lastScan: string | null; nextScan: string | null }
export interface LocalMetadata { title: string | null; overview: string | null; year: number | null; showTitle: string | null; season: number | null; episode: number | null; tmdbId: number | null; imdbId: string | null; poster: string | null; backdrop: string | null; tags: string[]; source: string; kind: string }

export interface MediaItem {
  id: string;
  libraryId: string;
  libraryName: string;
  title: string;
  kind: 'video' | 'audio';
  relativePath: string;
  mimeType: string;
  sizeBytes: number;
  durationSeconds: number | null;
  width: number | null;
  height: number | null;
  videoCodec: string | null;
  audioCodec: string | null;
  available: boolean;
  addedAt: string;
  modifiedAt: string;
  hasThumbnail: boolean;
  positionSeconds: number;
  isWatched: boolean;
  isDemo: boolean;
  sourceTitle: string;
  content: ContentMatch | null;
  matchRevision: number;
  episodes: MediaEpisodeSegment[];
  moviePart: { editionId: string; name: string; number: number; count: number } | null;
}

export interface CatalogPage { items: MediaItem[]; total: number; offset: number; limit: number }
export interface DiscoveryPersonalPage { items: ContentEntry[]; total: number; offset: number; limit: number }
export interface Stats { libraries: number; items: number; videos: number; audio: number; sizeBytes: number; activeJobs: number }
export interface ScanJob {
  id: string;
  libraryId: string;
  libraryName: string;
  status: 'queued' | 'running' | 'completed' | 'failed' | 'cancelled';
  processed: number;
  added: number;
  attempt: number;
  message: string;
  createdAt: string;
  startedAt: string | null;
  finishedAt: string | null;
  cancelRequested: boolean;
}
export interface SystemInfo {
  version: string;
  framework: string;
  os: string;
  dataPath: string;
  ffmpegAvailable: boolean;
  ffprobeAvailable: boolean;
  playbackMode: 'direct' | 'direct+remux+software-hls';
  metadataSource: string;
  downloadEngine: string;
  demoPath: string | null;
}

let csrfToken = '';
let currentSession: Session | null = null;

export class ApiError extends Error {
  readonly status: number;
  constructor(message: string, status: number) { super(message); this.name = 'ApiError'; this.status = status; }
}

export async function request<T>(path: string, options: RequestInit = {}): Promise<T> {
  const authChange = options.method && options.method !== 'GET' && ['/auth/logout', '/auth/login', '/profiles/switch', '/account/password', '/account/pin'].includes(path);
  if (authChange) window.dispatchEvent(new Event('home-offline-lock'));
  if (authChange) window.dispatchEvent(new Event('home-watch-stop'));
  if (authChange) window.dispatchEvent(new Event('home-receiver-stop'));
  if (authChange) await clearLocalPush(currentSession);
  const headers = new Headers(options.headers);
  if(options.body&&!(options.body instanceof FormData)&&!headers.has('Content-Type'))headers.set('Content-Type','application/json');
  if (options.method && !['GET', 'HEAD'].includes(options.method)) headers.set('X-CSRF-TOKEN', csrfToken);
  if (options.method && !['GET', 'HEAD'].includes(options.method) && currentSession?.authenticated) {
    headers.set('X-Home-User', currentSession.userId ?? ''); headers.set('X-Home-Session', currentSession.sessionId ?? '');
  }
  const response = await fetch(appUrl(`/api${path}`), { ...options, headers, credentials: 'same-origin' });
  if (!response.ok) {
    if (authChange && currentSession) void syncLocalPush(currentSession).catch(() => {});
    if (response.status === 401 && path !== '/auth/login') window.dispatchEvent(new Event('home-session-expired'));
    const data = await response.json().catch(() => null);
    const fallback = response.status === 401 ? 'Oturumun sona erdi. Yeniden giriş yap.'
      : response.status === 429 ? 'Biraz bekleyip tekrar dene.' : 'Sunucuya ulaşılamadı. Yeniden deneyebilirsin.';
    throw new ApiError(data?.message || fallback, response.status);
  }
  return response.status === 204 ? undefined as T : response.json() as Promise<T>;
}

export async function loadSession(): Promise<Session> {
  const session = await request<Session>('/session', { signal: AbortSignal.timeout(6000) });
  csrfToken = session.csrfToken;
  currentSession = session;
  window.dispatchEvent(new CustomEvent('home-session-state', { detail: session }));
  void syncLocalPush(session).catch(() => {});
  return session;
}

export const mutate = <T = void>(path: string, data?: unknown, method = 'POST', keepalive = false) =>
  request<T>(path, { method, body: data === undefined ? undefined : JSON.stringify(data), keepalive });

export function broadcastAuthChange() {
  try { localStorage.setItem('home-auth-change', `${Date.now()}-${Math.random()}`); } catch { /* Storage may be unavailable in private mode. */ }
}
export interface Account {
  id: string; displayName: string; role: 'admin' | 'member' | 'viewer'; enabled: boolean; allLibraries: boolean; libraryIds: string[];
  canDiscover: boolean; canAcquire: boolean; autoApprove: boolean; monthlyRequestLimit: number; createdAt: string; isAdmin: boolean; canRequest: boolean;
  maximumAge: number | null; allowUnrated: boolean; pinEnabled: boolean;
}
export interface LoginSession { id: string; userId: string; device: string; createdAt: string; expiresAt: string; current: boolean }
export interface ContentRequest {
  id: string; contentId: string; requestedBy: string; requesterName: string; status: string; season: number | null; episode: number | null;
  monitorMode: string; qualityProfileId: string; note: string; createdAt: string; updatedAt: string; metadata: ProviderContent;
  scopeEpisodeCount: number | null;
}
export interface NotificationItem { id: string; kind: string; title: string; message: string; contentId: string | null; createdAt: string; readAt: string | null }
export interface RequestEvent { id: string; requestId: string; actorName: string; status: string; note: string; createdAt: string }
export interface AutomationSettings { enabled: boolean; autoGrab: boolean; allowUpgrades: boolean; cutoffResolution: number; searchIntervalMinutes: number; delayMinutes: number; minimumAgeHours: number; maximumDailyGrabs: number; sameQualityUpgrades: boolean }
export interface AutomationRule { contentId: string; title: string; settings: AutomationSettings; revision: number; lastRun: string | null; nextRun: string; lastError: string | null; budgetUserId: string | null; budgetName: string | null }
export interface AutomationJob { id: string; contentId: string; season: number | null; episode: number | null; targetKey: string; ruleRevision: number; status: string; attempts: number; dueAt: string; message: string; releaseId: string | null; fingerprint: string | null; downloadId: string | null; createdAt: string }
export interface BlockedRelease { contentId: string; title: string; fingerprint: string; reason: string; blockedAt: string }
export interface MusicArtist { id: string; name: string; sortName: string | null; country: string | null; disambiguation: string | null; source: string; mbid: string | null }
export interface MusicArtistCredit { mbid: string | null; name: string; creditedName: string; joinPhrase: string; sortName: string | null; artistId: string | null }
export interface MusicAlbum { id: string; artist: string; title: string; date: string | null; releaseGroupMbid: string | null; releaseMbid: string | null; tracks: number; availableTracks: number; source: string; artworkMediaId: string | null; artistCredits?: MusicArtistCredit[] }
export interface MusicTrack { mediaId: string; albumId: string; title: string; artist: string; disc: number; number: number; recordingMbid: string | null; lyrics: string | null; replayGainDb: number | null; bitDepth: number | null; media: MediaItem; artistCredits?: MusicArtistCredit[] }
export interface MusicLibraryTrack { mediaId: string; albumId: string; title: string; artist: string; album: string; disc: number; number: number; favorite: boolean; media: MediaItem }
export interface MusicPage<T> { items: T[]; total: number; offset: number; limit: number }
export interface ProviderAlbum { id: string; title: string; artist: string; date: string | null; type: string; providerUrl: string; secondaryTypes?: string[]; artistCredits?: MusicArtistCredit[] }
export interface MusicReleaseSummary { id: string; groupId: string; title: string; artist: string; date: string | null; country: string | null; status: string | null; formats: string[]; trackCount: number; artistCredits?: MusicArtistCredit[] }
export interface MusicReleaseTrack { id: string; recordingId: string; disc: number; position: number; number: string; title: string; artist: string; lengthMs: number | null; artistCredits?: MusicArtistCredit[] }
export interface LocalMusicArtist { artist: MusicArtist; albums: number; tracks: number }
export interface MusicRelation { type: string; direction: string; targetType: string; id: string; title: string; providerUrl: string; album: ProviderAlbum | null; video: boolean }
export interface MusicRecording { id: string; title: string; video: boolean; artistCredits: MusicArtistCredit[]; relations: MusicRelation[] }
export interface MusicVideoLink { id: string; video: MediaItem; recordingMbid: string | null; videoRecordingMbid: string | null; revision: number; current: boolean }
export interface MusicListReceipt { followed: number; alreadyFollowed: number; monitorsCreated: number; monitorsPreserved: number }
export interface MusicListCandidate { kind: string; mbid: string; name: string; album: ProviderAlbum | null; artist: MusicArtist | null }
export interface MusicListJob { id: string; source: string; sourceId: string; kind: string; name: string; revision: number; status: string; cursor: number; total: number | null; skipped: number; message: string; createdAt: string; expiresAt: string; items: MusicListCandidate[]; receipt: MusicListReceipt | null }
export interface LyricLine { seconds: number | null; text: string; endSeconds: number | null; words: { seconds: number; endSeconds: number | null; text: string }[] | null }
export interface MusicRelease { metadata: MusicReleaseSummary; tracks: MusicReleaseTrack[] }
export interface MusicAssignment { mediaId: string; disc: number; position: number }
export interface MusicMatchPreview { albumId: string; release: MusicRelease; files: { mediaId: string; fileTitle: string; disc: number; position: number; recordingId: string | null; releaseTrackId: string | null; reasons: string[] }[]; unavailableTracks: number; canApply: boolean }
export interface MusicQualityRules { losslessOnly: boolean; codecs: string[] | null; minimumBitrateKbps: number; maximumSizeGiB: number; minimumSeeders: number; completeAlbum: boolean; minimumBitDepth?: number; minimumSampleRate?: number }
export interface MusicSourceResult { id: string; indexerId: string; indexerName: string; title: string; protocol: string; sizeBytes: number | null; seeders: number | null; downloadId: string | null; evaluation: { eligible: boolean; score: number; rejections: string[]; codec: string | null; lossless: boolean } }
export interface Playlist { id: string; name: string; count: number; updatedAt: string }
export interface MusicMonitorSettings { enabled: boolean; mode: string; primaryTypes: string[]; excludedSecondaryTypes: string[]; releaseStatuses: string[]; countries: string[]; formats: string[]; search: boolean; autoGrab: boolean; allowUpgrades: boolean; cutoff: string; cutoffBitrateKbps: number; refreshHours: number; searchIntervalMinutes: number; delayMinutes: number; maximumDailyGrabs: number; quality: MusicQualityRules }
export interface MusicMonitor { id: string; userId: string; kind: string; mbid: string; name: string; settings: MusicMonitorSettings; revision: number; cursor: number; createdAt: string; nextRun: string; lastRun: string | null; lastError: string | null }
export interface MonitoredMusicAlbum { monitorId: string; metadata: ProviderAlbum; wanted: boolean; override: boolean | null; firstSeen: string }
export interface MusicAutomationJob { id: string; monitorId: string; groupId: string; title: string; revision: number; status: string; attempts: number; dueAt: string; message: string; resultId: string | null; downloadId: string | null; release: MusicRelease | null }
export interface MusicPlaybackPreferences { replayGain: 'off' | 'track' | 'album'; preampDb: number; preventClipping: boolean }
export interface MusicPcmInfo { mediaId: string; fingerprint: string; frames: number; sampleRate: number; channels: number; trackGainDb: number | null; albumGainDb: number | null; trackPeak: number | null; albumPeak: number | null; loudnessLufs: number | null; title: string; artist: string; albumId: string | null }
export interface MusicSuggestion { media: MediaItem; artist: string; similarity: number; reason: string }
export interface MusicTask { id: string; mediaId: string; libraryId: string; kind: string; status: string; revision: number; message: string; createdAt: string; updatedAt: string; historyAvailable: boolean }
export interface MusicTagPreview { mediaId: string; fingerprint: string; writable: boolean; currentPath: string; proposedPath: string; current: { title: string; artist: string; album: string }; proposed: { title: string; artist: string; album: string; disc: number; track: number }; reason: string | null; metadataHash: string | null }
export interface SubtitleConnection { configured: boolean; authenticated: boolean; username: string | null; expiresAt: string | null; remaining: number | null; resetAt: string | null; lastError: string | null }
export interface SubtitleProfile { languages: string[]; hearingImpaired: string; forced: string; enabled: boolean; autoDownload: boolean; minimumScore: number; maximumDailyDownloads: number; skipAudioLanguage: boolean; allowUpgrades: boolean; cutoffScore: number; minimumUpgradeDelta: number; searchIntervalHours: number; excludedAudioLanguages: string[] }
export interface SubtitlePolicy { profile: SubtitleProfile; stamp: { globalRevision: number; overrideRevision: number; connectionRevision: number }; inherited: boolean; override: SubtitleProfile | null; upgradesPaused: boolean }
export interface SubtitleVersion { id: string; mediaId: string; language: string; title: string; extension: string; createdAt: string; forced: boolean; hearingImpaired: boolean; origin: string; score: number | null; providerFileId: number | null; retired: boolean; supersedesId: string | null }
export interface SubtitleSyncJob { id: string; mediaId: string; trackId: string; audioStream: number; revision: number; status: string; progress: number; message: string; createdAt: string; updatedAt: string; subtitleId: string | null; result: { offsetSeconds: number; timeScale: number; score: number; alternativeScore: number; voiceSeconds: number; cues: number; canApply: boolean; reason: string } | null }
export interface SubtitleCandidate { id: string; mediaId: string; fileId: number; language: string; name: string; release: string; forced: boolean; hearingImpaired: boolean; hashMatched: boolean; trusted: boolean; eligible: boolean; score: number; reasons: string[]; providerUrl: string | null }
export interface SubtitleWanted { mediaId: string; title: string; language: string; status: string; message: string; nextRun: string }
export interface UsenetServer { id: string; name: string; host: string; port: number; security: string; username: string; configured: boolean; connections: number; priority: number; enabled: boolean; revision: number; lastTest: string | null; lastError: string | null; retryAt: string | null; downloadedBytes: number }
export interface PersonalContent { favorite: boolean; rating: number | null }
export interface ContentCollection { id: string; name: string; shared: boolean; owned: boolean; count: number; updatedAt: string }
export interface ContentPresentation { title: string | null; overview: string | null; minimumAge: number | null; blocked: boolean; tags: string[] }

export const thumbnail = (item: MediaItem) => appUrl(`/api/media/${item.id}/thumbnail?v=${encodeURIComponent(item.modifiedAt)}`);
export const artwork = (item: MediaItem, type: 'poster' | 'backdrop') =>
  appUrl(`/api/media/${item.id}/artwork/${type}?v=${encodeURIComponent(item.content?.matchedAt ?? '')}`);

export interface ProviderContent {
  provider: 'tmdb' | 'tvmaze'; providerId: number; kind: 'movie' | 'show'; title: string;
  originalTitle: string; overview: string; releaseDate: string | null; posterUrl: string | null;
  backdropUrl: string | null; providerUrl: string; genres: string[];
  imdbId?: string | null; tvdbId?: number | null; rating?: number | null; runtimeMinutes?: number | null;
  minimumAge?: number | null; ageLabel?: string | null;
  aliases?: CatalogAlias[];
}
export interface ContentMatch extends Omit<ProviderContent, 'provider'> {
  id: string; provider: string; seasonNumber: number | null; episodeNumber: number | null;
  episodeTitle: string | null; episodeOverview: string | null; stillUrl: string | null;
  edition: string; matchedAt: string; versionCount: number;
}
export interface MetadataStatus { tmdbConfigured: boolean; language: string; providers: string[] }
export interface FilenameHint { query: string; year: number | null; seasonNumber: number | null; episodeNumber: number | null; episodeNumbers: number[]; airDate: string | null; absoluteNumber: number | null; endAbsoluteNumber: number | null; partNumber: number | null; ambiguous: boolean }
export interface Download {
  id: string; infoHash: string; name: string; sourceKind: string;
  desiredState: 'running' | 'paused' | 'cancelled'; status: string; progress: number;
  totalBytes: number; downloadSpeed: number; uploadSpeed: number; peers: number; message: string;
  importedLibraryId: string | null; scanJobId: string | null; importedAt: string | null;
  createdAt: string; updatedAt: string; revision: number;
  acquisition: { contentId: string; title: string; status: string; message: string } | null;
  musicAcquisition: { releaseId: string; title: string; status: string; message: string } | null;
  priority: number; uploadedBytes: number; receivedBytes: number; seedSeconds: number; archived: boolean; payloadCleanedAt: string | null;
  ownership: { userId: string | null; displayName: string | null; origin: string; generation: number; chargedBytes: number; waitReason: string | null; retryAt: string | null };
}
export interface DownloadBudgetPolicy { enabled: boolean; monthlyAcquisitions: number | null; monthlyPayloadBytes: number | null; maximumPayloadBytes: number | null; maximumConcurrent: number; revision: number }
export interface DownloadBudgetUsage { userId: string; displayName: string; policy: DownloadBudgetPolicy; period: string; resetsAt: string; acquisitions: number; committedBytes: number; reservedBytes: number; receivedBytes: number; uploadedBytes: number; activeTransfers: number; waitingTransfers: number }
export interface DownloadBudgetHistory { items: { id: string; downloadId: string | null; title: string; action: string; bytes: number; period: string; createdAt: string }[]; total: number; offset: number; limit: number }
export interface TransferPolicy { priority: number; downloadKiB: number; uploadKiB: number; seedRatio: number | null; seedMinutes: number | null; seedStop: 'either' | 'both'; files: string[] | null; filePriorities: Record<string, number> | null }
export interface SourceSeedRules { minimumRatio: number; minimumMinutes: number; downloadFactor: number; uploadFactor: number; seedType: string }
export interface TransferFile { path: string; length: number; selected: boolean; priority: number }
export interface CustomFormat { name: string; score: number; pattern: string | null; codecs: string[] | null; sources: string[] | null; protocols: string[] | null; hdr: string | null; required: boolean; negate: boolean }
export interface DiscoveryPage { items: ProviderContent[]; provider: string; page: number; totalPages: number | null; hasMore: boolean; nextPage?: number | null }
export interface DiscoveryGenre { id: number; name: string }
export interface ContentEntry {
  id: string; metadata: ProviderContent; monitorMode: string; qualityProfileId: string;
  requestedAt: string | null; lastSyncAt: string | null; lastError: string | null;
  fileCount: number; episodeCount: number; missingCount: number; requested: boolean; syncState: string; requestStatus: string | null;
  catalogRevision: number;
}
export interface CatalogAlias { provider: string; kind: string; providerId: number; title: string | null; originalTitle: string | null }
export interface CatalogIdentity { content: ContentEntry; aliases: CatalogAlias[]; names: string[]; localNames: string[] }
export interface CatalogMergePreview { targetId: string; sourceId: string | null; targetRevision: number; sourceRevision: number | null; target: ProviderContent; source: ProviderContent; evidence: string[]; conflicts: string[]; files: number; requests: number; canApply: boolean; episodes: { id: string; season: number; number: number; title: string; airDate: string | null; targetId: string | null; automatic: boolean; targets: { sourceEpisodeId: string; targetEpisodeId: string; startSeconds: number | null; endSeconds: number | null }[] }[] }
export interface MediaEpisodeSegment { episodeId: string; season: number; number: number; title: string; position: number; startSeconds: number | null; endSeconds: number | null }
export interface EpisodeNumbering { mode: string; source: string; revision: number; entries: { number: number; season: number; episode: number; providerId?: number | null; sourceSeason?: number | null; sourceEpisode?: number | null; episodeId?: string | null }[]; orderId: string | null; valid: boolean }
export interface ProviderEpisode { providerId: number; seasonNumber: number; episodeNumber: number; title: string; overview: string; airDate: string | null; stillUrl: string | null; runtimeMinutes: number | null }
export interface EpisodeCatalogRow { id: string; metadata: ProviderEpisode; retired: boolean }
export interface EpisodeCatalogPage { items: EpisodeCatalogRow[]; total: number; offset: number; limit: number }
export interface EpisodeRefreshPreview { planId: string; contentId: string; catalogRevision: number; expiresAt: string; changes: { incoming: ProviderEpisode; existingId: string | null; previousSeason: number | null; previousEpisode: number | null; action: string; needsChoice: boolean }[]; retired: EpisodeCatalogRow[]; conflicts: string[]; notes: string[]; canApply: boolean }
export interface CatalogEpisodeOrder { id: string; contentId: string; name: string; kind: string; source: string; revision: number; items: { season: number; episode: number; episodeIds: string[]; coordinates: { season: number; episode: number }[] }[] }
export interface ProviderOrderDetails { id: string; name: string; type: number; fingerprint: string; groups: { id: string; name: string; position: number; episodes: ProviderEpisode[] }[] }
export interface EpisodeBoundaryJob { id: string; mediaId: string; fingerprint: string; matchRevision: number; revision: number; status: string; progress: number; message: string; createdAt: string; result: { segments: { episodeId: string; startSeconds: number; endSeconds: number }[]; method: string; evidence: string[]; warnings: string[]; canApply: boolean } | null }
export interface LocatedEpisodes { contentId: string; numberingSource: string; provider: string | null; providerId: number | null; episodes: { providerId: number; seasonNumber: number; episodeNumber: number; title: string; airDate: string | null }[] }
export interface MovieEdition { id: string; contentId: string; name: string; revision: number; partCount: number; files: MediaItem[]; complete: boolean }
export interface VideoSequence { items: MediaItem[]; index: number; editionId: string | null }
export interface EpisodeEntry { id: string; contentId: string; contentTitle: string; seasonNumber: number; episodeNumber: number; title: string; airDate: string | null; state: string; fileCount: number }
export interface EpisodePage { items: EpisodeEntry[]; total: number; offset: number; limit: number; seasons: number[] }
export interface QualityRules { resolutions: number[]; maxSizeGiB: number; minSeeders: number; requiredTerms: string[]; forbiddenTerms: string[]; preferredTerms: string[]; allowUnknownResolution: boolean; minSizeGiB: number; languages: string[] | null; sources: string[] | null; customFormats: CustomFormat[] | null; minimumCustomScore: number; protocols: string[] | null; preferredProtocol: string; maximumAgeDays: number }
export interface QualityProfile { id: string; name: string; rules: QualityRules }
export interface IndexerInfo {
  id: string; name: string; endpoint: string; protocol: string; configured: boolean; enabled: boolean;
  revision: number; providerId: string | null;
  priority: number; categories: string; allowedHosts: string;
  capabilities: { maxPageSize: number; searches: Record<string,string[]>; categories: Record<string,string> } | null;
  lastCheckedAt: string | null; lastSuccessAt: string | null; lastError: string | null; retryAfter: string | null;
}
export interface ReleaseResult {
  id: string; indexerId: string; indexerName: string; title: string; protocol: string; sizeBytes: number | null; seeders: number | null;
  publishedAt: string | null; downloadId: string | null; fingerprint: string;
  evaluation: { eligible: boolean; score: number; qualityScore: number; customScore: number; rejections: string[]; notes: string[]; parsed: { resolution: number | null; source: string; codec: string | null; season: number | null; episode: number | null; seasonPack: boolean; proper: boolean; hdr: string | null; properVersion: number } };
}
export interface ReleaseSearchResult { items: ReleaseResult[]; sources: { indexerId: string; name: string; results: number; error: string | null; moreAvailable: boolean }[] }
export interface ReleaseTarget { entry: ContentEntry; season?: number; episode?: number }

export interface MediaStreamInfo {
  index: number; kind: 'video' | 'audio' | 'subtitle'; codec: string; language: string; title: string;
  isDefault: boolean; forced: boolean; hearingImpaired: boolean; width: number | null; height: number | null;
  channels: number | null; channelLayout: string | null; sampleRate: number | null; bitRate: number | null;
  profile: string | null; pixelFormat: string | null; colorTransfer: string | null; colorPrimaries: string | null;
  dolbyVision: boolean;
}
export interface SubtitleTrack { id: string; language: string; title: string; format: string; origin: string; forced: boolean; hearingImpaired: boolean; playable: boolean; burnable: boolean; image: boolean; score: number | null }
export interface MediaTracks {
  mediaId: string; fingerprint: string; durationSeconds: number | null; container: string | null; bitRate: number | null;
  streams: MediaStreamInfo[]; chapters: { index: number; startSeconds: number; endSeconds: number; title: string }[]; subtitles: SubtitleTrack[];
}
export interface PlaybackSession {
  id: string | null; mediaId: string; mode: string; url: string; startSeconds: number; durationSeconds: number;
  reasons: string[]; audioStream: number | null; state: string; error: string | null; requestedStartSeconds: number;
  encoder: string;
  subtitleId: string | null; subtitleBurned: boolean;
}

export interface PlaybackLimits { maximumStreams: number; maximumStreamsPerDevice: number; revision: number }
export interface UserPlaybackLimits { maximumStreams: number; maximumDevices: number; revision: number }
export interface PlaybackDevice { id: string; name: string; maximumStreams: number | null; revision: number; createdAt: string; lastSeenAt: string; current: boolean }
export interface PlaybackActivity { id: string; userId: string; displayName: string; deviceId: string; deviceName: string; mediaId: string; title: string; mode: string; status: string; startedAt: string; lastSeenAt: string; paused: boolean; positionSeconds: number; endedAt: string | null; endReason: string | null; currentDevice: boolean }
export interface PlaybackUsage { server: PlaybackLimits; user: UserPlaybackLimits; deviceMaximumStreams: number; active: PlaybackActivity[]; history: PlaybackActivity[] }
export interface NotificationChannel { id: string; userId: string; name: string; kind: 'webhook' | 'smtp' | 'push'; enabled: boolean; events: string[]; maximumHourlyAttempts: number; revision: number; target: string; secretConfigured: boolean; createdAt: string; dropped: number }
export interface NotificationDelivery { id: string; channelId: string; channelName: string; userId: string; kind: string; title: string; message: string; createdAt: string; isTest: boolean; status: string; attempts: number; revision: number; nextAttemptAt: string; finishedAt: string | null; result: string }
export interface NotificationAttempt { number: number; startedAt: string; finishedAt: string | null; result: string }
export interface PushServerConfiguration { enabled: boolean; subject: string; publicKey: string | null; revision: number; keyRevision: number }
export interface BrowserPushRegistration { channelId: string; userId: string; sessionId: string; bindingId: string; name: string; events: string[]; revision: number; keyRevision: number; expiresAt: string }
export interface BrowserPushDevice { channelId: string; name: string; deviceName: string; revision: number; events: string[]; current: boolean }
export interface BrowserPushState { userId: string; sessionId: string; configuration: PushServerConfiguration; current: BrowserPushRegistration | null; devices: BrowserPushDevice[] }

export function bytes(value: number): string {
  if (!value) return '0 B';
  const index = Math.min(Math.floor(Math.log(value) / Math.log(1024)), 4);
  return `${new Intl.NumberFormat('tr-TR', { maximumFractionDigits: index ? 1 : 0 }).format(value / 1024 ** index)} ${['B', 'KB', 'MB', 'GB', 'TB'][index]}`;
}

export function duration(value: number | null): string {
  const turkish = typeof document !== 'undefined' && document.documentElement?.lang?.startsWith('tr');
  if (value === null) return turkish ? 'Süre bilinmiyor' : 'Unknown duration';
  if (value < 60) return `${Math.round(value)} ${turkish ? 'sn' : 's'}`;
  if (value < 3600) return `${Math.floor(value / 60)} ${turkish ? 'dk' : 'min'}`;
  return `${Math.floor(value / 3600)} ${turkish ? 'sa' : 'h'} ${Math.floor(value % 3600 / 60)} ${turkish ? 'dk' : 'min'}`;
}

export function clock(value: number): string {
  if (!Number.isFinite(value)) return '0:00';
  const seconds = Math.floor(value);
  const parts = [Math.floor(seconds / 60) % 60, seconds % 60].map(n => String(n).padStart(2, '0'));
  return seconds >= 3600 ? `${Math.floor(seconds / 3600)}:${parts.join(':')}` : `${Math.floor(seconds / 60)}:${parts[1]}`;
}

export function resolution(item: MediaItem): string {
  if (!item.height) return item.kind === 'audio' ? (item.audioCodec?.toUpperCase() ?? 'SES') : 'VİDEO';
  return item.height >= 2160 ? '4K' : item.height >= 1080 ? '1080p' : item.height >= 720 ? '720p' : `${item.height}p`;
}

export function date(value: string): string {
  return new Intl.DateTimeFormat('tr-TR', { day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' }).format(new Date(value));
}
