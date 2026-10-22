param([string]$CacheRoot, [string]$OutputRoot, [ValidateSet('tiny-crate','wrap-it-up')][string]$GameId)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (-not $CacheRoot) { $CacheRoot = Join-Path $root 'artifacts\games\cache' }
if (-not $OutputRoot) { $OutputRoot = Join-Path $root 'server\HomeServer.Api\GamePacks' }
$null = New-Item -ItemType Directory -Force -Path $CacheRoot, $OutputRoot
function Download-CodeDependency($url, $path, $hash) {
  if (-not (Test-Path -LiteralPath $path)) {
    & 'C:\Windows\System32\curl.exe' --fail --location --silent --show-error --retry 2 --output "$path.partial" $url
    if ($LASTEXITCODE) { throw "Code dependency download failed: $url" }
    Move-Item -LiteralPath "$path.partial" -Destination $path
  }
  $actual = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
  if ($hash -and $actual -ne $hash) { throw "Code dependency hash mismatch: $path" }
  return $actual
}
$editorArchive = Join-Path $CacheRoot 'godot-3.6.1-win64.zip'
$templateArchive = Join-Path $CacheRoot 'godot-3.6.1-templates.zip'
$null = Download-CodeDependency 'https://github.com/godotengine/godot-builds/releases/download/3.6.1-stable/Godot_v3.6.1-stable_win64.exe.zip' $editorArchive 'a0500fdd6c57cd463c89334bf2b40f040b16309a6ca768196fe32eb2a8467852'
$null = Download-CodeDependency 'https://github.com/godotengine/godot-builds/releases/download/3.6.1-stable/Godot_v3.6.1-stable_export_templates.tpz' $templateArchive '0c5661fc40d642b0c54cfee8bf975dd100109b82610ce5afb9af37b4f3cccdc5'
$editorFolder = Join-Path $CacheRoot 'godot-3.6.1'
if (-not (Test-Path $editorFolder)) { Expand-Archive -LiteralPath $editorArchive -DestinationPath $editorFolder }
$editor = Join-Path $editorFolder 'Godot_v3.6.1-stable_win64.exe'
$template = Join-Path $CacheRoot 'web-release.zip'
if (-not (Test-Path $template)) {
  Add-Type -AssemblyName System.IO.Compression.FileSystem
  $zip = [IO.Compression.ZipFile]::OpenRead($templateArchive)
  try { $entry = $zip.Entries | Where-Object { $_.FullName -eq 'templates/webassembly_release.zip' }; if (-not $entry) { throw 'Single-threaded web template missing.' }; [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $template, $false) } finally { $zip.Dispose() }
}
$font = Join-Path $CacheRoot 'Roboto-Regular.ttf'
$fontLicense = Join-Path $CacheRoot 'Roboto-LICENSE.txt'
$null = Download-CodeDependency 'https://raw.githubusercontent.com/googlefonts/roboto/v2.138/src/hinted/Roboto-Regular.ttf' $font '56a45233d29f11b4dfb86d248e921939d115778f87325e7ae8cc108383d6664d'
$null = Download-CodeDependency 'https://raw.githubusercontent.com/googlefonts/roboto/v2.138/LICENSE' $fontLicense 'c71d239df91726fc519c6eb72d318ec65820627232b2f796219e87dcf35d0ab4'
$games = @(
  @{id='tiny-crate';repo='HarmonyHoney/tiny_crate';commit='c9b835c46ac5a98cf2aaaa0ee824378af14ff003';title='Tiny Crate Classic';projectName='Tiny Crate';license='Unlicense';licenseFile='UNLICENSE';cover='media/image/title_splash.png';backdrop='media/image/screens/1.png';aspect='16/9';genres=@('platformer','puzzle');savePaths=@('save/0/box.save','save/1/box.save','save/2/box.save','save/3/box.save','save/4/box.save','save/5/box.save');source='https://harmonymonroe.itch.io/tinycrate'},
  @{id='wrap-it-up';repo='HarmonyHoney/wrap_it_up';commit='ac8bb8d0586e49cfd7a1b0039ce540b6fdda3f1a';title='Wrap It Up';projectName='Wrap It Up';license='MIT';licenseFile='LICENSE';cover='media/image/UI/title_splash.png';backdrop='media/image/screenshot/shot1.png';aspect='1/1';genres=@('platformer','arcade');savePaths=@('home-progress.json');source='https://harmonymonroe.itch.io/wrap-it-up'}
)
foreach ($game in $games) {
  if ($GameId -and $game.id -ne $GameId) { continue }
  $archive = Join-Path $CacheRoot ($game.id + '-' + $game.commit + '.zip')
  $sourceHash = Download-CodeDependency "https://codeload.github.com/$($game.repo)/zip/$($game.commit)" $archive $null
  $work = Join-Path $root ('artifacts\games\build-' + $game.id + '-' + [guid]::NewGuid().ToString('N'))
  Expand-Archive -LiteralPath $archive -DestinationPath $work
  $project = (Get-ChildItem -LiteralPath $work -Directory | Select-Object -First 1).FullName
  $projectFile = Join-Path $project 'project.godot'
  $text = [IO.File]::ReadAllText($projectFile)
  $text = $text.Replace('[autoload]', "[autoload]`n`nHomeBridge=`"*res://home/HomeBridge.gd`"")
  $text = $text.Replace('window/size/fullscreen=true','window/size/fullscreen=false').Replace('size/fullscreen=true','size/fullscreen=false')
  $text = $text.Replace('[rendering]', "[rendering]`n`nquality/driver/driver_name=`"GLES2`"`nvram_compression/import_etc=true")
  [IO.File]::WriteAllText($projectFile, $text)
  $homeFolder = Join-Path $project 'home'
  $null = New-Item -ItemType Directory -Path $homeFolder
  Copy-Item (Join-Path $PSScriptRoot 'games\HomeBridge.gd') $homeFolder
  # Remove unreviewed font bundles from this generated work tree. Every actual reference uses the audited Apache-2.0 font.
  Get-ChildItem -LiteralPath $project -Recurse -File -Include '*.tscn','*.tres' | ForEach-Object {
    $content = [IO.File]::ReadAllText($_.FullName)
    $replaced = [regex]::Replace($content, 'res://media/font/[^"\r\n]+\.(?:ttf|otf|woff2?)', 'res://home/Roboto-Regular.ttf')
    if ($content -ne $replaced) { [IO.File]::WriteAllText($_.FullName, $replaced) }
  }
  Copy-Item -LiteralPath $font -Destination (Join-Path $homeFolder 'Roboto-Regular.ttf')
  if ($game.id -eq 'tiny-crate') {
    # Upstream's tool property setter reads a runtime autoload in the editor. Preserve gameplay, guard only editor evaluation.
    $keyScript = Join-Path $project 'src\menu\options\Key.gd'
    $keyCode = [IO.File]::ReadAllText($keyScript)
    if (-not $keyCode.Contains('func set_is_connect(arg := is_connect):')) { throw 'Pinned Tiny Crate adapter site changed.' }
    [IO.File]::WriteAllText($keyScript, $keyCode.Replace('func set_is_connect(arg := is_connect):', "func set_is_connect(arg := is_connect):`n`tif Engine.editor_hint: return"))
  }
  $build = Join-Path $work 'web'
  $null = New-Item -ItemType Directory -Path $build
  $preset = @"
[preset.0]
name="Home Web"
platform="HTML5"
runnable=true
export_filter="all_resources"
include_filter=""
exclude_filter="media/font/*.ttf,media/font/*.otf,media/font/*.woff,media/font/*.woff2,export/*,linux/*,media/image/screens/steam/*"
export_path="$($build.Replace('\','/'))/index.html"
script_export_mode=1
[preset.0.options]
custom_template/debug="$($template.Replace('\','/'))"
custom_template/release="$($template.Replace('\','/'))"
variant/export_type=0
vram_texture_compression/for_desktop=true
vram_texture_compression/for_mobile=true
html/export_icon=false
html/custom_html_shell=""
html/canvas_resize_policy=2
html/focus_canvas_on_start=true
progressive_web_app/enabled=false
"@
  [IO.File]::WriteAllText((Join-Path $project 'export_presets.cfg'), $preset)
  $process = Start-Process -FilePath $editor -ArgumentList @('--no-window','--path',"`"$project`"",'--editor','--quit') -Wait -PassThru -RedirectStandardOutput (Join-Path $work 'import.log') -RedirectStandardError (Join-Path $work 'import-error.log')
  if ($process.ExitCode) { throw "Godot import failed: $work" }
  # The first export completes Godot 3's import queue. Publish only a fresh-process export after that cache exists.
  $process = Start-Process -FilePath $editor -ArgumentList @('--no-window','--path',"`"$project`"",'--export','"Home Web"',"`"$build\index.html`"") -Wait -PassThru -RedirectStandardOutput (Join-Path $work 'warmup-export.log') -RedirectStandardError (Join-Path $work 'warmup-export-error.log')
  if ($process.ExitCode) { throw "Godot warmup export failed: $work" }
  $process = Start-Process -FilePath $editor -ArgumentList @('--no-window','--path',"`"$project`"",'--export','"Home Web"',"`"$build\index.html`"") -Wait -PassThru -RedirectStandardOutput (Join-Path $work 'export.log') -RedirectStandardError (Join-Path $work 'export-error.log')
  if ($process.ExitCode -or -not (Test-Path (Join-Path $build 'index.wasm')) -or -not (Test-Path (Join-Path $build 'index.pck'))) { throw "Godot export failed: $work" }
  if (Select-String -LiteralPath (Join-Path $work 'export-error.log') -Pattern 'SCRIPT ERROR:|^ERROR:' -Quiet) { throw "Game source did not pass clean-export checks: $work" }
  Copy-Item (Join-Path $PSScriptRoot 'games\index.html'),(Join-Path $PSScriptRoot 'games\home-game.js'),(Join-Path $PSScriptRoot 'games\home-game.css') -Destination $build -Force
  $node = (Get-Command node -ErrorAction SilentlyContinue).Source
  if (-not $node) { $node = Join-Path $env:LOCALAPPDATA 'Temp\opencode\runtimes\node-v24.21.0-win-x64\node.exe' }
  & $node (Join-Path $PSScriptRoot 'games\patch-godot-runtime.mjs') (Join-Path $build 'index.js')
  if ($LASTEXITCODE) { throw 'Godot runtime adapter verification failed.' }
  & $node (Join-Path $PSScriptRoot 'games\verify-game-pack.mjs') (Join-Path $build 'index.pck')
  if ($LASTEXITCODE) { throw 'Game pack runtime dependency verification failed.' }
  Copy-Item (Join-Path $project $game.cover) (Join-Path $build 'cover.png')
  Copy-Item (Join-Path $project $game.backdrop) (Join-Path $build 'backdrop.png')
  $licenses = Join-Path $build 'licenses'
  $null = New-Item -ItemType Directory -Path $licenses
  Copy-Item (Join-Path $project $game.licenseFile) (Join-Path $licenses 'Game.txt')
  Copy-Item $fontLicense (Join-Path $licenses 'Roboto.txt')
  $null = Download-CodeDependency 'https://raw.githubusercontent.com/godotengine/godot/3.6.1-stable/LICENSE.txt' (Join-Path $licenses 'Godot.txt') $null
  $null = Download-CodeDependency 'https://raw.githubusercontent.com/godotengine/godot/3.6.1-stable/COPYRIGHT.txt' (Join-Path $licenses 'Godot-third-party.txt') $null
  $manifest = [ordered]@{id=$game.id;title=$game.title;publisher='Harmony Monroe';sourceUrl=$game.source;sourceRepository="https://github.com/$($game.repo)";sourceCommit=$game.commit;sourceArchiveSha256=$sourceHash;version='classic-home.1';homeRuntimeRevision=2;engine='Godot 3.6.1';license=$game.license;kind='web';aspect=$game.aspect;genres=$game.genres;savePaths=$game.savePaths;files=@()}
  $manifest.files = @(Get-ChildItem $build -File -Recurse | ForEach-Object { [pscustomobject][ordered]@{path=$_.FullName.Substring($build.Length + 1).Replace('\','/');size=$_.Length;sha256=(Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()} })
  $manifest | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $build 'manifest.json') -Encoding UTF8
  $destination = Join-Path $OutputRoot $game.id
  if (Test-Path $destination) { throw "Game pack already exists; inspect it instead of overwriting: $destination" }
  Move-Item -LiteralPath $build -Destination $destination
  [ordered]@{game=$game.id;sourceHash=$sourceHash;files=$manifest.files.Count;bytes=($manifest.files | Measure-Object -Property size -Sum).Sum;work=$work;destination=$destination} | ConvertTo-Json
}
