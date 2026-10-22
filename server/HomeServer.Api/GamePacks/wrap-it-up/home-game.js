/* Home-owned transport for reviewed browser games. No cookies, APIs or persistent storage in this frame. */
(() => {
  'use strict';
  const canvas = document.getElementById('canvas');
  const nonce = location.hash.slice(1);
  let started = false, parentOrigin = '', lastFlush = 0;
  const home = window.homeGame = { initialFiles: {}, paused: true, engineReady: false, startError: null, phase: 'waiting', saveSequence: 0, savedSequence: -1,
    save(payload) { send('save', { files: JSON.parse(payload) }); } };
  function send(type, data = {}) { if (parentOrigin) parent.postMessage({ protocol: 'home-game-v1', nonce, type, ...data }, parentOrigin); }
  function paused(value) { home.paused = value; if (value) send('paused'); }
  async function start() {
    if (started) return;
    started = true;
    home.phase = 'loading';
    if (!Engine.isWebGLAvailable(1)) { send('error', { code: 'webgl-unavailable' }); return; }
    const engine = new Engine({ executable: 'index', mainPack: 'index.pck', canvas, canvasResizePolicy: 2,
      persistentPaths: [], serviceWorker: '', focusCanvas: true, locale: 'en', onExit: () => send('exit'),
      onProgress: (current, total) => send('loading', { progress: total > 0 ? Math.min(1, current / total) : 0 }) });
    home.engine = engine;
    try { await engine.startGame(); home.phase = 'started'; send('ready'); canvas.focus(); }
    catch (error) {
      // Keep the actual startup cause available for focused diagnostics without exporting ticket URLs or stacks.
      home.startError = { name: String(error?.name || 'Error').slice(0, 80), message: String(error?.message || error).replace(/https?:\/\/[^\s'"<>]+/g, '[game resource]').slice(0, 500), phase: home.phase };
      send('error', { code: 'engine-start-failed' });
    }
  }
  window.addEventListener('message', event => {
    const message = event.data;
    if (event.source !== parent || !message || message.protocol !== 'home-game-v1' || message.nonce !== nonce) return;
    if (!parentOrigin) {
      if (message.type !== 'init' || !/^https?:\/\//.test(event.origin)) return;
      parentOrigin = event.origin;
    }
    if (event.origin !== parentOrigin) return;
    if (message.type === 'init') { home.initialFiles = message.files || {}; void start(); }
    if (message.type === 'pause') { paused(true); home.saveSequence++; }
    if (message.type === 'resume') { home.paused = false; canvas.focus(); }
    if (message.type === 'flush') { home.saveSequence++; lastFlush = home.saveSequence; }
  });
  document.addEventListener('visibilitychange', () => { if (document.hidden) paused(true); });
  window.addEventListener('blur', () => paused(true));
  window.addEventListener('keydown', event => {
    if (event.key === 'Escape') { event.preventDefault(); event.stopImmediatePropagation(); paused(true); send('menu'); }
  }, true);
  setInterval(() => { if (lastFlush && home.savedSequence === lastFlush) { lastFlush = 0; send('flushed'); } }, 100);
  parent.postMessage({ protocol: 'home-game-v1', nonce, type: 'hello' }, '*');
})();
