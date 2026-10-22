import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

test('driver review pins software archives and signatures but does not install or alter boot trust', async () => {
  const source = await readFile(new URL('../../tools/stage-native-display-review.ps1', import.meta.url), 'utf8');
  assert.match(source, /e24210692b442b39af763536330ce78b423f19342b7a7792c26de3944e418b3a/);
  assert.match(source, /Get-AuthenticodeSignature/); assert.match(source, /Existing extracted file differs from the pinned archive/);
  assert.doesNotMatch(source, /Start-Process|pnputil|bcdedit|Import-Certificate|Enable-PnpDevice|Disable-PnpDevice/);
});
test('DevCon review validates the official Microsoft CAB and its binary as separate identities', async () => {
  const source = await readFile(new URL('../../tools/stage-devcon-review.ps1', import.meta.url), 'utf8');
  assert.match(source, /download\.microsoft\.com/); assert.match(source, /6ecfaf7042c525ddb6c0bf0501becfbce85cbdf2a33b1bca67734fea567af3b8/);
  assert.match(source, /a92d4bc174079df703512e778e3192f524e4d0e33fc0ef718acc43c29e3d6e30/);
  assert.match(source, /Microsoft Corporation/); assert.doesNotMatch(source, /Devcon\.Installer\.exe|Start-Process|-update|-Force/);
});
test('display installation requires explicit consent, normal UAC and exact files; no automatic reboot or other driver installation', async () => {
  const source = await readFile(new URL('../../tools/install-native-display.ps1', import.meta.url), 'utf8');
  assert.match(source, /if \(-not \$Approved\)/); assert.match(source, /-Verb RunAs/); assert.match(source, /Root\\MttVDD/);
  assert.match(source, /Existing C:|existing C:/i); assert.match(source, /Refusing to overwrite a previous installation receipt/);
  assert.match(source, /primaryDisplayChangeRequested = \$false/); assert.match(source, /gamepadDriverInstalled = \$false/);
  assert.doesNotMatch(source, /Restart-Computer|shutdown\.exe|bcdedit|Import-Certificate|Set-Display|Win\s*\+\s*P|ViGEmBus.*install/);
});
