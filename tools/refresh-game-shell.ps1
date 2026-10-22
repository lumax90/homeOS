param([string]$PackageRoot)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (-not $PackageRoot) { $PackageRoot = Join-Path $root 'server\HomeServer.Api\GamePacks' }
$node = (Get-Command node -ErrorAction SilentlyContinue).Source
if (-not $node) { $node = Join-Path $env:LOCALAPPDATA 'Temp\opencode\runtimes\node-v24.21.0-win-x64\node.exe' }
foreach ($id in @('tiny-crate','wrap-it-up')) {
  $folder = Join-Path $PackageRoot $id
  $manifestPath = Join-Path $folder 'manifest.json'
  $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
  if ($manifest.id -ne $id -or $manifest.kind -ne 'web' -or $manifest.version -ne 'classic-home.1') { throw 'Unexpected game package; do not overwrite an unfamiliar build.' }
  foreach ($asset in $manifest.files) {
    $path = Join-Path $folder $asset.path
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $asset.sha256) { throw "Existing game asset changed: $id/$($asset.path)" }
  }
  foreach ($file in @('index.html','home-game.js','home-game.css')) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot "games\$file") -Destination (Join-Path $folder $file) -Force }
  & $node (Join-Path $PSScriptRoot 'games\patch-godot-runtime.mjs') (Join-Path $folder 'index.js')
  if ($LASTEXITCODE) { throw 'Godot runtime adapter verification failed.' }
  $manifest | Add-Member -NotePropertyName homeRuntimeRevision -NotePropertyValue 2 -Force
  $manifest.files = @(Get-ChildItem -LiteralPath $folder -File -Recurse | Where-Object { $_.FullName -ne $manifestPath } | ForEach-Object { [pscustomobject]@{path=$_.FullName.Substring($folder.Length + 1).Replace('\','/');size=$_.Length;sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()} })
  $manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
  Write-Output "${id}: reviewed shell refreshed; game data and save version unchanged."
}
