param(
  [string]$InstallRoot = (Join-Path $env:LOCALAPPDATA 'Home'),
  [string]$DataPath,
  [ValidateRange(1,65535)][int]$Port = 5080,
  [switch]$NoStart,
  [switch]$NoShortcut
)
$ErrorActionPreference = 'Stop'
$package = Get-Content (Join-Path $PSScriptRoot '.home-package.json') -Raw | ConvertFrom-Json
if ($package.Runtime -ne 'win-x64') { throw 'Windows x64 package required.' }
$key = Join-Path $PSScriptRoot 'update-trust.pem'
& (Join-Path $PSScriptRoot 'HomeServer.Api.exe') --verify-package $PSScriptRoot $key
if ($LASTEXITCODE) { throw 'The package signature or file hashes do not match.' }
$shortcutPath = Join-Path ([Environment]::GetFolderPath('StartMenu')) 'Programs\Home Media Server.lnk'
if (-not $NoShortcut -and (Test-Path $shortcutPath)) { throw 'A Home shortcut already exists. Use -NoShortcut to retain it.' }
$InstallRoot = [IO.Path]::GetFullPath($InstallRoot)
if (Test-Path (Join-Path $InstallRoot 'install.json')) { throw 'An installation exists here. Use the application updater or select another installation directory.' }
if ($InstallRoot.StartsWith($PSScriptRoot.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Choose an install directory outside the extracted package.' }
if (-not $DataPath) { $DataPath = Join-Path $InstallRoot 'data' }
$DataPath = [IO.Path]::GetFullPath($DataPath)
$target = Join-Path $InstallRoot ("releases\" + $package.Version)
if ($DataPath.StartsWith($target.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase) -or $DataPath -eq $target) { throw 'Store application data outside release directories.' }
if (Test-Path $target) { throw 'This release directory already exists. Use the application updater for upgrades.' }
$null = New-Item -ItemType Directory -Path $target -Force
Get-ChildItem -LiteralPath $PSScriptRoot -Force | Copy-Item -Destination $target -Recurse
$null = New-Item -ItemType Directory -Path $DataPath -Force
@{Application='Home';Executable=(Join-Path $target 'HomeServer.Api.exe');DataPath=$DataPath;Port=$Port} | ConvertTo-Json | Set-Content (Join-Path $InstallRoot 'install.json') -Encoding UTF8
Copy-Item (Join-Path $target 'Start-Home.ps1') -Destination (Join-Path $InstallRoot 'Start-Home.ps1')
if (-not $NoShortcut) {
  $shortcut = (New-Object -ComObject WScript.Shell).CreateShortcut($shortcutPath)
  $shortcut.TargetPath = (Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe')
  $shortcut.Arguments = '-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "' + (Join-Path $InstallRoot 'Start-Home.ps1') + '"'
  $shortcut.WorkingDirectory = $InstallRoot
  $shortcut.Save()
}
Write-Output "Home installed: $InstallRoot. Data: $DataPath"
if (-not $NoStart) { & (Join-Path $InstallRoot 'Start-Home.ps1') }
