import { randomId } from './identity';

// A document owns one visible player across direct, HLS and PCM component changes.
// Separate tabs get separate command identities; the server supplies the device identity.
let command = 0;
let progress = 0;
export const browserPlayback = {
  clientId: randomId(),
  nextRevision: () => ++command,
  nextProgress: () => ++progress,
};
