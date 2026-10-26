"""Isolated approval bridge contracts; never touches Docker or downloads software/media."""
import importlib.util
import json
from pathlib import Path
import struct
import unittest
from unittest.mock import Mock

spec = importlib.util.spec_from_file_location('managed_updater', Path(__file__).parents[2] / 'deploy/casaos/managed-updater.py')
bridge = importlib.util.module_from_spec(spec)
spec.loader.exec_module(bridge)
digest = 'sha256:' + 'b' * 64
reference = bridge.REPOSITORY + '@' + digest


class DockerFixture:
    def __init__(self, approval=True, idle=True):
        self.calls = []
        self.changed = False
        self.idle = idle
        self.current = {'Id': 'old-container', 'Image': 'old-image', 'Config': {
            'Image': bridge.REPOSITORY + ':stable', 'Labels': {
                'com.centurylinklabs.watchtower.enable': 'true', 'com.centurylinklabs.watchtower.scope': 'home',
                'com.centurylinklabs.watchtower.lifecycle.pre-update': '/app/HomeServer.Api --container-update-prepare'}},
            'State': {'Running': True, 'Health': {'Status': 'healthy'}}, 'Mounts': [{'Type': 'bind', 'Destination': '/data'}]}
        self.target = {'Id': 'target-image', 'RepoDigests': [reference], 'Config': {'Labels': {
            'org.opencontainers.image.version': '0.99.0', 'org.opencontainers.image.revision': 'b' * 40}}}
        self.state = {'protocol': 1, 'enabled': True, 'availability': {
            'approvedDigest': digest if approval else None, 'phase': 'approved' if approval else 'available',
            'candidate': {'digest': digest, 'version': '0.99.0', 'revision': 'b' * 40}}}

    def inspect(self, name):
        self.calls.append(('inspect', name))
        return self.current if not self.changed else {**self.current, 'Id': 'new-container', 'Image': 'target-image'}

    def image(self, name):
        self.calls.append(('image', name))
        return {'Id': 'old-image', 'RepoDigests': []} if name == 'old-image' else self.target

    def agent(self, name, action='status', digest=None):
        self.calls.append(('agent', action, digest))
        if action == 'ready':
            self.state['availability']['phase'] = 'ready' if self.idle else 'waiting'
        return self.state

    def request(self, method, path, **options):
        self.calls.append(('request', method, path))


class ManagedUpdaterTests(unittest.TestCase):
    def test_no_approval_means_no_pull_tag_or_watchtower(self):
        docker = DockerFixture(approval=False); run = Mock()
        self.assertEqual('idle', bridge.tick(docker, 'home', 'home', run))
        self.assertFalse(any(c[0] == 'request' for c in docker.calls)); run.assert_not_called()

    def test_paused_updates_do_not_pull_even_with_a_saved_approval(self):
        docker = DockerFixture(); docker.state['enabled'] = False
        run = Mock(); self.assertEqual('idle', bridge.tick(docker, 'home', 'home', run)); run.assert_not_called()
        self.assertFalse(any(c[0] == 'request' for c in docker.calls))

    def test_busy_home_downloads_approved_image_but_does_not_replace(self):
        docker = DockerFixture(idle=False); run = Mock()
        self.assertEqual('waiting', bridge.tick(docker, 'home', 'home', run)); run.assert_not_called()
        requests = [c[2] for c in docker.calls if c[0] == 'request']
        self.assertEqual(1, len(requests)); self.assertIn('sha256%3A' + 'b' * 64, requests[0])

    def test_only_approved_digest_is_pulled_then_watchtower_cannot_pull_stable(self):
        docker = DockerFixture()
        def run(arguments, **options):
            self.assertIn('--no-pull', arguments); self.assertIn('--run-once', arguments)
            self.assertEqual('home', arguments[-1]); self.assertNotIn('home-updater', arguments)
            self.assertNotIn('--cleanup', arguments); docker.changed = True
            return Mock(returncode=0)
        self.assertEqual('updated', bridge.tick(docker, 'home', 'home', run))
        pull = next(c[2] for c in docker.calls if c[0] == 'request' and '/images/create' in c[2])
        self.assertNotIn('%3Astable', pull); self.assertIn('sha256%3A' + 'b' * 64, pull)

    def test_wrong_target_revision_never_tags_or_starts_watchtower(self):
        docker = DockerFixture(); docker.target['Config']['Labels']['org.opencontainers.image.revision'] = 'c' * 40
        run = Mock()
        with self.assertRaises(RuntimeError): bridge.tick(docker, 'home', 'home', run)
        run.assert_not_called(); self.assertFalse(any('/tag?' in c[-1] for c in docker.calls if c[0] == 'request'))

    def test_wrong_scope_or_missing_persistent_data_is_rejected(self):
        for change in ('scope', 'mount'):
            docker = DockerFixture()
            if change == 'scope': docker.current['Config']['Labels']['com.centurylinklabs.watchtower.scope'] = 'other'
            else: docker.current['Mounts'] = []
            with self.assertRaises(RuntimeError): bridge.tick(docker, 'home', 'home', Mock())
            self.assertFalse(any(c[0] == 'request' for c in docker.calls))

    def test_approval_cannot_name_another_registry_or_moving_tag(self):
        docker = DockerFixture(); docker.state['availability']['approvedDigest'] = 'other/image:stable'
        with self.assertRaises(RuntimeError): bridge.tick(docker, 'home', 'home', Mock())
        self.assertFalse(any(c[0] == 'request' for c in docker.calls))

    def test_exec_frames_are_bounded_and_stderr_is_not_a_command_response(self):
        value = json.dumps({'protocol': 1}).encode()
        frame = lambda stream, data: bytes([stream, 0, 0, 0]) + struct.pack('>I', len(data)) + data
        self.assertEqual(value.decode(), bridge.decode_exec(frame(2, b'isolated log') + frame(1, value)))
        for invalid in (b'bad', frame(1, value)[:-1], frame(3, value), frame(1, b'x' * 16385)):
            with self.assertRaises(RuntimeError): bridge.decode_exec(invalid)


if __name__ == '__main__':
    unittest.main()
