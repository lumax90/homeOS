param([string]$Archive = (Join-Path $env:LOCALAPPDATA 'Temp\opencode\MinGit-2.56.0.2-64-bit.zip'), [string]$ReviewRoot = (Join-Path $env:LOCALAPPDATA 'Temp\opencode\native-gaming-runtime-v034-review'))
$ErrorActionPreference = 'Stop'
$expected = 'da35e72aa21c005a5a0d298cfbae110bc1609a815730ea0dde84b01a1b3cd3be'
if ((Get-Item -LiteralPath $Archive).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Reviewed shell archive cannot be a link.' }
if ((Get-FileHash -LiteralPath $Archive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected) { throw 'Existing MinGit archive does not match the official GitHub release digest.' }
$root = Join-Path $ReviewRoot 'mingit'; $destination = Join-Path $root 'files'
[IO.Directory]::CreateDirectory($destination) | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead($Archive); $files = @(); $expanded = 0L
try {
    if ($zip.Entries.Count -gt 10000) { throw 'Shell archive entry limit exceeded.' }
    foreach ($entry in $zip.Entries) {
        if ($entry.FullName.EndsWith('/')) { continue }
        $relative = $entry.FullName
        if ($relative -match '(^/|\\|:|(^|/)\.\.(/|$))' -or $entry.Length -gt 32MB) { throw 'Unsafe shell archive entry.' }
        $expanded += $entry.Length; if ($expanded -gt 256MB) { throw 'Shell archive expansion limit exceeded.' }
        $path = Join-Path $destination $relative
        if (Test-Path -LiteralPath $path) { throw 'A staged shell file already exists; it was preserved.' }
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path)) | Out-Null
        $input = $entry.Open(); $output = [IO.File]::Open($path,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
        try { $input.CopyTo($output) } finally { $input.Dispose(); $output.Dispose() }
        $files += [ordered]@{path=$relative;bytes=$entry.Length;sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()}
    }
} finally { $zip.Dispose() }
$receipt = [ordered]@{source=[ordered]@{id='mingit';version='2.56.0.2';license='GPL-2.0 Git / shell and runtime notices in archive (development only)';url='https://github.com/git-for-windows/git/releases/download/v2.56.0.windows.2/MinGit-2.56.0.2-64-bit.zip'};observedArchiveSha256=$expected;archiveBytes=(Get-Item -LiteralPath $Archive).Length;integrityKind='github-release-sha256';files=$files;installed=$false;launchedGame=$false}
[IO.File]::WriteAllText((Join-Path $root 'review.json'),($receipt | ConvertTo-Json -Depth 8),(New-Object Text.UTF8Encoding $false))
[ordered]@{reviewedShell='MinGit 2.56.0.2';files=$files.Count;downloaded=$false;installed=$false} | ConvertTo-Json
