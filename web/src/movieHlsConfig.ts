import type {HlsConfig} from 'hls.js';

/** A demand-paced file playlist is not a wall-clock live broadcast. */
export function movieHlsConfig(startPosition:number,workerPath:string):Partial<HlsConfig>{
  return {workerPath,enableWorker:true,lowLatencyMode:false,startPosition:Math.max(0,startPosition),
    maxBufferLength:30,maxMaxBufferLength:60,backBufferLength:30,maxBufferSize:32*1024*1024,
    liveMaxUnchangedPlaylistRefresh:Infinity,
    // hls.js requires both values explicitly when a max latency is supplied.
    liveSyncDurationCount:3,liveMaxLatencyDurationCount:Infinity,maxLiveSyncPlaybackRate:1};
}
