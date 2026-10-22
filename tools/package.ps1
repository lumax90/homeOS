param(
  [ValidateSet('win-x64','linux-x64','linux-arm64')][string]$Runtime = 'win-x64',
  [string]$Version,
  [string]$SigningKey,
  [string]$OutputRoot
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) { $dotnet = Join-Path $env:LOCALAPPDATA 'Temp\opencode\runtimes\dotnet-10\dotnet.exe' }
if (-not $Version) { [xml]$project = Get-Content (Join-Path $root 'server\HomeServer.Api\HomeServer.Api.csproj'); $Version = $project.Project.PropertyGroup.Version }
if (-not $SigningKey) { $SigningKey = Join-Path $root '.data\package-signing\private.pem' }
if (-not $OutputRoot) { $OutputRoot = Join-Path $root 'artifacts\packages' }
$output = Join-Path $OutputRoot "home-$Version-$Runtime.zip"
if (Test-Path $output) { throw "Package already exists: $output" }
$publish = Join-Path $OutputRoot "publish-$Version-$Runtime"
& $dotnet publish (Join-Path $root 'server\HomeServer.Api\HomeServer.Api.csproj') -c Release -r $Runtime --self-contained true "-p:Version=$Version" -o $publish --nologo
if ($LASTEXITCODE) { throw 'Publish failed.' }
Copy-Item (Join-Path $PSScriptRoot 'distribution\*') -Destination $publish -Recurse
& $dotnet run --project (Join-Path $PSScriptRoot 'Home.Packager\Home.Packager.csproj') -c Release --no-launch-profile -- $publish $Runtime $output $SigningKey
if ($LASTEXITCODE) { throw 'Package signing failed.' }
Write-Output $output
