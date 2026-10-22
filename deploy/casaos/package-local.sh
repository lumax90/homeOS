#!/usr/bin/env bash
set -euo pipefail
root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)"
output="${1:?Choose a new local output directory}"
image="${2:-home:0.43.0}"
[[ ! -e "$output" ]] || { echo 'Output already exists; it was preserved.' >&2; exit 1; }
test "$(docker image inspect -f '{{.Os}}/{{.Architecture}}' "$image")" = linux/amd64
mkdir -p "$output"
docker tag "$image" home:0.43.0
docker save --output "$output/home-0.43.0-linux-amd64.tar" home:0.43.0
cp "$root/deploy/casaos/"{docker-compose.yml,load-home.sh,README.md} "$output/"
docker image inspect home:0.43.0 >"$output/image.json"
docker run --rm --entrypoint /bin/cat home:0.43.0 /app/NativeRuntime/proof.json >"$output/native-proof.json"
docker run --rm --entrypoint /bin/cat home:0.43.0 /app/NativeRuntime/manifest.json >"$output/native-manifest.json"
cd "$output"
sha256sum home-0.43.0-linux-amd64.tar >home-0.43.0-linux-amd64.tar.sha256
printf 'Local CasaOS package: %s\n' "$output"
