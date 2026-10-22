import type { GameCheckpoint } from './model.ts';
export interface GameSavePulse { running: boolean; saveRevision?: number; files?: Record<string, string> }
/** A single bounded writer: coalesce frames, serialize CAS revisions, retain failed data for explicit retry. */
export class GameSaveQueue {
  private revision: number; private pending: Record<string, string> | undefined; private inFlight: Promise<void> | null = null;
  private running = false; private saved: string;
  private latest: Record<string, string>;
  private send: (pulse: GameSavePulse) => Promise<GameCheckpoint>;
  constructor(checkpoint: GameCheckpoint, send: (pulse: GameSavePulse) => Promise<GameCheckpoint>) { this.send = send; this.revision = checkpoint.revision; this.saved = JSON.stringify(checkpoint.files); this.latest = { ...checkpoint.files }; }
  stage(files: Record<string, string>) { this.latest = { ...files }; this.pending = !this.inFlight && JSON.stringify(files) === this.saved ? undefined : { ...files }; }
  get dirty() { return !!this.pending || !!this.inFlight; }
  get hasSavedData() { return this.saved !== '{}'; }
  unloadPulse(): GameSavePulse { return { running: false, saveRevision: this.revision, files: { ...this.latest } }; }
  flush(running: boolean): Promise<void> {
    this.running = running;
    if (this.inFlight) return this.inFlight;
    const run = async () => {
      do {
        const files = this.pending; this.pending = undefined;
        try {
          const result = await this.send({ running: this.running, ...(files ? { files, saveRevision: this.revision } : {}) });
          this.revision = result.revision;
          if (files) this.saved = JSON.stringify(files);
        } catch (error) { this.pending ??= files; throw error; }
      } while (this.pending);
    };
    this.inFlight = run().finally(() => { this.inFlight = null; });
    return this.inFlight;
  }
}
