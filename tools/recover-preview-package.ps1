param(
    [string]$Publish = (Join-Path $PSScriptRoot '..\artifacts\packages\publish-0.34.1-win-x64'),
    [string]$Data = (Join-Path $PSScriptRoot '..\.data\preview')
)
$ErrorActionPreference = 'Stop'
# Offline recovery after the signed 0.34.0 activation failed cold catalog validation.
# No rollback/DB restore, no game deletion, and no mutation of an existing signed release directory.
if (Get-NetTCPConnection -LocalPort 5080 -State Listen -ErrorAction SilentlyContinue) { throw 'A preview listener exists; do not replace a running server offline.' }
$activePath = Join-Path $Data 'updates\active.json'; $active = Get-Content -LiteralPath $activePath -Raw | ConvertFrom-Json
if ($active.Version -ne '0.34.0') { throw 'Unexpected active release; it was preserved.' }
$trust = Join-Path $active.Directory 'update-trust.pem'
$verify = Join-Path $Publish 'HomeServer.Api.exe'
& $verify --verify-package $Publish $trust
if ($LASTEXITCODE -ne 0) { throw 'Recovery package failed the original trusted-key check.' }
$manifest = Get-Content -LiteralPath (Join-Path $Publish '.home-package.json') -Raw | ConvertFrom-Json
if ($manifest.Version -ne '0.34.1' -or $manifest.Schema -ne 41) { throw 'Unexpected recovery package version/schema.' }
$release = Join-Path (Join-Path $Data 'updates\releases') ([Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $release | Out-Null
Get-ChildItem -LiteralPath $Publish -Force | Copy-Item -Destination $release -Recurse
& $verify --verify-package $release $trust
if ($LASTEXITCODE -ne 0) { throw 'Copied recovery files failed verification; the active pointer was not changed.' }
$record = [ordered]@{Directory=$release;Version='0.34.1';ActivatedAt=[DateTimeOffset]::UtcNow.ToString('O')}
$temporary = $activePath + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
[IO.File]::WriteAllText($temporary,($record | ConvertTo-Json),(New-Object Text.UTF8Encoding $false))
Move-Item -LiteralPath $temporary -Destination $activePath -Force
$ffmpeg = Join-Path $env:LOCALAPPDATA 'Temp\opencode\runtimes\ffmpeg\ffmpeg-9.0.2-essentials_build\bin\ffmpeg.exe'
$ffprobe = Join-Path ([IO.Path]::GetDirectoryName($ffmpeg)) 'ffprobe.exe'
$logs = Join-Path $Data 'logs'; $tag = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')
$process = Start-Process -FilePath (Join-Path $release 'HomeServer.Api.exe') -WorkingDirectory $release -ArgumentList @('--urls','http://localhost:5080','--Home:DataPath',('"'+$Data+'"'),'--Home:Preview','true','--Home:FfmpegPath',('"'+$ffmpeg+'"'),'--Home:FfprobePath',('"'+$ffprobe+'"')) -RedirectStandardOutput (Join-Path $logs ('recover-'+$tag+'.stdout.log')) -RedirectStandardError (Join-Path $logs ('recover-'+$tag+'.stderr.log')) -PassThru
$healthy=$false
for($i=0;$i -lt 45;$i++) { try { $health=Invoke-RestMethod 'http://127.0.0.1:5080/api/health' -TimeoutSec 2; if($health.version -eq '0.34.1'){$healthy=$true;break} }catch{} Start-Sleep -Milliseconds 500 }
if(-not $healthy){throw 'Verified recovery version did not start; inspect its isolated recovery logs.'}
$report=[ordered]@{verifiedAt=[DateTimeOffset]::UtcNow.ToString('O');version='0.34.1';processId=$process.Id;directory=$release;previousFailedRelease=$active.Directory;dataRestored=$false;sourceGamesDeleted=$false;transfersResumed=$false;originalTrustUsed=$true}
$report | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $PSScriptRoot '..\artifacts\update-live-0.34.1.json') -Encoding UTF8
$report | ConvertTo-Json
