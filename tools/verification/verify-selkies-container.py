"""Run the Selkies proof in a fresh labelled non-root container and data volume.

Usage: python verify-selkies-container.py IMAGE OUTPUT --python PROTOCOL_PYTHON
The protocol interpreter needs the pinned aiortc/aiohttp verification packages.
"""
import argparse
import json
import subprocess
import time
import uuid
from pathlib import Path


def command(*args, **options):
    return subprocess.check_output(list(args), text=True, **options).strip()


def main(args):
    root = Path(args.output).resolve()
    if root.exists():
        raise SystemExit("The evidence directory already exists; its contents were preserved.")
    root.mkdir(parents=True)
    name = "home-selkies-proof-" + uuid.uuid4().hex[:12]
    volume = name + "-data"
    tools = root / "tools"
    fixtures = root / "fixtures"
    # Keep the venv entry path: resolving its symlink would run system Python
    # without the protocol dependencies installed in that environment.
    interpreter = str(Path(args.python).absolute())
    source = Path(__file__).resolve().parents[2]
    gateway = json.loads(command("docker", "network", "inspect", "bridge"))[0]["IPAM"]["Config"][0]["Gateway"]
    created = False
    success = False
    try:
        with (root / "fixture-build.log").open("w") as output:
            subprocess.run(["docker", "build", "--target", "verification-tools", "--output", "type=local,dest=" + str(tools), "."], cwd=source, stdout=output, stderr=subprocess.STDOUT, check=True)
        command(str(tools / "native-probe" / "Home.NativeLinuxProbe"), "--fixtures", str(fixtures))
        command("docker", "volume", "create", "--label", "home.isolated-verification=selkies", volume)
        command("docker", "run", "--rm", "--network", "none", "--user", "0:0", "--entrypoint", "/app/prepare-data.sh", "--mount", f"type=volume,source={volume},target=/data", args.image)

        def start():
            command("docker", "run", "-d", "--name", name, "--label", "home.isolated-verification=selkies", "--init",
                    "--read-only", "--cap-drop", "ALL", "--security-opt", "no-new-privileges", "--tmpfs", "/tmp:rw,nosuid,size=512m",
                    "--stop-timeout", "180", "-p", "127.0.0.1::8080", "-p", "62405:62405/udp", "-p", "62405:62405/tcp",
                    "-e", "HOME=/data", "-e", "XDG_CACHE_HOME=/data/cache", "-e", "MESA_SHADER_CACHE_MAX_SIZE=128M",
                    "-e", "Home__Gaming__PublicHost=" + gateway, "-e", "Home__Games__Diagnostics=true",
                    "--mount", f"type=volume,source={volume},target=/data", args.image)
            port = json.loads(command("docker", "inspect", name))[0]["NetworkSettings"]["Ports"]["8080/tcp"][0]["HostPort"]
            for _ in range(90):
                if subprocess.run(["docker", "exec", name, "/app/HomeServer.Api", "--healthcheck"], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL).returncode == 0:
                    return "http://127.0.0.1:" + port
                time.sleep(1)
            raise RuntimeError("Isolated Home did not become healthy")

        created = True
        base = start()
        with (root / "protocol.log").open("w") as output:
            subprocess.run([interpreter, str(source / "tools/verification/verify-selkies.py"), "--base", base,
                            "--container", name, "--media-host", gateway, "--fixtures", str(fixtures),
                            "--output", str(root / "result.json"), "--leave-active"], stdout=output, stderr=subprocess.STDOUT, check=True)
        command("docker", "stop", "--timeout", "180", name)
        (root / "container.log").write_text(command("docker", "logs", name, stderr=subprocess.STDOUT))
        command("docker", "rm", name)
        base = start()
        with (root / "recreation.log").open("w") as output:
            subprocess.run([interpreter, str(source / "tools/verification/verify-selkies.py"), "--base", base,
                            "--container", name, "--fixtures", str(fixtures), "--output", str(root / "recreation.json"),
                            "--resume", str(root / "result.json")], stdout=output, stderr=subprocess.STDOUT, check=True)
        success = True
        print("Seven original systems: standard/Selkies/standard, idle cleanup, ownership and active shutdown/recreation passed.")
    finally:
        if created:
            subprocess.run(["docker", "stop", "--timeout", "180", name], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            with (root / "container-final.log").open("w") as output:
                subprocess.run(["docker", "logs", name], stdout=output, stderr=subprocess.STDOUT)
            if success:
                command("docker", "rm", name)
                command("docker", "volume", "rm", volume)
        (root / "receipt.json").write_text(json.dumps({"image": args.image, "success": success, "originalIsolatedFixturesOnly": True,
            "browserAutomation": False, "internetMediaRequests": 0, "retainedVolumeOnFailure": None if success else volume}, indent=2))


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("image")
    parser.add_argument("output")
    parser.add_argument("--python", required=True)
    main(parser.parse_args())
