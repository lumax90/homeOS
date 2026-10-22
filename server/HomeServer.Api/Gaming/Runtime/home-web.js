/* Home-owned profile/input/scheduling adapter. Original games remain separate, opaque-origin programs. */
(() => {
  'use strict';
  const nonce = location.hash.slice(1), raf = window.requestAnimationFrame.bind(window), caf = window.cancelAnimationFrame.bind(window);
  const timeout = window.setTimeout.bind(window), clear = window.clearTimeout.bind(window), now = performance.now.bind(performance), epoch = Date.now.bind(Date);
  let parentOrigin = '', paused = true, initialized = false, recipe = null, pausedAt = now(), pausedTotal = 0, nextTask = 1, lastSave = '';
  const tasks = new Map(), values = Object.create(null), cookies = Object.create(null);
  const home = window.homeGame = { phase: 'waiting', engineReady: false, startError: null };
  const gameNow = () => (paused ? pausedAt : now()) - pausedTotal;
  const send = (type, payload = {}) => { if (parentOrigin) parent.postMessage({ protocol: 'home-game-v1', nonce, type, ...payload }, parentOrigin); };
  Date.now = () => epoch() - (pausedTotal + (paused ? now() - pausedAt : 0));
  function schedule(callback, delay, repeat, args) {
    const id = nextTask++, task = { native: 0, due: gameNow() + Math.max(0, Number(delay) || 0), cancelled: false }; tasks.set(id, task);
    const run = () => { if (task.cancelled) return; const remaining = task.due - gameNow(); if (paused || remaining > 1) { task.native = timeout(run, paused ? 80 : Math.min(remaining, 1000)); return; }
      if (!repeat) tasks.delete(id); if (typeof callback === 'function') callback(...args); else (0, eval)(String(callback));
      if (repeat && !task.cancelled) { task.due = gameNow() + Math.max(1, Number(delay) || 0); task.native = timeout(run, Math.max(1, Number(delay) || 0)); } };
    task.native = timeout(run, Math.max(0, Number(delay) || 0)); return id;
  }
  window.setTimeout = (callback, delay, ...args) => schedule(callback, delay, false, args);
  window.setInterval = (callback, delay, ...args) => schedule(callback, delay, true, args);
  window.clearTimeout = window.clearInterval = id => { const task = tasks.get(id); if (task) { task.cancelled = true; clear(task.native); tasks.delete(id); } };
  const frames = new Map();
  window.requestAnimationFrame = callback => { const id = nextTask++; const task = { native: 0, cancelled: false }; frames.set(id, task); const run = () => { if (task.cancelled) return; if (paused) { task.native = raf(run); return; } frames.delete(id); callback(gameNow()); }; task.native = raf(run); return id; };
  window.cancelAnimationFrame = id => { const task = frames.get(id); if (task) { task.cancelled = true; caf(task.native); frames.delete(id); } };
  function setPaused(value) { if (value === paused) return; if (value) pausedAt = now(); else pausedTotal += now() - pausedAt; paused = value; document.documentElement.classList.toggle('home-game-paused', paused); if (paused) save(); }
  const storage = { getItem: key => Object.hasOwn(values, String(key)) ? values[String(key)] : null, setItem: (key, value) => { values[String(key)] = String(value); }, removeItem: key => { delete values[String(key)]; }, clear: () => { for (const key of Object.keys(values)) delete values[key]; }, key: index => Object.keys(values)[index] ?? null, get length() { return Object.keys(values).length; } };
  Object.defineProperty(window, 'localStorage', { value: new Proxy(storage, { get: (target, key) => key in target ? Reflect.get(target, key) : Object.hasOwn(values, String(key)) ? values[String(key)] : undefined, set: (target, key, value) => { target.setItem(key, value); return true; } }), configurable: false });
  Object.defineProperty(document, 'cookie', { get: () => Object.entries(cookies).map(([key, value]) => key + '=' + value).join('; '), set: text => { const pair = String(text).split(';', 1)[0], separator = pair.indexOf('='); if (separator > 0) cookies[pair.slice(0, separator)] = pair.slice(separator + 1); }, configurable: false });
  function save() { const text = JSON.stringify({ storage: values, cookies }); if (new TextEncoder().encode(text).length > 32768) { send('error', { code: 'save-too-large' }); return; } if (text !== lastSave) { lastSave = text; send('save', { files: { 'web-profile.json': text } }); } }
  function visible(element) { const style = getComputedStyle(element); return element.isConnected && element.getClientRects().length && style.visibility !== 'hidden' && style.display !== 'none' && !element.closest('[hidden]') && !element.classList.contains('disabled') && !element.classList.contains('locked'); }
  function candidates() { return [...document.querySelectorAll('button,a.restart-button,a.retry-button,a.keep-playing-button,[data-action],.button,.headerButton,#start,#s-controlType,#s-quality,#s-hud,#pauseBtn,#restartBtn,#restart,#startBtn,#openSideBar,#board .tile')].filter(visible); }
  function move(direction) { const list = candidates(); if (!list.length) return; let active = document.activeElement; if (!list.includes(active)) { list[0].tabIndex = 0; list[0].focus(); return; }
    const origin = active.getBoundingClientRect(), ox = origin.left + origin.width / 2, oy = origin.top + origin.height / 2; let winner = null, score = Infinity;
    for (const item of list) { if (item === active) continue; const box = item.getBoundingClientRect(), dx = box.left + box.width / 2 - ox, dy = box.top + box.height / 2 - oy; const primary = direction === 'left' ? -dx : direction === 'right' ? dx : direction === 'up' ? -dy : dy; if (primary <= 1) continue; const cross = direction === 'left' || direction === 'right' ? Math.abs(dy) : Math.abs(dx), weight = primary + cross * 3; if (weight < score) { score = weight; winner = item; } }
    if (winner) { winner.tabIndex = 0; winner.focus(); winner.scrollIntoView({ block: 'nearest', inline: 'nearest' }); }
  }
  function key(name) { const code = { ArrowUp: 38, ArrowDown: 40, ArrowLeft: 37, ArrowRight: 39, Enter: 13, ' ': 32 }[name]; for (const type of ['keydown', 'keyup']) { const event = new KeyboardEvent(type, { key: name, code: name, bubbles: true, cancelable: true }); Object.defineProperties(event, { keyCode: { value: code }, which: { value: code } }); (document.activeElement || document.body).dispatchEvent(event); } }
  function act() { const active = document.activeElement, target = candidates().includes(active) ? active : candidates()[0]; if (!target) { key('Enter'); return; }
    if (recipe.adapter === 'oh-hi' || recipe.adapter === 'hextris') { const box = target.getBoundingClientRect(); target.dispatchEvent(new MouseEvent('mousedown', { bubbles: true, clientX: box.left + box.width / 2, clientY: box.top + box.height / 2 })); target.dispatchEvent(new MouseEvent('mouseup', { bubbles: true })); }
    else target.click();
  }
  const previous = new Map(); let chordHeld = true, repeatAt = 0;
  function poll(time) {
    if (!paused && recipe) { let pads = []; try { pads = navigator.getGamepads ? Array.from(navigator.getGamepads()).filter(pad => pad?.connected && pad.mapping === 'standard') : []; } catch {}
      const pad = pads[0]; if (pad) { const down = index => !!pad.buttons[index]?.pressed, chord = down(8) && down(9); if (!chord) chordHeld = false; else if (!chordHeld) { setPaused(true); send('menu'); }
        if (!paused && recipe.input !== 'native') { const direction = down(12) || pad.axes[1] < -.5 ? 'up' : down(13) || pad.axes[1] > .5 ? 'down' : down(14) || pad.axes[0] < -.5 ? 'left' : down(15) || pad.axes[0] > .5 ? 'right' : null;
          if (direction && (previous.get('direction') !== direction || time > repeatAt)) { const world = recipe.adapter === 'a-dark-room' && window.World && window.Engine?.activeModule === window.World; if (recipe.input === 'digital' || world) key({ up: 'ArrowUp', down: 'ArrowDown', left: 'ArrowLeft', right: 'ArrowRight' }[direction]); else move(direction); repeatAt = time + 210; }
          if (down(0) && !previous.get('confirm')) { if (recipe.adapter === 'hextris' && window.gameState !== 1) act(); else recipe.input === 'digital' ? key('Enter') : act(); } previous.set('confirm', down(0)); previous.set('direction', direction); }
      } else previous.clear(); }
    raf(poll);
  }
  async function initialize(files) {
    if (initialized) return; initialized = true; home.phase = 'loading';
    try { const profile = files?.['web-profile.json'] ? JSON.parse(files['web-profile.json']) : {}; for (const [key, value] of Object.entries(profile.storage || {})) if (typeof value === 'string') values[key] = value; Object.assign(cookies, profile.cookies || {});
      recipe = await fetch('home-config.json').then(response => { if (!response.ok) throw new Error('config load'); return response.json(); });
      window.lang = 'en'; window.oldIE = false;
      for (const path of recipe.scripts) {
        if (recipe.adapter === 'hexgl' && path === 'launch.js') { const original = bkcore.Utils.getURLParameter; bkcore.Utils.getURLParameter = name => name === 'quality' ? 1 : name === 'controlType' ? ((navigator.getGamepads && Array.from(navigator.getGamepads()).some(pad => pad?.connected)) ? 3 : 0) : original(name); }
        await new Promise((resolve, reject) => { const script = document.createElement('script'); script.src = path; script.onload = resolve; script.onerror = () => reject(new Error('script load: ' + path)); document.body.appendChild(script); });
      }
      if (recipe.adapter === 'oh-hi') window.app?.fontsLoaded?.();
      home.phase = 'started'; home.engineReady = true; send('ready'); raf(poll);
      const autosave = () => { if (initialized) save(); timeout(autosave, 4000); }; timeout(autosave, 4000);
    } catch (error) { home.startError = { name: error.name, message: String(error.message).slice(0, 200) }; send('error', { code: 'engine-start-failed' }); }
  }
  window.addEventListener('message', event => { const data = event.data; if (event.source !== parent || data?.protocol !== 'home-game-v1' || data.nonce !== nonce) return;
    if (!parentOrigin) { if (data.type !== 'init' || !/^https?:\/\//.test(event.origin)) return; parentOrigin = event.origin; } if (event.origin !== parentOrigin) return;
    if (data.type === 'init') void initialize(data.files); if (data.type === 'pause') { setPaused(true); send('paused'); } if (data.type === 'resume') { previous.clear(); chordHeld = true; setPaused(false); }
    if (data.type === 'flush') { save(); send('flushed'); }
  });
  window.addEventListener('keydown', event => { if (event.key === 'Escape') { event.preventDefault(); event.stopImmediatePropagation(); setPaused(true); send('menu'); } else if (paused) { event.preventDefault(); event.stopImmediatePropagation(); } }, true);
  document.addEventListener('click', event => { const anchor = event.target.closest?.('a[href]'); if (anchor && /^(?:https?:)?\/\//.test(anchor.getAttribute('href'))) event.preventDefault(); }, true);
  window.addEventListener('blur', () => { setPaused(true); send('paused'); }); document.addEventListener('visibilitychange', () => { if (document.hidden) { setPaused(true); send('paused'); } });
  parent.postMessage({ protocol: 'home-game-v1', nonce, type: 'hello' }, '*');
})();
