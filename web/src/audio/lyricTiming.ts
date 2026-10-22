import type { LyricLine } from '../api.ts';

export function lyricIndex(lines: readonly LyricLine[], seconds: number): number {
  if (!Number.isFinite(seconds) || seconds < 0) return -1;
  let selected = -1;
  for (let index = 0; index < lines.length; index++) {
    const start = lines[index].seconds;
    if (start !== null && start <= seconds && (selected < 0 || start > (lines[selected].seconds ?? -1))) selected = index;
  }
  return selected;
}
export function lyricWordActive(start: number, end: number | null, seconds: number): boolean {
  return Number.isFinite(seconds) && seconds >= start && (end === null || seconds < end);
}
