import { useEffect, useState } from 'react';
import { Disc3, Film } from 'lucide-react';
import { artwork, thumbnail, type MediaItem } from '../api';

export function Artwork({ item, eager = false, mode = 'poster' }: { item: MediaItem; eager?: boolean; mode?: 'poster' | 'backdrop' }) {
  const remote = mode === 'poster' ? item.content?.posterUrl : item.content?.backdropUrl || item.content?.stillUrl;
  const preferred = remote ? artwork(item, mode) : item.hasThumbnail ? thumbnail(item) : '';
  const [failed, setFailed] = useState(false);
  useEffect(() => setFailed(false), [preferred]);
  const source = !failed ? preferred : item.hasThumbnail && remote ? thumbnail(item) : '';
  if (source) return <img src={source} alt="" loading={eager ? 'eager' : 'lazy'} decoding="async" onError={event => {
    if (!failed) setFailed(true); else event.currentTarget.style.visibility = 'hidden';
  }} />;
  return <div className={`art-placeholder art-placeholder--${item.kind}`}><div className="art-orbit" /><span>{item.kind === 'audio' ? <Disc3 size={48} strokeWidth={1} /> : <Film size={42} strokeWidth={1} />}</span></div>;
}
