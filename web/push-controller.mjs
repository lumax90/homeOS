// Pure push/account state machine. Storage and browser effects are supplied by the service worker.
export function createPushController({ load, save, fetchState, show, closeExcept, open, now = Date.now }) {
  let generation = 0;
  let tail = Promise.resolve();
  const serial = operation => { const result = tail.then(operation); tail = result.catch(() => {}); return result; };
  const id = value => typeof value === 'string' && /^[a-f0-9]{32}$/.test(value);
  const valid = value => value && id(value.bindingId) && id(value.channelId) && id(value.sessionId) && typeof value.userId === 'string'
    && Number.isFinite(Date.parse(value.expiresAt)) && Date.parse(value.expiresAt) > now();
  async function replace(state, binding) {
    const next = valid(binding) ? binding : null;
    if (state.binding?.bindingId === next?.bindingId) { await closeExcept(next?.bindingId ?? null); return state; }
    const result = { binding: next, seen: [] };
    await save(result); await closeExcept(next?.bindingId ?? null); return result;
  }
  return {
    async sync(expected) {
      const epoch = ++generation;
      const remote = await fetchState();
      return serial(async () => {
        if (epoch !== generation || expected && (expected.userId !== remote.userId || expected.sessionId !== remote.sessionId)) return null;
        const state = await load(); if (epoch !== generation) return null;
        return (await replace(state, remote.current)).binding;
      });
    },
    clear(expected) {
      ++generation;
      return serial(async () => {
        const state = await load();
        if (expected && (state.binding?.userId !== expected.userId || state.binding?.sessionId !== expected.sessionId)) return;
        await replace(state, null);
      });
    },
    async push(message) {
      if (!message || message.v !== 1 || !id(message.bindingId) || !id(message.deliveryId) || !Number.isSafeInteger(message.expiresAt)
        || message.expiresAt < now() || message.expiresAt > now() + 300000 || typeof message.test !== 'boolean') return false;
      const epoch = generation; let remote;
      try { remote = await fetchState(); } catch { /* A short-lived generic notification can be delivered while Home is off the current network. */ }
      return serial(async () => {
        if (epoch !== generation) return false;
        let state = await load(); if (epoch !== generation) return false;
        if (remote !== undefined) state = await replace(state, remote.current);
        if (!valid(state.binding) || state.binding.bindingId !== message.bindingId || message.expiresAt < now() || state.seen.includes(message.deliveryId)) return false;
        await show({ bindingId: message.bindingId, deliveryId: message.deliveryId, test: message.test });
        await save({ binding: state.binding, seen: [...state.seen, message.deliveryId].slice(-128) }); return true;
      });
    },
    async click(data) {
      const epoch = generation; let remote;
      try { remote = await fetchState(); } catch { }
      return serial(async () => {
        if (epoch !== generation) return false;
        let state = await load();
        if (remote !== undefined) state = await replace(state, remote.current);
        if (data?.homePush !== true || !valid(state.binding) || state.binding.bindingId !== data.bindingId) return false;
        await open(); return true;
      });
    },
  };
}
