import type { LocalProgress, OfflineProgressResult } from './types.ts';

export function acknowledgeProgress(current: LocalProgress, sent: LocalProgress, result: OfflineProgressResult): LocalProgress {
  if (current.acknowledged > sent.sequence) return current;
  if (result.status !== 'applied') return { ...current, conflict: result };
  return { ...current, baseStamp: result.stamp, acknowledged: Math.max(current.acknowledged, sent.sequence), pending: current.sequence > sent.sequence };
}
