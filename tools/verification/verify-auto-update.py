"""Isolated Docker/Watchtower integration: no user media, ROM execution or provider acquisition."""
import hashlib
import http.cookiejar
import json
import os
from pathlib import Path
import secrets
import socket
import sqlite3
import subprocess
import sys
import time
import urllib.error
import urllib.request

image, destination = sys.argv[1:]
root = Path(destination).resolve()
if root.exists():
    raise RuntimeError('Existing verification data was preserved; choose a new directory.')
root.mkdir(parents=True)
project = 'home-auto-' + secrets.token_hex(6)
home, updater, other, registry = [project + '-' + suffix for suffix in ['home', 'updater', 'other', 'registry']]
label = 'home.isolated-verification=' + project
containers = []

def run(*args, check=True, timeout=240):
    value = subprocess.run(['docker', *args], text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=timeout)
    if check and value.returncode:
        raise RuntimeError(value.stdout[-12000:])
    return value.stdout.strip()

def inspect(name):
    return json.loads(run('inspect', name))[0]

def database(path, query, parameters=()):
    code = "import json,sqlite3,sys; db=sqlite3.connect(sys.argv[1]); rows=db.execute(sys.argv[2],json.loads(sys.argv[3])).fetchall(); db.commit(); db.close(); print(json.dumps(rows))"
    result = subprocess.run(['sudo', sys.executable, '-c', code, str(path), query, json.dumps(parameters)], text=True, capture_output=True, timeout=30, check=True)
    return json.loads(result.stdout)

def port():
    with socket.socket() as server:
        server.bind(('127.0.0.1', 0))
        return server.getsockname()[1]

def healthy(name, timeout=120):
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        state = inspect(name)['State']
        if state.get('Health', {}).get('Status') == 'healthy':
            return
        if not state['Running']:
            raise RuntimeError(run('logs', name, check=False)[-8000:])
        time.sleep(1)
    raise RuntimeError('Health deadline exceeded: ' + name)

class Client:
    def __init__(self, address):
        self.address = address
        self.opener = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))
        self.headers = {}
    def call(self, path, body=None, method=None):
        headers = dict(self.headers)
        if body is not None:
            headers['Content-Type'] = 'application/json'
        request = urllib.request.Request(self.address + '/api' + path, data=None if body is None else json.dumps(body).encode(), headers=headers, method=method or ('POST' if body is not None else 'GET'))
        with self.opener.open(request, timeout=30) as response:
            data = response.read()
            return json.loads(data) if data else None
    def session(self):
        result = self.call('/session')
        self.headers = {'X-CSRF-TOKEN': result['csrfToken']}
        if result['authenticated']:
            self.headers.update({'X-Home-User': result['userId'], 'X-Home-Session': result['sessionId']})
        return result

try:
    run('image', 'inspect', image)
    recipe = json.loads(run('compose', '-f', str(Path(__file__).resolve().parents[2] / 'deploy/casaos/docker-compose.yml'), 'config', '--format', 'json'))
    assert recipe['services']['home-updater']['entrypoint'] == ['/usr/bin/python3', '/app/managed-updater.py']
    # Retain the publisher's backup/recreation/self-update regression. The
    # new consent bridge has separate isolated contract checks.
    updater_arguments = ['--scope', project, '--label-enable', '--enable-lifecycle-hooks', '--update-on-start', '--interval', '900', '--stop-timeout', '180s', '--rolling-restart', '--use-compose-depends-on=false']
    once_arguments = list(updater_arguments)
    if '--update-on-start' in once_arguments: once_arguments.remove('--update-on-start')
    interval = once_arguments.index('--interval'); del once_arguments[interval:interval + 2]
    # Registry is verification software only. It is bound to loopback and removed at the end.
    registry_port, http_port = port(), port()
    run('run', '-d', '--name', registry, '--label', label, '-p', f'127.0.0.1:{registry_port}:5000', 'registry:3')
    containers.append(registry)
    deadline = time.monotonic() + 30
    while True:
        try:
            with urllib.request.urlopen(f'http://127.0.0.1:{registry_port}/v2/', timeout=2) as response:
                assert response.status == 200
            break
        except (OSError, urllib.error.URLError):
            if time.monotonic() >= deadline:
                raise
            time.sleep(1)
    reference = f'127.0.0.1:{registry_port}/{project}:stable'
    for marker in ['a', 'b']:
        folder = root / marker
        folder.mkdir()
        (folder / 'Dockerfile').write_text(f'FROM {image}\nENV HOME_SOURCE_REVISION={marker * 40}\nLABEL home.isolated-release={marker} org.opencontainers.image.revision={marker * 40}\n')
        run('build', '-q', '-t', project + ':' + marker, str(folder))
    run('tag', project + ':a', reference)
    run('push', '--platform', 'linux/amd64', reference)
    data, media = root / 'data', root / 'media'
    data.mkdir(); media.mkdir()
    original = media / 'ORIGINAL-FIXTURE.txt'
    original.write_text('Synthetic automatic-update preservation fixture.\n')
    original_hash = hashlib.sha256(original.read_bytes()).hexdigest()
    run('run', '--rm', '--network', 'none', '--read-only', '--cap-drop', 'ALL', '--cap-add', 'CHOWN', '--user', '0:0', '--entrypoint', '/bin/sh', '--mount', f'type=bind,source={data},target=/data', image, '/app/prepare-data.sh')
    hooks = {
        'com.docker.compose.project': project,
        'com.docker.compose.service': 'home',
        'com.docker.compose.depends_on': 'init:service_completed_successfully:false',
        'com.centurylinklabs.watchtower.enable': 'true',
        'com.centurylinklabs.watchtower.scope': project,
        'com.centurylinklabs.watchtower.lifecycle.pre-update': '/app/HomeServer.Api --container-update-prepare',
        'com.centurylinklabs.watchtower.lifecycle.pre-update-timeout': '3',
        'com.centurylinklabs.watchtower.lifecycle.post-update': '/app/HomeServer.Api --container-update-complete',
        'com.centurylinklabs.watchtower.lifecycle.post-update-timeout': '3',
        'com.centurylinklabs.watchtower.lifecycle.post-check': '/app/HomeServer.Api --container-update-cancel',
    }
    labels = [part for key, value in hooks.items() for part in ['--label', key + '=' + value]]
    run('run', '-d', '--name', home, '--hostname', home, '--label', label, *labels, '--init', '--read-only', '--cap-drop', 'ALL', '--security-opt', 'no-new-privileges:true', '--user', '1654:1654', '--stop-timeout', '180', '--tmpfs', '/tmp:rw,nosuid,size=512m', '-e', 'Home__Updates__Automatic=true', '-e', 'Home__Downloads__LoopbackOnly=true', '-p', f'127.0.0.1:{http_port}:8080', '--mount', f'type=bind,source={data},target=/data', '--mount', f'type=bind,source={media},target=/media,readonly', reference)
    containers.append(home)
    healthy(home)
    run('exec', home, 'sh', '-ec', 'test "$(stat -c %a /tmp/home-container-update)" = 700; test "$(stat -c %a /tmp/home-container-update/control.sock)" = 600')
    client = Client(f'http://127.0.0.1:{http_port}')
    client.session()
    client.call('/setup', {'displayName': 'Isolated update owner', 'password': 'isolated-update-fixture-password'})
    initial_session = client.session()['sessionId']
    run('exec', home, 'sh', '-ec', 'printf "original persistent fixture\n" > /data/update-fixture.txt')
    run('stop', '--time', '180', home)
    transfer = secrets.token_hex(16)
    database(data / 'home.db', "INSERT INTO downloads(id,info_hash,name,source_kind,source,desired_state,status,total_bytes,message,created_at,updated_at,owner_id) VALUES(?,?,?,?,?,'paused','paused',4,'isolated paused fixture',datetime('now'),datetime('now'),'1')", (transfer, 'original-update-fixture-hash', 'Isolated paused transfer', 'torrent', 'not-a-network-source'))
    run('start', home); healthy(home); client.session()
    game_id = secrets.token_hex(16)
    client.call('/games/tiny-crate/sessions', {'clientId': game_id, 'ticket': secrets.token_hex(32)})
    before = inspect(home)
    # A same-scope, enabled container outside the explicit names must remain untouched.
    run('run', '-d', '--name', other, '--label', label, '--label', 'com.docker.compose.project=' + project, '--label', 'com.docker.compose.service=init', '--label', 'com.centurylinklabs.watchtower.enable=true', '--label', 'com.centurylinklabs.watchtower.scope=' + project, '--no-healthcheck', '--entrypoint', '/bin/sleep', reference, 'infinity')
    containers.append(other)
    other_id = inspect(other)['Id']
    run('tag', project + ':b', reference)
    run('push', '--platform', 'linux/amd64', reference)
    target_digest = inspect(project + ':b')['RepoDigests'][0].split('@', 1)[1]
    # Explicit isolated fixture consent, never a production injection endpoint.
    availability = {'format': 1, 'candidate': {'version': before['Config']['Labels']['org.opencontainers.image.version'],
        'revision': 'b' * 40, 'digest': target_digest, 'totalBytes': 100, 'estimatedDownloadBytes': 40},
        'approvedDigest': target_digest, 'phase': 'approved'}
    runner = root / 'bridge-fixture.py'
    runner.write_text("import importlib.util,sys\nspec=importlib.util.spec_from_file_location('bridge','/app/managed-updater.py')\nbridge=importlib.util.module_from_spec(spec);spec.loader.exec_module(bridge)\nbridge.REPOSITORY=sys.argv[1]\nprint(bridge.tick(bridge.Docker(),sys.argv[2],sys.argv[3]))\n")
    def bridge_once(name):
        # Only this isolated diagnostic imports a local-registry override. The
        # production bridge has no arbitrary-repository configuration switch.
        output = run('run', '--rm', '--label', label, '--network', 'host', '--read-only', '--cap-drop', 'ALL', '--user', '0:0',
            '--tmpfs', '/tmp:rw,size=32m', '--tmpfs', '/data:rw,size=1m',
            '--mount', 'type=bind,source=/var/run/docker.sock,target=/var/run/docker.sock,readonly',
            '--mount', f'type=bind,source={runner},target=/bridge-fixture.py,readonly',
            '--entrypoint', '/usr/bin/python3', image, '/bridge-fixture.py', reference.rsplit(':', 1)[0], home, project, timeout=360)
        (root / (name + '.log')).write_text(output)
        return output

    def watch_once(name):
        output = run('run', '--rm', '--label', label, '--label', 'com.centurylinklabs.watchtower=true', '--label', 'com.centurylinklabs.watchtower.scope=' + project, '--network', 'host', '--read-only', '--cap-drop', 'ALL', '--user', '0:0', '--tmpfs', '/tmp:rw,size=32m', '--tmpfs', '/data:rw,size=1m', '--entrypoint', '/app/Watchtower/watchtower', '--mount', 'type=bind,source=/var/run/docker.sock,target=/var/run/docker.sock,readonly', image, '--run-once', *once_arguments, home, timeout=300)
        (root / (name + '.log')).write_text(output)

    assert bridge_once('default-no-consent').endswith('idle')
    assert inspect(home)['Id'] == before['Id'] and client.call('/operations/backups') == []
    run('exec', home, '/usr/bin/python3', '-c', "import pathlib,sys; p=pathlib.Path('/data/updates/container/availability.json');p.parent.mkdir(parents=True,exist_ok=True);p.write_text(sys.argv[1])", json.dumps(availability))
    assert bridge_once('consented-busy-deferral').endswith('waiting')
    assert inspect(home)['Id'] == before['Id'], 'An active game was interrupted.'
    assert client.call('/operations/backups') == [], 'Busy deferral created a backup.'
    client.call('/games/sessions/' + game_id, method='DELETE')
    assert bridge_once('consented-idle-update').endswith('updated')
    healthy(home)
    after = inspect(home)
    assert after['Id'] != before['Id']
    assert after['Config']['User'] == before['Config']['User'] == '1654:1654'
    assert after['HostConfig']['ReadonlyRootfs']
    assert next(m for m in after['Mounts'] if m['Destination'] == '/data')['Source'] == str(data)
    assert not next(m for m in after['Mounts'] if m['Destination'] == '/media')['RW']
    assert client.session()['sessionId'] == initial_session
    assert run('exec', home, 'cat', '/data/update-fixture.txt') == 'original persistent fixture'
    assert hashlib.sha256(original.read_bytes()).hexdigest() == original_hash
    automatic = client.call('/operations/container-updates')
    assert automatic['last']['state'] == 'updated' and automatic['last']['fromRevision'] == 'a' * 40 and automatic['last']['toRevision'] == 'b' * 40
    assert len(client.call('/operations/backups')) == 1
    assert database(data / 'home.db', 'SELECT desired_state,status FROM downloads WHERE id=?', (transfer,)) == [['paused', 'paused']]
    assert inspect(other)['Id'] == other_id
    watch_once('idempotent-scan')
    assert inspect(home)['Id'] == after['Id'] and len(client.call('/operations/backups')) == 1
    # Run a real updater daemon on the old image while the registry points at the new one.
    # Explicit hostnames match CasaOS's parser output; the bundled executable must survive self-replacement.
    run('tag', project + ':a', reference)
    updater_arguments[updater_arguments.index('--interval') + 1] = '5'
    updater_before = run('run', '-d', '--name', updater, '--hostname', updater, '--label', label, '--label', 'com.centurylinklabs.watchtower=true', '--label', 'com.centurylinklabs.watchtower.enable=true', '--label', 'com.centurylinklabs.watchtower.scope=' + project, '--network', 'host', '--read-only', '--cap-drop', 'ALL', '--user', '0:0', '--health-cmd', '/app/Watchtower/watchtower --health-check', '--health-interval', '5s', '--health-timeout', '3s', '--health-start-period', '5s', '--tmpfs', '/tmp:rw,size=32m', '--tmpfs', '/data:rw,size=1m', '--mount', 'type=bind,source=/var/run/docker.sock,target=/var/run/docker.sock,readonly', '--entrypoint', '/app/Watchtower/watchtower', reference, *updater_arguments, home, updater)
    containers.append(updater)
    deadline = time.monotonic() + 150
    while time.monotonic() < deadline:
        raw = run('inspect', updater, check=False)
        try:
            current = json.loads(raw)[0]
        except (json.JSONDecodeError, IndexError):
            time.sleep(2)
            continue
        if current['Id'] != updater_before and current['State']['Running']:
            assert 'HOME_SOURCE_REVISION=' + 'b' * 40 in current['Config']['Env']
            healthy(updater, timeout=45)
            break
        time.sleep(2)
    else:
        raise RuntimeError('The scoped updater did not replace itself.')
    assert inspect(other)['Id'] == other_id
    report = {'checks': ['private-control-socket', 'default-no-consent-no-replacement', 'consented-bridge-active-game-deferral', 'exact-digest-bridge-backup-and-replacement', 'same-login-and-paused-transfer-preservation', 'read-only-source-and-data-mount-preservation', 'unrelated-container-exclusion', 'idempotent-scan', 'legacy-watchtower-self-replacement'], 'errors': [], 'userRomExecuted': False, 'internetMediaAcquired': False}
    (root / 'result.json').write_text(json.dumps(report, indent=2))
    (root / 'before.json').write_text(json.dumps(before, indent=2))
    (root / 'after.json').write_text(json.dumps(after, indent=2))
    print(json.dumps(report, indent=2))
finally:
    for name in reversed(containers):
        (root / (name + '.log')).write_text(run('logs', name, check=False)[-50000:])
    # Only containers carrying this invocation's marker are removed, including a renamed predecessor.
    owned = run('ps', '-aq', '--filter', 'label=' + label, check=False).splitlines()
    for identifier in owned:
        run('rm', '-f', '-v', identifier, check=False)
