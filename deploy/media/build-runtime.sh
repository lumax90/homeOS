#!/usr/bin/env bash
set -euo pipefail
source_archive="${1:?pinned FFmpeg source archive}"
prefix=/app/MediaRuntime
sources=/media-corresponding-source
mkdir -p "$prefix" "$sources/ubuntu" /media-build
tar -xzf "$source_archive" -C /media-build --strip-components=1
cp "$source_archive" "$sources/jellyfin-ffmpeg-source.tar.gz"
cd /media-build
# The published Jellyfin build applies this pinned upstream patch series.
# In particular it supplies the remove_dovi filter used by Home's P8.1 path.
while IFS= read -r change; do
  [[ -n "$change" && "$change" != \#* ]] || continue
  patch --batch -p1 < "debian/patches/$change"
done < debian/patches/series
./configure --prefix="$prefix" --extra-version=Jellyfin \
  --disable-unstable --disable-static --enable-shared --disable-doc --disable-debug --disable-ffplay \
  --disable-autodetect --enable-gpl --enable-version3 --enable-pthreads \
  --enable-libx264 --enable-libx265 --enable-libvpx --enable-libopus --enable-libmp3lame \
  --enable-libdav1d --enable-libvorbis --enable-libass --enable-libzimg \
  --enable-gnutls --enable-zlib --enable-libxml2 --enable-libfreetype \
  --enable-libfontconfig --enable-libfribidi --enable-libharfbuzz \
  --extra-ldflags="-Wl,-rpath,$prefix/lib"
make -j2
make install
mkdir -p "$prefix/licenses"
cp COPYING.GPLv3 LICENSE.md "$prefix/licenses/"
cp /build-media-runtime.sh "$sources/build-runtime.sh"
cp /build-media-runtime.sh "$prefix/licenses/build-runtime.sh"
"$prefix/bin/ffmpeg" -version > "$prefix/licenses/ffmpeg-version.txt" 2>&1
"$prefix/bin/ffprobe" -version > "$prefix/licenses/ffprobe-version.txt" 2>&1
"$prefix/bin/ffmpeg" -L > "$prefix/licenses/ffmpeg-license.txt" 2>&1

# Find exact Ubuntu binary packages used by the built runtime. Their signed
# archive source packages are retained, not just hyperlinks to mutable branches.
ldd "$prefix/bin/ffmpeg" "$prefix/bin/ffprobe" "$prefix"/lib/*.so > "$prefix/licenses/linked-libraries.txt"
awk '/=> \/(lib|usr)/ {print $3} /^[[:space:]]*\/(lib|usr)/ {print $1}' "$prefix/licenses/linked-libraries.txt" | sort -u > /media-library-paths.txt
while IFS= read -r file; do
  resolved="$(readlink -f "$file")"
  owner="$(dpkg-query -S "$resolved" 2>/dev/null | head -n1 | sed 's/: \/.*//' || true)"
  if [[ -z "$owner" ]]; then
    owner="$(dpkg-query -S "$file" 2>/dev/null | head -n1 | sed 's/: \/.*//' || true)"
  fi
  test -n "$owner"
  printf '%s\n' "$owner"
done < /media-library-paths.txt | sort -u > /media-runtime-packages.txt
while IFS= read -r package; do
  dpkg-query -W -f='${binary:Package}=${Version}\n' "$package"
done < /media-runtime-packages.txt > /media-runtime-dependencies.txt
while IFS= read -r package; do
  # libc/toolchain runtime is a system library; retain its notices and identify
  # it, without pretending it is a bundled codec modification.
  name="${package%%:*}"
  if [[ -f "/usr/share/doc/$name/copyright" ]]; then
    cp "/usr/share/doc/$name/copyright" "$prefix/licenses/ubuntu-$name-copyright.txt"
  fi
  dpkg-query -W -f='${source:Package}=${source:Version}\n' "$package"
done < /media-runtime-packages.txt | sort -u > "$sources/ubuntu-source-packages.txt"
cd "$sources/ubuntu"
while IFS= read -r package; do
  case "$package" in glibc=*|gcc-*=*) continue;; esac
  apt-get source --download-only "$package"
done < "$sources/ubuntu-source-packages.txt"
cd "$sources"
find . -type f ! -name SHA256SUMS -print0 | sort -z | xargs -0 sha256sum > SHA256SUMS
cp ubuntu-source-packages.txt "$prefix/licenses/ubuntu-source-packages.txt"
tar -czf "$prefix/licenses/corresponding-source.tar.gz" .
printf '%s\n' 'Pinned unmodified Jellyfin-FFmpeg source and exact signed Ubuntu codec-library sources are retained in corresponding-source.tar.gz. System libc/toolchain runtime is identified separately.' > "$prefix/licenses/source-availability.txt"
