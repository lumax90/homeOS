#!/usr/bin/env bash
set -euo pipefail
root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
[[ "$(uname -m)" == x86_64 ]] || { echo 'This package requires an Intel/AMD x64 Linux server.' >&2; exit 1; }
docker info >/dev/null
cd "$root"
sha256sum --check home-0.43.0-linux-amd64.tar.sha256
docker load --input home-0.43.0-linux-amd64.tar
compose=docker-compose.yml
if [[ -f docker-compose.local.yml ]]; then compose=docker-compose.local.yml; fi
docker compose -f "$compose" config --quiet
run=(docker run --rm --pull never --network none --read-only --cap-drop ALL --security-opt no-new-privileges:true --entrypoint /bin/sh)
if docker volume inspect home_home-data >/dev/null 2>&1; then
  if ! "${run[@]}" --user 1654:1654 --mount type=volume,source=home_home-data,target=/data,readonly home:0.43.0 \
    -ec 'entries="$(find /data -mindepth 1 -maxdepth 1 -print -quit)"; test -z "$entries"'; then
    echo 'Existing Home data was found in home_home-data. Keep its original Compose/mount; do not switch to an empty bind directory. See README.md.' >&2
    exit 1
  fi
fi
"${run[@]}" --user 0:0 --cap-add CHOWN --volume /DATA/AppData/home/data:/data home:0.43.0 -ec '
  if [ "$(stat -c %u:%g /data)" != 1654:1654 ]; then
    entries="$(find /data -mindepth 1 -maxdepth 1 -print -quit)"
    if [ -n "$entries" ]; then
      echo "Existing /DATA/AppData/home/data has different ownership. Preserve it and check its permissions before installing." >&2
      exit 1
    fi
    chown 1654:1654 /data
  fi
'
"${run[@]}" --user 1654:1654 --mount type=bind,source=/DATA/AppData/home/data,target=/data home:0.43.0 \
  -ec 'check="$(mktemp /data/.home-install-check.XXXXXX)"; rm -- "$check"'
printf '\nData ready: /DATA/AppData/home/data -> /data (UID/GID 1654:1654).\n'
printf '\nImage loaded. In CasaOS choose Custom Install > Import and use %s.\n' "$compose"
printf 'Then open http://SERVER-IP:5080 from your TV, phone or computer.\n'
