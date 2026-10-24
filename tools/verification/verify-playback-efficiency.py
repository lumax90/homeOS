"""Isolated published-image protocol verification. Generates synthetic media locally; no browser or internet media acquisition."""
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
    raise RuntimeError('Existing evidence was preserved; choose a new verification directory.')
root.mkdir(parents=True)
media, data = root / 'media', root / 'data'
media.mkdir(); data.mkdir()
name = 'home-playback-fixture-' + secrets.token_hex(6)
with socket.socket() as listener:
    listener.bind(('127.0.0.1', 0))
    port = listener.getsockname()[1]

def docker(*arguments, timeout=240, check=True):
    result = subprocess.run(['docker', *arguments], text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=timeout)
    if check and result.returncode:
        raise RuntimeError(result.stdout[-12000:])
    return result.stdout.strip()

def fixture(filename, high_resolution):
    arguments = ['-hide_banner', '-loglevel', 'error', '-nostdin', '-f', 'lavfi', '-i', 'color=size=3840x2160:rate=1' if high_resolution else 'testsrc2=size=128x72:rate=12',
        '-f', 'lavfi', '-i', 'anullsrc=channel_layout=5.1:sample_rate=48000', '-t', '9' if high_resolution else '180', '-threads', '1',
        '-c:v', 'libx265' if high_resolution else 'libx264', '-preset', 'ultrafast', '-g', '4' if high_resolution else '12', '-pix_fmt', 'yuv420p10le' if high_resolution else 'yuv420p', '-c:a', 'ac3', '-b:a', '384k']
    if high_resolution:
        arguments += ['-x265-params', 'pools=1:frame-threads=1:log-level=error:colorprim=bt2020:transfer=smpte2084:colormatrix=bt2020nc']
    docker('run', '--rm', '--network', 'none', '--user', '0:0', '--entrypoint', '/usr/bin/ffmpeg', '--mount', f'type=bind,source={media},target=/fixtures', image, *arguments, '/fixtures/' + filename)

class Client:
    def __init__(self):
        self.opener = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))
        self.headers = {}
    def call(self, path, body=None, method=None, raw=False, extra=None):
        request = urllib.request.Request(f'http://127.0.0.1:{port}/api' + path, data=None if body is None else json.dumps(body).encode(),
            headers={**self.headers, **({'Content-Type': 'application/json'} if body is not None else {}), **(extra or {})}, method=method or ('POST' if body is not None else 'GET'))
        with self.opener.open(request, timeout=60) as response:
            value = response.read()
            return (response.status, value) if raw else json.loads(value) if value else None
    def session(self):
        result = self.call('/session'); self.headers['X-CSRF-TOKEN'] = result['csrfToken']
        return result

def wait(read, predicate, seconds=45):
    deadline = time.monotonic() + seconds
    while time.monotonic() < deadline:
        value = read()
        if predicate(value):
            return value
        time.sleep(.2)
    raise RuntimeError('Verification deadline exceeded.')

try:
    fixture('isolated-4k-hdr.mkv', True)
    fixture('isolated-production-window.mkv', False)
    originals = {file.name: hashlib.sha256(file.read_bytes()).hexdigest() for file in media.glob('*.mkv')}
    docker('run', '--rm', '--network', 'none', '--user', '0:0', '--read-only', '--cap-drop', 'ALL', '--cap-add', 'CHOWN', '--entrypoint', '/bin/sh', '--mount', f'type=bind,source={data},target=/data', image, '/app/prepare-data.sh')
    docker('run', '-d', '--name', name, '--read-only', '--cap-drop', 'ALL', '--security-opt', 'no-new-privileges:true', '--init', '--user', '1654:1654', '--tmpfs', '/tmp:rw,size=512m',
        '--mount', f'type=bind,source={data},target=/data', '--mount', f'type=bind,source={media},target=/media,readonly', '-p', f'127.0.0.1:{port}:8080',
        '-e', 'Home__Downloads__LoopbackOnly=true', '-e', 'Home__Playback__AheadSeconds=12', '-e', 'Home__Playback__CacheGiBPerSession=0.1', image)
    wait(lambda: json.loads(docker('inspect', name))[0]['State'], lambda state: state.get('Health', {}).get('Status') == 'healthy', 120)
    client = Client(); client.session(); client.call('/setup', {'displayName': 'Isolated playback fixture', 'password': 'isolated-playback-fixture-password'}); identity = client.session()['sessionId']
    created = client.call('/libraries', {'name': 'Synthetic verification fixtures', 'path': '/media', 'kind': 'movies'})
    wait(lambda: client.call('/jobs'), lambda jobs: any(job['id'] == created['job']['id'] and job['status'] == 'completed' for job in jobs))
    catalog = client.call('/catalog')['items']

    def start(filename, preserve_audio):
        item = next(item for item in catalog if item['relativePath'] == filename)
        tracks = client.call('/media/' + item['id'] + '/tracks')
        video = next(track for track in tracks['streams'] if track['kind'] == 'video')
        audio = next(track for track in tracks['streams'] if track['kind'] == 'audio')
        profile = {'containers': [], 'videoCodecs': ['h264', 'hevc'], 'audioCodecs': ['aac', 'ac3'], 'hls': True, 'maxAudioChannels': 6,
            'capabilities': {'fingerprint': tracks['fingerprint'], 'videoStream': video['index'], 'audioStream': audio['index'], 'formats': [
                {'container': 'mp4', 'transport': 'hls.js', 'original': preserve_audio, 'videoCopy': True, 'audioCopy': preserve_audio, 'compatible': True}]}}
        session = client.call('/playback', {'mediaId': item['id'], 'client': profile, 'clientId': secrets.token_hex(16), 'clientRevision': 1})
        return session, tracks

    session, tracks = start('isolated-4k-hdr.mkv', False)
    assert session['mode'] == 'audio-transcode' and session['encoder'] == 'copy' and session['videoCodec'] == 'hevc'
    original_video = next(stream for stream in tracks['streams'] if stream['kind'] == 'video')
    assert original_video['width'] == 3840 and original_video['height'] == 2160 and original_video['codecString'].startswith('hvc1.2.')
    path = '/playback/' + session['id']
    assert '#EXT-X-MAP:URI="init.mp4"' in client.call(path + '/index.m3u8', raw=True)[1].decode()
    sample = root / 'remuxed-sample.mp4'
    with sample.open('wb') as output:
        for part in ['init.mp4', 'segment000000.m4s']:
            output.write(client.call(path + '/' + part, raw=True)[1])
            status, content = client.call(path + '/' + part, raw=True, extra={'Range': 'bytes=0-15'})
            assert status == 206 and len(content) == 16
            try:
                Client().call(path + '/' + part)
                raise AssertionError('Anonymous segment access succeeded.')
            except urllib.error.HTTPError as error:
                assert error.code == 401
    output_probe = json.loads(docker('run', '--rm', '--network', 'none', '--entrypoint', '/usr/bin/ffprobe', '--mount', f'type=bind,source={root},target=/evidence,readonly', image,
        '-v', 'error', '-show_streams', '-of', 'json', '/evidence/remuxed-sample.mp4'))
    video = next(stream for stream in output_probe['streams'] if stream['codec_type'] == 'video')
    audio = next(stream for stream in output_probe['streams'] if stream['codec_type'] == 'audio')
    assert (video['codec_name'], video['width'], video['height'], video['pix_fmt'], video['color_transfer']) == ('hevc', 3840, 2160, 'yuv420p10le', 'smpte2084')
    assert (audio['codec_name'], audio['channels']) == ('aac', 2)
    client.call(path, method='DELETE')

    session, _ = start('isolated-production-window.mkv', True)
    path = '/playback/' + session['id']
    paused = wait(lambda: client.call(path), lambda state: state['telemetry']['throttled'])
    assert paused['state'] != 'failed' and paused['telemetry']['windowEndSeconds'] < 40
    time.sleep(1)
    assert client.call(path)['telemetry']['windowEndSeconds'] == paused['telemetry']['windowEndSeconds']
    client.call(path + '/heartbeat', {'positionSeconds': paused['telemetry']['windowEndSeconds'] - 4, 'paused': False})
    wait(lambda: client.call(path), lambda state: state['telemetry']['windowEndSeconds'] > paused['telemetry']['windowEndSeconds'] + 3)
    wait(lambda: client.call(path), lambda state: state['telemetry']['throttled'])
    client.call(path, method='DELETE')
    assert client.session()['sessionId'] == identity
    assert originals == {file.name: hashlib.sha256(file.read_bytes()).hexdigest() for file in media.glob('*.mkv')}
    report = {'checks': ['4k-hevc-main10-hdr-preservation', 'audio-only-conversion', 'fmp4-init-and-segment-range', 'anonymous-denial', 'non-root-producer-backpressure', 'resume-and-close-while-suspended', 'source-byte-and-login-preservation'],
        'errors': [], 'internetMediaAcquired': False, 'automatedBrowserRun': False}
    (root / 'result.json').write_text(json.dumps(report, indent=2)); print(json.dumps(report, indent=2))
finally:
    (root / 'container.log').write_text(docker('logs', name, check=False)[-50000:])
    docker('rm', '-f', '-v', name, check=False)
