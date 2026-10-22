import { request, type Session } from '../api';
import type { WatchTransport } from './types.ts';

export function watchTransport(session: Session): WatchTransport {
  const identity = { userId: session.userId, sessionId: session.sessionId };
  const send = <T>(path: string, data: unknown, signal?: AbortSignal, keepalive = false): Promise<T> => request<T>('/watch' + path, {
    method: 'POST', body: JSON.stringify({ ...data as object, identity }), signal: signal ? AbortSignal.any([signal, AbortSignal.timeout(6000)]) : AbortSignal.timeout(6000), keepalive,
  });
  return {
    create: (input, signal) => send('/rooms', input, signal),
    join: (room, clientId, clientRevision, role, signal) => send(`/rooms/${room}/join`, { clientId, clientRevision, role }, signal),
    leave: (clientId, clientRevision) => send(`/clients/${clientId}/leave`, { clientRevision }, undefined, true),
    pulse: (room, member, input, signal) => send(`/rooms/${room}/members/${member}/heartbeat`, input, signal),
    snapshot: (room, member, signal) => request(`/watch/rooms/${room}?member=${encodeURIComponent(member)}`, { signal: AbortSignal.any([signal, AbortSignal.timeout(6000)]) }),
    command: (room, input, signal) => send(`/rooms/${room}/commands`, input, signal),
  };
}
