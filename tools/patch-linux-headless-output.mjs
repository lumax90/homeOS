import {readFile,writeFile} from 'node:fs/promises';
import {join,resolve} from 'node:path';
import {createHash} from 'node:crypto';
const root=resolve(process.argv[2]??'');if(!process.argv[2])throw Error('Use <isolated released source directory>');
const path=join(root,'runloop.c'),before=await readFile(path,'utf8');
const anchor='         if (      (video_st->flags & VIDEO_FLAG_ACTIVE)\n               && !(video_st->current_video->frame == video_null.frame))\n            result |= RETRO_AV_ENABLE_VIDEO;';
if(before.indexOf(anchor)<0||before.indexOf(anchor)!==before.lastIndexOf(anchor))throw Error('Released AV capability anchor changed.');
// The encoder is a real output consumer even with no display or audio device attached.
const after=before.replace(anchor,anchor+'\n\n         if (recording_state_get_ptr()->data)\n            result |= RETRO_AV_ENABLE_VIDEO | RETRO_AV_ENABLE_AUDIO;');
await writeFile(path,after);
await writeFile(join(root,'home-linux-output.json'),JSON.stringify({adapter:'headless-output-1',source:'runloop.c',before:createHash('sha256').update(before).digest('hex'),after:createHash('sha256').update(after).digest('hex'),purpose:'Expose actual recording consumers to RETRO_ENVIRONMENT_GET_AUDIO_VIDEO_ENABLE; preserve later netplay/runahead masks'},null,2));
