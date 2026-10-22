/** Godot 3.6.1's optional native PWA hooks must tolerate throwing Navigator getters in opaque frames. */
export function patchGodotSandbox(source) {
  const beforeCallback = 'function _godot_js_pwa_cb(p_update_cb){if("serviceWorker"in navigator){const cb=GodotRuntime.get_func(p_update_cb);navigator.serviceWorker.getRegistration().then(GodotPWA.updateState.bind(null,cb))}}';
  const afterCallback = 'function _godot_js_pwa_cb(p_update_cb){let serviceWorker;try{serviceWorker=navigator.serviceWorker}catch(error){return}if(serviceWorker){const cb=GodotRuntime.get_func(p_update_cb);serviceWorker.getRegistration().then(GodotPWA.updateState.bind(null,cb)).catch(function(){})}}';
  const beforeUpdate = 'function _godot_js_pwa_update(){if("serviceWorker"in navigator&&GodotPWA.hasUpdate){navigator.serviceWorker.getRegistration().then(function(reg){if(!reg||!reg.waiting){return}reg.waiting.postMessage("update")});return 0}return 1}';
  const afterUpdate = 'function _godot_js_pwa_update(){let serviceWorker;try{serviceWorker=navigator.serviceWorker}catch(error){return 1}if(serviceWorker&&GodotPWA.hasUpdate){serviceWorker.getRegistration().then(function(reg){if(!reg||!reg.waiting){return}reg.waiting.postMessage("update")}).catch(function(){});return 0}return 1}';
  for (const [before, after] of [[beforeCallback, afterCallback], [beforeUpdate, afterUpdate]]) {
    if (source.includes(after)) continue;
    if (source.split(before).length !== 2) throw new Error('Pinned Godot 3.6.1 PWA adapter site changed; review the runtime before patching.');
    source = source.replace(before, after);
  }
  const note = '// Home compatibility patch: optional PWA hooks tolerate disabled sandbox APIs; opaque-origin isolation remains enabled.\n';
  return source.startsWith(note) ? source : note + source;
}
