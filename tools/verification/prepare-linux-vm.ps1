param([string]$Root=(Join-Path $env:LOCALAPPDATA 'Temp\opencode\home-linux-lan-verification'))
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Path $Root -Force | Out-Null
$sources=@(
  @{name='qemu.exe';url='https://qemu.weilnetz.de/w64/qemu-w64-setup-20260811.exe';algorithm='SHA512';hash='5bcf9eed634e8575a37b74f445af41a2fe4106da512d0c30c368301d4c105037fdfab40a5287367a28a957624cddebbc8c07e16c88ab6634f554cdf3d16bf543'},
  @{name='ubuntu-noble-20261017.img';url='https://cloud-images.ubuntu.com/noble/20261017/noble-server-cloudimg-amd64.img';algorithm='SHA256';hash='04e8f2461a16a44e78a370284471f29616cdd9baf1893df25807b71f985b6c94'},
  @{name='7zip.msi';url='https://github.com/ip7z/7zip/releases/download/26.05/7z2605-x64.msi';algorithm='SHA256';hash='3fe4ab99fa15629c652944309a2b2bc6e41fc933fc6b63f5df1ebcf3aea68263'}
)
foreach($source in $sources){
  $path=Join-Path $Root $source.name
  if(-not(Test-Path -LiteralPath $path)){& curl.exe --fail --location --retry 3 --silent --show-error --output ($path+'.partial') $source.url;if($LASTEXITCODE -ne 0){throw ('Software download failed: '+$source.name)};Move-Item -LiteralPath ($path+'.partial') -Destination $path}
  if((Get-FileHash -LiteralPath $path -Algorithm $source.algorithm).Hash.ToLowerInvariant() -ne $source.hash){throw ('Software integrity mismatch; file preserved: '+$path)}
}
$extract=Join-Path $Root '7zip-extracted'
if(-not(Test-Path -LiteralPath $extract)){
  $process=Start-Process msiexec.exe -ArgumentList @('/a',('"'+(Join-Path $Root '7zip.msi')+'"'),'/qn',('TARGETDIR="'+$extract+'"'),'/l*v',('"'+(Join-Path $Root '7zip-extract.log')+'"')) -Wait -PassThru
  if($process.ExitCode -ne 0){throw ('MSI archive extraction failed: '+$process.ExitCode)}
}
$seven=(Get-ChildItem -LiteralPath $extract -Filter 7z.exe -Recurse | Select-Object -First 1).FullName
if(-not $seven){throw 'Extracted archive tool missing.'}
$qemu=Join-Path $Root 'qemu'
if(-not(Test-Path -LiteralPath $qemu)){& $seven x (Join-Path $Root 'qemu.exe') ('-o'+$qemu) '-y' | Out-Null;if($LASTEXITCODE -ne 0){throw 'Portable QEMU extraction failed.'}}
if(-not(Test-Path -LiteralPath (Join-Path $qemu 'qemu-system-x86_64.exe'))){throw 'QEMU system binary missing.'}
$disk=Join-Path $Root 'diagnostic.qcow2'
if(-not(Test-Path -LiteralPath $disk)){& (Join-Path $qemu 'qemu-img.exe') create -f qcow2 -F qcow2 -b (Join-Path $Root 'ubuntu-noble-20261017.img') $disk 24G;if($LASTEXITCODE -ne 0){throw 'Diagnostic overlay creation failed.'}}
$sources | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $Root 'software-provenance.json') -Encoding UTF8
[ordered]@{root=$Root;qemu=(Join-Path $qemu 'qemu-system-x86_64.exe');disk=$disk;hostOsChanged=$false;userDataTouched=$false} | ConvertTo-Json
