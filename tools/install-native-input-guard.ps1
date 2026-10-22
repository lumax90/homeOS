param([switch]$Approved)
$ErrorActionPreference = 'Stop'
if (-not $Approved) { throw 'Explicit approval is required for this firewall rule.' }
$program = Join-Path $env:LOCALAPPDATA 'Temp\opencode\native-gaming-runtime-v034-review\retroarch\files\RetroArch-Win64\retroarch.exe'
$expected = '81c11b6f24932bf7918f05eee8928035bff3887335fd2a081507c75e9d94d06a'
if ((Get-Item -LiteralPath $program).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'The reviewed executable cannot be a link.' }
if ((Get-FileHash -LiteralPath $program -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected) { throw 'The reviewed RetroArch executable changed.' }
if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    $process = Start-Process -FilePath "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" -Verb RunAs -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', ('"' + $PSCommandPath + '"'), '-Approved') -Wait -PassThru
    exit $process.ExitCode
}
$name = 'Home.NativePrototype.LocalInput.v034'
$ranges = @('0.0.0.0-126.255.255.255', '128.0.0.0-255.255.255.255')
$existing = Get-NetFirewallRule -Name $name -PolicyStore PersistentStore -ErrorAction SilentlyContinue
if ($existing) {
    $application = $existing | Get-NetFirewallApplicationFilter
    $ports = $existing | Get-NetFirewallPortFilter
    $addresses = $existing | Get-NetFirewallAddressFilter
    if ($existing.Action -ne 'Block' -or $existing.Direction -ne 'Inbound' -or $existing.Enabled -ne 'True' -or $application.Program -ne $program -or $ports.Protocol -ne 'UDP' -or @($ports.LocalPort).Count -ne 2 -or $ports.LocalPort -notcontains '62401' -or $ports.LocalPort -notcontains '62406' -or @($addresses.RemoteAddress).Count -ne 2 -or $addresses.RemoteAddress -notcontains $ranges[0] -or $addresses.RemoteAddress -notcontains $ranges[1]) { throw 'An existing firewall rule differs; it was preserved.' }
} else {
    New-NetFirewallRule -Name $name -DisplayName 'Home native prototype - local input only' -Description 'User-approved restriction for the exact reviewed RetroArch prototype; no port is opened.' -Direction Inbound -Action Block -Enabled True -Program $program -Protocol UDP -LocalPort 62401,62406 -RemoteAddress $ranges -Profile Any -PolicyStore PersistentStore | Out-Null
}
$rule = Get-NetFirewallRule -Name $name -PolicyStore ActiveStore
$application = $rule | Get-NetFirewallApplicationFilter; $ports = $rule | Get-NetFirewallPortFilter; $addresses = $rule | Get-NetFirewallAddressFilter
$result = [ordered]@{ checkedAt = [DateTimeOffset]::UtcNow.ToString('O'); name = [string]$rule.Name; action = [string]$rule.Action; direction = [string]$rule.Direction; enabled = [string]$rule.Enabled; program = [string]$application.Program; programSha256 = $expected; protocol = [string]$ports.Protocol; localPorts = @($ports.LocalPort | ForEach-Object { [string]$_ }); remoteAddresses = @($addresses.RemoteAddress | ForEach-Object { [string]$_ }); profiles = [string]$rule.Profile; openedAnyPort = $false; rebootPerformed = $false; changedOtherRules = $false }
$root = Join-Path $env:LOCALAPPDATA 'Temp\opencode\native-input-guard-20261019'
if (-not (Test-Path -LiteralPath $root)) { New-Item -ItemType Directory -Path $root | Out-Null }
$receipt = Join-Path $root 'result.json'
if (Test-Path -LiteralPath $receipt) { throw 'A firewall receipt already exists; it was preserved.' }
[IO.File]::WriteAllText($receipt, ($result | ConvertTo-Json -Depth 5), (New-Object Text.UTF8Encoding $false))
$result | ConvertTo-Json -Depth 5
