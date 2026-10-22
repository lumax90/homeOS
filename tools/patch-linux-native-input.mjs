import {readFile,writeFile} from 'node:fs/promises';
import {join,resolve} from 'node:path';
import {createHash} from 'node:crypto';

const root=resolve(process.argv[2]??'');if(!process.argv[2])throw Error('Use <isolated source root>');
const upstream=await readFile(join(root,'input-driver-upstream.c'),'utf8');if(createHash('sha256').update(upstream).digest('hex')!=='558cc50291b96e5319a44891e2c36af427ca4b60e05d0ed9e2af2733fa94ff0e')throw Error('Upstream receiver pin mismatch.');
const path=join(root,'retroarch/input/input_driver.c');let original=await readFile(path,'utf8');
const start=upstream.indexOf('/* A sender sends one message for each control that changed.'),signature=upstream.indexOf('static void input_remote_poll(',start),body=upstream.indexOf('{',signature);let depth=0,end=-1;
for(let i=body;i<upstream.length;i++){if(upstream[i]==='{')depth++;if(upstream[i]==='}'&&--depth===0){end=i+1;break;}}
if(start<0||signature<0||body<0||end<0)throw Error('Pinned upstream receiver missing.');const receiver=upstream.slice(start,end);
if(!receiver.includes('socket_receive_all_nonblocking')||!receiver.includes('INPUT_REMOTE_DRAIN_MAX 64'))throw Error('Unexpected receiver semantics.');
const old=original.indexOf('#ifdef HAVE_NETWORKGAMEPAD\n   /* Poll remote */'),next=original.indexOf('#ifdef HAVE_BSV_MOVIE',old);if(old<0||next<0||!original.slice(old,next).includes('errno != EAGAIN'))throw Error('Released source anchor changed.');
original=original.slice(0,old)+'#if defined(HAVE_NETWORKING) && defined(HAVE_NETWORKGAMEPAD)\n   if (input_st->remote)\n      input_remote_poll(input_st, settings, max_users);\n#endif\n'+original.slice(next);
const insert=original.indexOf('\n#endif\n\nstatic int16_t input_state_device');if(insert<0)throw Error('Input source boundary changed.');original=original.slice(0,insert)+'\n'+receiver+'\n'+original.slice(insert);await writeFile(path,original);
