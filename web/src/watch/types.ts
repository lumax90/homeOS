export interface WatchParticipant { id: string; userId: string; name: string; device: string; role: 'player' | 'controller'; connected: boolean; ready: boolean; state: string; positionSeconds: number; playing: boolean; owner: boolean }
export interface WatchRoom {
  id: string; mode: 'together' | 'remote'; ownerId: string; ownerName: string; mediaId: string; title: string; kind: 'video' | 'audio'; fingerprint: string; matchRevision: number;
  state: 'paused' | 'waiting' | 'playing' | 'closed'; positionSeconds: number; durationSeconds: number; anchorUnixMs: number; serverUnixMs: number;
  rate: number; volume: number; muted: boolean; revision: number; syncRevision: number; epoch: number; guestControl: boolean; canControl: boolean; canManage: boolean; message: string;
  participants: WatchParticipant[]; invitations: { userId: string; name: string; removed: boolean }[];
}
export interface WatchConnection { room: WatchRoom; memberId: string; clientId: string; clientRevision: number; role: 'player' | 'controller' }
export interface WatchSummary { id: string; mode: 'together' | 'remote'; mediaId: string; title: string; kind: 'video' | 'audio'; ownerName: string; device: string; state: string; players: number; owned: boolean; joined: boolean }
export interface WatchCommand {
  action: 'play' | 'pause' | 'seek' | 'rate' | 'volume' | 'media' | 'invite' | 'remove' | 'control' | 'close';
  positionSeconds?: number; rate?: number; volume?: number; muted?: boolean; mediaId?: string; name?: string; userId?: string; enabled?: boolean;
}
export interface WatchCreate { clientId: string; clientRevision: number; playbackId: string; mode: 'together' | 'remote'; positionSeconds: number; playing: boolean; rate: number; volume: number; muted: boolean }
export interface WatchPulse { clientRevision: number; sequence: number; epoch: number; syncRevision: number; playbackId: string | null; positionSeconds: number; ready: boolean; playing: boolean; state: string; revision: number }
export interface WatchCommandRequest extends WatchCommand { memberId: string; clientRevision: number; commandId: string; expectedRevision: number }
export interface WatchTransport {
  create(input: WatchCreate, signal: AbortSignal): Promise<WatchConnection>;
  join(id: string, clientId: string, clientRevision: number, role: 'player' | 'controller', signal: AbortSignal): Promise<WatchConnection>;
  leave(clientId: string, clientRevision: number): Promise<void>;
  snapshot(id: string, member: string, signal: AbortSignal): Promise<WatchRoom>;
  pulse(id: string, member: string, input: WatchPulse, signal: AbortSignal): Promise<WatchRoom>;
  command(id: string, input: WatchCommandRequest, signal: AbortSignal): Promise<{ appliedRevision: number; duplicate: boolean; room: WatchRoom }>;
}
export interface WatchPlayerState { mediaId: string; playbackId: string | null; position: number; playing: boolean; ready: boolean; armed: boolean; buffering: boolean; preparing?: boolean; error: boolean; rate: number; volume: number; muted: boolean }
export interface WatchPlayer {
  read(): WatchPlayerState;
  pause(): void;
  play(): Promise<boolean>;
  seek(position: number): void;
  speed(rate: number): void;
  volume(volume: number, muted: boolean): void;
  restart(position: number): Promise<void>;
  stop(message: string): void;
}
