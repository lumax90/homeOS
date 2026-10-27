"""Exact corresponding sources/notices for the PPSSPP and linked Ubuntu profile.

Runs in the source-packaging image, never acquires ROMs/BIOS/media. Ubuntu
source downloads use authenticated archive indexes and remain checksum-bound.
"""
import hashlib
import json
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tarfile
import time

root = Path(sys.argv[1])
sources = Path('/psp-corresponding-source')
sources.mkdir(exist_ok=True)
(sources / 'ubuntu').mkdir(exist_ok=True)
licenses = root / 'licenses'
shutil.copy2('/usr/share/common-licenses/GPL-3', licenses / 'GPL-3.txt')
(licenses / 'Home-PSP-profile-NOTICE.txt').write_text(
    'Home PSP Linux x64 software profile\n'
    'PPSSPP 1.20.4 upstream is GPL-2.0-or-later. This linked profile is\n'
    'distributed under GPL-3.0-or-later to accommodate its linked GPLv3/\n'
    'Apache-2.0-compatible components. Individual data/dependency notices\n'
    'remain in the indexed source archive and Ubuntu copyright files.\n'
    'Home adaptation: private anonymous Linux RAM backing (memfd).\n'
    'No Sony BIOS, commercial game or user media is distributed.\n'
)
pin = json.loads((root / 'source.json').read_text())
shutil.copy2(licenses / 'ppsspp-corresponding-source.tar.gz', sources / 'ppsspp-source.tar.gz')
for name in ('build.sh', 'patch-private-memory.py', 'write-manifest.py', 'home-private-memory.patch', 'source-revisions.txt'):
    shutil.copy2(licenses / name, sources / name)
shutil.copy2(root / 'source.json', sources / 'source.json')
shutil.copy2('/core-catalog.json', sources / 'core-catalog.json')
shutil.copy2(licenses / 'GPL-3.txt', sources / 'GPL-3.txt')
shutil.copy2(licenses / 'Home-PSP-profile-NOTICE.txt', sources / 'Home-PSP-profile-NOTICE.txt')

def output(args):
    return subprocess.check_output(args, text=True, timeout=30).strip()

paths = set()
for line in output(['ldd', str(root / 'ppsspp_libretro.so')]).splitlines():
    if 'not found' in line:
        raise RuntimeError('Unresolved PSP dependency.')
    match = re.search(r'=> (/(?:usr|lib)[^ ]+)', line) or re.search(r'^\s*(/(?:usr|lib)[^ ]+)', line)
    if match:
        paths.add(match[1])

packages = set()
for file in sorted(paths):
    owner = None
    for candidate in (str(Path(file).resolve()), file):
        try:
            owner = output(['dpkg-query', '-S', candidate]).splitlines()[0].rsplit(': ', 1)[0]
            break
        except subprocess.CalledProcessError:
            pass
    if not owner:
        raise RuntimeError('Unknown linked PSP library owner: ' + file)
    packages.add(owner)

dependencies, source_packages = [], set()
for package in sorted(packages):
    identity = output(['dpkg-query', '-W', '-f=${binary:Package}=${Version}', package])
    source = output(['dpkg-query', '-W', '-f=${source:Package}=${source:Version}', package])
    dependencies.append(identity)
    source_packages.add(source)
    name = package.split(':')[0]
    notice = Path('/usr/share/doc') / name / 'copyright'
    if not notice.is_file():
        raise RuntimeError('Missing linked PSP library copyright: ' + package)
    shutil.copy2(notice, licenses / ('ubuntu-' + name + '-copyright.txt'))

(licenses / 'runtime-dependencies.txt').write_text('\n'.join(dependencies) + '\n')
(sources / 'ubuntu-source-packages.txt').write_text('\n'.join(sorted(source_packages)) + '\n')
shutil.copy2(sources / 'ubuntu-source-packages.txt', licenses / 'ubuntu-source-packages.txt')

deadline = time.monotonic() + 1200
for package in sorted(source_packages):
    # The standard libc/toolchain runtime is identified/noticed separately.
    if package.startswith('glibc=') or re.match(r'gcc-\d+=', package):
        continue
    budget = min(180, int(deadline - time.monotonic()))
    if budget < 1:
        raise RuntimeError('PSP corresponding-source acquisition budget expired.')
    subprocess.run(['apt-get', 'source', '--download-only', package], cwd=sources / 'ubuntu', check=True, timeout=budget)
    if sum(path.stat().st_size for path in (sources / 'ubuntu').iterdir() if path.is_file()) > 3 * 1024**3:
        raise RuntimeError('PSP corresponding-source archive exceeded its size bound.')

notice_index = []
with tarfile.open(sources / 'ppsspp-source.tar.gz') as archive:
    for item in archive.getmembers():
        if item.isfile() and re.search(r'(^|/)([^/]*(?:license|copying|copyright|ofl|notice)[^/]*)$', item.name, re.I):
            if item.size > 2 * 1024 * 1024:
                continue
            data = archive.extractfile(item).read()
            notice_index.append({'path': item.name, 'bytes': len(data), 'sha256': hashlib.sha256(data).hexdigest()})
if not any(item['path'].endswith('/LICENSE.TXT') for item in notice_index):
    raise RuntimeError('PPSSPP source licence is absent.')
(licenses / 'upstream-notice-index.json').write_text(json.dumps(notice_index, indent=2) + '\n')
shutil.copy2(licenses / 'upstream-notice-index.json', sources / 'upstream-notice-index.json')
shutil.copy2(__file__, sources / 'package-source.py')
shutil.copy2(__file__, licenses / 'package-source.py')
checksums = []
for path in sorted(sources.rglob('*')):
    if path.is_file() and path.name != 'SHA256SUMS':
        checksums.append(hashlib.file_digest(path.open('rb'), 'sha256').hexdigest() + '  ./' + path.relative_to(sources).as_posix())
(sources / 'SHA256SUMS').write_text('\n'.join(checksums) + '\n')
with tarfile.open(licenses / 'corresponding-source.tar.gz', 'w:gz') as archive:
    archive.add(sources, arcname='.', recursive=True)
(licenses / 'source-availability.json').write_text(json.dumps({
    'format': 1, 'sourceCommit': pin['sourceCommit'], 'profile': pin['profile'],
    'memoryAdapter': pin['memoryAdapter'], 'linkedPackages': len(packages),
    'sourcePackages': len(source_packages), 'upstreamNotices': len(notice_index),
    'systemRuntimeExcluded': ['glibc', 'gcc runtime'],
    'archiveSha256': hashlib.file_digest((licenses / 'corresponding-source.tar.gz').open('rb'), 'sha256').hexdigest(),
}, indent=2) + '\n')
print(json.dumps({'sourcePackages': len(source_packages), 'linkedPackages': len(packages), 'upstreamNotices': len(notice_index)}))
