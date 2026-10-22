// Isolated, deterministic DOM/event contract fixture. This is not a browser suite
// and does not model native TV hardware, React Aria rendering or real user activation.
export function controllerDom() {
  let doc;
  class Events {
    listeners=new Map();
    addEventListener(type,fn){const list=this.listeners.get(type)??new Set();list.add(fn);this.listeners.set(type,list);}
    removeEventListener(type,fn){this.listeners.get(type)?.delete(fn);}
    fire(type,event={}){for(const fn of this.listeners.get(type)??[])fn(event);}
    dispatchEvent(event){this.fire(event.type,event);return !event.defaultPrevented;}
  }
  class Element extends EventTarget {
    tagName;children=[];parentElement=null;attrs=new Map();dataset={};style={};hidden=false;inert=false;disabled=false;clicks=0;events=[];
    rect={left:0,top:0,right:100,bottom:50};textContent='fixture';id='';
    constructor(tag='div',attributes={},rect){super();this.tagName=tag.toUpperCase();for(const [key,value] of Object.entries(attributes))this.setAttribute(key,value);if(rect)this.rect=rect;}
    setAttribute(key,value){this.attrs.set(key,String(value));if(key==='id')this.id=String(value);if(key.startsWith('data-'))this.dataset[key.slice(5).replace(/-([a-z])/g,(_,c)=>c.toUpperCase())]=String(value);}
    getAttribute(key){return this.attrs.get(key)??null;}
    hasAttribute(key){return this.attrs.has(key);}
    add(child){child.parentElement=this;this.children.push(child);return child;}
    remove(){if(this.parentElement)this.parentElement.children=this.parentElement.children.filter(child=>child!==this);this.parentElement=null;}
    contains(other){return other===this||this.children.some(child=>child.contains(other));}
    get isConnected(){return this===doc.body||doc.body.contains(this);}
    get classList(){return{contains:value=>(this.attrs.get('class')??'').split(' ').includes(value)};}
    matches(selector){return selector.split(',').some(raw=>{
      let part=raw.trim();const not=/:not\(([^)]+)\)/.exec(part);if(not){if(this.matches(not[1]))return false;part=part.replace(not[0],'');}
      if(part.includes(' ')){const pieces=part.split(/\s+/);const last=pieces.pop();return this.matches(last)&&!!this.parentElement?.closest(pieces.join(' '));}
      if(part===':disabled')return this.disabled;if(part==='[hidden]')return this.hidden||this.attrs.has('hidden');if(part==='[inert]')return this.inert||this.attrs.has('inert');
      const tag=/^[a-z]+/i.exec(part)?.[0];if(tag&&this.tagName!==tag.toUpperCase())return false;
      for(const match of part.matchAll(/\.([\w-]+)/g))if(!this.classList.contains(match[1]))return false;
      for(const match of part.matchAll(/\[([\w-]+)(?:(\^?=)["']?([^"'\]]+)["']?)?\]/g)){
        const actual=match[1]==='type'?this.type:match[1].startsWith('data-')?this.dataset[match[1].slice(5).replace(/-([a-z])/g,(_,c)=>c.toUpperCase())]:this.getAttribute(match[1]);
        if(actual===null||actual===undefined)return false;if(match[2]==='='&&actual!==match[3])return false;if(match[2]==='^='&&!actual.startsWith(match[3]))return false;
      }
      return true;
    });}
    closest(selector){for(let element=this;element;element=element.parentElement)if(element.matches(selector))return element;return null;}
    querySelectorAll(selector){const all=[];for(const child of this.children){if(child.matches(selector))all.push(child);all.push(...child.querySelectorAll(selector));}return all;}
    querySelector(selector){return this.querySelectorAll(selector)[0]??null;}
    getClientRects(){for(let element=this;element;element=element.parentElement)if(element.hidden||element.style.display==='none')return[];return[this.rect];}
    getBoundingClientRect(){return this.rect;}
    focus(){doc.activeElement=this;doc.fire('focusin');}
    scrollIntoView(){this.scrolled=true;}
    scrollBy(options){this.scrollOptions=options;}
    click(){this.clicks++;this.dispatchEvent(new Event('click'));}
    dispatchEvent(event){this.events.push(event.type);return super.dispatchEvent(event);}
  }
  class Input extends Element {
    type='text';readOnly=false;maxLength=-1;inputMode='';labels=[];step='';min='';max='';_value='';
    constructor(type='text'){super('input');this.type=type;}
    get value(){return this._value;}
    set value(value){this._value=this.type==='date'&&value&&!/^\d{4}-\d\d-\d\d$/.test(value)?'':String(value);}
  }
  class TextArea extends Input {constructor(){super();this.tagName='TEXTAREA';}get value(){return this._value;}set value(value){this._value=String(value);}}
  class Select extends Element {multiple=true;options=[];constructor(){super('select');}}
  class KeyEvent extends Event {constructor(type,options){super(type,options);this.key=options.key;}}
  const frames=new Map(),timers=new Map();let id=0;const raf=fn=>{frames.set(++id,fn);return id;};
  doc=new Events();doc.body=new Element('body');doc.documentElement=new Element('html');doc.hidden=false;doc.focused=true;doc.activeElement=doc.body;doc.fullscreenElement=null;doc.exits=0;
  doc.hasFocus=()=>doc.focused;doc.querySelectorAll=selector=>doc.body.querySelectorAll(selector);doc.querySelector=selector=>selector==='meta[name="home-base"]'?{content:'/home'}:doc.body.querySelector(selector);
  doc.exitFullscreen=async()=>{doc.exits++;doc.fullscreenElement=null;};
  const win=new Events();win.isSecureContext=true;win.setTimeout=fn=>{timers.set(++id,fn);return id;};
  win.scrollBy=options=>{win.scrollOptions=options;};
  const storage=new Map();const nav={getGamepads:()=>env.pads};const env={doc,win,nav,storage,frames,timers,pads:[],Element,Input,TextArea,Select,
    frame(){const pending=[...frames.values()];frames.clear();for(const fn of pending)fn(0);},
    key(key,target=doc.activeElement,extra={}){const event={key,keyCode:0,target,isTrusted:true,defaultPrevented:false,repeat:false,ctrlKey:false,metaKey:false,altKey:false,preventDefault(){this.defaultPrevented=true;},stopPropagation(){this.stopped=true;},...extra};win.fire('keydown',event);return event;}
  };
  Object.defineProperty(globalThis,'navigator',{value:nav,writable:true,configurable:true});
  Object.assign(globalThis,{window:win,document:doc,HTMLElement:Element,HTMLInputElement:Input,HTMLTextAreaElement:TextArea,HTMLSelectElement:Select,KeyboardEvent:KeyEvent,
    localStorage:{getItem:key=>storage.get(key)??null,setItem:(key,value)=>storage.set(key,value)},location:{pathname:'/home/library'},innerWidth:1280,innerHeight:720,
    getComputedStyle:element=>({display:element.style.display??'block',visibility:element.style.visibility??'visible',opacity:element.style.opacity??'1'}),
    matchMedia:()=>({matches:true}),requestAnimationFrame:raf,cancelAnimationFrame:frame=>frames.delete(frame),clearTimeout:timer=>timers.delete(timer),MutationObserver:class{constructor(fn){this.callback=fn;}observe(){}disconnect(){}}
  });
  return env;
}
