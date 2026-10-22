#!/usr/bin/env bash
set -euo pipefail
cd "$HOME/home-source"
target="${1:-final}"
log="$HOME/home-build-$target.log"
if ! docker build --target "$target" -t "home-$target:0.43.0" --progress=plain . >"$log" 2>&1; then
  tail -n 70 "$log"
  exit 1
fi
tail -n 12 "$log"
