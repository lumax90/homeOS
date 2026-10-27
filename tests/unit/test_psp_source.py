"""Original source-contract fixtures only; no software/media download."""
import hashlib
import io
import json
from pathlib import Path
import subprocess
import sys
import tarfile
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / 'deploy/psp/verify-source.py'
COMMIT = 'fa50bb1976065c4f8b1b47af227d367fe9771555'

def digest(data):
    return hashlib.sha256(data).hexdigest()

def tar_bytes(files):
    result = io.BytesIO()
    with tarfile.open(fileobj=result, mode='w:gz') as archive:
        for name, data in files.items():
            item = tarfile.TarInfo('./' + name)
            item.size = len(data)
            archive.addfile(item, io.BytesIO(data))
    return result.getvalue()

class PspSourceTests(unittest.TestCase):
    def run_fixture(self, mutation=None):
        pin = {'sourceCommit': COMMIT, 'profile': 'libretro-software-native-1x', 'memoryAdapter': 'linux-anonymous-memfd-v1', 'submodules': ['ext/fixture']}
        original = {'LICENSE.TXT': b'Original isolated licence fixture', 'CMakeLists.txt': b'original', '.gitmodules': b'original',
            'libretro/libretro.cpp': b'original', 'Common/MemArenaPosix.cpp': b'Home: using private anonymous PSP RAM backing\nSYS_memfd_create', 'ext/fixture/source.c': b'original'}
        payload = b'Original isolated Ubuntu source payload'
        descriptor = ('Source: isolated-psp\nVersion: 1.0\nChecksums-Sha256:\n ' + digest(payload) + ' ' + str(len(payload)) + ' isolated.orig.tar.gz\n').encode()
        files = {'ppsspp-source.tar.gz': tar_bytes(original), 'ubuntu-source-packages.txt': b'isolated-psp=1.0\n',
            'ubuntu/isolated.dsc': descriptor, 'ubuntu/isolated.orig.tar.gz': payload,
            'upstream-notice-index.json': json.dumps([{'path': './LICENSE.TXT', 'bytes': len(original['LICENSE.TXT']), 'sha256': digest(original['LICENSE.TXT'])}]).encode()}
        for name in ('build.sh', 'patch-private-memory.py', 'write-manifest.py', 'package-source.py', 'source-revisions.txt', 'home-private-memory.patch', 'core-catalog.json', 'source.json', 'GPL-3.txt', 'Home-PSP-profile-NOTICE.txt'):
            files[name] = b'Original isolated build input'
        if mutation:
            mutation(files)
        files['SHA256SUMS'] = ('\n'.join(digest(data) + '  ./' + name for name, data in sorted(files.items())) + '\n').encode()
        data = tar_bytes(files)
        evidence = ROOT / 'artifacts' / 'psp-source-tests'
        evidence.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(prefix='isolated-', dir=evidence) as directory:
            root = Path(directory)
            (root / 'licenses').mkdir()
            (root / 'source.json').write_text(json.dumps(pin))
            (root / 'licenses/corresponding-source.tar.gz').write_bytes(data)
            (root / 'licenses/source-availability.json').write_text(json.dumps({**pin, 'archiveSha256': digest(data), 'sourcePackages': 1}))
            return subprocess.run([sys.executable, str(SCRIPT), str(root)], capture_output=True, text=True, timeout=20)

    def test_complete_exact_source_closure_passes(self):
        result = self.run_fixture()
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertTrue(json.loads(result.stdout)['sourceVerified'])

    def test_missing_exact_source_descriptor_fails_closed(self):
        result = self.run_fixture(lambda files: files.pop('ubuntu/isolated.dsc'))
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('Exact linked PSP library source missing', result.stderr)

    def test_descriptor_payload_tamper_fails_even_with_fresh_outer_checksums(self):
        result = self.run_fixture(lambda files: files.update({'ubuntu/isolated.orig.tar.gz': b'X' * len(files['ubuntu/isolated.orig.tar.gz'])}))
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('Source descriptor payload hash mismatch', result.stderr)

    def test_missing_build_recipe_fails_closed(self):
        result = self.run_fixture(lambda files: files.pop('patch-private-memory.py'))
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('build input or notice missing', result.stderr)

    def test_archive_path_escape_fails_without_extracting_any_file(self):
        result = self.run_fixture(lambda files: files.update({'../outside': b'original'}))
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('Invalid source archive path', result.stderr)

if __name__ == '__main__':
    unittest.main()
