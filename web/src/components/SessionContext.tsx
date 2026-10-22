import { createContext, useContext } from 'react';
import type { Session } from '../api';

export const SessionContext = createContext<Session | null>(null);
export function useSession() {
  const session = useContext(SessionContext);
  if (!session) throw new Error('Oturum bağlamı bulunamadı.');
  return session;
}
