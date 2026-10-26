import {test} from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import {controllerDom} from '../helpers/controller-dom.mjs';
controllerDom();
const {ControllerRuntime}=await import('../../web/src/controller/runtime.ts');
const {focusCandidates,controllerScope,writeText}=await import('../../web/src/controller/dom.ts');

function fixture(){const env=controllerDom();const controller=new ControllerRuntime();const stop=controller.start();return{...env,controller,stop};}
function box(x,y){return{left:x,top:y,right:x+100,bottom:y+100};}
test('gameplay ownership stops shell actions and polling, and resumes only after every owner releases',()=>{
  const f=fixture();const button=f.doc.body.add(new f.Element('button'));button.focus();
  const first=f.controller.acquireGameplay(),second=f.controller.acquireGameplay();assert.equal(f.controller.getSnapshot().gameplay,true);assert.equal(f.controller.command('confirm'),false);assert.equal(button.clicks,0);assert.equal(f.timers.size,0);
  first();assert.equal(f.controller.getSnapshot().gameplay,true);second();assert.equal(f.controller.getSnapshot().gameplay,false);second();assert.equal(f.controller.getSnapshot().gameplay,false);f.stop();
});
test('idle controller support leaves pointer mode, focus and actions unchanged',()=>{
  const f=fixture();const first=f.doc.body.add(new f.Element('button'));first.focus();f.frame();
  assert.equal(f.controller.getSnapshot().mode,'pointer');assert.equal(f.doc.activeElement,first);assert.equal(first.clicks,0);assert.equal(f.timers.size,1);f.stop();assert.equal(f.timers.size,0);
});
test('a connected neutral gamepad does not change appearance until fresh input, and disconnect returns to pointer',()=>{
  const f=fixture();const button=f.doc.body.add(new f.Element('button'));button.focus();
  const pad={index:0,id:'isolated-pad',mapping:'standard',connected:true,axes:[0,0],buttons:Array.from({length:17},()=>({pressed:false,value:0}))};
  f.pads.push(pad);f.win.fire('gamepadconnected');assert.equal(f.controller.getSnapshot().mode,'pointer');assert.equal(button.clicks,0);
  pad.buttons[0]={pressed:true,value:1};f.frame();assert.equal(f.controller.getSnapshot().mode,'controller');assert.equal(button.clicks,1);
  f.frame();assert.equal(button.clicks,1);f.pads.length=0;f.win.fire('gamepaddisconnected');assert.equal(f.controller.getSnapshot().mode,'pointer');f.stop();
});
test('direction navigation skips disabled, hidden and inert controls and keeps a visible column',()=>{
  const f=fixture();const top=f.doc.body.add(new f.Element('button',{},box(120,0)));const disabled=f.doc.body.add(new f.Element('button',{},box(120,110)));disabled.disabled=true;
  const hidden=f.doc.body.add(new f.Element('div'));hidden.hidden=true;hidden.add(new f.Element('button',{},box(120,110)));
  const bottom=f.doc.body.add(new f.Element('button',{},box(120,230)));top.focus();assert.equal(focusCandidates(f.doc.body).length,2);
  f.controller.command('down');assert.equal(f.doc.activeElement,bottom);assert.equal(f.controller.getSnapshot().mode,'controller');f.stop();
});
test('player focus shortcut reaches Play without activating it and restores the exact library target',()=>{
  const f=fixture();const card=f.doc.body.add(new f.Element('button',{'data-home-focus':'album-2'},box(500,300)));
  const dock=f.doc.body.add(new f.Element('div',{'data-controller-player-dock':'music'},{left:0,top:620,right:1280,bottom:710}));
  const cover=dock.add(new f.Element('button',{},box(20,630))),play=dock.add(new f.Element('button',{'data-controller-player-primary':''},box(600,630)));card.focus();
  assert.equal(f.controller.command('focusplayer'),true);assert.equal(f.doc.activeElement,play);assert.equal(play.clicks,0);assert.equal(cover.clicks,0);assert.notEqual(play.scrolled,true);
  f.controller.command('focusplayer');assert.equal(f.doc.activeElement,card);assert.equal(card.clicks,0);
  f.controller.command('focusplayer');card.remove();const restored=f.doc.body.add(new f.Element('button',{'data-home-focus':'album-2'},box(500,300)));f.controller.command('focusplayer');assert.equal(f.doc.activeElement,restored);f.stop();
});
test('player shortcut respects modal and gameplay ownership and remains inert without a dock',()=>{
  const f=fixture();const page=f.doc.body.add(new f.Element('button'));page.focus();assert.equal(f.controller.command('focusplayer'),false);
  const dock=f.doc.body.add(new f.Element('div',{'data-controller-player-dock':'radio'}));const play=dock.add(new f.Element('button',{'data-controller-player-primary':''}));
  const dialog=f.doc.body.add(new f.Element('div',{role:'dialog'}));const field=dialog.add(new f.Input());field.focus();assert.equal(f.controller.command('focusplayer'),false);assert.equal(f.doc.activeElement,field);
  dialog.remove();page.focus();const release=f.controller.acquireGameplay();assert.equal(f.controller.command('focusplayer'),false);assert.equal(f.doc.activeElement,page);release();assert.equal(play.clicks,0);f.stop();
});
test('dock horizontal movement stays among its controls rather than jumping into the page',()=>{
  const f=fixture();const page=f.doc.body.add(new f.Element('button',{},box(260,500)));const dock=f.doc.body.add(new f.Element('div',{'data-controller-player-dock':'music'}));const first=dock.add(new f.Element('button',{},box(20,600))),last=dock.add(new f.Element('button',{},box(150,600)));first.focus();
  f.controller.command('right');assert.equal(f.doc.activeElement,last);f.controller.command('right');assert.equal(f.doc.activeElement,last);assert.equal(page.clicks,0);f.stop();
});
test('pointer or programmatic focus resets a previous column before the next direction',()=>{
  const f=fixture();const top=f.doc.body.add(new f.Element('button',{},box(500,0))),lower=f.doc.body.add(new f.Element('button',{},box(500,160)));const side=f.doc.body.add(new f.Element('button',{},box(0,0))),sideBelow=f.doc.body.add(new f.Element('button',{},box(0,160)));top.focus();f.controller.command('down');assert.equal(f.doc.activeElement,lower);
  side.focus();f.controller.command('down');assert.equal(f.doc.activeElement,sideBelow);f.win.fire('pointerdown',{isTrusted:true});top.focus();f.controller.command('down');assert.equal(f.doc.activeElement,lower);f.stop();
});
test('fully transparent ancestor controls are not navigation candidates',()=>{
  const f=fixture();const hidden=f.doc.body.add(new f.Element('div'));hidden.style.opacity='0';hidden.add(new f.Element('button'));const visible=f.doc.body.add(new f.Element('button'));assert.deepEqual(focusCandidates(f.doc.body),[visible]);f.stop();
});
test('inner popup/dialog commands take priority even when root effects register later',()=>{
  const f=fixture();const wrapper=f.doc.body.add(new f.Element('div'));const dialog=wrapper.add(new f.Element('div',{role:'dialog'}));const button=dialog.add(new f.Element('button'));button.focus();const calls=[];
  f.controller.register(()=>wrapper,action=>{if(action==='back'){calls.push('dialog');return true;}return false;});
  f.controller.register(()=>f.doc.body,action=>{if(action==='back'){calls.push('root');return true;}return false;});
  assert.equal(controllerScope(),dialog);f.controller.command('back');assert.deepEqual(calls,['dialog']);
  const popup=f.doc.body.add(new f.Element('div',{class:'home-menu-surface'}));popup.add(new f.Element('div',{role:'option'})).focus();
  f.controller.register(()=>popup,action=>{calls.push('popup:'+action);return true;});f.controller.command('seekforward');assert.equal(calls.at(-1),'popup:seekforward');assert.equal(calls.length,2);f.stop();
});
test('Video.js menus consume transport actions and Back before player handlers',()=>{
  const f=fixture();const player=f.doc.body.add(new f.Element('div'));const popup=player.add(new f.Element('div',{class:'media-menu-popup'}));const item=popup.add(new f.Element('button',{role:'menuitemradio'}));item.focus();let transports=0;
  f.controller.register(()=>player,()=>{transports++;return true;});f.controller.command('playpause');f.controller.command('back');assert.equal(transports,0);assert.ok(item.events.includes('keydown'));f.stop();
});
test('Back exits fullscreen before closing playback, while a child popup closes first',async()=>{
  const f=fixture();const player=f.doc.body.add(new f.Element('div'));const button=player.add(new f.Element('button'));button.focus();f.doc.fullscreenElement=player;let closes=0;
  f.controller.register(()=>player,()=>{closes++;return true;});f.controller.command('back');assert.equal(f.doc.exits,1);assert.equal(closes,0);
  f.doc.fullscreenElement=player;const popup=player.add(new f.Element('div',{class:'home-menu-surface'}));popup.add(new f.Element('button',{role:'option'})).focus();let menus=0;
  f.controller.register(()=>popup,()=>{menus++;return true;});f.controller.command('back');assert.equal(f.doc.exits,1);assert.equal(menus,1);f.stop();
});
test('pointer takeover and ordinary text editing preserve normal input behavior',()=>{
  const f=fixture();const button=f.doc.body.add(new f.Element('button'));button.focus();f.controller.command('confirm');assert.equal(button.clicks,1);
  f.win.fire('pointermove',{isTrusted:true,movementX:4,movementY:0});assert.equal(f.controller.getSnapshot().mode,'pointer');
  const input=f.doc.body.add(new f.Input());input.focus();assert.equal(f.key('ArrowLeft',input).defaultPrevented,false);assert.equal(f.key('Enter',input).defaultPrevented,false);
  f.key('a',input);assert.equal(f.controller.getSnapshot().mode,'keyboard');assert.equal(input.events.length,0);f.stop();
});
test('ordinary keyboard Enter/Escape and Video.js shortcuts retain their native behavior after mouse use',()=>{
  const f=fixture();const skin=f.doc.body.add(new f.Element('div',{class:'home-video-skin'}));const button=skin.add(new f.Element('button'));button.focus();
  assert.equal(f.key('ArrowLeft',button).defaultPrevented,false);assert.equal(f.key('Enter',button).defaultPrevented,false);assert.equal(f.key('Escape',button).defaultPrevented,false);assert.equal(f.controller.getSnapshot().mode,'pointer');
  const slider=f.doc.body.add(new f.Input('range'));slider.focus();assert.equal(f.key('ArrowLeft',slider).defaultPrevented,false);assert.equal(f.controller.getSnapshot().mode,'pointer');f.stop();
});
test('remote OK edits a field in navigation mode and keyboard typing/caret keys remain native in keyboard mode',()=>{
  const f=fixture();const first=f.doc.body.add(new f.Element('button',{},box(0,0)));const input=f.doc.body.add(new f.Input());input.rect=box(0,150);first.focus();
  assert.equal(f.key('ArrowDown',first).defaultPrevented,true);assert.equal(f.doc.activeElement,input);assert.equal(f.key('Enter',input).defaultPrevented,true);assert.equal(f.controller.getSnapshot().keyboard,input);
  f.controller.closeKeyboard();f.key('a',input);assert.equal(f.key('ArrowLeft',input).defaultPrevented,false);assert.equal(f.key('Enter',input).defaultPrevented,false);f.stop();
});
test('remote/gamepad confirmation opens text entry, session changes discard the target and background input is inert',()=>{
  const f=fixture();const input=f.doc.body.add(new f.Input('password'));input.focus();f.controller.command('confirm');assert.equal(f.controller.getSnapshot().keyboard,input);
  f.win.fire('home-session-state',{detail:{userId:'first',sessionId:'a'}});f.win.fire('home-session-state',{detail:{userId:'second',sessionId:'b'}});assert.equal(f.controller.getSnapshot().keyboard,null);
  f.doc.hidden=true;assert.equal(f.controller.command('confirm'),false);assert.equal(f.controller.getSnapshot().keyboard,null);f.stop();
});
test('native input setter updates the value without fabricating an HTMLElement event, preserving limits and invalid formats',()=>{
  const f=fixture();const input=f.doc.body.add(new f.Input());input.value='old';let ownSetter=0;
  Object.defineProperty(input,'value',{get(){return this._value;},set(value){ownSetter++;this._value=value;},configurable:true});
  assert.equal(writeText(input,'new'),true);assert.equal(input.value,'new');assert.equal(ownSetter,0);assert.deepEqual(input.events,['input','change']);
  input.maxLength=3;assert.equal(writeText(input,'too long'),false);assert.equal(input.value,'new');
  const date=f.doc.body.add(new f.Input('date'));date.value='2026-10-18';assert.equal(writeText(date,'not-a-date'),false);assert.equal(date.value,'2026-10-18');f.stop();
});
test('expanded multiple selection enters edit mode, toggles explicitly and Back leaves it before closing a dialog',()=>{
  const f=fixture();const select=f.doc.body.add(new f.Select());select.options=[{disabled:false,selected:true,dataset:{}},{disabled:true,selected:false,dataset:{}},{disabled:false,selected:false,dataset:{}}];select.focus();let backs=0;
  f.controller.register(()=>f.doc.body,action=>{if(action==='back'){backs++;return true;}return false;});
  f.controller.command('confirm');assert.equal(select.options[0].selected,true);f.controller.command('down');f.controller.command('confirm');assert.equal(select.options[2].selected,true);assert.deepEqual(select.events,['change']);
  f.controller.command('back');assert.equal(backs,0);f.controller.command('back');assert.equal(backs,1);f.stop();
});
test('native sliders change a bounded value and commit through existing input/change/keyup handlers',()=>{
  const f=fixture();const input=f.doc.body.add(new f.Input('range'));input.value='.9';input.min='0';input.max='1';input.step='.2';input.focus();
  f.controller.command('right');assert.equal(input.value,'1');assert.deepEqual(input.events,['input','change','keydown','keyup']);f.stop();
});
test('scroll input stays inside its active dialog and trigger scrolling does not activate page buttons',()=>{
  const f=fixture();const dialog=f.doc.body.add(new f.Element('div',{role:'dialog'}));const button=dialog.add(new f.Element('button'));button.focus();
  f.controller.command('scrolldown');assert.equal(dialog.scrollOptions.top,90);assert.equal(f.win.scrollOptions,undefined);assert.equal(button.clicks,0);
  f.controller.command('seekforward');assert.equal(dialog.scrollOptions.top,180);f.stop();
});
test('missing gamepad API on HTTP does not disable remote navigation, and disabled support cancels text entry',async()=>{
  const env=controllerDom();env.win.isSecureContext=false;delete env.nav.getGamepads;const c=new ControllerRuntime();const stop=c.start();const input=env.doc.body.add(new env.Input());input.focus();
  assert.equal(c.getSnapshot().capability,'insecure');c.command('confirm','remote');assert.equal(c.getSnapshot().keyboard,input);c.setPreferences({enabled:false});await Promise.resolve();assert.equal(c.getSnapshot().keyboard,null);assert.equal(c.getSnapshot().mode,'pointer');assert.equal(c.command('confirm'),false);stop();
});
test('HTTP browsers that actually expose gamepads can activate navigation without a blanket secure-context gate',()=>{
  const env=controllerDom();env.win.isSecureContext=false;const c=new ControllerRuntime();const stop=c.start();const button=env.doc.body.add(new env.Element('button'));button.focus();
  const pad={index:0,id:'original-http-pad',mapping:'standard',connected:true,axes:[0,0],buttons:Array.from({length:17},()=>({pressed:false,value:0}))};
  env.pads.push(pad);env.win.fire('gamepadconnected');assert.equal(c.getSnapshot().capability,'available');assert.equal(c.getSnapshot().mode,'pointer');
  pad.buttons[0]={pressed:true,value:1};env.frame();assert.equal(c.getSnapshot().mode,'controller');assert.equal(button.clicks,1);
  const release=c.acquireGameplay();env.frame();assert.equal(button.clicks,1);assert.equal(env.frames.size,0);release();stop();
});
test('real gamepad refusal over HTTP stays fail-closed and can be reprobed after focus returns',()=>{
  const env=controllerDom();env.win.isSecureContext=false;env.nav.getGamepads=()=>{throw new DOMException('original policy refusal','SecurityError');};
  const c=new ControllerRuntime();const stop=c.start();assert.equal(c.getSnapshot().capability,'insecure');assert.equal(env.timers.size,0);assert.equal(env.frames.size,0);
  const button=env.doc.body.add(new env.Element('button'));button.focus();env.nav.getGamepads=()=>env.pads;
  const pad={index:0,id:'original-http-pad',mapping:'standard',connected:true,axes:[0,0],buttons:Array.from({length:17},()=>({pressed:false,value:0}))};env.pads.push(pad);
  env.win.fire('focus');assert.equal(c.getSnapshot().capability,'available');assert.equal(button.clicks,0);pad.buttons[0]={pressed:true,value:1};env.frame();assert.equal(button.clicks,1);stop();
});
test('missing gamepad API on a trusted origin reports unsupported without starting a polling loop',()=>{
  const env=controllerDom();delete env.nav.getGamepads;const c=new ControllerRuntime();const stop=c.start();assert.equal(c.getSnapshot().capability,'unsupported');assert.equal(env.timers.size,0);assert.equal(env.frames.size,0);stop();
});
test('preferences persist per base path and cross-tab disabling stops controller commands',async()=>{
  const f=fixture();const button=f.doc.body.add(new f.Element('button'));button.focus();f.controller.setPreferences({deadzone:.5});await Promise.resolve();
  assert.equal(JSON.parse(f.storage.get('home-controller-v1:/home')).deadzone,.5);f.win.fire('storage',{key:'home-controller-v1:/home',newValue:JSON.stringify({enabled:false})});
  assert.equal(f.controller.command('confirm'),false);assert.equal(button.clicks,0);f.stop();
});
test('button assignment consumes input and neutralizes the held button without firing a navigation action',async()=>{
  const f=fixture();const button=f.doc.body.add(new f.Element('button'));button.focus();const pad={index:0,id:'isolated-pad',mapping:'standard',connected:true,axes:[0,0],buttons:Array.from({length:17},()=>({pressed:false,value:0}))};
  f.pads.push(pad);f.win.fire('gamepadconnected');f.controller.startBinding(pad.id,'confirm');f.frame();pad.buttons[2]={pressed:true,value:1};f.frame();await Promise.resolve();
  assert.equal(f.controller.getSnapshot().preferences.mappings[pad.id].confirm,2);assert.equal(button.clicks,0);assert.equal(f.controller.getSnapshot().binding,null);f.frame();assert.equal(button.clicks,0);f.stop();
});
test('a page heading focus is repaired to an actionable element and route memory restores media identity',()=>{
  const f=fixture();const card=f.doc.body.add(new f.Element('a',{href:'/home/media/fixture','data-home-focus':'isolated-media'}));card.focus();f.controller.command('left');
  const heading=f.doc.body.add(new f.Element('h1',{'data-page-heading':'',tabindex:'-1'}));heading.focus();f.frame();assert.equal(f.doc.activeElement,card);
  card.remove();const restored=f.doc.body.add(new f.Element('a',{href:'/home/media/fixture','data-home-focus':'isolated-media'}));heading.focus();f.win.fire('home-controller-route');f.frame();assert.equal(f.doc.activeElement,restored);f.stop();
});
test('permission-policy failure is reported without starting navigation or leaking timers',()=>{
  const env=controllerDom();env.nav.getGamepads=()=>{throw new DOMException('isolated denied','SecurityError');};const c=new ControllerRuntime();const stop=c.start();assert.equal(c.getSnapshot().capability,'blocked');assert.equal(c.getSnapshot().mode,'pointer');assert.equal(env.timers.size,0);stop();
});
test('future exclusive input ownership prevents Home navigation without introducing any gameplay UI',()=>{
  const f=fixture();const scope=f.doc.body.add(new f.Element('div',{'data-controller-scope':'future-exclusive'}));scope.add(new f.Element('button')).focus();const calls=[];
  f.controller.register(()=>scope,action=>{calls.push(action);return false;},true);f.controller.command('confirm');assert.deepEqual(calls,['confirm']);assert.equal(scope.children[0].clicks,0);
  f.doc.fullscreenElement=scope;f.controller.command('back');assert.deepEqual(calls,['confirm','back']);assert.equal(f.doc.exits,0);f.stop();
});
test('controller styling is dormant by default and all overlays/player commands are attached to the shared provider',async()=>{
  const css=await readFile(new URL('../../web/src/controller/controller.css',import.meta.url),'utf8');const main=await readFile(new URL('../../web/src/main.tsx',import.meta.url),'utf8');
  assert.match(css,/html\[data-home-input="controller"\]/);assert.doesNotMatch(css,/^\s*(body|\.media-card|\.workspace)\s*\{/m);assert.match(main,/<ControllerProvider><App \/><\/ControllerProvider>/);
  for(const file of ['HomeSelect','MainNavigation','Dialogs','Player','MusicPlayer','LiveTv','Podcasts','Offline'])assert.match(await readFile(new URL(`../../web/src/components/${file}.tsx`,import.meta.url),'utf8'),/useControllerCommands/);
  const player=await readFile(new URL('../../web/src/components/Player.tsx',import.meta.url),'utf8');assert.match(player,/controllerPlayback\(action, mediaPort.current/);assert.match(player,/!room \|\| room.canControl/);
});
