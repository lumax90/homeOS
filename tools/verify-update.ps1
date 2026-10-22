param(
  [Parameter(Mandatory=$true)][string]$CurrentDirectory,
  [Parameter(Mandatory=$true)][string]$UpdatePackage,
  [string]$CurrentVersion = '0.11.0',
  [string]$NextVersion = '0.11.1'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$data = Join-Path $env:LOCALAPPDATA ('Temp\opencode\home-update-verification-' + [guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $data
$listener = New-Object Net.Sockets.TcpListener([Net.IPAddress]::Loopback, 0)
$listener.Start()
$port = $listener.LocalEndpoint.Port
$listener.Stop()
$base = "http://127.0.0.1:$port"
function Wait-Version([string]$expected) {
  $end = [DateTime]::UtcNow.AddSeconds(45)
  do {
    try { $value = Invoke-RestMethod "$base/api/health" -TimeoutSec 2; if ($value.version -eq $expected) { return } } catch { }
    Start-Sleep -Milliseconds 200
  } while ([DateTime]::UtcNow -lt $end)
  throw "Expected Home $expected did not become healthy."
}
function Refresh-Csrf {
  $state = Invoke-RestMethod "$base/api/session" -WebSession $script:session
  $script:headers = @{'X-CSRF-TOKEN'=$state.csrfToken}
}
try {
  $exe = Join-Path ([IO.Path]::GetFullPath($CurrentDirectory)) 'HomeServer.Api.exe'
  $arguments = @('--urls',$base,'--Home:DataPath',$data,'--Home:Downloads:LoopbackOnly','true','--Home:FfmpegPath','absent','--Home:FfprobePath','absent')
  $process = Start-Process -FilePath $exe -ArgumentList $arguments -WorkingDirectory ([IO.Path]::GetFullPath($CurrentDirectory)) -PassThru -RedirectStandardOutput (Join-Path $data 'startup.stdout.log') -RedirectStandardError (Join-Path $data 'startup.stderr.log') -WindowStyle Hidden
  Wait-Version $CurrentVersion
  $state = Invoke-RestMethod "$base/api/session" -SessionVariable session
  $headers = @{'X-CSRF-TOKEN'=$state.csrfToken}
  $credentials = @{displayName='Update verification';password='isolated-update-verification-2026'} | ConvertTo-Json
  $null = Invoke-RestMethod "$base/api/setup" -Method Post -WebSession $session -Headers $headers -ContentType 'application/json' -Body $credentials
  Refresh-Csrf
  $before = Invoke-RestMethod "$base/api/account" -WebSession $session
  $prepared = Invoke-RestMethod "$base/api/operations/updates" -Method Post -WebSession $session -Headers $headers -ContentType 'application/zip' -InFile ([IO.Path]::GetFullPath($UpdatePackage)) -DisableKeepAlive -TimeoutSec 120
  if ($prepared.version -ne $NextVersion) { throw 'Unexpected staged version.' }
  $null = Invoke-RestMethod "$base/api/operations/updates/apply" -Method Post -WebSession $session -Headers $headers -TimeoutSec 60
  Wait-Version $NextVersion
  Refresh-Csrf
  $after = Invoke-RestMethod "$base/api/account" -WebSession $session
  if ($after.id -ne $before.id -or $after.displayName -ne $before.displayName) { throw 'Account changed during update.' }
  $status = Invoke-RestMethod "$base/api/operations/updates" -WebSession $session
  if ($status.rollback.version -ne $CurrentVersion) { throw 'Rollback target was not retained.' }
  $null = Invoke-RestMethod "$base/api/operations/updates/rollback" -Method Post -WebSession $session -Headers $headers -TimeoutSec 60
  Wait-Version $CurrentVersion
  Refresh-Csrf
  $state = Invoke-RestMethod "$base/api/session" -WebSession $session
  if ($state.authenticated) { throw 'Restored sessions should be invalidated.' }
  $null = Invoke-RestMethod "$base/api/auth/login" -Method Post -WebSession $session -Headers $headers -ContentType 'application/json' -Body $credentials
  $restored = Invoke-RestMethod "$base/api/account" -WebSession $session
  if ($restored.displayName -ne $before.displayName) { throw 'Original account did not survive rollback.' }
  $result = @{verifiedAt=(Get-Date -Format o);fromVersion=$CurrentVersion;toVersion=$NextVersion;rollbackVersion=$CurrentVersion;accountRetained=$true;sessionsRevokedOnRestore=$true;dataDirectory=$data;internetMediaTransfers=0}
  $result | ConvertTo-Json | Set-Content (Join-Path $root "artifacts\update-roundtrip-$CurrentVersion-$NextVersion.json") -Encoding UTF8
  $result | ConvertTo-Json
} finally {
  Get-CimInstance Win32_Process -Filter "Name = 'HomeServer.Api.exe'" | Where-Object { $_.CommandLine -like "*$data*" } | ForEach-Object { Stop-Process -Id $_.ProcessId -ErrorAction SilentlyContinue }
}
