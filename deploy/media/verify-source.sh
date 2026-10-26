#!/usr/bin/env bash
set -euo pipefail
archive="${1:?corresponding source archive}"
work="$(mktemp -d)"
trap 'rm -rf -- "$work"' EXIT
tar -xzf "$archive" -C "$work"
cd "$work"
sha256sum -c SHA256SUMS >/dev/null
test -s jellyfin-ffmpeg-source.tar.gz
test -s build-runtime.sh
while IFS= read -r package; do
  source="${package%%=*}"
  version="${package#*=}"
  case "$source" in glibc|gcc-*) continue;; esac
  found=false
  for descriptor in ubuntu/*.dsc; do
    [[ -f "$descriptor" ]] || continue
    if grep -Fxq "Source: $source" "$descriptor" && grep -Fxq "Version: $version" "$descriptor"; then
      found=true
      break
    fi
  done
  [[ "$found" == true ]] || { echo "Missing exact source descriptor: $source=$version" >&2; exit 1; }
done < ubuntu-source-packages.txt
printf '%s\n' 'Corresponding source files, hashes and exact non-system dependency descriptors verified.'
