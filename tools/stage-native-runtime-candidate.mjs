import { readFile, mkdir, writeFile } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import { join, resolve } from 'node:path';

// Publish only a prepared runtime descriptor. No game import, runtime enable, download or OS changes.
const flags = ['--review-root', '--core-build', '--frontend-build', '--input-proof', '--state-path'];
if (process.argv.length !== 12 || flags.some((flag,index) => process.argv[2+index*2] !== flag)) throw new Error('Use --review-root <cache> --core-build <receipt> --frontend-build <receipt> --input-proof <passed native report> --state-path <Home state>');
const [root, corePath, frontendPath, proofPath, state] = flags.map((_,index) => resolve(process.argv[3+index*2]));
const core = JSON.parse(await readFile(corePath,'utf8')), frontend = JSON.parse(await readFile(frontendPath,'utf8'));
const proofBytes = await readFile(proofPath), proof = JSON.parse(proofBytes);
const retro = JSON.parse(await readFile(join(root,'retroarch','review.json'),'utf8')), media = JSON.parse(await readFile(join(root,'mediamtx','review.json'),'utf8'));
if (core.sourceVersion !== '0.10.5' || core.sourceCommit !== '26b7884bc25a5933960f3cdcd98bac1ae14d42e2' || frontend.sourceVersion !== '1.22.2' || frontend.sourceCommit !== '69a4f0ea1e8aaf442ae4858f2e7f2b31a1776576' || frontend.upstreamInputFix !== 'cfb1204314dd6b061a7b1cda77221735939bf5ba' || !frontend.patchedPrototype) throw new Error('Exact reviewed released-core/upstream-input prototype required.');
if (proof.error !== null || proof.frontendSha256 !== frontend.executableSha256 || proof.coreSha256 !== core.coreSha256 || !proof.streaming?.simultaneousHeldInputVerified || proof.streaming.held?.consecutiveHeldFrames < 10 || !proof.streaming.nativeSaveRestoreVerified || !proof.streaming.windowless || !proof.streaming.privateInputGuard || proof.streaming.whepStatus !== 201 || !proof.streaming.negotiatedH264Opus) throw new Error('Matching full native input/stream/save proof required.');
const sha = bytes => createHash('sha256').update(bytes).digest('hex');
const asset = (review,id,name) => { const entries=review.files.filter(file => file.path.split('/').at(-1) === name); if(entries.length!==1)throw new Error('Unique reviewed asset required: '+name); const file=entries[0]; return {path:join(root,id,'files',file.path),bytes:file.bytes,sha256:file.sha256}; };
const profile = {format:1,enabled:false,coreVersion:core.sourceVersion,coreCommit:core.sourceCommit,retroArch:{path:frontend.executablePath,bytes:frontend.executableBytes,sha256:frontend.executableSha256},core:{path:core.corePath,bytes:core.coreBytes,sha256:core.coreSha256},coreInfo:asset(retro,'retroarch','mgba_libretro.info'),mediaMtx:asset(media,'mediamtx','mediamtx.exe'),dependencies:frontend.dependencies,continuousInputVerified:true};
for (const file of [...profile.dependencies,profile.retroArch,profile.core,profile.coreInfo,profile.mediaMtx]) { const bytes=await readFile(file.path); if(bytes.length!==file.bytes || sha(bytes)!==file.sha256)throw new Error('Reviewed runtime file changed: '+file.path); }
const candidate = {format:1,frontendVersion:frontend.sourceVersion,frontendCommit:frontend.sourceCommit,inputFix:frontend.upstreamInputFix,mediaMtxVersion:'1.21.2',profile,proof:{path:proofPath,bytes:proofBytes.length,sha256:sha(proofBytes)}};
const text=JSON.stringify(candidate,null,2); if(Buffer.byteLength(text)>128*1024)throw new Error('Candidate descriptor too large.');
await mkdir(join(state,'games'),{recursive:true});const target=join(state,'games','native-runtime-candidate.json');
try { await writeFile(target,text,{flag:'wx'}); } catch(error) { if(error.code!=='EEXIST'||await readFile(target,'utf8')!==text)throw error; }
console.log(JSON.stringify({prepared:true,enabled:false,launchedGame:false,downloadedRomOrBios:false,changedFirewall:false,statePath:state}));
