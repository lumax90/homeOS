param([string]$Root=(Join-Path $env:LOCALAPPDATA 'Temp\opencode\home-linux-lan-verification'))
$ErrorActionPreference='Stop'
$node=Join-Path $env:LOCALAPPDATA 'Temp\opencode\runtimes\node-v24.21.0-win-x64\node.exe'
$seed=Join-Path $Root 'seed';New-Item -ItemType Directory -Path $seed -Force | Out-Null
$key=Join-Path $Root 'diagnostic-key'
if(-not(Test-Path -LiteralPath $key)){& ssh-keygen.exe -q -t ed25519 -N '""' -f $key;if($LASTEXITCODE -ne 0){throw 'Diagnostic SSH key generation failed.'}}
$public=(Get-Content -LiteralPath ($key+'.pub') -Raw).Trim()
$config=@"
#cloud-config
hostname: home-linux-verification
manage_etc_hosts: true
ssh_pwauth: false
users:
  - name: diagnostic
    groups: [sudo, docker]
    shell: /bin/bash
    sudo: ['ALL=(ALL) NOPASSWD:ALL']
    ssh_authorized_keys:
      - $public
package_update: true
packages: [docker.io, docker-buildx, docker-compose-v2, python3-venv]
runcmd:
  - [systemctl, enable, --now, docker]
  - [touch, /home/diagnostic/cloud-ready]
"@
[IO.File]::WriteAllText((Join-Path $seed 'user-data'),$config,(New-Object Text.UTF8Encoding $false))
[IO.File]::WriteAllText((Join-Path $seed 'meta-data'),"instance-id: home-linux-verification-043`nlocal-hostname: home-linux-verification`n",(New-Object Text.UTF8Encoding $false))
$seedServer=Start-Process -FilePath $node -ArgumentList @(('"'+(Join-Path $PSScriptRoot 'seed-server.mjs')+'"'),('"'+$seed+'"')) -RedirectStandardOutput (Join-Path $Root 'seed.stdout.log') -RedirectStandardError (Join-Path $Root 'seed.stderr.log') -PassThru
$qemu=Join-Path $Root 'qemu\qemu-system-x86_64.exe'
$vm=Start-Process -FilePath $qemu -WorkingDirectory (Split-Path $qemu -Parent) -ArgumentList @('-machine','q35,accel=whpx:tcg','-cpu','max','-smp','4','-m','4096','-display','none','-serial',('file:"'+(Join-Path $Root 'serial.log')+'"'),'-monitor','none','-drive',('file="'+(Join-Path $Root 'diagnostic.qcow2')+'",format=qcow2,if=virtio'),'-netdev','user,id=net0,hostfwd=tcp:127.0.0.1:62300-:22,hostfwd=tcp:127.0.0.1:62301-:5080','-device','virtio-net-pci,netdev=net0','-smbios','"type=1,serial=ds=nocloud-net;s=http://10.0.2.2:62302/"') -RedirectStandardOutput (Join-Path $Root 'vm.stdout.log') -RedirectStandardError (Join-Path $Root 'vm.stderr.log') -PassThru
[ordered]@{vmPid=$vm.Id;seedPid=$seedServer.Id} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $Root 'processes.json') -Encoding UTF8
$ssh=@('-i',$key,'-p','62300','-o','BatchMode=yes','-o','ConnectTimeout=2','-o','StrictHostKeyChecking=accept-new','-o',('UserKnownHostsFile='+ (Join-Path $Root 'known_hosts')),'diagnostic@127.0.0.1')
$ready=$false
for($attempt=0;$attempt -lt 150;$attempt++){
  $vm.Refresh();if($vm.HasExited){throw ('Diagnostic VM exited: '+(Get-Content -LiteralPath (Join-Path $Root 'vm.stderr.log') -Raw))}
  $ErrorActionPreference='Continue';& ssh.exe @ssh true *> $null;$status=$LASTEXITCODE;$ErrorActionPreference='Stop'
  if($status -eq 0){$ready=$true;break};Start-Sleep -Seconds 2
}
if(-not $ready){throw 'Diagnostic VM SSH readiness timed out.'}
& ssh.exe @ssh 'cloud-init status --wait'
if($LASTEXITCODE -notin @(0,2)){throw 'Diagnostic VM provisioning failed.'}
& ssh.exe @ssh 'docker version --format "{{.Server.Version}}"'
if($LASTEXITCODE -ne 0){throw 'Diagnostic Docker daemon unavailable.'}
Stop-Process -Id $seedServer.Id
[ordered]@{root=$Root;vmPid=$vm.Id;sshPort=62300;httpPort=62301;ready=$true} | ConvertTo-Json
