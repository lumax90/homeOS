param([string]$OutputRoot = (Join-Path $env:LOCALAPPDATA 'Temp\opencode\native-display-review-25.7.23\tools'))
$ErrorActionPreference = 'Stop'
# Obtain only Microsoft's signed DevCon from an official WDK CAB. Do not run a third-party installer.
$url = 'https://download.microsoft.com/download/7/b/f/7bfc8dbe-00cb-47de-b856-70e696ef4f46/wdk/Installers/787bee96dbd26371076b37b13c405890.cab'
$cabHash = '6ecfaf7042c525ddb6c0bf0501becfbce85cbdf2a33b1bca67734fea567af3b8'
$expected = 'a92d4bc174079df703512e778e3192f524e4d0e33fc0ef718acc43c29e3d6e30'
$entry = 'filbad6e2cce5ebc45a401e19c613d0a28f'
$root = [IO.Path]::GetFullPath($OutputRoot)
if (-not (Test-Path -LiteralPath $root)) { New-Item -ItemType Directory -Path $root | Out-Null }
if ((Get-Item -LiteralPath $root).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Review root cannot be a link.' }
$cab = Join-Path $root 'Microsoft-WDK-DevCon.cab'
if (-not (Test-Path -LiteralPath $cab)) { Invoke-WebRequest -Uri $url -OutFile $cab -UseBasicParsing }
if ((Get-FileHash -LiteralPath $cab -Algorithm SHA256).Hash.ToLowerInvariant() -ne $cabHash) { throw 'The official Microsoft WDK CAB does not match its pinned SHA-256.' }
$cabSignature = Get-AuthenticodeSignature -LiteralPath $cab
if ($cabSignature.Status -ne 'Valid' -or $cabSignature.SignerCertificate.Subject -notmatch 'Microsoft Corporation') { throw 'Microsoft WDK CAB Authenticode verification failed.' }
$extracted = Join-Path $root $entry
if (-not (Test-Path -LiteralPath $extracted)) {
    & "$env:SystemRoot\System32\expand.exe" "-F:$entry" $cab $root
    if ($LASTEXITCODE -ne 0) { throw 'Could not extract the selected WDK binary.' }
}
if ((Get-FileHash -LiteralPath $extracted -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected) { throw 'Microsoft DevCon binary does not match its reviewed SHA-256.' }
$signature = Get-AuthenticodeSignature -LiteralPath $extracted
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'Microsoft Corporation') { throw 'Microsoft DevCon Authenticode verification failed.' }
$binary = Join-Path $root 'devcon.exe'
if (-not (Test-Path -LiteralPath $binary)) { [IO.File]::Copy($extracted, $binary, $false) }
if ((Get-FileHash -LiteralPath $binary -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected) { throw 'Existing devcon.exe differs; it was preserved.' }
[ordered]@{ source = $url; cabSha256 = $cabHash; binarySha256 = $expected; signature = [string]$signature.Status; signer = $signature.SignerCertificate.Subject; file = $binary; executedInstallation = $false } | ConvertTo-Json
