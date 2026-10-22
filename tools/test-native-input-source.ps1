param([string]$ReviewRoot = (Join-Path $env:LOCALAPPDATA 'Temp\opencode\native-gaming-runtime-v034-review'))
$ErrorActionPreference = 'Stop'
$source = Join-Path $ReviewRoot 'retroarch-source-1222'; $fixRoot = Join-Path $ReviewRoot 'retroarch-upstream-input-fix'
$manifest = Get-Content -LiteralPath (Join-Path $source 'home-source-review.json') -Raw | ConvertFrom-Json
$fix = Get-Content -LiteralPath (Join-Path $fixRoot 'home-fix-review.json') -Raw | ConvertFrom-Json
if ($manifest.commit -ne '69a4f0ea1e8aaf442ae4858f2e7f2b31a1776576' -or $fix.commit -ne 'cfb1204314dd6b061a7b1cda77221735939bf5ba') { throw 'Pinned released source/fix required.' }
function Read-Source([string]$Relative) {
    $file = @($manifest.files | Where-Object { $_.path -eq $Relative }); $path = Join-Path $source $Relative
    if ($file.Count -ne 1 -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $file[0].sha256) { throw 'Pinned regression source changed.' }
    return [IO.File]::ReadAllText($path)
}
function Function-Text([string]$Text, [string]$Signature) {
    $start = $Text.IndexOf($Signature); if ($start -lt 0) { throw 'Pinned function missing.' }
    $body = $Text.IndexOf('{',$start); $level = 0
    for ($at = $body; $at -lt $Text.Length; $at++) {
        if ($Text[$at] -eq '{') { $level++ }
        if ($Text[$at] -eq '}') { $level--; if ($level -eq 0) { return $Text.Substring($start,$at-$start+1) } }
    }
    throw 'Pinned function body incomplete.'
}
$input = Read-Source 'input/input_driver.c'; $types = Read-Source 'input/input_driver.h'; $network = Read-Source 'libretro-common/net/net_socket.c'
$upstreamPath = Join-Path $fixRoot 'input_driver.c'
if ((Get-FileHash -LiteralPath $upstreamPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $fix.sha256) { throw 'Pinned fix changed.' }
$upstream = [IO.File]::ReadAllText($upstreamPath)
$oldStart = $input.IndexOf("#ifdef HAVE_NETWORKGAMEPAD`n   /* Poll remote */"); $oldEnd = $input.IndexOf('#ifdef HAVE_BSV_MOVIE',$oldStart)
$typeStart = $types.IndexOf('struct remote_message'); $typeEnd = $types.IndexOf('typedef struct input_list_element_t',$typeStart)
$harness = [IO.File]::ReadAllText((Join-Path $PSScriptRoot '..\tests\native\RetroArchRemoteInput.c'))
$harness = $harness.Replace('/* HOME_UPSTREAM_TYPES */',$types.Substring($typeStart,$typeEnd-$typeStart))
$harness = $harness.Replace('/* HOME_UPSTREAM_PARSE */',(Function-Text $input 'static void input_remote_parse_packet('))
$harness = $harness.Replace('/* HOME_UPSTREAM_RECEIVE */',(Function-Text $network 'ssize_t socket_receive_all_nonblocking('))
$harness = $harness.Replace('/* HOME_UPSTREAM_FIXED_POLL */',"#define INPUT_REMOTE_DRAIN_MAX 64`n"+(Function-Text $upstream 'static void input_remote_poll('))
$harness = $harness.Replace('/* HOME_UPSTREAM_RELEASED_POLL */',$input.Substring($oldStart,$oldEnd-$oldStart))
$tools = Get-Content -LiteralPath (Join-Path $ReviewRoot 'mingw\review.json') -Raw | ConvertFrom-Json
$compiler = @($tools.files | Where-Object { $_.path.EndsWith('/bin/gcc.exe') }); $gcc = Join-Path (Join-Path $ReviewRoot 'mingw\files') $compiler[0].path
if ($compiler.Count -ne 1 -or (Get-FileHash -LiteralPath $gcc -Algorithm SHA256).Hash.ToLowerInvariant() -ne $compiler[0].sha256) { throw 'Reviewed compiler required.' }
$output = Join-Path $ReviewRoot ('input-source-test-' + [Guid]::NewGuid().ToString('N')); [IO.Directory]::CreateDirectory($output) | Out-Null
$file = Join-Path $output 'input-source-test.c'; [IO.File]::WriteAllText($file,$harness,(New-Object Text.UTF8Encoding $false))
$binary = Join-Path $output 'input-source-test.exe'
& $gcc '-std=gnu99' '-O2' '-static-libgcc' ('-I'+(Join-Path $source 'libretro-common\include')) '-o' $binary $file '-lws2_32'
if ($LASTEXITCODE -ne 0) { throw 'Native source harness did not compile.' }
$result = & $binary; if ($LASTEXITCODE -ne 0) { throw 'Native input source regression failed.' }
$report = [ordered]@{checkedAt=[DateTimeOffset]::UtcNow.ToString('O');sourceCommit=$manifest.commit;upstreamInputFix=$fix.commit;output=$output;result=($result | ConvertFrom-Json);launchedEmulator=$false;changedFirewall=$false;openedExternalListener=$false}
[IO.File]::WriteAllText((Join-Path $output 'input-source-test.json'),($report | ConvertTo-Json -Depth 6),(New-Object Text.UTF8Encoding $false))
$report | ConvertTo-Json -Depth 6
