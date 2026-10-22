param([string]$OutputPath = 'artifacts/native-gaming-host.json')
$ErrorActionPreference = 'Stop'

# Read-only inventory. No driver/service/container installation, executable launch, ROM search or download.
Add-Type -AssemblyName System.Windows.Forms
$screens = @([System.Windows.Forms.Screen]::AllScreens | ForEach-Object {
    [ordered]@{ name = $_.DeviceName; primary = $_.Primary; width = $_.Bounds.Width; height = $_.Bounds.Height }
})
$drivers = @(Get-CimInstance Win32_SystemDriver | Where-Object { $_.Name -match 'ViGEm|VirtualHID|VirtualDisplay|IddSample|IddDriver' } | ForEach-Object {
    [ordered]@{ name = $_.Name; state = $_.State; startMode = $_.StartMode }
})
$processes = @(Get-Process | Where-Object { $_.ProcessName -match '^(sunshine|retroarch|web-server|streamer|moonlight)$' } | ForEach-Object {
    [ordered]@{ name = $_.ProcessName; id = $_.Id }
})
$result = [ordered]@{
    inspectedAt = [DateTimeOffset]::UtcNow.ToString('O')
    host = 'windows'
    screens = $screens
    matchingDrivers = $drivers
    matchingProcesses = $processes
    matchingPnpDrivers = @(Get-CimInstance Win32_PnPSignedDriver | Where-Object { $_.DeviceName -match 'Virtual Display|ViGEm|MttVDD' -or $_.DriverProviderName -match 'MikeTheTech|LizardByte|Nefarius' } | ForEach-Object { [ordered]@{ name = $_.DeviceName; provider = $_.DriverProviderName; version = $_.DriverVersion; inf = $_.InfName } })
    driverDetectionIsExhaustive = $false
    captureIsolationVerified = $false
    nativeExecutionVerified = $false
    streamingVerified = $false
    changesPerformed = @()
}
$full = [IO.Path]::GetFullPath($OutputPath)
if (Test-Path -LiteralPath $full) { throw 'Refusing to overwrite an existing host inspection report. Choose a new OutputPath.' }
$parent = [IO.Path]::GetDirectoryName($full)
if (-not (Test-Path -LiteralPath $parent)) { New-Item -ItemType Directory -Path $parent | Out-Null }
$json = $result | ConvertTo-Json -Depth 7
[IO.File]::WriteAllText($full, $json, (New-Object Text.UTF8Encoding $false))
$json
