param([string]$ReviewRoot=(Join-Path $env:LOCALAPPDATA 'Temp\opencode\native-gaming-runtime-v034-review'),[ValidateSet('gl','glcore','d3d11','vulkan')][string]$Renderer='glcore')
$ErrorActionPreference='Stop'
$review=Get-Content -LiteralPath (Join-Path $ReviewRoot 'mingw\review.json') -Raw|ConvertFrom-Json
$entry=@($review.files|Where-Object{$_.path.EndsWith('/bin/gcc.exe')});if($entry.Count-ne 1){throw 'Unique reviewed compiler required.'}
$compiler=Join-Path (Join-Path $ReviewRoot 'mingw\files') $entry[0].path
if((Get-FileHash -LiteralPath $compiler -Algorithm SHA256).Hash.ToLowerInvariant()-ne $entry[0].sha256){throw 'Reviewed compiler changed.'}
$source=Join-Path $PSScriptRoot '..\tests\native\SyntheticGpu.c';$source=[IO.Path]::GetFullPath($source)
$header=Join-Path $ReviewRoot 'retroarch-source-1222\libretro-common\include\libretro.h'
$manifest=Get-Content -LiteralPath (Join-Path $ReviewRoot 'retroarch-source-1222\home-source-review-expanded.json') -Raw|ConvertFrom-Json
$info=@($manifest.files|Where-Object{$_.path-eq 'libretro-common/include/libretro.h'})
if($info.Count-ne 1-or (Get-FileHash -LiteralPath $header -Algorithm SHA256).Hash.ToLowerInvariant()-ne $info[0].sha256){throw 'Pinned libretro ABI header required.'}
$root=Join-Path $ReviewRoot ('gpu-fixture-build-'+[Guid]::NewGuid().ToString('N'));[IO.Directory]::CreateDirectory($root)|Out-Null
$target=Join-Path $root 'home_gpu_fixture_libretro.dll'
$env:PATH=(Split-Path $compiler -Parent)+';'+$env:PATH
$arguments=@('-shared','-O2','-std=gnu99','-static-libgcc',('-I'+(Split-Path $header -Parent)),('-I'+(Join-Path $ReviewRoot 'vulkan-headers\include')),$source,'-o',$target)
if($Renderer-eq 'd3d11'){$arguments+='-DHOME_DIAG_D3D11'}elseif($Renderer-eq 'vulkan'){$arguments+='-DHOME_DIAG_VULKAN'}elseif($Renderer-eq 'gl'){$arguments+='-DHOME_DIAG_GL'}
& $compiler @arguments
if($LASTEXITCODE-ne 0){throw 'Original isolated GPU fixture compile failed.'}
$receipt=[ordered]@{source='tests/native/SyntheticGpu.c';sourceSha256=(Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant();backendSourceSha256=(Get-FileHash -LiteralPath (Join-Path (Split-Path $source -Parent) 'SyntheticGpuBackends.h') -Algorithm SHA256).Hash.ToLowerInvariant();renderer=$Renderer;binaryPath=$target;bytes=(Get-Item -LiteralPath $target).Length;sha256=(Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant();syntheticDiagnosticOnly=$true;downloadedRomOrBios=$false;normalLibrary=$false}
[IO.File]::WriteAllText((Join-Path $root 'fixture.json'),($receipt|ConvertTo-Json),(New-Object Text.UTF8Encoding $false));$receipt|ConvertTo-Json
