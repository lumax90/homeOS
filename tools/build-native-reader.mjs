import {readFile,writeFile} from 'node:fs/promises';
import {createHash} from 'node:crypto';

// Preserve the pinned upstream WebRTC implementation; only supply per-reader scoped HTTP authentication.
const original=new URL('../web/public/native/reader.js',import.meta.url);
const bytes=await readFile(original);
const expected='a802f229b803c33713d4c69c4cc0d480108a5bf384947aeee4aaf04268bf85c1';
if(createHash('sha256').update(bytes).digest('hex')!==expected)throw new Error('Pinned MediaMTX reader changed.');
const source=bytes.toString('utf8'),binding='window.MediaMTXWebRTCReader = MediaMTXWebRTCReader;';
if(!source.endsWith(binding+'\n'))throw new Error('Unexpected upstream export binding.');
const output=`/* Generated Home HTTP adapter around MediaMTX 1.21.2 (${expected}).
 * Original MIT notice: MediaMTX-LICENSE.txt. Do not edit the pinned WebRTC source. */
(function(window) {
  window.HomeNativeWebRTCReader = function(conf) {
    const endpoint = new URL(conf.url);
    if (!['http:', 'https:'].includes(endpoint.protocol) || endpoint.origin !== window.location.origin || !/\\/game-native\\/[a-f0-9]{32}\\/whep$/.test(endpoint.pathname) || endpoint.search || endpoint.hash || endpoint.username || endpoint.password || !/^[a-f0-9]{64}$/i.test(conf.token)) throw new Error('Invalid Home WHEP scope');
    const fetch = async (input, init = {}) => {
      const address = new URL(String(input), endpoint);
      if (address.origin !== endpoint.origin || address.username || address.password || address.search || address.hash || !(address.pathname === endpoint.pathname || address.pathname.startsWith(endpoint.pathname + '/'))) throw new Error('WHEP request left its Home session');
      const headers = new Headers(init.headers);
      headers.set('Authorization', 'Bearer ' + conf.token);
      const response = await window.fetch(address.href, {...init, headers, credentials: 'same-origin', redirect: 'error'});
      if (init.method === 'POST' && response.status === 201) {
        const location = response.headers.get('Location');
        if (!location) throw new Error('Missing Home WHEP resource');
        const resource = new URL(location, endpoint);
        if (resource.origin !== endpoint.origin || resource.search || resource.hash || resource.username || resource.password || !resource.pathname.startsWith(endpoint.pathname + '/') || !/^[a-f0-9-]{32,36}$/i.test(resource.pathname.slice(endpoint.pathname.length + 1))) throw new Error('WHEP resource left its Home session');
      }
      return response;
    };
${source.slice(0,source.length-binding.length-1)}
    return new MediaMTXWebRTCReader(conf);
  };
})(window);
`;
await writeFile(new URL('../web/public/native/home-reader.js',import.meta.url),output);
console.log('Native reader: pinned upstream + session-scoped HTTP adapter');
