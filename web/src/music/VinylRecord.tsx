import './vinyl.css';

/** The same original decorative record used by the library hero and visual-mode fallback. */
export function VinylRecord({spinning=false}:{spinning?:boolean}){
  return <div className={`music-vinyl-record${spinning?' is-spinning':''}`} aria-hidden="true"><div className="music-vinyl-platter"><div className="music-vinyl-label"><span>home.</span><i/><small>STEREO</small></div><i className="music-vinyl-spindle"/></div><div className="music-vinyl-reflection"/></div>;
}
export function VinylBackdrop({playing}:{playing:boolean}){
  return <div className="music-vinyl-backdrop" aria-hidden="true"><VinylRecord spinning={playing}/></div>;
}
