param([string]$ReviewRoot=(Join-Path $env:LOCALAPPDATA 'Temp\opencode\native-gaming-runtime-v034-review'),[switch]$Graphics,[switch]$HostAdapter,[switch]$AllGraphics)
$ErrorActionPreference = 'Stop'
$source = Join-Path $ReviewRoot 'retroarch-source-1222'
$headers = Join-Path $ReviewRoot 'ffmpeg-headers-423'
$fixRoot = Join-Path $ReviewRoot 'retroarch-upstream-input-fix'
function Require-Hash([string]$Path, [string]$Sha256) {
    if ((Get-Item -LiteralPath $Path).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Reviewed build input cannot be a link.' }
    if ((Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $Sha256) { throw "Reviewed input changed: $Path" }
}
$sourceReceipt = Join-Path $source 'home-source-review-expanded.json'; if (-not (Test-Path -LiteralPath $sourceReceipt)) { $sourceReceipt = Join-Path $source 'home-source-review.json' }
if($AllGraphics){$sourceReceipt=Join-Path $source 'home-source-review-graphics.json'}
$manifest = Get-Content -LiteralPath $sourceReceipt -Raw | ConvertFrom-Json
$headerManifest = Get-Content -LiteralPath (Join-Path $headers 'home-source-review.json') -Raw | ConvertFrom-Json
$fix = Get-Content -LiteralPath (Join-Path $fixRoot 'home-fix-review.json') -Raw | ConvertFrom-Json
if ($manifest.commit -ne '69a4f0ea1e8aaf442ae4858f2e7f2b31a1776576' -or $headerManifest.commit -ne 'd3b963cc41824a3c5b2758ac896fb23e20a87875' -or $fix.commit -ne 'cfb1204314dd6b061a7b1cda77221735939bf5ba' -or $manifest.downloadedRomOrBios -or $headerManifest.downloadedRomOrBios) { throw 'Pinned released source and exact upstream fix required.' }
Require-Hash (Join-Path $fixRoot 'input_driver.c') $fix.sha256
function Reviewed-Tool([string]$Suffix, [string]$Id = 'mingw') {
    $review = Get-Content -LiteralPath (Join-Path (Join-Path $ReviewRoot $Id) 'review.json') -Raw | ConvertFrom-Json
    $file = @($review.files | Where-Object { $_.path.EndsWith($Suffix, [StringComparison]::OrdinalIgnoreCase) })
    if ($file.Count -ne 1) { throw 'Unique reviewed build tool required.' }
    $path = Join-Path (Join-Path (Join-Path $ReviewRoot $Id) 'files') $file[0].path; Require-Hash $path $file[0].sha256; return $path
}
$gcc = Reviewed-Tool '/bin/gcc.exe'; $make = Reviewed-Tool '/bin/mingw32-make.exe'
$shell = Reviewed-Tool '/bin/sh.exe' 'mingit'
$shellReview = Get-Content -LiteralPath (Join-Path $ReviewRoot 'mingit\review.json') -Raw | ConvertFrom-Json
foreach ($file in $shellReview.files) { Require-Hash (Join-Path (Join-Path $ReviewRoot 'mingit\files') $file.path) $file.sha256 }
$build = Join-Path $ReviewRoot ('retroarch-backport-build-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $build | Out-Null
foreach ($selection in @(@{root=$source; manifest=$manifest; prefix=''}, @{root=$headers; manifest=$headerManifest; prefix='ffmpeg'})) {
    foreach ($file in $selection.manifest.files) {
        $input = Join-Path $selection.root $file.path; Require-Hash $input $file.sha256
        $relative = $file.path; if ($selection.prefix) { $relative = Join-Path $selection.prefix $relative }
        $destination = Join-Path $build $relative
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination)) | Out-Null
        [IO.File]::Copy($input, $destination, $false)
    }
}
$inputFile = Join-Path $build 'input\input_driver.c'
$original = [IO.File]::ReadAllText($inputFile)
$upstream = [IO.File]::ReadAllText((Join-Path $fixRoot 'input_driver.c'))
$functionStart = $upstream.IndexOf('/* A sender sends one message for each control that changed.')
$signature = $upstream.IndexOf('static void input_remote_poll(', $functionStart)
if ($functionStart -lt 0 -or $signature -lt 0) { throw 'Pinned upstream receiver function not found.' }
$bodyStart = $upstream.IndexOf('{', $signature); $level = 0; $functionEnd = -1
for ($at = $bodyStart; $at -lt $upstream.Length; $at++) {
    if ($upstream[$at] -eq '{') { $level++ }
    if ($upstream[$at] -eq '}') { $level--; if ($level -eq 0) { $functionEnd = $at + 1; break } }
}
if ($functionEnd -lt 0) { throw 'Upstream receiver body is incomplete.' }
$receiver = $upstream.Substring($functionStart, $functionEnd - $functionStart)
if (-not $receiver.Contains('socket_receive_all_nonblocking') -or -not $receiver.Contains('INPUT_REMOTE_DRAIN_MAX 64')) { throw 'Unexpected upstream receiver semantics.' }
$oldStart = $original.IndexOf("#ifdef HAVE_NETWORKGAMEPAD`n   /* Poll remote */")
$oldEnd = $original.IndexOf('#ifdef HAVE_BSV_MOVIE', $oldStart)
if ($oldStart -lt 0 -or $oldEnd -lt 0 -or -not $original.Substring($oldStart, $oldEnd-$oldStart).Contains('errno != EAGAIN')) { throw 'Released receiver block changed.' }
$replacement = "#if defined(HAVE_NETWORKING) && defined(HAVE_NETWORKGAMEPAD)`n   if (input_st->remote)`n      input_remote_poll(input_st, settings, max_users);`n#endif`n"
$patched = $original.Substring(0,$oldStart) + $replacement + $original.Substring($oldEnd)
$insertion = $patched.IndexOf("`n#endif`n`nstatic int16_t input_state_device", [StringComparison]::Ordinal)
if ($insertion -lt 0) { throw 'Released input function boundary changed.' }
$patched = $patched.Insert($insertion, "`n"+$receiver+"`n")
[IO.File]::WriteAllText($inputFile, $patched, (New-Object Text.UTF8Encoding $false))
if($HostAdapter){& (Join-Path $env:LOCALAPPDATA 'Temp\opencode\runtimes\node-v24.21.0-win-x64\node.exe') (Join-Path $PSScriptRoot 'patch-native-host-adapter.mjs') $build;if($LASTEXITCODE-ne 0){throw 'Pinned Home input/mux adapter could not be applied.'}}
# Generated FFmpeg public architecture header, matching the reviewed 4.2.3 x64 Windows DLLs. Not emulator source.
[IO.File]::WriteAllText((Join-Path $build 'ffmpeg\libavutil\avconfig.h'), "#ifndef AVUTIL_AVCONFIG_H`n#define AVUTIL_AVCONFIG_H`n#define AV_HAVE_BIGENDIAN 0`n#define AV_HAVE_FAST_UNALIGNED 1`n#endif`n", (New-Object Text.UTF8Encoding $false))
$retro = Get-Content -LiteralPath (Join-Path $ReviewRoot 'retroarch\review.json') -Raw | ConvertFrom-Json
$dependencies = @()
foreach ($file in $retro.files | Where-Object { $_.path -match '\.dll$' }) {
    $input = Join-Path (Join-Path $ReviewRoot 'retroarch\files') $file.path; Require-Hash $input $file.sha256
    $destination = Join-Path $build ([IO.Path]::GetFileName($file.path)); [IO.File]::Copy($input,$destination,$false)
    $dependencies += [ordered]@{path=$destination;bytes=$file.bytes;sha256=$file.sha256}
}
$env:PATH = (Split-Path $gcc -Parent) + ';' + (Split-Path $shell -Parent) + ';' + $env:PATH
$forwardBuild = $build.Replace('\','/')
$arguments = @('-f','Makefile.win','-j4','V=1','HAVE_FFMPEG=1','HAVE_AVCODEC=1','HAVE_AVFORMAT=1','HAVE_AVUTIL=1','HAVE_AVDEVICE=0','HAVE_SWSCALE=1','HAVE_SWRESAMPLE=1','HAVE_NETWORKGAMEPAD=1','HAVE_BUILTINZLIB=1','HAVE_ZLIB=0','HAVE_7ZIP=0','HAVE_MENU=1','HAVE_RGUI=1','HAVE_OVERLAY=1','HAVE_MATERIALUI=0','HAVE_CG=0','HAVE_FREETYPE=0','HAVE_STB_FONT=0','HAVE_OPENGL=0','HAVE_D3D9=0','HAVE_DINPUT=0','HAVE_XINPUT=0','HAVE_WINMM=0','HAVE_DSOUND=0','HAVE_WASAPI=0','HAVE_XAUDIO=0','HAVE_CLOUDSYNC=0','HAVE_CHEEVOS=0','FFMPEG_LIBS=-lws2_32','CFLAGS=-O2 -std=gnu99 -DHAVE_NETWORKGAMEPAD -I. -Ideps -Iffmpeg -Wno-error=incompatible-pointer-types','LDCXXFLAGS=-static-libstdc++ -lole32 -lcomdlg32 -lgdi32')
# Match the released frontend's GCC10 treatment of older FFmpeg const-qualified APIs.
# Warnings stay visible in build.log and must be reviewed; no upstream codec/camera source is patched.
$arguments += 'UNAME=Windows','GIT_VERSION=69a4f0e+home-cfb12043'
if($Graphics){$arguments=@($arguments|Where-Object{$_ -ne 'HAVE_OPENGL=0'});$arguments+='HAVE_OPENGL=1','HAVE_OPENGL_CORE=1','HAVE_GLSL=1','HAVE_D3D11=0','HAVE_VULKAN=0'}
if($AllGraphics){
    $arguments=@($arguments|Where-Object{$_ -notin @('HAVE_D3D11=0','HAVE_VULKAN=0')});$arguments+='HAVE_D3D11=1','HAVE_VULKAN=1','HAVE_SLANG=1','HAVE_BUILTINGLSLANG=1','HAVE_SPIRV_CROSS=1','HAVE_SHADERPIPELINE=1'
    $d3d=Join-Path $build 'gfx\drivers\d3d11.c';$text=[IO.File]::ReadAllText($d3d);$text=$text.Replace('typedef ID3D11TracingDevice*            D3D11TracingDevice;','typedef struct ID3D11TracingDevice*     D3D11TracingDevice;');[IO.File]::WriteAllText($d3d,$text,(New-Object Text.UTF8Encoding $false))
    $vulkan=Get-Content -LiteralPath (Join-Path $ReviewRoot 'vulkan-headers\review.json') -Raw|ConvertFrom-Json
    if($vulkan.commit-ne 'acbb9391c0a597272800cd3f4363661c00a27c77'-or $vulkan.downloadedRomOrBios){throw 'Pinned Vulkan API headers required.'}
    foreach($file in $vulkan.files){$input=Join-Path (Join-Path $ReviewRoot 'vulkan-headers') $file.path;Require-Hash $input $file.sha256;$target=Join-Path (Join-Path $build 'vulkan-headers') $file.path;[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target))|Out-Null;[IO.File]::Copy($input,$target,$false)}
    $includes='-I. -Ideps -Iffmpeg -Ivulkan-headers/include -Igfx/include -Ideps/SPIRV-Cross -Ideps/glslang/glslang -Ideps/glslang/glslang/glslang/OSDependent/Windows -Ideps/glslang/glslang/OGLCompilersDLL -Ideps/glslang/glslang/glslang/MachineIndependent -Ideps/glslang/glslang/glslang/Public -Ideps/glslang/glslang/SPIRV'
    $arguments=@($arguments|Where-Object{-not $_.StartsWith('CFLAGS=')});$arguments+='CFLAGS=-O2 -std=gnu99 -DHAVE_NETWORKGAMEPAD '+$includes+' -Wno-error=incompatible-pointer-types'
    $arguments+='CXXFLAGS=-O2 -std=gnu++17 '+$includes+' -fpermissive';$arguments=@($arguments|Where-Object{-not $_.StartsWith('LDCXXFLAGS=')});$arguments+='LDCXXFLAGS=-static-libstdc++ -lole32 -lcomdlg32 -lgdi32 -ldxguid'
}
$arguments += 'SHELL=' + $shell.Replace('\','/')
foreach ($library in @(@('AVCODEC','avcodec-58.dll'),@('AVFORMAT','avformat-58.dll'),@('AVUTIL','avutil-56.dll'),@('AVDEVICE','avdevice-58.dll'),@('SWSCALE','swscale-5.dll'),@('SWRESAMPLE','swresample-3.dll'))) {
    $arguments += $library[0] + '_LIBS=' + $forwardBuild + '/' + $library[1]
}
$log = Join-Path $build 'build.log'
Push-Location $build
try {
    # Windows PowerShell treats native stderr as ErrorRecords. Preserve compiler output and judge the exit code.
    $ErrorActionPreference = 'Continue'; & $make @arguments *> $log; $resultCode = $LASTEXITCODE
} finally { $ErrorActionPreference = 'Stop'; Pop-Location }
if ($resultCode -ne 0) { Get-Content -LiteralPath $log -Tail 45; throw "Upstream backport build failed; all inputs preserved at $build" }
$binary = Join-Path $build 'retroarch.exe'
if (-not (Test-Path -LiteralPath $binary)) { throw 'Native frontend binary missing.' }
$receipt = [ordered]@{sourceVersion='1.22.2';sourceCommit=$manifest.commit;upstreamInputFix=$fix.commit;patchedPrototype=$true;changedSourceFiles=@('input/input_driver.c');patchedInputSha256=(Get-FileHash -LiteralPath $inputFile -Algorithm SHA256).Hash.ToLowerInvariant();executablePath=$binary;executableBytes=(Get-Item -LiteralPath $binary).Length;executableSha256=(Get-FileHash -LiteralPath $binary -Algorithm SHA256).Hash.ToLowerInvariant();dependencies=$dependencies;ffmpegVersion='4.2.3';ffmpegHeaderCommit=$headerManifest.commit;configuration=$arguments;graphicsBuild=[bool]$Graphics;renderingApis=@($(if($Graphics){'gl','glcore'}else{'null'}));launchedEmulator=$false;openedListener=$false;continuousInputVerified=$false;installedDriver=$false;reproducibleBuildVerified=$false}
if($HostAdapter){$receipt.homeHostAdapter='home-host-1';$receipt.hostAdapter=Get-Content -LiteralPath (Join-Path $build 'home-host-adapter.json') -Raw|ConvertFrom-Json;$receipt.changedSourceFiles=@('input/input_driver.c')+@($receipt.hostAdapter.changes|ForEach-Object{$_.path})|Select-Object -Unique}
if($AllGraphics){$receipt.renderingApis=@('gl','glcore','d3d11','vulkan');$receipt.changedSourceFiles=@($receipt.changedSourceFiles)+@('gfx/drivers/d3d11.c');$receipt.d3dTracingForwardDeclaration=[ordered]@{path='gfx/drivers/d3d11.c';sha256=(Get-FileHash -LiteralPath $d3d -Algorithm SHA256).Hash.ToLowerInvariant()}}
[IO.File]::WriteAllText((Join-Path $build 'home-frontend-build.json'),($receipt | ConvertTo-Json -Depth 8),(New-Object Text.UTF8Encoding $false))
$receipt | ConvertTo-Json -Depth 8
