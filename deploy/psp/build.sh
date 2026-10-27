#!/usr/bin/env bash
set -euo pipefail
prefix=/app/PspRuntime
if [[ ! -d /psp-source/.git ]]; then
  git clone --depth 1 --branch v1.20.4 https://github.com/hrydgard/ppsspp.git /psp-source
fi
cd /psp-source
test "$(git rev-parse HEAD)" = fa50bb1976065c4f8b1b47af227d367fe9771555
git submodule update --init --recursive -- libretro/libretro-common ext/armips ext/glslang ext/SPIRV-Cross ext/lua ext/rcheevos ext/cpu_features ext/libchdr ext/zstd ext/aemu_postoffice ext/OpenXR-SDK ext/rapidjson ext/miniupnp ext/naett ext/freetype ext/nanosvg ext/discord-rpc
git submodule status --recursive > /psp-source-revisions.txt
python3 /patch-private-memory.py /psp-source
git diff --exit-code -- . ':(exclude)Common/MemArenaPosix.cpp'
git submodule foreach --recursive 'git diff --exit-code'
cmake -S /psp-source -B /psp-build -G Ninja -DCMAKE_BUILD_TYPE=Release \
  -DLIBRETRO=ON -DUNITTEST=OFF -DHEADLESS=OFF -DATLAS_TOOL=OFF -DUSING_QT_UI=OFF \
  -DUSE_SYSTEM_FFMPEG=ON -DUSE_FFMPEG=ON -DUSE_DISCORD=OFF -DUSE_MINIUPNPC=OFF \
  -DHTTPS_NOT_AVAILABLE=ON -DOPENXR=OFF -DUSE_WAYLAND_WSI=OFF -DUSE_SYSTEM_ZSTD=ON -DUSE_SYSTEM_FREETYPE=ON
cmake --build /psp-build --target ppsspp_libretro -j2
mkdir -p "$prefix/assets" "$prefix/licenses"
cp /psp-build/lib/ppsspp_libretro.so "$prefix/"
cp -a /psp-build/assets/. "$prefix/assets/"
cp /psp-source/LICENSE.TXT "$prefix/licenses/PPSSPP.txt"
cp /psp-source-revisions.txt "$prefix/licenses/source-revisions.txt"
cp /build-psp.sh "$prefix/licenses/build.sh"
cp /patch-private-memory.py "$prefix/licenses/patch-private-memory.py"
cp /write-psp-manifest.py "$prefix/licenses/write-manifest.py"
cp /runtime.json "$prefix/source.json"
git diff -- Common/MemArenaPosix.cpp > "$prefix/licenses/home-private-memory.patch"
tar --exclude=.git -czf "$prefix/licenses/ppsspp-corresponding-source.tar.gz" -C /psp-source .
ldd "$prefix/ppsspp_libretro.so" > "$prefix/licenses/linked-libraries.txt"
python3 /write-psp-manifest.py "$prefix" /runtime.json /core-catalog.json
