import {writeFile} from 'node:fs/promises';
import {stockVideos} from '../web/src/music/sceneSources.ts';

// Inspect Commons' text metadata only. Never request an image, video, audio body or browser page playback.
const query=new URLSearchParams({action:'query',format:'json',titles:stockVideos.map(video=>video.commonsTitle).join('|'),prop:'videoinfo',viprop:'url|size|extmetadata|derivatives'});
const response=await fetch('https://commons.wikimedia.org/w/api.php?'+query,{headers:{'User-Agent':'Home-MediaSourceReview/0.41.1'},signal:AbortSignal.timeout(45000)});
if(!response.ok)throw Error('Commons metadata HTTP '+response.status);const text=await response.text();if(text.length>2*1024*1024)throw Error('Oversized metadata response.');
const pages=Object.values(JSON.parse(text).query?.pages??{}),path=url=>decodeURIComponent(new URL(url).pathname),sources=[];
for(const video of stockVideos){
  const page=pages.find(page=>page.title===video.commonsTitle),data=page?.videoinfo?.[0];if(!data)throw Error('Missing source metadata: '+video.id);
  if(data.extmetadata?.LicenseShortName?.value!==video.license)throw Error('Source license changed: '+video.id);
  if(![data.url,...(data.derivatives??[]).map(item=>item.src)].filter(Boolean).some(url=>path(url)===path(video.url)))throw Error('Selected file is absent from the published derivatives: '+video.id);
  sources.push({id:video.id,pageId:page.pageid,title:page.title,author:video.author,license:video.license,licenseUrl:video.licenseUrl,source:video.source,url:video.url,originalWidth:data.width,originalHeight:data.height,durationSeconds:data.duration});
}
const report={verifiedAt:new Date().toISOString(),videos:sources.length,sourceMediaBodiesRequested:0,sources};
await writeFile('artifacts/music-visual-source-review-v0411.json',JSON.stringify(report,null,2),{flag:'wx'});console.log(JSON.stringify({videos:sources.length,licenses:[...new Set(sources.map(item=>item.license))],sourceMediaBodiesRequested:0}));
