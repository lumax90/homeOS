param(
  [string]$BaseUrl = 'http://127.0.0.1:5080',
  [string]$ExpectedVersion = '0.26.0',
  [Parameter(Mandatory=$true)][string]$OutputPath
)
$ErrorActionPreference = 'Stop'
if (-not $env:HOME_USERNAME -or -not $env:HOME_PASSWORD) { throw 'Set HOME_USERNAME and HOME_PASSWORD.' }
$signedIn = $false
try {
  $health = Invoke-RestMethod "$BaseUrl/api/health" -TimeoutSec 5
  if ($health.version -ne $ExpectedVersion) { throw 'Unexpected preview version.' }
  $session = Invoke-RestMethod "$BaseUrl/api/session" -SessionVariable homeSession
  $credentials = @{displayName=$env:HOME_USERNAME;password=$env:HOME_PASSWORD} | ConvertTo-Json
  $null = Invoke-RestMethod "$BaseUrl/api/auth/login" -Method Post -WebSession $homeSession -Headers @{'X-CSRF-TOKEN'=$session.csrfToken} -ContentType 'application/json' -Body ([Text.Encoding]::UTF8.GetBytes($credentials))
  $signedIn = $true
  $session = Invoke-RestMethod "$BaseUrl/api/session" -WebSession $homeSession
  $headers = @{'X-CSRF-TOKEN'=$session.csrfToken}
  $runtime = Invoke-RestMethod "$BaseUrl/api/operations/runtime" -WebSession $homeSession
  $updates = Invoke-RestMethod "$BaseUrl/api/operations/updates" -WebSession $homeSession
  $restore = Invoke-RestMethod "$BaseUrl/api/operations/restore" -WebSession $homeSession
  $playback = Invoke-RestMethod "$BaseUrl/api/account/playback?all=true" -WebSession $homeSession
  $downloads = Invoke-RestMethod "$BaseUrl/api/downloads" -WebSession $homeSession
  $requests = Invoke-RestMethod "$BaseUrl/api/requests" -WebSession $homeSession
  $content = Invoke-RestMethod "$BaseUrl/api/content" -WebSession $homeSession
  $sources = Invoke-RestMethod "$BaseUrl/api/indexers" -WebSession $homeSession
  if ($playback.active.Count -gt 0) { throw 'Preview playback is active; finish playback before restarting.' }
  if ([version]$health.version -ge [version]'0.32.0') {
    $gameSessions = Invoke-RestMethod "$BaseUrl/api/games/sessions/active" -WebSession $homeSession
    if (@($gameSessions | Where-Object { $_.id }).Count -gt 0) { throw 'Preview gameplay is active; exit the game before restarting.' }
  }
  if (@($downloads | Where-Object { $_.desiredState -eq 'running' }).Count -gt 0) { throw 'Preview transfers are active; pause them before this update.' }
  if ($updates.pending -or $restore.pending) { throw 'An existing update or restore is pending; inspect it first.' }
  if (-not $updates.trusted -or -not $updates.canApply) { throw 'The preview cannot apply a trusted update.' }
  $job = Invoke-RestMethod "$BaseUrl/api/operations/backups" -Method Post -WebSession $homeSession -Headers $headers
  $deadline = [DateTime]::UtcNow.AddSeconds(90)
  do {
    $jobs = Invoke-RestMethod "$BaseUrl/api/operations/jobs" -WebSession $homeSession
    $current = $jobs | Where-Object { $_.id -eq $job.id } | Select-Object -First 1
    if ($current.status -eq 'failed') { throw $current.message }
    if ($current.status -eq 'completed') { break }
    Start-Sleep -Milliseconds 300
  } while ([DateTime]::UtcNow -lt $deadline)
  if ($current.status -ne 'completed') { throw 'Preview backup did not complete.' }
  $backups = Invoke-RestMethod "$BaseUrl/api/operations/backups" -WebSession $homeSession
  $backup = $backups | Where-Object { $_.id -eq $job.id } | Select-Object -First 1
  if (-not $backup) { throw 'The completed backup is missing.' }
  $backupPath = Join-Path $env:LOCALAPPDATA ("Temp\opencode\home-preview-before-update-" + $job.id + '.zip')
  Invoke-WebRequest "$BaseUrl/api/operations/backups/$($job.id)" -WebSession $homeSession -OutFile $backupPath -UseBasicParsing -TimeoutSec 90
  $result = [ordered]@{recordedAt=(Get-Date -Format o);version=$health.version;schema=$backup.schema;userId=$session.userId;processId=$runtime.processId;statePath=$runtime.statePath;keyFingerprint=$updates.keyFingerprint;backupId=$backup.id;backupPath=$backupPath;requestIds=@($requests | ForEach-Object {$_.id});contentIds=@($content | ForEach-Object {$_.id});sourceIds=@($sources | ForEach-Object {$_.id});downloadIds=@($downloads | ForEach-Object {$_.id});activePlayback=$playback.active.Count}
  $result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $OutputPath -Encoding UTF8
  [ordered]@{version=$result.version;schema=$result.schema;backupId=$result.backupId;requests=$result.requestIds.Count;content=$result.contentIds.Count;sources=$result.sourceIds.Count;downloads=$result.downloadIds.Count;activePlayback=$result.activePlayback} | ConvertTo-Json
} finally {
  if ($signedIn) { try { $session = Invoke-RestMethod "$BaseUrl/api/session" -WebSession $homeSession -TimeoutSec 5; $null = Invoke-RestMethod "$BaseUrl/api/auth/logout" -Method Post -WebSession $homeSession -Headers @{'X-CSRF-TOKEN'=$session.csrfToken} -TimeoutSec 5 } catch {} }
}
