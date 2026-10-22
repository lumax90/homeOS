$ErrorActionPreference = 'Stop'
$settings = Get-Content (Join-Path $PSScriptRoot 'install.json') -Raw | ConvertFrom-Json
$log = Join-Path $settings.DataPath 'logs'
$null = New-Item -ItemType Directory -Path $log -Force
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$arguments = '--Home:DataPath "' + $settings.DataPath.TrimEnd('\').Replace('"','\"') + '" --urls "http://0.0.0.0:' + $settings.Port + '"'
Start-Process -FilePath $settings.Executable -ArgumentList $arguments -WorkingDirectory (Split-Path $settings.Executable -Parent) -RedirectStandardOutput (Join-Path $log "$stamp.stdout.log") -RedirectStandardError (Join-Path $log "$stamp.stderr.log") -WindowStyle Hidden
Start-Process "http://localhost:$($settings.Port)"
