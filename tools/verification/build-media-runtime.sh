#!/usr/bin/env bash
set -euo pipefail
test "$(hostname)" = home-linux-verification
stamp="${1:?source snapshot required}"
target="${2:-final}"
[[ "$target" == final || "$target" == media-build || "$target" == psp-build || "$target" == psp-source || "$target" == psp-proof || "$target" == handheld-build ]]
[[ "$stamp" =~ ^[a-f0-9]{16}$ ]]
root="$HOME/home-media-source-$stamp"
test ! -e "$root"
mkdir "$root"
tar -xf "$HOME/home-media-source.tar" -C "$root"
cd "$root"
if ! docker build --target "$target" -t "home-media:local-$stamp" --progress=plain . > "$HOME/home-media-build-$stamp.log" 2>&1; then
  tail -n 100 "$HOME/home-media-build-$stamp.log"
  exit 1
fi
tail -n 14 "$HOME/home-media-build-$stamp.log"
docker image inspect "home-media:local-$stamp" --format '{{.Id}} {{.Size}}'
