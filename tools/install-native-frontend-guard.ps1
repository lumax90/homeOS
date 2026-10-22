param([Parameter(Mandatory=$true)][string]$FrontendBuild, [switch]$Approved)
$ErrorActionPreference = 'Stop'
if (-not $Approved) { throw 'Separate explicit approval is required for this new executable firewall rule.' }
$receiptPath = [IO.Path]::GetFullPath($FrontendBuild)
$reviewRoot = Join-Path $env:LOCALAPPDATA 'Temp\opencode\native-gaming-runtime-v034-review'
if (-not $receiptPath.StartsWith($reviewRoot + '\',[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($receiptPath) -ne 'home-frontend-build.json') { throw 'Reviewed isolated frontend receipt required.' }
$build = Get-Content -LiteralPath $receiptPath -Raw | ConvertFrom-Json
$program = [IO.Path]::GetFullPath($build.executablePath)
$directory = [IO.Path]::GetDirectoryName($receiptPath); $leaf = [IO.Path]::GetFileName($directory)
if ($build.sourceVersion -ne '1.22.2' -or $build.sourceCommit -ne '69a4f0ea1e8aaf442ae4858f2e7f2b31a1776576' -or $build.upstreamInputFix -ne 'cfb1204314dd6b061a7b1cda77221735939bf5ba' -or -not $build.patchedPrototype -or $leaf -notmatch '^retroarch-backport-build-[a-f0-9]{32}$' -or $program -ne (Join-Path $directory 'retroarch.exe')) { throw 'The exact isolated upstream-backport executable is required.' }
foreach ($path in @($reviewRoot,$directory,$receiptPath,$program)) { if ((Get-Item -LiteralPath $path).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Reviewed guard target cannot be a link.' } }
if ((Get-Item -LiteralPath $program).Length -ne $build.executableBytes -or (Get-FileHash -LiteralPath $program -Algorithm SHA256).Hash.ToLowerInvariant() -ne $build.executableSha256) { throw 'The reviewed frontend executable changed.' }
if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    $process = Start-Process -FilePath "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" -Verb RunAs -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-File',('"'+$PSCommandPath+'"'),'-FrontendBuild',('"'+$receiptPath+'"'),'-Approved') -Wait -PassThru
    exit $process.ExitCode
}
$name = 'Home.NativePrototype.UpstreamInput.' + $leaf.Substring('retroarch-backport-build-'.Length)
$ranges = @('0.0.0.0-126.255.255.255','128.0.0.0-255.255.255.255')
$existing = Get-NetFirewallRule -Name $name -PolicyStore PersistentStore -ErrorAction SilentlyContinue
if ($existing) {
    $application = $existing | Get-NetFirewallApplicationFilter; $ports = $existing | Get-NetFirewallPortFilter; $addresses = $existing | Get-NetFirewallAddressFilter
    if ($existing.Action -ne 'Block' -or $existing.Direction -ne 'Inbound' -or $existing.Enabled -ne 'True' -or $existing.Profile -ne 'Any' -or $application.Program -ne $program -or $ports.Protocol -ne 'UDP' -or @($ports.LocalPort).Count -ne 2 -or $ports.LocalPort -notcontains '62401' -or $ports.LocalPort -notcontains '62406' -or @($addresses.RemoteAddress).Count -ne 2 -or $addresses.RemoteAddress -notcontains $ranges[0] -or $addresses.RemoteAddress -notcontains $ranges[1]) { throw 'An existing rule differs and was preserved.' }
} else {
    New-NetFirewallRule -Name $name -DisplayName 'Home upstream-input prototype - local input only' -Description 'Separately user-approved restriction for one exact reviewed frontend; no port is opened and the earlier rule is preserved.' -Direction Inbound -Action Block -Enabled True -Program $program -Protocol UDP -LocalPort 62401,62406 -RemoteAddress $ranges -Profile Any -PolicyStore PersistentStore | Out-Null
}
$rule = Get-NetFirewallRule -Name $name -PolicyStore ActiveStore
$application = $rule | Get-NetFirewallApplicationFilter; $ports = $rule | Get-NetFirewallPortFilter; $addresses = $rule | Get-NetFirewallAddressFilter
$result = [ordered]@{checkedAt=[DateTimeOffset]::UtcNow.ToString('O');name=[string]$rule.Name;action=[string]$rule.Action;direction=[string]$rule.Direction;enabled=[string]$rule.Enabled;profiles=[string]$rule.Profile;program=[string]$application.Program;programSha256=$build.executableSha256;protocol=[string]$ports.Protocol;localPorts=@($ports.LocalPort);remoteAddresses=@($addresses.RemoteAddress);openedAnyPort=$false;changedOtherRules=$false;installedDriver=$false;rebootPerformed=$false}
$outputRoot = Join-Path (Join-Path $env:LOCALAPPDATA 'Temp\opencode\native-frontend-input-guard') $leaf
[IO.Directory]::CreateDirectory($outputRoot) | Out-Null
$output = Join-Path $outputRoot ('result-'+[Guid]::NewGuid().ToString('N')+'.json')
[IO.File]::WriteAllText($output,($result | ConvertTo-Json -Depth 6),(New-Object Text.UTF8Encoding $false))
$result | ConvertTo-Json -Depth 6
