"""Home approval bridge; Watchtower retains container-recreation responsibility.

Only the selected Home container is controlled. Checks happen in Home and never
pull layers. The bridge pulls an immutable approved digest, then uses Watchtower
with --no-pull, so registry tag movement cannot select a different release.
"""
import base64
import http.client
import json
import os
import re
import socket
import struct
import subprocess
import threading
import time
from urllib.parse import quote, urlencode

REPOSITORY = 'ghcr.io/lumax90/homeos'
DIGEST = re.compile(r'sha256:[0-9a-f]{64}\Z')
NAME = re.compile(r'[a-zA-Z0-9][a-zA-Z0-9_.-]{0,63}\Z')


class UnixConnection(http.client.HTTPConnection):
    def connect(self):
        self.sock = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM)
        self.sock.settimeout(self.timeout)
        self.sock.connect('/var/run/docker.sock')


class Docker:
    def __init__(self):
        version = self.request('GET', '/version', versioned=False)['ApiVersion']
        parts = tuple(map(int, version.split('.')))
        if parts < (1, 41):
            raise RuntimeError('Docker API 1.41 or newer is required.')
        self.version = 'v' + '.'.join(map(str, min(parts, (1, 45))))

    def request(self, method, path, body=None, versioned=True, raw=False, pull=False, progress=None):
        connection = UnixConnection('localhost', timeout=1800 if pull else 30)
        response = None
        try:
            target = ('/' + self.version if versioned else '') + path
            connection.request(method, target, None if body is None else json.dumps(body), {'Content-Type': 'application/json'})
            response = connection.getresponse()
            if response.status not in (200, 201, 204):
                raise RuntimeError('Docker operation deferred: HTTP ' + str(response.status))
            if pull:
                # Consume bounded progress lines, not image payloads or unbounded logs.
                total = 0
                while True:
                    line = response.readline(65537)
                    if not line:
                        break
                    if progress:
                        progress()
                    total += len(line)
                    if len(line) > 65536 or total > 32 * 1024 * 1024:
                        raise RuntimeError('Docker pull progress exceeded its bound.')
                    if json.loads(line).get('error'):
                        raise RuntimeError('The approved image could not be downloaded.')
                return None
            data = response.read(1024 * 1024 + 1)
            if len(data) > 1024 * 1024:
                raise RuntimeError('Docker response exceeded its bound.')
            return data if raw else json.loads(data) if data else None
        finally:
            if response is not None:
                response.close()
            connection.close()

    def inspect(self, name):
        return self.request('GET', '/containers/' + quote(name, safe='') + '/json')

    def image(self, reference):
        return self.request('GET', '/images/' + quote(reference, safe='') + '/json')

    def agent(self, name, action='status', digest=None):
        command = {'protocol': 1, 'action': action, 'digest': digest}
        payload = base64.b64encode(json.dumps(command).encode()).decode()
        instance = self.request('POST', '/containers/' + quote(name, safe='') + '/exec', {
            'Cmd': ['/app/HomeServer.Api', '--container-update-agent', payload],
            'AttachStdout': True, 'AttachStderr': True, 'Tty': False, 'User': '1654:1654'})
        identity = instance['Id']
        if not re.fullmatch(r'[0-9a-f]{64}', identity):
            raise RuntimeError('Invalid Docker exec identity.')
        output = self.request('POST', '/exec/' + identity + '/start', {'Detach': False, 'Tty': False}, raw=True)
        text = decode_exec(output)
        result = self.request('GET', '/exec/' + identity + '/json')
        if result.get('Running') or result.get('ExitCode') != 0:
            raise RuntimeError('Home did not approve this update operation.')
        value = json.loads(text)
        if value.get('protocol') != 1 or value.get('exitCode', 0) != 0:
            raise RuntimeError('Unsupported Home update control response.')
        return value


def decode_exec(data):
    chunks = []
    while data:
        if len(data) < 8:
            raise RuntimeError('Truncated Docker exec output.')
        stream, length = data[0], struct.unpack('>I', data[4:8])[0]
        if stream not in (1, 2) or data[1:4] != b'\0\0\0' or length > 16384 or len(data) < 8 + length:
            raise RuntimeError('Invalid Docker exec output.')
        if stream == 1:
            chunks.append(data[8:8 + length])
        data = data[8 + length:]
    text = b''.join(chunks)
    if len(text) > 16384:
        raise RuntimeError('Home update status exceeded its bound.')
    return text.decode()


def validate_home(info, scope):
    config = info['Config']
    labels = config.get('Labels') or {}
    if config.get('Image') != REPOSITORY + ':stable' or labels.get('com.centurylinklabs.watchtower.scope') != scope \
            or labels.get('com.centurylinklabs.watchtower.enable') != 'true' \
            or labels.get('com.centurylinklabs.watchtower.lifecycle.pre-update') != '/app/HomeServer.Api --container-update-prepare' \
            or not info.get('State', {}).get('Running'):
        raise RuntimeError('Home requires the managed-update recipe; nothing was changed.')
    if not any(m.get('Destination') == '/data' and m.get('Type') in ('bind', 'volume') for m in info.get('Mounts', [])):
        raise RuntimeError('A persistent Home data mount is required.')


def pull_approved(docker, name, reference, approved):
    stopped, cancelled = threading.Event(), threading.Event()
    deadline = time.monotonic() + 1800
    def heartbeat():
        while not stopped.wait(20):
            try:
                status = docker.agent(name)
                if not status.get('enabled') or status['availability'].get('approvedDigest') != approved:
                    cancelled.set()
            except Exception:
                cancelled.set()
    def progress():
        if cancelled.is_set() or time.monotonic() >= deadline:
            raise RuntimeError('Approved download cancelled or expired; no installation was started.')
    worker = threading.Thread(target=heartbeat, daemon=True)
    worker.start()
    try:
        docker.request('POST', '/images/create?' + urlencode({'fromImage': reference}), pull=True, progress=progress)
        progress()
    finally:
        stopped.set(); worker.join(timeout=2)


def tick(docker, name, scope, run=subprocess.run):
    before = docker.inspect(name)
    validate_home(before, scope)
    image = docker.image(before['Image'])
    installed = next((r.split('@', 1)[1] for r in image.get('RepoDigests', [])
                      if r.startswith(REPOSITORY + '@') and DIGEST.fullmatch(r.split('@', 1)[1])), None)
    status = docker.agent(name, digest=installed)
    availability = status['availability']
    approved = availability.get('approvedDigest')
    if not status.get('enabled') or not approved:
        return 'idle'
    candidate = availability.get('candidate') or {}
    if not DIGEST.fullmatch(approved) or candidate.get('digest') != approved:
        raise RuntimeError('Invalid approved release identity.')
    if availability.get('phase') not in ('approved', 'downloading', 'waiting', 'ready'):
        return availability.get('phase', 'idle')
    reference = REPOSITORY + '@' + approved
    docker.agent(name, 'downloading', approved)
    # This request uses only the approved digest; never pull mutable stable here.
    pull_approved(docker, name, reference, approved)
    target = docker.image(reference)
    labels = target.get('Config', {}).get('Labels') or {}
    if reference not in target.get('RepoDigests', []) or labels.get('org.opencontainers.image.version') != candidate.get('version') \
            or labels.get('org.opencontainers.image.revision') != candidate.get('revision'):
        raise RuntimeError('Downloaded release identity did not match approval.')
    state = docker.agent(name, 'ready', approved)
    if state['availability'].get('phase') != 'ready':
        return 'waiting'
    # Local stable aliases the downloaded approval for the no-pull Watchtower
    # scan. Only this bridge manages that alias; external Docker administrators
    # must not run concurrent image/tag/recreation operations during an update.
    docker.request('POST', '/images/' + quote(reference, safe='') + '/tag?' + urlencode({'repo': REPOSITORY, 'tag': 'stable'}))
    if docker.image(REPOSITORY + ':stable')['Id'] != target['Id']:
        raise RuntimeError('Local update target changed; replacement refused.')
    result = run(['/app/Watchtower/watchtower', '--run-once', '--no-pull', '--scope', scope,
                  '--label-enable', '--enable-lifecycle-hooks', '--stop-timeout', '180s',
                  '--rolling-restart', '--use-compose-depends-on=false', name], timeout=600, check=False)
    if result.returncode:
        raise RuntimeError('Watchtower did not complete replacement; retain the application backup.')
    deadline = time.monotonic() + 120
    while time.monotonic() < deadline:
        after = docker.inspect(name)
        if after['Image'] == target['Id'] and after.get('State', {}).get('Health', {}).get('Status') == 'healthy':
            docker.agent(name)
            return 'updated'
        if after['Id'] == before['Id']:
            return 'waiting'
        time.sleep(2)
    raise RuntimeError('The approved container did not become healthy. Review its retained backup; do not blindly downgrade its database.')


def main():
    name = os.environ.get('HOME_UPDATE_CONTAINER', 'home')
    scope = os.environ.get('HOME_UPDATE_SCOPE', 'home')
    if not NAME.fullmatch(name) or not NAME.fullmatch(scope):
        raise RuntimeError('Invalid managed Home identity.')
    while True:
        docker = None
        try:
            docker = Docker()
            phase = tick(docker, name, scope)
            if phase == 'updated':
                print('Approved Home update completed and healthy.', flush=True)
        except Exception as error:
            # Do not log Docker config/env, registry tokens or user paths.
            print('Home update deferred: ' + type(error).__name__, flush=True)
            if docker is not None:
                try:
                    status = docker.agent(name)
                    approval = status.get('availability', {}).get('approvedDigest')
                    if approval:
                        docker.agent(name, 'failed', approval)
                except Exception:
                    pass
        time.sleep(10)


if __name__ == '__main__':
    main()
