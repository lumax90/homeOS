"""Read-only DS/3DS source closure check: exact files, source descriptors/notices."""
import hashlib
import json
from pathlib import Path, PurePosixPath
import re
import sys
import tarfile

root=Path(sys.argv[1]);licenses=root/'licenses';pin=json.loads((root/'source.json').read_text());proof=json.loads((licenses/'source-availability.json').read_text())
if proof['sourceCommits']!={core['id']:core['sourceCommit'] for core in pin['cores']}:raise RuntimeError('Handheld source identities changed.')
path=licenses/'corresponding-source.tar.gz'
with path.open('rb') as file:
    if hashlib.file_digest(file,'sha256').hexdigest()!=proof['archiveSha256']:raise RuntimeError('Handheld source archive hash mismatch.')
def safe(name):
    name=PurePosixPath(name)
    if name.is_absolute() or '..' in name.parts:raise RuntimeError('Unsafe handheld source path.')
    return str(name).removeprefix('./')
with tarfile.open(path) as archive:
    members={safe(item.name):item for item in archive.getmembers() if item.isfile()}
    def text(name):
        if members[name].size>4*1024*1024:raise RuntimeError('Handheld source metadata is too large.')
        return archive.extractfile(members[name]).read().decode()
    declared=set()
    for line in text('SHA256SUMS').splitlines():
        digest,name=line.split('  ',1);name=safe(name)
        if name in declared or name not in members:raise RuntimeError('Invalid handheld source manifest.')
        with archive.extractfile(members[name]) as file:
            if hashlib.file_digest(file,'sha256').hexdigest()!=digest:raise RuntimeError('Handheld source file hash mismatch: '+name)
        declared.add(name)
    if declared!=set(members)-{'SHA256SUMS'}:raise RuntimeError('Unqualified handheld source file.')
    if not {'build.sh','write-manifest.py','patch-citra-revision.py','core-catalog.json','source.json'}.issubset(declared):raise RuntimeError('Handheld build/manifest recipe is incomplete.')
    descriptors={}
    for name in members:
        if name.startswith('ubuntu/') and name.endswith('.dsc'):
            data=text(name);source=re.search(r'^Source: (.+)$',data,re.M)[1];version=re.search(r'^Version: (.+)$',data,re.M)[1]
            hashes=re.search(r'^Checksums-Sha256:\n((?: [^\n]+\n)+)',data,re.M)
            if hashes is None:raise RuntimeError('Source descriptor has no hashes.')
            for line in hashes[1].splitlines():
                digest,size,leaf=line.split();dependency='ubuntu/'+safe(leaf)
                if dependency not in members or members[dependency].size!=int(size):raise RuntimeError('Missing handheld dependency source.')
                with archive.extractfile(members[dependency]) as file:
                    if hashlib.file_digest(file,'sha256').hexdigest()!=digest:raise RuntimeError('Dependency source hash mismatch.')
            descriptors[source+'='+version]=name
    identities=text('ubuntu-source-packages.txt').splitlines()
    for identity in identities:
        if identity.startswith('glibc=') or re.match(r'gcc-\d+=',identity):continue
        if identity not in descriptors:raise RuntimeError('Exact handheld source descriptor missing: '+identity)
    notices=json.loads(text('upstream-notice-index.json'))
    for name in ('desmume-source.tar.gz','citra-source.tar.gz'):
        with tarfile.open(fileobj=archive.extractfile(members[name])) as upstream:
            files={safe(item.name):item for item in upstream.getmembers() if item.isfile()}
            required='desmume/COPYING' if name.startswith('desmume') else 'license.txt'
            if required not in files:raise RuntimeError('Upstream licence absent.')
            if name.startswith('citra'):
                revision=upstream.extractfile(files['CMakeModules/GenerateBuildInfo.cmake']).read()
                if b'Home: script-mode' not in revision or b'rev-parse HEAD' not in revision:raise RuntimeError('Citra native-state revision fix source absent.')
            for notice in (entry for entry in notices if entry['archive']==name):
                file=safe(notice['path'])
                if file not in files or files[file].size!=notice['bytes']:raise RuntimeError('Upstream notice absent.')
                if hashlib.sha256(upstream.extractfile(files[file]).read()).hexdigest()!=notice['sha256']:raise RuntimeError('Upstream notice hash mismatch.')
    print(json.dumps({'sourceVerified':True,'sourcePackages':len(identities),'sourceDescriptors':len(descriptors),'upstreamNotices':len(notices)}))
