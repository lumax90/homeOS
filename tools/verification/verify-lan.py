"""Real HTTP/WebSocket/ICE/DTLS/SRTP verification against an isolated Home container.

Consumes only locally generated ORIGINAL-ISOLATED fixtures. No browser automation,
user library, external station/ROM/BIOS/media fetch or public service is used.
"""
import argparse
import asyncio
import hashlib
import ipaddress
import json
import secrets
import time
from pathlib import Path
from urllib.parse import urlparse, urljoin

import aiohttp
from aiortc import RTCConfiguration, RTCPeerConnection, RTCRtpReceiver, RTCSessionDescription

NAME = "ORIGINAL isolated LAN verification"
PASSWORD = "isolated-native-lan-043"
SYSTEMS = ["gb", "gbc", "gba", "nes", "snes", "megadrive", "ps1"]


class Home:
    def __init__(self, base):
        self.base = base.rstrip("/")
        self.client = aiohttp.ClientSession(cookie_jar=aiohttp.CookieJar(unsafe=True), timeout=aiohttp.ClientTimeout(total=90))
        self.headers = {}

    async def api(self, path, method="GET", payload=None, expected=200, **kwargs):
        async with self.client.request(method, self.base + path, json=payload, headers=self.headers | kwargs.pop("headers", {}), **kwargs) as response:
            text = await response.text()
            assert response.status == expected, (path, response.status, text[:1000])
            return json.loads(text) if text else None

    async def session(self):
        info = await self.api("/api/session")
        self.headers = {"X-CSRF-TOKEN": info["csrfToken"]}
        if info["authenticated"]:
            self.headers |= {"X-Home-User": info["userId"], "X-Home-Session": info["sessionId"]}
        return info

    async def login(self):
        await self.session()
        await self.api("/api/auth/login", "POST", {"displayName": NAME, "password": PASSWORD})
        assert (await self.session())["displayName"] == NAME


async def media_workflows(home, other, fixtures):
    results = []
    for kind, folder, name, containers, codecs in [
        ("movies", "movies", "ORIGINAL-VIDEO.mp4", ["mov", "mp4"], ["aac"]),
        ("music", "music", "ORIGINAL-AUDIO.wav", ["wav"], ["pcm_s16le"]),
    ]:
        file = fixtures / "media" / folder / name
        original = file.read_bytes()
        existing = next((item for item in await home.api("/api/libraries") if item["path"] == "/media/" + folder and item["name"] == "ORIGINAL isolated " + kind), None)
        library = existing["id"] if existing else (await home.api("/api/libraries", "POST", {"name": "ORIGINAL isolated " + kind, "path": "/media/" + folder, "kind": kind}, expected=201))["library"]["id"]
        async with asyncio.timeout(50):
            while True:
                page = await home.api("/api/catalog?library=" + library)
                if page["items"] and page["items"][0]["durationSeconds"]:
                    break
                await asyncio.sleep(.25)
        item = page["items"][0]
        request = {"mediaId": item["id"], "clientId": secrets.token_hex(16), "clientRevision": 1,
            "client": {"containers": containers, "videoCodecs": ["h264"], "audioCodecs": codecs, "hls": True, "maxAudioChannels": 2}}
        playback = await home.api("/api/playback", "POST", request)
        try:
            assert playback["mode"] == "direct", playback
            async with home.client.get(home.base + playback["url"], headers={"Range": "bytes=0-63"}) as response:
                assert response.status == 206 and (await response.read()) == original[:64]
            async with other.client.get(home.base + playback["url"]) as denied:
                assert denied.status in (403, 404, 410), denied.status
        finally:
            await home.api("/api/playback/" + playback["id"], "DELETE", expected=204)
        hls_checked = False
        if kind == "movies":
            request |= {"clientRevision": 2, "forceCompatible": True, "maxHeight": 480}
            playback = await home.api("/api/playback", "POST", request)
            try:
                assert playback["mode"] != "direct", playback
                async with home.client.get(home.base + playback["url"]) as response:
                    playlist = await response.text()
                    assert response.status == 200 and "#EXTM3U" in playlist
                segment = next(line.strip() for line in playlist.splitlines() if line.strip() and not line.startswith("#"))
                async with home.client.get(urljoin(home.base + playback["url"], segment)) as response:
                    assert response.status == 200 and len(await response.read()) > 0
                hls_checked = True
            finally:
                await home.api("/api/playback/" + playback["id"], "DELETE", expected=204)
        assert file.read_bytes() == original
        results.append({"kind": kind, "id": item["id"], "library": library, "range206": True, "foreignLoginDenied": True, "hls": hls_checked, "sourceSha256": hashlib.sha256(original).hexdigest()})
    return results


async def game(home, other, system, fixtures):
    extension = {"snes": "sfc", "megadrive": "md", "ps1": "cue"}.get(system, system)
    file = fixtures / f"ORIGINAL-ISOLATED-{system}.{extension}"
    body = file.read_bytes()
    assert len(body) <= 1024 * 1024 and file.name.startswith("ORIGINAL-ISOLATED-")
    headers = {"X-Home-Rom-Name": file.name, "X-Home-Rom-System": system}
    if system == "ps1":
        disc = aiohttp.FormData()
        disc.add_field("files", body, filename=file.name, content_type="application/octet-stream")
        track = fixtures / "ORIGINAL-ISOLATED-ps1.bin"
        disc.add_field("files", track.read_bytes(), filename=track.name, content_type="application/octet-stream")
        rom = await home.api("/api/games/roms", "POST", data=disc, headers=headers)
    else:
        rom = await home.api("/api/games/roms", "POST", data=body, headers=headers | {"Content-Type": "application/octet-stream"})
        assert rom["sha256"] == hashlib.sha256(body).hexdigest()
    await home.api(f"/api/games/{rom['id']}/preference", "PUT", {"inLibrary": True, "favorite": True})
    ticket = secrets.token_hex(32)
    launch = await home.api(f"/api/games/{rom['id']}/native-sessions", "POST", {"clientId": secrets.token_hex(16), "ticket": ticket})
    pc = RTCPeerConnection(RTCConfiguration(iceServers=[]))
    readers = []
    current = {"frames": 0, "audio": 0, "rgb": None, "width": 0, "height": 0}
    socket = None
    pulse = None
    active = True
    sequence = 0
    epoch = launch["inputEpoch"]
    started = time.monotonic()

    async def command(action):
        return await home.api(f"/api/games/native/sessions/{launch['id']}/command", "POST", {"action": action})

    async def consume(track):
        try:
            while True:
                frame = await track.recv()
                if track.kind == "video":
                    rgb = frame.reformat(format="rgb24")
                    offset = (rgb.height // 2) * rgb.planes[0].line_size + (rgb.width // 2) * 3
                    pixel = list(memoryview(rgb.planes[0])[offset:offset + 3])
                    current.update(frames=current["frames"] + 1, rgb=pixel, width=frame.width, height=frame.height)
                else:
                    current["audio"] += 1
        except asyncio.CancelledError:
            pass
        except Exception:
            pass

    @pc.on("track")
    def track_received(track):
        readers.append(asyncio.create_task(consume(track)))

    async def wait_color(pressed):
        before = current["frames"]
        async with asyncio.timeout(15):
            while True:
                rgb = current["rgb"]
                if rgb and current["frames"] > before:
                    match = (rgb[0] < 90 if pressed else rgb[0] > 170) if system == "gb" else (rgb[0] > rgb[1] + 30 if pressed else rgb[1] > rgb[0] + 30)
                    if match:
                        return rgb
                await asyncio.sleep(.015)

    async def hold(buttons):
        nonlocal sequence
        while True:
            sequence += 1
            await socket.send_json({"sequence": sequence, "buttons": buttons, "inputEpoch": epoch})
            await asyncio.sleep(.10)

    try:
        assert launch["localOnly"] is False
        # The second login must neither steal the active game nor consume the first login's stream ticket.
        await other.api(f"/api/games/{rom['id']}/native-sessions", "POST", {"clientId": secrets.token_hex(16), "ticket": secrets.token_hex(32)}, expected=409)
        async with other.client.request("OPTIONS", home.base + launch["url"], headers={"Authorization": "Bearer " + ticket}) as denied:
            assert denied.status in (401, 404), denied.status
        options = await home.api(f"/api/games/native/sessions/{launch['id']}/options")
        if system == "gb":
            palette = next((option for option in options if option["key"] == "mgba_gb_colors"), None)
            if palette and "Grayscale" in palette["values"]:
                await home.api(f"/api/games/native/sessions/{launch['id']}/options", "PUT", {"key": palette["key"], "value": "Grayscale"})
        video = pc.addTransceiver("video", direction="recvonly")
        video.setCodecPreferences([codec for codec in RTCRtpReceiver.getCapabilities("video").codecs if codec.mimeType.lower() == "video/h264"])
        audio = pc.addTransceiver("audio", direction="recvonly")
        audio.setCodecPreferences([codec for codec in RTCRtpReceiver.getCapabilities("audio").codecs if codec.mimeType.lower() == "audio/opus"])
        await pc.setLocalDescription(await pc.createOffer())
        async with home.client.post(home.base + launch["url"], data=pc.localDescription.sdp, headers={"Authorization": "Bearer " + ticket, "Content-Type": "application/sdp"}) as response:
            answer = await response.text()
            assert response.status == 201, (response.status, answer)
            location = response.headers["Location"]
        advertised = urlparse(home.base).hostname
        candidates = [line for line in answer.splitlines() if line.startswith("a=candidate:")]
        assert any(" " + advertised + " 62405 " in line for line in candidates), candidates
        # Force the non-loopback published route even when this protocol client runs on the Docker host.
        answer = "\r\n".join(line for line in answer.splitlines() if not line.startswith("a=candidate:") or " " + advertised + " 62405 " in line) + "\r\n"
        await pc.setRemoteDescription(RTCSessionDescription(sdp=answer, type="answer"))
        socket = await home.client.ws_connect(home.base + f"/api/games/native/sessions/{launch['id']}/input-stream", origin=home.base)
        await socket.send_json({"ticket": ticket})
        assert (await socket.receive(timeout=3)).data == "ok"
        epoch = (await command("resume"))["inputEpoch"]
        pulse = asyncio.create_task(hold(0))
        released = await wait_color(False)
        pulse.cancel()
        await asyncio.gather(pulse, return_exceptions=True)
        pulse = asyncio.create_task(hold(1 << 8))
        input_start = time.monotonic()
        pressed = await wait_color(True)
        diagnostic_ms = (time.monotonic() - input_start) * 1000
        await asyncio.sleep(.25)
        pulse.cancel()
        await asyncio.gather(pulse, return_exceptions=True)
        pulse = asyncio.create_task(hold(0))
        await wait_color(False)
        assert current["audio"] > 0 and pc.iceConnectionState in ("completed", "connected")
        pulse.cancel()
        await asyncio.gather(pulse, return_exceptions=True)
        pulse = None
        await socket.close()
        socket = None
        saved = await command("save")
        assert saved["hasSave"]
        await home.api(f"/api/games/native/sessions/{launch['id']}/input", "POST", {"sequence": sequence + 1, "buttons": 1 << 8, "inputEpoch": epoch}, expected=409)
        await command("end")
        active = False
        assert not await home.api("/api/games/sessions/active")
        second = await home.api(f"/api/games/{rom['id']}/native-sessions", "POST", {"clientId": secrets.token_hex(16), "ticket": secrets.token_hex(32)})
        assert second["hasSave"]
        await home.api(f"/api/games/native/sessions/{second['id']}/command", "POST", {"action": "discard"})
        return {"system": system, "gameId": rom["id"], "romSha256": rom["sha256"], "released": released, "pressed": pressed,
            "decodedVideoFrames": current["frames"], "decodedAudioFrames": current["audio"], "width": current["width"], "height": current["height"],
            "nonLoopbackIceHost": advertised, "whepStatus": 201, "inputSocket": True, "saveRestored": True, "foreignLoginDenied": True,
            "staleEpochDenied": True, "diagnosticInputToDecodedFrameMs": round(diagnostic_ms, 1), "seconds": round(time.monotonic() - started, 2)}
    except Exception as error:
        raise RuntimeError(f"{error!r}; receiver={current}; ICE={pc.iceConnectionState}") from error
    finally:
        if pulse:
            pulse.cancel()
            await asyncio.gather(pulse, return_exceptions=True)
        if socket:
            await socket.close()
        if active:
            try:
                await command("discard")
            except Exception:
                pass
        await pc.close()
        for task in readers:
            task.cancel()
        await asyncio.gather(*readers, return_exceptions=True)


async def main(args):
    host = urlparse(args.base).hostname
    assert ipaddress.ip_address(host).is_private and not ipaddress.ip_address(host).is_loopback, "Use the diagnostic Docker host's non-loopback IP."
    fixtures = Path(args.fixtures)
    assert "ORIGINAL SYNTHETIC" in (fixtures / "README.txt").read_text()
    home, other = Home(args.base), Home(args.base)
    report = {"version": "0.43.0", "originalIsolatedFixturesOnly": True, "internetMediaRequests": 0, "browserAutomation": False, "cases": [], "errors": []}
    try:
        info = await home.session()
        if args.resume:
            await home.login()
            before = json.loads(Path(args.resume).read_text())
            assert not await home.api("/api/games/sessions/active")
            for case in before["cases"]:
                entry = await home.api("/api/games/" + case["gameId"])
                assert entry["romSha256"] == case["romSha256"] and entry["state"]["favorite"] and entry["state"]["hasSave"]
            if before.get("shutdownGame"):
                entry = await home.api("/api/games/" + before["shutdownGame"]["id"])
                assert entry["state"]["saveRevision"] > before["shutdownGame"]["saveRevision"], entry["state"]
            report["recreationPreserved"] = True
            print(json.dumps(report, indent=2))
            Path(args.output).write_text(json.dumps(report, indent=2))
            return
        if info["configured"]:
            assert args.continue_isolated, "Refusing to modify an existing Home installation."
            await home.login()
            assert not await home.api("/api/games/sessions/active")
            if Path(args.output).exists():
                report = json.loads(Path(args.output).read_text())
                assert report["originalIsolatedFixturesOnly"] and report["version"] == "0.43.0"
                report.setdefault("previousErrors", []).extend(report["errors"])
                report["errors"] = []
        else:
            await home.api("/api/setup", "POST", {"displayName": NAME, "password": PASSWORD})
            await home.session()
        await other.login()
        preferences = (await home.api("/api/games/library/settings"))["options"]
        await home.api("/api/games/library/settings", "PUT", preferences | {"automaticMetadata": False})
        status = await home.api("/api/games/native/status")
        assert status["available"] and not status["localOnly"] and status["platform"] == "linux-x64", status
        if report.get("nativeHost") != status["version"]:
            report.setdefault("previousCases", []).extend(report["cases"])
            report["cases"] = []
        report["nativeHost"] = status["version"]
        if not report.get("media"):
            report["media"] = await media_workflows(home, other, fixtures)
        Path(args.output).write_text(json.dumps(report, indent=2))
        for system in SYSTEMS:
            if any(case["system"] == system for case in report["cases"]):
                continue
            try:
                result = await game(home, other, system, fixtures)
                report["cases"].append(result)
                print(json.dumps(result), flush=True)
            except Exception as error:
                report["errors"].append({"system": system, "error": repr(error)})
                print(json.dumps(report["errors"][-1]), flush=True)
            Path(args.output).write_text(json.dumps(report, indent=2))
        if args.leave_active and not report["errors"]:
            case = next(item for item in report["cases"] if item["system"] == "gba")
            entry = await home.api("/api/games/" + case["gameId"])
            launch = await home.api(f"/api/games/{case['gameId']}/native-sessions", "POST", {"clientId": secrets.token_hex(16), "ticket": secrets.token_hex(32)})
            await home.api(f"/api/games/native/sessions/{launch['id']}/command", "POST", {"action": "resume"})
            report["shutdownGame"] = {"id": case["gameId"], "saveRevision": entry["state"]["saveRevision"]}
        assert not await home.api("/api/downloads")
        Path(args.output).write_text(json.dumps(report, indent=2))
        assert not report["errors"], report["errors"]
    finally:
        await home.client.close()
        await other.client.close()


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--base", required=True)
    parser.add_argument("--fixtures", required=True)
    parser.add_argument("--output", required=True)
    parser.add_argument("--resume")
    parser.add_argument("--leave-active", action="store_true")
    parser.add_argument("--continue-isolated", action="store_true")
    asyncio.run(main(parser.parse_args()))
