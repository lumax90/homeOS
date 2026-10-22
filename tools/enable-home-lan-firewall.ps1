param([ValidateRange(1024,65535)][int]$HttpPort=5080)
$ErrorActionPreference='Stop'
$identity=[Security.Principal.WindowsIdentity]::GetCurrent()
if(-not([Security.Principal.WindowsPrincipal]::new($identity)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){throw 'Run this firewall configuration as administrator.'}
$definitions=@(
  @{Name=('Home-LAN-Web-'+$HttpPort);Protocol='TCP';Port=$HttpPort},
  @{Name='Home-LAN-Game-UDP-62405';Protocol='UDP';Port=62405},
  @{Name='Home-LAN-Game-TCP-62405';Protocol='TCP';Port=62405}
)
foreach($definition in $definitions){
  $existing=Get-NetFirewallRule -Name $definition.Name -ErrorAction SilentlyContinue
  if($existing){if($existing.Group -ne 'Home LAN'){throw ('An unrelated rule owns '+$definition.Name)};continue}
  New-NetFirewallRule -Name $definition.Name -DisplayName $definition.Name -Group 'Home LAN' -Direction Inbound -Action Allow -Enabled True -Profile Any -Protocol $definition.Protocol -LocalPort $definition.Port -RemoteAddress LocalSubnet | Out-Null
}
Get-NetFirewallRule -Group 'Home LAN' | Select-Object Name,Enabled,Direction,Action
