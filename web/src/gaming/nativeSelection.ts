export type NativeCoreFilter='basic'|'installed'|'all';
export function visibleNativeCores<T extends {categories:string[];basicSystems?:string[];installed:boolean}>(cores:T[],filter:NativeCoreFilter):T[]{
  return cores.filter(core=>core.categories.includes('Emulator')&&(filter==='all'||filter==='basic'&&(core.basicSystems?.length??0)>0||filter==='installed'&&core.installed));
}
export function visibleNativeSystems<T extends {id:string;basic?:boolean}>(systems:T[],advanced:boolean,selected:string):T[]{
  return systems.filter(system=>advanced||system.basic===true||system.id===selected);
}
