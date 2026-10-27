"""Exact DS/3DS sources and notices; software only, never ROM/BIOS acquisition."""
import hashlib
import json
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tarfile
import time

root=Path(sys.argv[1]);licenses=root/'licenses';sources=Path('/handheld-corresponding-source')
sources.mkdir(exist_ok=True);(sources/'ubuntu').mkdir(exist_ok=True)
pin=json.loads((root/'source.json').read_text())
for name in ('desmume-source.tar.gz','citra-source.tar.gz','DeSmuME.txt','Citra.txt','citra-source-revisions.txt','build.sh','patch-citra-revision.py','citra-scm-revision.patch'):
    shutil.copy2(licenses/name,sources/name)
shutil.copy2(root/'source.json',sources/'source.json');shutil.copy2('/core-catalog.json',sources/'core-catalog.json')
shutil.copy2('/write-handheld-manifest.py',sources/'write-manifest.py')
shutil.copy2('/write-handheld-manifest.py',licenses/'write-manifest.py')
shutil.copy2('/usr/share/common-licenses/GPL-3',licenses/'GPL-3.txt');shutil.copy2(licenses/'GPL-3.txt',sources/'GPL-3.txt')
def output(args):return subprocess.check_output(args,text=True,timeout=30).strip()
paths=set()
for core in pin['cores']:
    for line in output(['ldd',str(root/'cores'/(core['id']+'_libretro.so'))]).splitlines():
        if 'not found' in line:raise RuntimeError('Unresolved handheld dependency.')
        found=re.search(r'=> (/(?:usr|lib)[^ ]+)',line) or re.search(r'^\s*(/(?:usr|lib)[^ ]+)',line)
        if found:paths.add(found[1])
packages=set()
for file in paths:
    owner=None
    for candidate in (str(Path(file).resolve()),file):
        try:owner=output(['dpkg-query','-S',candidate]).splitlines()[0].rsplit(': ',1)[0];break
        except subprocess.CalledProcessError:pass
    if not owner:raise RuntimeError('Unknown handheld linked library owner: '+file)
    packages.add(owner)
identities=set();dependencies=[]
for package in sorted(packages):
    dependencies.append(output(['dpkg-query','-W','-f=${binary:Package}=${Version}',package]))
    identities.add(output(['dpkg-query','-W','-f=${source:Package}=${source:Version}',package]))
    name=package.split(':')[0];notice=Path('/usr/share/doc')/name/'copyright'
    if not notice.is_file():raise RuntimeError('Missing dependency notice: '+package)
    shutil.copy2(notice,licenses/('ubuntu-'+name+'-copyright.txt'))
(licenses/'runtime-dependencies.txt').write_text('\n'.join(dependencies)+'\n')
(sources/'ubuntu-source-packages.txt').write_text('\n'.join(sorted(identities))+'\n');shutil.copy2(sources/'ubuntu-source-packages.txt',licenses/'ubuntu-source-packages.txt')
deadline=time.monotonic()+1200
for identity in sorted(identities):
    if identity.startswith('glibc=') or re.match(r'gcc-\d+=',identity):continue
    budget=min(180,int(deadline-time.monotonic()))
    if budget<1:raise RuntimeError('Handheld source acquisition budget expired.')
    subprocess.run(['apt-get','source','--download-only',identity],cwd=sources/'ubuntu',timeout=budget,check=True)
notices=[]
for archive_name in ('desmume-source.tar.gz','citra-source.tar.gz'):
    with tarfile.open(sources/archive_name) as archive:
        for item in archive.getmembers():
            if item.isfile() and item.size<2*1024*1024 and re.search(r'(^|/)[^/]*(license|copying|copyright|notice)[^/]*$',item.name,re.I):
                data=archive.extractfile(item).read();notices.append({'archive':archive_name,'path':item.name,'bytes':len(data),'sha256':hashlib.sha256(data).hexdigest()})
(licenses/'upstream-notice-index.json').write_text(json.dumps(notices,indent=2)+'\n');shutil.copy2(licenses/'upstream-notice-index.json',sources/'upstream-notice-index.json')
shutil.copy2(__file__,sources/'package-source.py');shutil.copy2(__file__,licenses/'package-source.py')
checks=[]
for path in sorted(sources.rglob('*')):
    if path.is_file() and path.name!='SHA256SUMS':
        with path.open('rb') as file:checks.append(hashlib.file_digest(file,'sha256').hexdigest()+'  ./'+path.relative_to(sources).as_posix())
(sources/'SHA256SUMS').write_text('\n'.join(checks)+'\n')
with tarfile.open(licenses/'corresponding-source.tar.gz','w:gz') as archive:archive.add(sources,arcname='.')
with (licenses/'corresponding-source.tar.gz').open('rb') as file:digest=hashlib.file_digest(file,'sha256').hexdigest()
(licenses/'source-availability.json').write_text(json.dumps({'format':1,'sourceCommits':{core['id']:core['sourceCommit'] for core in pin['cores']},'archiveSha256':digest,'sourcePackages':len(identities),'linkedPackages':len(packages),'upstreamNotices':len(notices)},indent=2)+'\n')
print(json.dumps({'sourcePackages':len(identities),'linkedPackages':len(packages),'upstreamNotices':len(notices)}))
