import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { clock, request, type LyricLine } from '../api';
import { lyricIndex, lyricWordActive } from '../audio/lyricTiming';

export function Lyrics({ mediaId, seconds, onSeek, duration }: { mediaId: string; seconds: number; onSeek?: (seconds: number) => void; duration?: number }) {
  const data = useQuery({ queryKey: ['lyrics', mediaId], queryFn: ({ signal }) => request<LyricLine[]>(`/music/media/${mediaId}/lyrics`, { signal }) });
  const [all, setAll] = useState(false); const [limit, setLimit] = useState(100); const [hold, setHold] = useState<number | null>(null);
  const lines = data.data ?? []; const active = lyricIndex(lines, seconds); const anchor = hold ?? active; const timed = lines.some(line => line.seconds !== null);
  const start = timed && onSeek && !all && anchor >= 0 ? Math.max(0, anchor - 2) : 0;
  const shown = timed && onSeek && !all && anchor >= 0 ? lines.slice(start, Math.max(anchor + 4, start + 6)) : lines.slice(0, limit);
  const canSeek = (value: number) => !!onSeek && (duration === undefined || duration <= 0 || value <= duration);
  return <section className="player-lyrics" onFocusCapture={() => setHold(active)} onBlurCapture={event => { if (!event.currentTarget.contains(event.relatedTarget)) setHold(null); }}><h3>Şarkı sözleri</h3>
    {timed && onSeek && <button className="lyric-view-toggle" onClick={() => { setAll(value => !value); setHold(null); }}>{all ? 'Çalan satırı izle' : 'Tüm sözleri göster'}</button>}
    {shown.length ? shown.map((line, index) => <p key={start + index} className={line.seconds !== null && lyricWordActive(line.seconds, line.endSeconds, seconds) ? 'current' : ''}>
      {onSeek && line.seconds !== null && <button className="lyric-time" onClick={() => onSeek(line.seconds!)} disabled={!canSeek(line.seconds)} aria-label={`${clock(line.seconds)} konumuna git`}>{clock(line.seconds)}</button>}
      {line.words?.length ? line.words.map((word, part) => onSeek ? <button key={part} className={lyricWordActive(word.seconds, word.endSeconds, seconds) ? 'lyric-word active' : 'lyric-word'} onClick={() => onSeek(word.seconds)} disabled={!canSeek(word.seconds)} aria-label={`${word.text.trim()} · ${clock(word.seconds)} konumuna git`}>{word.text}</button> : <span key={part}>{word.text}</span>) : line.text}
    </p>) : <p>{data.isPending ? 'Şarkı sözleri okunuyor…' : 'Dosyada veya yanında LRC, ELRC ya da metin söz bulunmuyor.'}</p>}
    {(!timed || all || !onSeek || active < 0) && lines.length > limit && <button className="lyric-view-toggle" onClick={() => setLimit(value => value + 100)}>Diğer satırlar</button>}{data.error && <p className="form-error">{data.error.message}</p>}
  </section>;
}
