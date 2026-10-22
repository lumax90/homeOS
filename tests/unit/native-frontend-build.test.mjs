import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

test('frontend source review pins released source, matching codec headers and one upstream input fix without acquiring ROMs', async () => {
  const stage = await readFile(new URL('../../tools/stage-native-frontend-source.mjs', import.meta.url), 'utf8');
  assert.match(stage, /69a4f0ea1e8aaf442ae4858f2e7f2b31a1776576/);
  assert.match(stage, /d3b963cc41824a3c5b2758ac896fb23e20a87875/);
  assert.match(stage, /cfb1204314dd6b061a7b1cda77221735939bf5ba/);
  assert.match(stage, /gitHash\(bytes\) !== entry\.sha/);
  assert.match(stage, /downloadedRomOrBios: false/);
  assert.doesNotMatch(stage, /releases\/latest|\/master\/|\.zip|\.7z|\.tar/);
});
test('frontend build is a separate labeled prototype and cannot install or promote itself', async () => {
  const build = await readFile(new URL('../../tools/build-native-frontend.ps1', import.meta.url), 'utf8');
  assert.match(build, /retroarch-backport-build-/);
  assert.match(build, /patchedPrototype=\$true/);
  assert.match(build, /changedSourceFiles=@\('input\/input_driver\.c'\)/);
  assert.match(build, /socket_receive_all_nonblocking/);
  assert.match(build, /INPUT_REMOTE_DRAIN_MAX 64/);
  assert.match(build, /continuousInputVerified=\$false/);
  assert.match(build, /HAVE_DINPUT=0/); assert.match(build, /HAVE_XINPUT=0/);
  assert.doesNotMatch(build, /New-NetFirewallRule|Start-Service|RunAs|pnputil|devcon|native-runtime-profile\.json/);
});
test('native input uses valid bounded releases and the stronger probe observes held and simultaneous keys', async () => {
  const [protocol, probe] = await Promise.all([
    readFile(new URL('../../server/HomeServer.Api/Gaming/RetroPadProtocol.cs', import.meta.url), 'utf8'),
    readFile(new URL('../../tools/Home.NativeProbe/Program.cs', import.meta.url), 'utf8'),
  ]);
  assert.match(protocol, /yield return Button\(id,\s*false,\s*allowed\)/); assert.doesNotMatch(protocol, /Neutral\(\) => \[0\]/);
  assert.match(probe, /ConsecutiveHeldFrames < 10/); assert.match(probe, /WaitFixtureKeys\(0x03ec/);
  assert.match(probe, /!loopback&&!NativeLocalInputGuard\.InspectPorts\(executable,\[62401,62406\]\)/);
  assert.match(probe, /var loopback=frontend\?\.HostAdapter\?\.LoopbackOnly==true/);
  assert.doesNotMatch(probe, /RetroPadProtocol\.Neutral|Replace\("input_player1_joypad_index/);
});
test('a new frontend firewall rule requires separate approval and never reuses or changes the earlier executable rule', async () => {
  const guard = await readFile(new URL('../../tools/install-native-frontend-guard.ps1', import.meta.url), 'utf8');
  assert.match(guard, /if \(-not \$Approved\)/); assert.match(guard, /-Verb RunAs/);
  assert.match(guard, /Home\.NativePrototype\.UpstreamInput\./); assert.match(guard, /Get-FileHash/);
  assert.match(guard, /-Direction Inbound -Action Block/); assert.match(guard, /-LocalPort 62401,62406/);
  assert.match(guard, /changedOtherRules=\$false/);
  assert.doesNotMatch(guard, /Set-NetFirewallRule|Remove-NetFirewallRule|Set-NetFirewallProfile|New-Service|pnputil|devcon|Restart-Computer/);
});
test('Home native manager smoke requires matching full-core proof rather than promoting the source-only harness', async () => {
  const probe = await readFile(new URL('../../tools/Home.NativeHomeProbe/Program.cs', import.meta.url), 'utf8');
  assert.match(probe, /GetProperty\("frontendSha256"\).*frontend\.ExecutableSha256/);
  assert.match(probe, /GetProperty\("coreSha256"\).*core\.CoreSha256/);
  assert.match(probe, /GetProperty\("consecutiveHeldFrames"\).*<10/);
  assert.match(probe, /source harness evidence is insufficient/);
  assert.match(probe, /repeated\.InputEpoch!=running\.InputEpoch/);
  assert.match(probe, /nativeSnapshotRestored=second\.HasSave/);
  assert.match(probe, /browserDecodeVerified=false/);
});
