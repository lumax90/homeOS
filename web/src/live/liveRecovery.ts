export function terminalLiveStatus(status: number | undefined) { return status !== undefined && status >= 400 && status < 500 && status !== 408 && status !== 429; }
export function liveTarget(start: number, end: number, buffer = 12) { return Number.isFinite(start) && Number.isFinite(end) && end > start ? Math.max(start, end - Math.max(4, Math.min(24, buffer))) : null; }

/** Consecutive outages are bounded. One brief playing event cannot reset the retry budget. */
export class LiveRecoveryPolicy {
  private attempts = 0;
  private stableSince: number | null = null;
  bufferSeconds = 12;
  playing(now: number) { this.stableSince ??= now; }
  stalled() { this.stableSince = null; this.bufferSeconds = Math.min(24, this.bufferSeconds + 4); }
  next(now: number): number | null {
    if (this.stableSince !== null && now - this.stableSince >= 60_000) this.attempts = 0;
    this.stableSince = null;
    return [500, 1500, 3000, 6000][this.attempts++] ?? null;
  }
}

/** Late preparation replies release only their own lease, never a newer channel or reconnect. */
export class LiveAttemptOwner {
  private epoch = 0;
  private stopped = false;
  id: string | null = null;
  private readonly release: (id: string) => void;
  constructor(release: (id: string) => void) { this.release = release; }
  begin() { return ++this.epoch; }
  current(epoch: number) { return !this.stopped && epoch === this.epoch; }
  accept(epoch: number, id: string) {
    if (!this.current(epoch)) { this.release(id); return false; }
    if (this.id && this.id !== id) this.release(this.id);
    this.id = id; return true;
  }
  discard(epoch: number) { if (this.current(epoch)) { ++this.epoch; const id = this.id; this.id = null; if (id) this.release(id); } }
  stop() { if (this.stopped) return; this.stopped = true; ++this.epoch; if (this.id) this.release(this.id); this.id = null; }
}
