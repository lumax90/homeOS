#!/usr/bin/env bash
set -euo pipefail
root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)"
image="${1:?Choose a built Linux x64 image}"
output="${2:?Choose a new isolated verification directory}"
[[ ! -e "$output" ]] || { echo 'Existing verification data was preserved; choose a new output directory.' >&2; exit 1; }
test "$(docker image inspect -f '{{.Os}}/{{.Architecture}}' "$image")" = linux/amd64
mkdir -p "$output/reports"
output="$(cd -- "$output" && pwd)"
data="$output/data"
reports="$output/reports"
project="home-install-$(date +%s)-$$"
docker compose -f "$root/deploy/casaos/docker-compose.yml" config --format json >"$reports/source-compose.json"
python3 - "$reports/source-compose.json" "$output/compose.json" "$image" "$data" "$project" <<'PY'
import json, sys
source, destination, image, directory, project = sys.argv[1:]
config = json.load(open(source))
config['name'] = project
for service in config['services'].values():
    service['image'] = image
    service['pull_policy'] = 'never'
    assert len(service['volumes']) == 1
    assert service['volumes'][0]['type'] == 'bind'
    assert service['volumes'][0]['target'] == '/data'
    assert service['volumes'][0]['source'] == '/DATA/AppData/home/data'
    service['volumes'][0]['source'] = directory
    for port in service.get('ports', []):
        port['host_ip'] = '127.0.0.1'
        port['published'] = '0'
for name, network in config.get('networks', {}).items():
    network['name'] = f'{project}_{name}'
json.dump(config, open(destination, 'w'), indent=2)
PY
compose=(docker compose --project-name "$project" -f "$output/compose.json")
cleanup() {
  status=$?
  trap - EXIT
  if [[ "$status" != 0 ]]; then
    "${compose[@]}" logs --no-color >"$reports/failure.log" 2>&1 || true
  fi
  "${compose[@]}" down --timeout 180 >/dev/null 2>&1 || true
  exit "$status"
}
trap cleanup EXIT

"${compose[@]}" up -d --wait --wait-timeout 120
home="$("${compose[@]}" ps -q home)"
initializer="$("${compose[@]}" ps -a -q home-data-init)"
docker inspect "$home" >"$reports/container.json"
docker inspect "$initializer" >"$reports/initializer.json"
address="$("${compose[@]}" port home 8080)"
curl --fail --silent --show-error "http://$address/api/health" >"$reports/health.json"
docker exec "$home" cat /app/NativeRuntime/proof.json >"$reports/native-proof.json"
docker exec "$home" cat /app/NativeRuntime/manifest.json >"$reports/native-manifest.json"
python3 - "$reports" "$data" "$root/package.json" <<'PY'
import json, pathlib, sys
reports, directory, package = sys.argv[1:]
def load(name):
    return json.load(open(pathlib.Path(reports) / name))
home, initializer = load('container.json')[0], load('initializer.json')[0]
assert home['Config']['User'] == '1654:1654'
assert home['HostConfig']['ReadonlyRootfs'] and not home['HostConfig']['Privileged']
assert home['State']['Health']['Status'] == 'healthy'
mount = next(m for m in home['Mounts'] if m['Destination'] == '/data')
assert mount['Type'] == 'bind' and mount['Source'] == directory and mount['RW']
assert initializer['State']['Status'] == 'exited' and initializer['State']['ExitCode'] == 0
assert initializer['HostConfig']['NetworkMode'] == 'none'
assert initializer['HostConfig']['ReadonlyRootfs']
assert initializer['HostConfig']['RestartPolicy'] == {'Name': 'on-failure', 'MaximumRetryCount': 3}
assert load('health.json') == {'status': 'ok', 'version': json.load(open(package))['version']}
print('PASS: automatic directory preparation, completed initializer and healthy non-root Home')
PY
test "$(stat -c %u:%g "$data")" = 1654:1654
docker exec "$home" sh -ec 'umask 077; printf "%s\n" "$1" > /data/.home-install-persistence' sh "$project"
before="$(docker exec "$home" sha256sum /data/.home-install-persistence)"
"${compose[@]}" down --timeout 180
"${compose[@]}" up -d --wait --wait-timeout 120
home="$("${compose[@]}" ps -q home)"
test "$(docker exec "$home" sha256sum /data/.home-install-persistence)" = "$before"
printf 'PASS: container and initializer recreation preserve data bytes\n'
"${compose[@]}" down --timeout 180

restricted=(docker run --rm --pull never --network none --read-only --cap-drop ALL --security-opt no-new-privileges:true)
"${restricted[@]}" --user 0:0 --cap-add CHOWN --entrypoint /bin/chown \
  --mount "type=bind,source=$data,target=/data" "$image" 0:0 /data
if "${compose[@]}" up -d --wait --wait-timeout 45 >"$reports/foreign-owner.log" 2>&1; then
  echo 'Home started with foreign-owned existing data.' >&2
  exit 1
fi
"${compose[@]}" logs --no-color home-data-init >"$reports/foreign-owner-initializer.log"
grep -q 'Existing Home /data is not owned by 1654:1654' "$reports/foreign-owner-initializer.log"
home="$("${compose[@]}" ps -a -q home)"
test "$(docker inspect -f '{{.State.Running}}' "$home")" = false
test "$(stat -c %u:%g "$data")" = 0:0
test "$("${restricted[@]}" --user 1654:1654 --entrypoint /usr/bin/sha256sum --mount "type=bind,source=$data,target=/data,readonly" "$image" /data/.home-install-persistence)" = "$before"
"${compose[@]}" down --timeout 180
"${restricted[@]}" --user 0:0 --cap-add CHOWN --entrypoint /bin/chown \
  --mount "type=bind,source=$data,target=/data" "$image" 1654:1654 /data
printf 'PASS: failed initialization blocks Home and preserves foreign-owned data\n'
python3 - "$reports/result.json" "$image" <<'PY'
import datetime, json, sys
json.dump({
    'verifiedAt': datetime.datetime.now(datetime.timezone.utc).isoformat(),
    'image': sys.argv[2],
    'checks': ['automatic-initialization-and-health', 'recreation-persistence', 'foreign-owner-preservation-and-start-block'],
    'errors': [],
    'mediaAcquired': False,
}, open(sys.argv[1], 'w'), indent=2)
PY
printf 'Container installation verification passed. Reports: %s\n' "$reports"
