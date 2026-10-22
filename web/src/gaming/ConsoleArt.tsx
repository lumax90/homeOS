import type {CSSProperties} from 'react';

export function consoleFamily(system?:string|null){
  if(['ps1','ps2','psp'].includes(system??''))return 'playstation';
  if(['nds','3ds'].includes(system??''))return 'dual-screen';
  if(['gb','gbc','gba','gamegear','ngp','wonderswan','lynx'].includes(system??''))return 'handheld';
  if(['megadrive','mastersystem','segacd','sega32x','saturn','dreamcast'].includes(system??''))return 'sega';
  if(['nes','snes','n64','gamecube','wii','wiiu'].includes(system??''))return 'nintendo';
  return system==='xbox'?'xbox':'classic';
}
const tones:Record<string,[string,string]>={playstation:['112,143,239','61,81,146'],'dual-screen':['224,93,113','121,69,117'],handheld:['163,184,120','78,113,87'],sega:['82,179,226','44,93,156'],nintendo:['226,142,97','132,66,92'],xbox:['116,186,103','57,112,68'],classic:['149,135,207','78,65,121']};
export function consoleStyle(system?:string|null):CSSProperties{const tone=tones[consoleFamily(system)];return{'--console-accent':tone[0],'--console-shade':tone[1]} as CSSProperties;}

/** Original decorative console silhouettes; no provider/game artwork or copied logos. */
export function ConsoleArt({system,className=''}:{system?:string|null;className?:string}){
  const family=consoleFamily(system);
  return <svg className={`console-art ${className}`} viewBox="0 0 400 250" fill="none" aria-hidden="true" focusable="false">
    <g stroke="currentColor" strokeWidth="1.6" strokeLinejoin="round">
      {family==='dual-screen'?<g transform="rotate(-12 210 125)"><rect x="100" y="22" width="208" height="103" rx="15" fill="currentColor" fillOpacity=".06"/><rect x="136" y="34" width="134" height="79" rx="7" fill="currentColor" fillOpacity=".08"/><path d="M105 129h198l26 90c2 7-3 13-10 13H89c-7 0-12-6-10-13z" fill="currentColor" fillOpacity=".06"/><rect x="141" y="145" width="126" height="66" rx="7"/><path d="M110 154v28m-14-14h28" strokeWidth="5"/><circle cx="287" cy="157" r="4"/><circle cx="304" cy="173" r="4"/><circle cx="286" cy="187" r="4"/><circle cx="271" cy="173" r="4"/><path d="M98 123h211" strokeWidth="5"/></g>:
      family==='handheld'?<g transform="rotate(15 205 124)"><path d="M132 9h142c13 0 21 10 21 23v174c0 21-23 35-43 35H130c-12 0-20-9-20-21V33c0-14 9-24 22-24z" fill="currentColor" fillOpacity=".06"/><rect x="127" y="28" width="151" height="103" rx="11" fill="currentColor" fillOpacity=".09"/><rect x="148" y="40" width="108" height="79" rx="4"/><path d="M156 157v38m-19-19h38" strokeWidth="11"/><circle cx="236" cy="173" r="10"/><circle cx="266" cy="157" r="10"/><path d="m179 215 13-4m10 4 13-4m32 1 18-5m-18 12 18-5" strokeWidth="4"/></g>:
      family==='playstation'?<g transform="rotate(-9 210 125)"><rect x="68" y="44" width="265" height="145" rx="18" fill="currentColor" fillOpacity=".06"/><circle cx="202" cy="115" r="57" fill="currentColor" fillOpacity=".055"/><circle cx="202" cy="115" r="43"/><circle cx="98" cy="153" r="14"/><circle cx="309" cy="153" r="9"/><path d="M76 185v18h249v-18M87 79h35m-35 9h35m-35 9h35m160-18h35m-35 9h35m-35 9h35M126 204v9h36v-9m78 0v9h36v-9"/><path d="m340 30 15 25h-30zM26 131h21v21H26m311 61 19 19m0-19-19 19"/><circle cx="358" cy="108" r="15"/></g>:
      <g transform="rotate(-9 205 125)"><rect x="65" y="58" width="268" height="133" rx={family==='sega'?45:16} fill="currentColor" fillOpacity=".06"/><path d="M77 187v16h247v-16"/><rect x="144" y="72" width="119" height="36" rx="6" fill="currentColor" fillOpacity=".08"/><path d="M108 119v42m-21-21h42" strokeWidth="10"/><circle cx="277" cy="146" r="10"/><circle cx="304" cy="127" r="10"/><path d="M169 160h22m12 0h22m-76 58h116" strokeWidth="4"/><path d="M107 53v-9h181v9"/></g>}
    </g>
    <circle cx="337" cy="211" r="32" stroke="currentColor" strokeOpacity=".12"/><circle cx="69" cy="39" r="24" stroke="currentColor" strokeOpacity=".12"/>
  </svg>;
}
