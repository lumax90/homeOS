"""Isolated image/runtime and existing movie/live protocol checks; no browser or internet media."""
import json
from pathlib import Path
import subprocess
import sys

image, destination = sys.argv[1:]
output = Path(destination).resolve()
if output.exists():
    raise RuntimeError('Existing evidence preserved; choose a new verification directory.')
output.mkdir(parents=True)
root = Path(__file__).resolve().parents[2]

def docker(*arguments):
    result = subprocess.run(['docker', *arguments], capture_output=True, text=True, timeout=60, check=True)
    return result.stdout

pin = json.loads((root / 'deploy/media/runtime.json').read_text())
config = json.loads(docker('image', 'inspect', image))[0]
assert config['Os'] == 'linux' and config['Architecture'] == 'amd64'
environment = dict(value.split('=', 1) for value in config['Config']['Env'] if '=' in value)
assert environment['Home__FfmpegPath'] == '/app/MediaRuntime/bin/ffmpeg'
assert environment['Home__FfprobePath'] == '/app/MediaRuntime/bin/ffprobe'
restricted = ['run', '--rm', '--network', 'none', '--read-only', '--cap-drop', 'ALL', '--user', '1654:1654']
version = docker(*restricted, '--entrypoint', environment['Home__FfmpegPath'], image, '-version')
assert '8.1.3-Jellyfin' in version and '--enable-nonfree' not in version
(output / 'runtime-version.txt').write_text(version)
installed = json.loads(docker(*restricted, '--entrypoint', '/bin/cat', image, '/app/MediaRuntime/source.json'))
assert installed == pin
proof = docker(*restricted, '--entrypoint', '/bin/cat', image, '/app/MediaRuntime/test-proof.txt')
assert 'checks passed' in proof
native = json.loads(docker(*restricted, '--entrypoint', '/bin/cat', image, '/app/NativeRuntime/manifest.json'))
lock = json.loads((root / 'deploy/native/runtime-lock.json').read_text())
assert native['frontend']['sha256'] == lock['assets']['frontend']
assert native['mediaMtx']['sha256'] == lock['assets']['mediaMtx']
assert all(core['binary']['sha256'] == lock['assets'][core['id']] for core in native['cores'])
for script, directory in [('verify-playback-efficiency.py', 'movie'), ('verify-live-stability.py', 'live')]:
    result = subprocess.run([sys.executable, str(root / 'tools/verification' / script), image, str(output / directory)],
        text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=600)
    (output / (directory + '.log')).write_text(result.stdout)
    if result.returncode:
        raise RuntimeError(result.stdout[-12000:])
report = {'checks': ['configured-pinned-media-runtime', 'runtime-notices-and-native-test-proof', 'canonical-game-binary-identity',
    'existing-movie-http-preservation-and-production', 'existing-live-http-recovery-and-isolation'],
    'errors': [], 'internetMediaAcquired': False, 'automatedBrowserRun': False,
    'movie': json.loads((output / 'movie/result.json').read_text()), 'live': json.loads((output / 'live/result.json').read_text())}
(output / 'result.json').write_text(json.dumps(report, indent=2))
print(json.dumps(report, indent=2))
