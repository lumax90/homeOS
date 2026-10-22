param([string]$ReviewRoot = (Join-Path $env:LOCALAPPDATA 'Temp\opencode\native-gaming-runtime-v034-review'))
$ErrorActionPreference = 'Stop'
$source = Join-Path $ReviewRoot 'mgba-source-0105'
$manifest = Get-Content -LiteralPath (Join-Path $source 'home-source-review.json') -Raw | ConvertFrom-Json
if ($manifest.commit -ne '26b7884bc25a5933960f3cdcd98bac1ae14d42e2' -or $manifest.version -ne '0.10.5' -or $manifest.downloadedRomOrBios) { throw 'A reviewed, released mGBA source snapshot is required.' }
foreach ($file in $manifest.files) {
    $path = Join-Path $source $file.path
    if ((Get-Item -LiteralPath $path).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Source file cannot be a link.' }
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $file.sha256) { throw "Source file changed: $($file.path)" }
}
function Tool-Path([string]$Id, [string]$Suffix) {
    $root = Join-Path $ReviewRoot $Id
    $review = Get-Content -LiteralPath (Join-Path $root 'review.json') -Raw | ConvertFrom-Json
    $file = @($review.files | Where-Object { $_.path.EndsWith($Suffix, [StringComparison]::OrdinalIgnoreCase) })
    if ($file.Count -ne 1) { throw "A unique reviewed tool is required: $Suffix" }
    $path = Join-Path (Join-Path $root 'files') $file[0].path
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $file[0].sha256) { throw "Tool changed: $Id" }
    return $path
}
$cmake = Tool-Path 'cmake' '/bin/cmake.exe'
$gcc = Tool-Path 'mingw' '/bin/gcc.exe'
$gpp = Tool-Path 'mingw' '/bin/g++.exe'
$make = Tool-Path 'mingw' '/bin/mingw32-make.exe'
$env:PATH = (Split-Path $gcc -Parent) + ';' + $env:PATH
$build = Join-Path $ReviewRoot ('mgba-build-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $build | Out-Null
$configuration = @('-S', $source, '-B', $build, '-G', 'MinGW Makefiles', "-DCMAKE_C_COMPILER=$gcc", "-DCMAKE_CXX_COMPILER=$gpp", "-DCMAKE_MAKE_PROGRAM=$make", '-DCMAKE_BUILD_TYPE=Release', '-DBUILD_LIBRETRO=ON', '-DSKIP_LIBRARY=ON', '-DBUILD_QT=OFF', '-DBUILD_SDL=OFF', '-DDISABLE_FRONTENDS=ON', '-DDISABLE_DEPS=ON', '-DUSE_FFMPEG=OFF', '-DUSE_LUA=OFF', '-DUSE_LIBZIP=OFF', '-DUSE_LZMA=OFF', '-DUSE_ZLIB=OFF', '-DUSE_PNG=OFF', '-DUSE_SQLITE3=OFF', '-DUSE_DISCORD_RPC=OFF', '-DENABLE_GDB_STUB=OFF', '-DCMAKE_SHARED_LINKER_FLAGS=-static-libgcc -static-libstdc++')
& $cmake @configuration
if ($LASTEXITCODE -ne 0) { throw 'Released mGBA core configuration failed; source was preserved.' }
& $cmake --build $build --target mgba_libretro --parallel 4
if ($LASTEXITCODE -ne 0) { throw 'Released mGBA core build failed; source was preserved.' }
$binary = Join-Path $build 'mgba_libretro.dll'
if (-not (Test-Path -LiteralPath $binary)) { throw 'Expected libretro core was not produced.' }
$result = [ordered]@{ sourceVersion = '0.10.5'; sourceCommit = $manifest.commit; license = 'MPL-2.0'; corePath = $binary; coreSha256 = (Get-FileHash -LiteralPath $binary -Algorithm SHA256).Hash.ToLowerInvariant(); coreBytes = (Get-Item -LiteralPath $binary).Length; configuration = $configuration; downloadedRomOrBios = $false; changedUpstreamSource = $false; reproducibleBuildVerified = $false }
[IO.File]::WriteAllText((Join-Path $build 'home-core-build.json'), ($result | ConvertTo-Json -Depth 6), (New-Object Text.UTF8Encoding $false))
$result | ConvertTo-Json -Depth 6
