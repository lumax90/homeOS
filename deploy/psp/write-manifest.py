"""Pinned PPSSPP software assets; never a ROM/BIOS acquisition."""
import hashlib
import json
from pathlib import Path
import sys

root, pin_file, catalogue = map(Path, sys.argv[1:])
pin = json.loads(pin_file.read_text())
catalogue = json.loads(catalogue.read_text())
definition = next(core for core in catalogue['entries'] if core['id'] == 'ppsspp')
info = root / 'ppsspp_libretro.info'
info.write_text(definition['info'])

def asset(path):
    data = path.read_bytes()
    return {'path': str(path.relative_to(root)).replace('\\', '/'), 'bytes': len(data), 'sha256': hashlib.sha256(data).hexdigest()}

assets = [asset(path) for path in sorted((root / 'assets').rglob('*')) if path.is_file()]
if not any(item['path'] == 'assets/ppge_atlas.zim' for item in assets):
    raise RuntimeError('PPSSPP system assets are incomplete.')
manifest = {'format': 1, 'platform': 'linux-x64', 'version': pin['version'], 'sourceCommit': pin['sourceCommit'],
    'profile': pin['profile'], 'memoryAdapter': pin['memoryAdapter'], 'binary': asset(root / 'ppsspp_libretro.so'), 'info': asset(info), 'assets': assets}
(root / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n')
