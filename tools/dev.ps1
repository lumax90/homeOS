param(
  [ValidateSet('Server', 'Web')][string]$Mode = 'Server',
  [switch]$Preview,
  [switch]$Test,
  [ValidateRange(1, 65535)][int]$Port = 5080
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$runtimes = Join-Path $env:LOCALAPPDATA 'Temp\opencode\runtimes'
Set-Location $root

if ($Mode -eq 'Server') {
  $dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
  if (-not $dotnet) { $dotnet = Join-Path $runtimes 'dotnet-10\dotnet.exe' }
  if (-not (Test-Path $dotnet)) { throw '.NET 10 SDK is required.' }
  $dataName = if ($Test) { 'e2e-v02' } elseif ($Preview) { 'preview' } else { 'app' }
  $env:Home__DataPath = Join-Path $root ".data\$dataName"
  $env:Home__Preview = ($Preview.IsPresent -or $Test.IsPresent).ToString()
  $env:Home__Downloads__LoopbackOnly = $Test.IsPresent.ToString()
  $env:Home__DemoPath = Join-Path $root '.data\demo-media'
  $ffmpeg = (Get-Command ffmpeg -ErrorAction SilentlyContinue).Source
  if (-not $ffmpeg -and (Test-Path (Join-Path $runtimes 'ffmpeg'))) {
    $ffmpeg = (Get-ChildItem -Path (Join-Path $runtimes 'ffmpeg') -Filter ffmpeg.exe -Recurse | Select-Object -First 1).FullName
  }
  if ($ffmpeg) {
    $env:Home__FfmpegPath = $ffmpeg
    $env:Home__FfprobePath = Join-Path (Split-Path $ffmpeg -Parent) 'ffprobe.exe'
  }
  $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
  & $dotnet run --project server/HomeServer.Api --no-launch-profile -- --urls "http://127.0.0.1:$Port"
} else {
  $node = (Get-Command node -ErrorAction SilentlyContinue).Source
  if (-not $node) { $node = Join-Path $runtimes 'node-v24.21.0-win-x64\node.exe' }
  if (-not (Test-Path $node)) { throw 'Node.js 24 LTS is required.' }
  $nodeDirectory = Split-Path $node -Parent
  $env:PATH = "$nodeDirectory;$env:PATH"
  & $node (Join-Path $nodeDirectory 'node_modules\npm\bin\npm-cli.js') run dev
}
