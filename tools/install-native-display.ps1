param(
    [switch]$Approved,
    [string]$ReviewRoot = (Join-Path $env:LOCALAPPDATA 'Temp\opencode\native-display-review-25.7.23'),
    [string]$ReportRoot = (Join-Path $env:LOCALAPPDATA 'Temp\opencode\native-display-install-20261019')
)
$ErrorActionPreference = 'Stop'
if (-not $Approved) { throw 'Explicit user approval is required to install the virtual display driver.' }
function Require-Hash([string]$Path, [string]$Expected) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Required reviewed file is missing: $Path" }
    if ((Get-Item -LiteralPath $Path).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'An installation input cannot be a filesystem link.' }
    if ((Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $Expected) { throw "Reviewed SHA-256 mismatch: $Path" }
}
$source = Join-Path $ReviewRoot 'files\VirtualDisplayDriver'
$expected = [ordered]@{
    'MttVDD.dll' = 'c9ca837f57a98fbd43bc416a7f535a95843626e7759eaf85cf0cd7ce334dbb05'
    'mttvdd.cat' = '08a0093fc9b2e32b287a6f8a77ca4de0a31830d29fc33d2b13a918dc859468f6'
    'MttVDD.inf' = '550d211fe481e74dfe3f9d724ed78be48b3a9113405965d683d9373e8d672f5d'
    'vdd_settings.xml' = 'edb2501d6d5da17f66d15d4b97a6f4a3f0d8963165ac4a6a6259d95118288020'
}
foreach ($name in $expected.Keys) { Require-Hash (Join-Path $source $name) $expected[$name] }
$devcon = Join-Path $ReviewRoot 'tools\devcon.exe'
Require-Hash $devcon 'a92d4bc174079df703512e778e3192f524e4d0e33fc0ef718acc43c29e3d6e30'
foreach ($name in @('MttVDD.dll', 'mttvdd.cat')) {
    $signature = Get-AuthenticodeSignature -LiteralPath (Join-Path $source $name)
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'SignPath Foundation') { throw 'Driver Authenticode verification failed.' }
}
$signature = Get-AuthenticodeSignature -LiteralPath $devcon
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'Microsoft Corporation') { throw 'DevCon Authenticode verification failed.' }
if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    # Normal UAC only. No privilege/driver-signature bypass, scheduled-task trick, or automatic restart.
    $arguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', ('"' + $PSCommandPath + '"'), '-Approved', '-ReviewRoot', ('"' + $ReviewRoot + '"'), '-ReportRoot', ('"' + $ReportRoot + '"'))
    $process = Start-Process -FilePath "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" -Verb RunAs -ArgumentList $arguments -Wait -PassThru
    exit $process.ExitCode
}
Add-Type -AssemblyName System.Windows.Forms
$before = @([System.Windows.Forms.Screen]::AllScreens | ForEach-Object { [ordered]@{ name = $_.DeviceName; primary = $_.Primary; x = $_.Bounds.X; y = $_.Bounds.Y; width = $_.Bounds.Width; height = $_.Bounds.Height } })
$existing = @(Get-PnpDevice -Class Display -ErrorAction SilentlyContinue | Where-Object { $_.FriendlyName -eq 'Virtual Display Driver' })
if ($existing.Count -gt 0) { throw 'An existing virtual display device was found. It will not be replaced or reconfigured.' }
if (-not (Test-Path -LiteralPath $ReportRoot)) { New-Item -ItemType Directory -Path $ReportRoot | Out-Null }
if ((Get-Item -LiteralPath $ReportRoot).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Report directory cannot be a link.' }
$report = Join-Path $ReportRoot 'result.json'; $beforeFile = Join-Path $ReportRoot 'before.json'
if ((Test-Path -LiteralPath $report) -or (Test-Path -LiteralPath $beforeFile)) { throw 'Refusing to overwrite a previous installation receipt.' }
[IO.File]::WriteAllText($beforeFile, ($before | ConvertTo-Json -Depth 5), (New-Object Text.UTF8Encoding $false))
$target = 'C:\VirtualDisplayDriver'
if (Test-Path -LiteralPath $target) { throw 'An existing C:\VirtualDisplayDriver folder was found; its files were preserved.' }
New-Item -ItemType Directory -Path $target | Out-Null
foreach ($name in $expected.Keys) { [IO.File]::Copy((Join-Path $source $name), (Join-Path $target $name), $false) }
# Upstream's manual install needs a root-enumerated device, not only an INF staged with PnPUtil.
$process = Start-Process -FilePath $devcon -ArgumentList @('install', 'C:\VirtualDisplayDriver\MttVDD.inf', 'Root\MttVDD') -NoNewWindow -Wait -PassThru -RedirectStandardOutput (Join-Path $ReportRoot 'devcon.stdout.log') -RedirectStandardError (Join-Path $ReportRoot 'devcon.stderr.log')
$code = $process.ExitCode
$devices = @(Get-PnpDevice -Class Display -ErrorAction SilentlyContinue | Where-Object { $_.FriendlyName -eq 'Virtual Display Driver' } | Select-Object FriendlyName,Status,InstanceId)
$result = [ordered]@{ completedAt = [DateTimeOffset]::UtcNow.ToString('O'); userApproved = $true; driverVersion = '11.30.4.434'; bundleVersion = '25.7.23'; devconExitCode = $code; devicePresent = ($devices.Count -gt 0); devices = $devices; rebootRequired = ($code -eq 1); rebootPerformed = $false; primaryDisplayChangeRequested = $false; gamepadDriverInstalled = $false; certificateOrBootChanges = @(); captureOrStreamingVerified = $false }
[IO.File]::WriteAllText($report, ($result | ConvertTo-Json -Depth 6), (New-Object Text.UTF8Encoding $false))
$result | ConvertTo-Json -Depth 6
if ($code -notin @(0,1)) { exit 2 }
if ($devices.Count -eq 0) { exit 3 }
