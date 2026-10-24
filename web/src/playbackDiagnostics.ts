export function playbackBuffer(video: Pick<HTMLVideoElement, 'currentTime' | 'buffered'> | null): number {
  if (!video) return 0;
  for (let index = 0; index < video.buffered.length; index++)
    if (video.currentTime >= video.buffered.start(index) && video.currentTime <= video.buffered.end(index)) return video.buffered.end(index) - video.currentTime;
  return 0;
}
