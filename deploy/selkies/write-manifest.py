"""Record the installed, pinned stream stack beside its compatible base runtime."""
import hashlib
import importlib.metadata
import importlib.resources
import json
import os
from pathlib import Path
import sys

root = Path(sys.argv[1]).resolve()
base = json.loads(Path(sys.argv[2]).read_text())
pin = json.loads((root / "source.json").read_text())
assert importlib.metadata.version("selkies") == pin["version"]

def asset(path, relative=False):
    path = Path(path).resolve()
    return {"path": path.relative_to(root).as_posix() if relative else str(path),
            "bytes": path.stat().st_size, "sha256": hashlib.sha256(path.read_bytes()).hexdigest()}

web = Path(str(importlib.resources.files("selkies.selkies_web"))).resolve()
module = web.parent
dependencies = [asset(Path(os.path.realpath(sys.executable)))]
# The release package is immutable; verify the executable Python/extension/client
# payload on launch, never on the input path or while the alternate backend is idle.
dependencies.extend(asset(path) for path in sorted(module.rglob("*"))
                    if path.is_file() and path.suffix in {".py", ".so", ".js", ".wasm"})
for package in ("pixelflux", "pcmflux"):
    dist = importlib.metadata.distribution(package)
    dependencies.extend(asset(dist.locate_file(file)) for file in dist.files or []
                        if str(file).endswith((".so", ".py")))
value = {"format": 1, "version": pin["version"], "baseFrontendSha256": base["frontend"]["sha256"],
         "frontend": asset(root / "retroarch", True), "python": sys.executable,
         "pythonTarget": os.path.realpath(sys.executable), "webRoot": str(web),
         "cores": [{"id": core["id"], "sha256": core["binary"]["sha256"]} for core in base["cores"]],
          "dependencies": list({item["path"]: item for item in dependencies}.values())}
if len(sys.argv) > 3:
    handheld_path=Path(sys.argv[3]).resolve()
    handheld=json.loads(handheld_path.read_text())
    assert handheld['platform']=='linux-x64'
    for core in handheld['cores']:
        assert core['id'] in ('desmume','citra')
        proof_path=handheld_path.parent/(core['id']+'-selkies-proof.json')
        proof=json.loads(proof_path.read_text())
        assert proof['coreSha256']==core['binary']['sha256']
        assert proof['baseFrontendSha256']==value['baseFrontendSha256']
        assert proof['frontendSha256']==value['frontend']['sha256'] and proof['selkiesVersion']==value['version']
        assert proof['error'] is None and all(proof[key] for key in ('videoFrames','inputVerified','touchVerified','saveRestore','publisherHealthy'))
        value['cores'].append({'id':core['id'],'sha256':core['binary']['sha256']})
(root / "manifest.json").write_text(json.dumps(value, indent=2) + "\n")
licenses = root / "licenses"
licenses.mkdir(exist_ok=True)
inventory = []
for dist in importlib.metadata.distributions():
    inventory.append({"name": dist.metadata["Name"], "version": dist.version,
                      "license": dist.metadata.get("License-Expression") or dist.metadata.get("License")})
    for file in dist.files or []:
        name = str(file)
        if ".dist-info/" in name and any(part in name.lower() for part in ("license", "copying", "notice")):
            path = Path(dist.locate_file(file))
            if path.is_file():
                target = licenses / "python" / name
                target.parent.mkdir(parents=True, exist_ok=True)
                target.write_bytes(path.read_bytes())
(licenses / "python-packages.json").write_text(json.dumps(inventory, indent=2) + "\n")
print(json.dumps({"version": value["version"], "frontend": value["frontend"], "verifiedFiles": len(value["dependencies"])}))
