import hashlib
import json
from pathlib import Path
import sys

root, pin_file, catalogue = map(Path, sys.argv[1:])
pin, catalogue = json.loads(pin_file.read_text()), json.loads(catalogue.read_text())
def asset(path):
    data = path.read_bytes()
    return {'path': path.relative_to(root).as_posix(), 'bytes': len(data), 'sha256': hashlib.sha256(data).hexdigest()}
cores = []
for entry in pin['cores']:
    definition = next(core for core in catalogue['entries'] if core['id'] == entry['id'])
    info = root / 'cores' / (entry['id'] + '_libretro.info')
    info.write_text(definition['info'])
    cores.append({**entry, 'binary': asset(root / 'cores' / (entry['id'] + '_libretro.so')), 'info': asset(info), 'proof': None})
(root / 'manifest.json').write_text(json.dumps({'format': 1, 'platform': pin['platform'], 'cores': cores}, indent=2) + '\n')
