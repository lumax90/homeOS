param(
  [Parameter(Mandatory=$true)][string]$CurrentVersion,
  [Parameter(Mandatory=$true)][string]$NextVersion,
  [Parameter(Mandatory=$true)][string]$UpdatePackage,
  [string]$BaseUrl = 'http://127.0.0.1:5080',
  [string]$OutputPath,
  [switch]$RestartStalledMetadata,
  [switch]$RestartCompletedSeeding
)
$ErrorActionPreference = 'Stop'
function Test-RestartableTransfer($download) {
  if ($RestartStalledMetadata -and $download.status -in @('metadata','queued') -and $download.totalBytes -eq 0 -and $download.receivedBytes -eq 0 -and $download.downloadSpeed -eq 0) { return $true }
  if ($RestartCompletedSeeding -and $download.status -in @('seeding','completed') -and $download.importedAt -and $download.progress -ge 100 -and $download.downloadSpeed -eq 0) { return $true }
  return $false
}
if (-not $env:HOME_USERNAME -or -not $env:HOME_PASSWORD) { throw 'Set HOME_USERNAME and HOME_PASSWORD for the existing administrator.' }
$before = Invoke-RestMethod "$BaseUrl/api/health" -TimeoutSec 5
if ($before.version -ne $CurrentVersion) { throw 'Unexpected live version; review before applying the update.' }
$signedIn = $false
try {
  $state = Invoke-RestMethod "$BaseUrl/api/session" -SessionVariable homeSession
  $headers = @{'X-CSRF-TOKEN'=$state.csrfToken}
  $credentials = @{displayName=$env:HOME_USERNAME;password=$env:HOME_PASSWORD} | ConvertTo-Json
  $null = Invoke-RestMethod "$BaseUrl/api/auth/login" -Method Post -WebSession $homeSession -Headers $headers -ContentType 'application/json' -Body ([Text.Encoding]::UTF8.GetBytes($credentials))
  $signedIn = $true
  $state = Invoke-RestMethod "$BaseUrl/api/session" -WebSession $homeSession
  $owner = $state.userId
  $usage = Invoke-RestMethod "$BaseUrl/api/account/playback?all=true" -WebSession $homeSession
  if ($usage.active.Count -gt 0) { throw 'The live server has active playback; defer this restart.' }
  if ([version]$before.version -ge [version]'0.32.0') {
    $gameSessions = Invoke-RestMethod "$BaseUrl/api/games/sessions/active" -WebSession $homeSession
    if (@($gameSessions | Where-Object { $_.id }).Count -gt 0) { throw 'The live server has active gameplay; exit the game before restarting.' }
  }
  $downloads = Invoke-RestMethod "$BaseUrl/api/downloads" -WebSession $homeSession
  $active = @($downloads | Where-Object { $_.desiredState -eq 'running' })
  if (@($active | Where-Object { -not (Test-RestartableTransfer $_) }).Count -gt 0) { throw 'The live server has active payload transfers; pause them before this restart.' }
  $priorRollback = Invoke-RestMethod "$BaseUrl/api/operations/updates" -WebSession $homeSession
  $headers = @{'X-CSRF-TOKEN'=$state.csrfToken}
  $prepared = Invoke-RestMethod "$BaseUrl/api/operations/updates" -Method Post -WebSession $homeSession -Headers $headers -ContentType 'application/zip' -InFile ([IO.Path]::GetFullPath($UpdatePackage)) -DisableKeepAlive -TimeoutSec 120
  if ($prepared.version -ne $NextVersion) { throw 'Unexpected staged update version.' }
  $current = @(Invoke-RestMethod "$BaseUrl/api/downloads" -WebSession $homeSession)
  $running = @($current | Where-Object { $_.desiredState -eq 'running' })
  if (@($running | Where-Object { $_.id -notin $active.id -or -not (Test-RestartableTransfer $_) }).Count -gt 0) { throw 'Transfers changed during staging; inspect them before applying.' }
  $usage = Invoke-RestMethod "$BaseUrl/api/account/playback?all=true" -WebSession $homeSession
  if ($usage.active.Count -gt 0) { throw 'Playback started during staging; defer applying this update.' }
  if ([version]$before.version -ge [version]'0.32.0') {
    $gameSessions = Invoke-RestMethod "$BaseUrl/api/games/sessions/active" -WebSession $homeSession
    if (@($gameSessions | Where-Object { $_.id }).Count -gt 0) { throw 'Gameplay started during staging; defer applying this update.' }
  }
  $null = Invoke-RestMethod "$BaseUrl/api/operations/updates/apply" -Method Post -WebSession $homeSession -Headers $headers -TimeoutSec 60
  $deadline = [DateTime]::UtcNow.AddSeconds(75)
  $healthy = $false
  while ([DateTime]::UtcNow -lt $deadline) {
    try { $health = Invoke-RestMethod "$BaseUrl/api/health" -TimeoutSec 2; if ($health.version -eq $NextVersion) { $healthy=$true; break } } catch { }
    Start-Sleep -Milliseconds 250
  }
  if (-not $healthy) { throw 'The updated Home version did not become healthy.' }
  $state = Invoke-RestMethod "$BaseUrl/api/session" -WebSession $homeSession
  if (-not $state.authenticated -or $state.userId -ne $owner) { throw 'The owner session was not retained.' }
  $runtime = Invoke-RestMethod "$BaseUrl/api/operations/runtime" -WebSession $homeSession
  $updates = Invoke-RestMethod "$BaseUrl/api/operations/updates" -WebSession $homeSession
  $result = [ordered]@{verifiedAt=(Get-Date -Format o);fromVersion=$before.version;version=$health.version;accountRetained=$true;processId=$runtime.processId;statePath=$runtime.statePath;rollback=$updates.rollback;priorRollback=$priorRollback.rollback;resumedMetadataIds=@($active | Where-Object {$_.status -in @('metadata','queued')} | ForEach-Object {$_.id});recoveredSeedingIds=@($active | Where-Object {$_.status -in @('seeding','completed')} | ForEach-Object {$_.id})}
  if (-not $OutputPath) { $OutputPath = Join-Path (Split-Path $PSScriptRoot -Parent) "artifacts\update-live-$NextVersion.json" }
  $result | ConvertTo-Json -Depth 5 | Set-Content $OutputPath -Encoding UTF8
  $result | ConvertTo-Json -Depth 5
} finally {
  if ($signedIn) {
    try {
      $state = Invoke-RestMethod "$BaseUrl/api/session" -WebSession $homeSession -TimeoutSec 5
      $null = Invoke-RestMethod "$BaseUrl/api/auth/logout" -Method Post -WebSession $homeSession -Headers @{'X-CSRF-TOKEN'=$state.csrfToken} -TimeoutSec 5
    } catch { }
  }
}
