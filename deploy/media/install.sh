#!/usr/bin/env bash
set -euo pipefail
runtime=/app/MediaRuntime/bin
notices=/app/MediaRuntime/licenses
test -s "$notices/corresponding-source.tar.gz"
bash /app/MediaRuntime/verify-source.sh "$notices/corresponding-source.tar.gz" > "$notices/source-verification.txt"
mkdir -p "$notices"
"$runtime/ffmpeg" -version > "$notices/ffmpeg-version.txt" 2>&1
"$runtime/ffprobe" -version > "$notices/ffprobe-version.txt" 2>&1
grep -q '8.1.3-Jellyfin' "$notices/ffmpeg-version.txt"
grep -q '8.1.3-Jellyfin' "$notices/ffprobe-version.txt"
if grep -q -- '--enable-nonfree' "$notices/ffmpeg-version.txt"; then
  echo 'Nonredistributable media runtime configuration refused.' >&2
  exit 1
fi
"$runtime/ffmpeg" -L > "$notices/ffmpeg-license.txt" 2>&1
# Inventory only: actual output tests, not this list, establish playback support.
"$runtime/ffmpeg" -hide_banner -decoders > "$notices/decoders.txt" 2>&1
"$runtime/ffmpeg" -hide_banner -filters > "$notices/filters.txt" 2>&1
"$runtime/ffmpeg" -hide_banner -bsfs > "$notices/bitstream-filters.txt" 2>&1
