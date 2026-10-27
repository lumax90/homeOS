"""Read-only PSP corresponding-source closure qualification; no downloads."""
import hashlib
import json
from pathlib import Path, PurePosixPath
import re
import sys
import tarfile

root = Path(sys.argv[1])
pin = json.loads((root / 'source.json').read_text())
licenses = root / 'licenses'
availability = json.loads((licenses / 'source-availability.json').read_text())
archive_path = licenses / 'corresponding-source.tar.gz'
if pin['sourceCommit'] != 'fa50bb1976065c4f8b1b47af227d367fe9771555' or any(availability[key] != pin[key] for key in ('sourceCommit', 'profile', 'memoryAdapter')):
    raise RuntimeError('PSP source identity mismatch.')
with archive_path.open('rb') as file:
    if hashlib.file_digest(file, 'sha256').hexdigest() != availability['archiveSha256']:
        raise RuntimeError('PSP corresponding-source archive hash mismatch.')

def safe(name):
    value = PurePosixPath(name)
    if value.is_absolute() or '..' in value.parts:
        raise RuntimeError('Invalid source archive path.')
    return str(value).removeprefix('./')

with tarfile.open(archive_path) as archive:
    members = {safe(item.name): item for item in archive.getmembers() if item.isfile()}
    def text(name):
        item = members[name]
        if item.size > 4 * 1024 * 1024:
            raise RuntimeError('Source metadata exceeded its bound.')
        return archive.extractfile(item).read().decode()
    for name in ('build.sh', 'patch-private-memory.py', 'write-manifest.py', 'package-source.py', 'source-revisions.txt', 'home-private-memory.patch', 'core-catalog.json', 'source.json', 'GPL-3.txt', 'Home-PSP-profile-NOTICE.txt'):
        if name not in members:
            raise RuntimeError('PSP source build input or notice missing: ' + name)
    declared = set()
    for line in text('SHA256SUMS').splitlines():
        digest, name = line.split('  ', 1)
        name = safe(name)
        if name in declared or name not in members or not re.fullmatch('[0-9a-f]{64}', digest):
            raise RuntimeError('Invalid PSP source checksum manifest.')
        with archive.extractfile(members[name]) as source:
            if hashlib.file_digest(source, 'sha256').hexdigest() != digest:
                raise RuntimeError('PSP source file hash mismatch: ' + name)
        declared.add(name)
    if declared != set(members) - {'SHA256SUMS'}:
        raise RuntimeError('PSP source archive contains unqualified files.')

    packages = text('ubuntu-source-packages.txt').splitlines()
    if len(packages) != availability['sourcePackages']:
        raise RuntimeError('PSP source package inventory mismatch.')
    dsc = {}
    for name in members:
        if name.startswith('ubuntu/') and name.endswith('.dsc'):
            content = text(name)
            source = re.search(r'^Source: (.+)$', content, re.M)[1]
            version = re.search(r'^Version: (.+)$', content, re.M)[1]
            section = re.search(r'^Checksums-Sha256:\n((?: [^\n]+\n)+)', content, re.M)
            if not section:
                raise RuntimeError('Source descriptor lacks SHA-256 manifest.')
            for line in section[1].splitlines():
                digest, size, leaf = line.split()
                dependency = 'ubuntu/' + safe(leaf)
                if dependency not in members or members[dependency].size != int(size):
                    raise RuntimeError('Incomplete source descriptor payload: ' + dependency)
                with archive.extractfile(members[dependency]) as file:
                    if hashlib.file_digest(file, 'sha256').hexdigest() != digest:
                        raise RuntimeError('Source descriptor payload hash mismatch.')
            dsc[source + '=' + version] = name
    for package in packages:
        if package.startswith('glibc=') or re.match(r'gcc-\d+=', package):
            continue
        if package not in dsc:
            raise RuntimeError('Exact linked PSP library source missing: ' + package)

    with tarfile.open(fileobj=archive.extractfile(members['ppsspp-source.tar.gz'])) as upstream:
        source = {safe(item.name): item for item in upstream.getmembers() if item.isfile()}
        required = ['LICENSE.TXT', 'CMakeLists.txt', '.gitmodules', 'libretro/libretro.cpp', 'Common/MemArenaPosix.cpp']
        for path in required:
            if path not in source:
                raise RuntimeError('Incomplete PPSSPP source: ' + path)
        memory = upstream.extractfile(source['Common/MemArenaPosix.cpp']).read()
        if b'Home: using private anonymous PSP RAM backing' not in memory or b'SYS_memfd_create' not in memory:
            raise RuntimeError('Shipped PSP memory adapter source is absent.')
        for module in pin['submodules']:
            if not any(path.startswith(module + '/') for path in source):
                raise RuntimeError('Pinned PSP submodule source is absent: ' + module)
        notices = json.loads(text('upstream-notice-index.json'))
        for notice in notices:
            path = safe(notice['path'])
            if path not in source or source[path].size != notice['bytes']:
                raise RuntimeError('Indexed upstream notice is absent.')
            data = upstream.extractfile(source[path]).read()
            if hashlib.sha256(data).hexdigest() != notice['sha256']:
                raise RuntimeError('Indexed upstream notice hash mismatch.')
    result = {'format': 1, 'sourceCommit': pin['sourceCommit'], 'sourcePackages': len(packages),
        'sourceDescriptors': len(dsc), 'upstreamNotices': len(notices), 'sourceVerified': True}
    print(json.dumps(result))
