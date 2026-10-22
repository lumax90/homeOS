param([string]$OutputRoot = (Join-Path $env:LOCALAPPDATA 'Temp\opencode\native-display-review-25.7.23'))
$ErrorActionPreference = 'Stop'

# Software artifact review only: NEVER installs a device, imports a certificate or changes test-signing/boot settings.
$url = 'https://github.com/VirtualDrivers/Virtual-Display-Driver/releases/download/25.7.23/VirtualDisplayDriver-x86.Driver.Only.zip'
$hash = 'e24210692b442b39af763536330ce78b423f19342b7a7792c26de3944e418b3a'
$root = [IO.Path]::GetFullPath($OutputRoot)
if (-not (Test-Path -LiteralPath $root)) { New-Item -ItemType Directory -Path $root | Out-Null }
if ((Get-Item -LiteralPath $root).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Review root cannot be a filesystem link.' }
$report = Join-Path $root 'review-summary.json'
if (Test-Path -LiteralPath $report) { throw 'Refusing to overwrite an existing review report.' }
$archive = Join-Path $root 'VirtualDisplayDriver-x86.Driver.Only.zip'
if (-not (Test-Path -LiteralPath $archive)) { Invoke-WebRequest -Uri $url -OutFile $archive -UseBasicParsing }
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $hash) { throw 'Pinned software archive SHA-256 does not match; nothing will be installed.' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead($archive)
try {
    $seen = @{}; [long]$total = 0
    if ($zip.Entries.Count -gt 64) { throw 'Too many driver package entries.' }
    foreach ($entry in $zip.Entries) {
        $name = $entry.FullName.Replace('\', '/')
        if ([IO.Path]::IsPathRooted($name) -or $name.Contains(':') -or @($name.Split('/') | Where-Object { $_ -eq '..' }).Count -gt 0) { throw 'Unsafe driver package path.' }
        if (($entry.ExternalAttributes -shr 16 -band 0xf000) -eq 0xa000) { throw 'Driver package contains a symbolic link.' }
        if ($seen.ContainsKey($name)) { throw 'Duplicate driver package path.' }
        $seen[$name] = $true; $total += $entry.Length
        if ($total -gt 10MB) { throw 'Driver package expands beyond the review limit.' }
    }
} finally { $zip.Dispose() }
$extract = Join-Path $root 'files'
if (-not (Test-Path -LiteralPath $extract)) { [IO.Compression.ZipFile]::ExtractToDirectory($archive, $extract) }
if ((Get-Item -LiteralPath $extract).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Review directory cannot be a filesystem link.' }
$zip = [IO.Compression.ZipFile]::OpenRead($archive)
try {
    foreach ($entry in $zip.Entries) {
        if ($entry.FullName.EndsWith('/')) { continue }
        $target = Join-Path $extract $entry.FullName
        if (-not (Test-Path -LiteralPath $target -PathType Leaf)) { throw 'Reviewed archive entry is missing from the extracted directory.' }
        if ((Get-Item -LiteralPath $target).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Extracted entry cannot be a filesystem link.' }
        $stream = $entry.Open(); $sha = [Security.Cryptography.SHA256]::Create()
        try { $expected = ([BitConverter]::ToString($sha.ComputeHash($stream))).Replace('-', '').ToLowerInvariant() }
        finally { $sha.Dispose(); $stream.Dispose() }
        if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected) { throw 'Existing extracted file differs from the pinned archive; it was preserved.' }
    }
} finally { $zip.Dispose() }
$files = @(Get-ChildItem -LiteralPath $extract -File -Recurse | ForEach-Object {
    if ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Review file cannot be a filesystem link.' }
    $signature = Get-AuthenticodeSignature -LiteralPath $_.FullName
    [ordered]@{ name = $_.FullName.Substring($extract.Length + 1); bytes = $_.Length; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(); signatureStatus = [string]$signature.Status; signer = $signature.SignerCertificate.Subject }
})
$infs = @(Get-ChildItem -LiteralPath $extract -Filter '*.inf' -File -Recurse | ForEach-Object { [ordered]@{ name = [string]$_.Name; text = [string](Get-Content -LiteralPath $_.FullName -Raw) } })
$result = [ordered]@{ reviewedAt = [DateTimeOffset]::UtcNow.ToString('O'); source = $url; archiveSha256 = $hash; files = $files; inf = $infs; installed = $false; deviceOrBootChanges = @() }
$json = $result | ConvertTo-Json -Depth 7
[IO.File]::WriteAllText($report, $json, (New-Object Text.UTF8Encoding $false))
$json
