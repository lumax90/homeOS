#!/usr/bin/env bash
set -euo pipefail
root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
[[ "$(uname -m)" == x86_64 ]] || { echo 'This package requires an Intel/AMD x64 Linux server.' >&2; exit 1; }
docker info >/dev/null
cd "$root"
sha256sum --check home-0.43.0-linux-amd64.tar.sha256
docker load --input home-0.43.0-linux-amd64.tar
docker compose -f docker-compose.yml config --quiet
printf '\nImage loaded. In CasaOS choose Custom Install > Import and use docker-compose.yml.\n'
printf 'Then open http://SERVER-IP:5080 from your TV, phone or computer.\n'
