import type { ControllerAction } from './model.ts';
export interface ControllerPlaybackPort { read(): { position: number; duration: number; ready: boolean; pending: boolean }; play(): Promise<void>; pause(): void; seek(seconds: number): void }
export function controllerPlayback(action: ControllerAction, port: ControllerPlaybackPort, paused: boolean, canControl: boolean, onError: (error: unknown) => void): boolean {
  if (!['playpause', 'seekback', 'seekforward'].includes(action)) return false;
  const state = port.read();
  if (!canControl || !state.ready || state.pending) return true;
  if (action === 'playpause') { if (paused) void port.play().catch(onError); else port.pause(); }
  else if (Number.isFinite(state.position) && Number.isFinite(state.duration) && state.duration > 0) port.seek(Math.max(0, Math.min(state.duration, state.position + (action === 'seekback' ? -10 : 10))));
  return true;
}
