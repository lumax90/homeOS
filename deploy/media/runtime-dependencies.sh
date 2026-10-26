#!/usr/bin/env bash
set -euo pipefail
list="${1:?exact runtime dependency list}"
mapfile -t packages < "$list"
test "${#packages[@]}" -gt 0
apt-get update
apt-get install -y --no-install-recommends "${packages[@]}"
rm -rf /var/lib/apt/lists/*
