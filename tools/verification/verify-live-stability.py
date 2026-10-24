"""Real container/HTTP live-stream checks using generated HLS and a private fault-injecting origin."""
import hashlib
import http.cookiejar
import json
from pathlib import Path
import secrets
import socket
import subprocess
import sys
import time
import urllib.error
import urllib.request

image, destination = sys.argv[1:]
root = Path(destination).resolve()
if root.exists():
    raise RuntimeError('Existing evidence was preserved; choose a new directory.')
root.mkdir(parents=True)
media, data = root / 'media', root / 'data'
media.mkdir(); data.mkdir()
name = 'home-live-fixture-' + secrets.token_hex(6)
origin, network = name + '-origin', name + '-net'
node = 'node:24-bookworm-slim@sha256:55fb5f5a527ea2c5c4a95f963e48ef90aa38c7b346054fdc8ee9957a2b55d205'
with socket.socket() as listener:
    listener.bind(('127.0.0.1', 0)); port = listener.getsockname()[1]

def docker(*args, check=True, timeout=180):
    result = subprocess.run(['docker', *args], capture_output=True, text=True, timeout=timeout)
    if check and result.returncode:
        raise RuntimeError((result.stdout + result.stderr)[-12000:])
    return result.stdout.strip()

class Client:
    def __init__(self):
        self.opener = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))
        self.headers = {}
    def call(self, path, body=None, method=None, raw=False, extra=None):
        headers = {**self.headers, **(extra or {})}
        if body is not None:
            headers['Content-Type'] = 'application/json'
        request = urllib.request.Request(f'http://127.0.0.1:{port}/api' + path, data=None if body is None else json.dumps(body).encode(), headers=headers, method=method or ('POST' if body is not None else 'GET'))
        with self.opener.open(request, timeout=65) as response:
            value = response.read()
            return (response.status, value) if raw else json.loads(value) if value else None
    def session(self):
        value = self.call('/session')
        self.headers = {'X-CSRF-TOKEN': value['csrfToken']}
        if value['authenticated']:
            self.headers.update({'X-Home-User': value['userId'], 'X-Home-Session': value['sessionId']})
        return value

def wait(read, predicate, seconds=120):
    until = time.monotonic() + seconds
    while time.monotonic() < until:
        value = read()
        if predicate(value):
            return value
        time.sleep(.25)
    raise RuntimeError('Live verification deadline exceeded.')

try:
    for audio in ['aac', 'ac3']:
        (media / audio).mkdir()
        docker('run', '--rm', '--network', 'none', '--user', '0:0', '--entrypoint', '/usr/bin/ffmpeg', '--mount', f'type=bind,source={media},target=/fixtures', image,
            '-hide_banner', '-loglevel', 'error', '-nostdin', '-f', 'lavfi', '-i', 'testsrc2=size=320x180:rate=24', '-f', 'lavfi', '-i', 'sine=sample_rate=48000',
            '-t', '12', '-threads', '1', '-c:v', 'libx264', '-preset', 'ultrafast', '-g', '48', '-c:a', audio, '-f', 'hls', '-hls_time', '4', '-hls_list_size', '0',
            '-hls_segment_filename', f'/fixtures/{audio}/segment%09d.ts', f'/fixtures/{audio}/index.m3u8')
    originals = {str(file.relative_to(media)): hashlib.sha256(file.read_bytes()).hexdigest() for file in media.rglob('*') if file.is_file()}
    docker('network', 'create', '--internal', network)
    script = Path(__file__).with_name('live-fixture-origin.mjs').resolve()
    docker('run', '-d', '--name', origin, '--network', network, '--network-alias', 'live-fixture-origin', '--read-only', '--cap-drop', 'ALL',
        '--mount', f'type=bind,source={media},target=/fixtures,readonly', '--mount', f'type=bind,source={script},target=/origin.mjs,readonly', node, 'node', '/origin.mjs', '/fixtures')
    docker('run', '--rm', '--network', 'none', '--user', '0:0', '--read-only', '--cap-drop', 'ALL', '--cap-add', 'CHOWN', '--entrypoint', '/bin/sh', '--mount', f'type=bind,source={data},target=/data', image, '/app/prepare-data.sh')
    docker('run', '-d', '--name', name, '--network', 'bridge', '--read-only', '--cap-drop', 'ALL', '--security-opt', 'no-new-privileges:true', '--init', '--user', '1654:1654',
        '--tmpfs', '/tmp:rw,size=512m', '--mount', f'type=bind,source={data},target=/data', '-p', f'127.0.0.1:{port}:8080', '-e', 'Home__Downloads__LoopbackOnly=true', image)
    docker('network', 'connect', network, name)
    wait(lambda: json.loads(docker('inspect', name))[0]['State'], lambda state: state.get('Health', {}).get('Status') == 'healthy')
    client = Client(); client.session(); client.call('/setup', {'displayName': 'Isolated live fixture', 'password': 'isolated-live-fixture-password'}); login = client.session()['sessionId']
    second = Client(); second.session(); second.call('/auth/login', {'displayName': 'Isolated live fixture', 'password': 'isolated-live-fixture-password'}); second.session()
    for audio in ['aac', 'ac3']:
        added = client.call('/live/sources', {'name': 'Synthetic ' + audio, 'streamUrl': f'http://live-fixture-origin:8090/{audio}/index.m3u8', 'allowLan': True, 'tuners': 1})
        source = added['source']
        channels = wait(lambda: client.call('/live/channels'), lambda rows: any(row['sourceId'] == source['id'] for row in rows), 40)
        channel = next(row for row in channels if row['sourceId'] == source['id'])
        prepared = client.call('/live/channels/' + channel['id'] + '/play', {'clientId': secrets.token_hex(16), 'clientRevision': 1, 'prepareOnly': True})
        assert prepared['prepared'] and prepared['plan'] is None
        assert 'live-fixture-origin' not in json.dumps(prepared)
        tracks = prepared['tracks']; video_track = next(track for track in tracks['streams'] if track['kind'] == 'video'); audio_track = next(track for track in tracks['streams'] if track['kind'] == 'audio')
        config = {'client': {'containers': [], 'videoCodecs': ['h264'], 'audioCodecs': ['aac'], 'hls': True,
            'capabilities': {'fingerprint': tracks['fingerprint'], 'videoStream': video_track['index'], 'audioStream': audio_track['index'],
                'formats': [{'container': 'mp4', 'transport': 'hls.js', 'original': audio == 'aac', 'videoCopy': True, 'audioCopy': audio == 'aac', 'compatible': True}]}}}
        path = '/live/sessions/' + prepared['id']
        for actor, extra, expected in [(second, None, 410), (client, {'X-CSRF-TOKEN': ''}, 400)]:
            try:
                actor.call(path + '/start', config, extra=extra)
                raise AssertionError('Invalid configuration command succeeded.')
            except urllib.error.HTTPError as error:
                assert error.code == expected
        launched = client.call(path + '/start', config)
        assert launched['plan']['copyVideo'] and launched['plan']['mode'] == ('remux' if audio == 'aac' else 'audio-transcode')
        assert client.call(path + '/start', config)['id'] == launched['id']
        sample = root / (audio + '-sample.mp4')
        with sample.open('wb') as output:
            for part in ['init.mp4', 'segment000000000.m4s']:
                output.write(client.call(path + '/' + part, raw=True)[1])
                status, body = client.call(path + '/' + part, raw=True, extra={'Range': 'bytes=0-15'})
                assert status == 206 and len(body) == 16
                try:
                    Client().call(path + '/' + part)
                    raise AssertionError('Anonymous live media access succeeded.')
                except urllib.error.HTTPError as error:
                    assert error.code == 401
        probe = json.loads(docker('run', '--rm', '--network', 'none', '--entrypoint', '/usr/bin/ffprobe', '--mount', f'type=bind,source={root},target=/evidence,readonly', image, '-v', 'error', '-show_streams', '-of', 'json', '/evidence/' + sample.name))
        video = next(stream for stream in probe['streams'] if stream['codec_type'] == 'video'); sound = next(stream for stream in probe['streams'] if stream['codec_type'] == 'audio')
        assert (video['codec_name'], video['width'], video['height'], sound['codec_name']) == ('h264', 320, 180, 'aac')
        client.call('/live/sources/' + source['id'], {'name': source['name'], 'enabled': False, 'expectedRevision': source['revision']}, 'PUT')
        try:
            client.call(path + '/init.mp4')
            raise AssertionError('Revoked source still allowed media access.')
        except urllib.error.HTTPError as error:
            assert error.code in (403, 410)
        client.call(path, method='DELETE')
    stats = json.loads(docker('exec', origin, 'node', '-e', "fetch('http://127.0.0.1:8090/stats').then(r=>r.text()).then(console.log)"))
    assert stats['failures'] == 2
    assert all(count >= 2 for path, count in stats['calls'].items() if path.endswith('segment000000001.ts'))
    assert originals == {str(file.relative_to(media)): hashlib.sha256(file.read_bytes()).hexdigest() for file in media.rglob('*') if file.is_file()}
    assert client.session()['sessionId'] == login
    report = {'checks': ['non-root-live-remux', 'audio-only-conversion', 'transient-origin-503-recovery', 'actor-and-csrf-bound-configuration', 'idempotent-start', 'fmp4-range-and-anonymous-denial', 'source-revocation', 'source-byte-and-login-preservation'],
        'errors': [], 'internetMediaAcquired': False, 'automatedBrowserRun': False}
    (root / 'result.json').write_text(json.dumps(report, indent=2)); print(json.dumps(report, indent=2))
finally:
    (root / 'container.log').write_text(docker('logs', name, check=False)[-50000:])
    for container in [name, origin]:
        docker('rm', '-f', '-v', container, check=False)
    docker('network', 'rm', network, check=False)
