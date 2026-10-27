#!/usr/bin/env bash
set -euo pipefail
prefix=/app/HandheldRuntime
mkdir -p "$prefix/cores" "$prefix/licenses"
if [[ ! -d /handheld-source/desmume/.git ]]; then
  git clone https://github.com/libretro/desmume.git /handheld-source/desmume
fi
git -C /handheld-source/desmume checkout --detach 95b4d798731caa809125b6c3c11d17cc332ff6ef
git -C /handheld-source/desmume diff --exit-code
make -C /handheld-source/desmume/desmume/src/frontend/libretro -f Makefile.libretro platform=unix DESMUME_JIT=1 -j2
cp /handheld-source/desmume/desmume/src/frontend/libretro/desmume_libretro.so "$prefix/cores/"
cp /handheld-source/desmume/desmume/COPYING "$prefix/licenses/DeSmuME.txt"
tar --exclude=.git -czf "$prefix/licenses/desmume-source.tar.gz" -C /handheld-source/desmume .
if [[ ! -d /handheld-source/citra/.git ]]; then
  git clone https://github.com/libretro/citra.git /handheld-source/citra
fi
git -C /handheld-source/citra checkout --detach a0483e9abe8134ae6b290cdca7d49cb77689f05b
git -C /handheld-source/citra submodule update --init --recursive --depth 1 -- \
  externals/boost externals/catch2 externals/cryptopp externals/cryptopp-cmake \
  externals/dds-ktx externals/dynarmic externals/enet externals/faad2/faad2 externals/fmt \
  externals/glslang externals/inih/inih externals/library-headers externals/libressl \
  externals/lodepng/lodepng externals/nihstro externals/sirit externals/soundtouch \
  externals/teakra externals/vma externals/vulkan-headers externals/xbyak externals/zstd \
  externals/libusb/libusb externals/cubeb externals/discord-rpc externals/cpp-jwt \
  externals/libyuv externals/sdl2/SDL externals/openal-soft externals/libadrenotools externals/oaknut
python3 /patch-citra-revision.py /handheld-source/citra
git -C /handheld-source/citra diff --exit-code -- . ':(exclude)CMakeModules/GenerateBuildInfo.cmake'
git -C /handheld-source/citra submodule status --recursive > "$prefix/licenses/citra-source-revisions.txt"
cmake -S /handheld-source/citra -B /handheld-build/citra -G Ninja -DCMAKE_BUILD_TYPE=Release \
  -DENABLE_LIBRETRO=ON -DENABLE_QT=OFF -DENABLE_SDL2=OFF -DENABLE_TESTS=OFF \
  -DENABLE_DEDICATED_ROOM=OFF -DENABLE_WEB_SERVICE=OFF -DENABLE_SCRIPTING=OFF \
  -DENABLE_CUBEB=OFF -DENABLE_OPENAL=OFF -DENABLE_LIBUSB=OFF -DUSE_DISCORD_PRESENCE=OFF \
  -DENABLE_COMPATIBILITY_LIST_DOWNLOAD=OFF -DCITRA_ENABLE_BUNDLE_TARGET=OFF \
  -DENABLE_LTO=OFF -DCITRA_WARNINGS_AS_ERRORS=OFF -DUSE_SYSTEM_OPENSSL=ON -DENABLE_OPT=OFF
pushd /handheld-build/citra/src/common >/dev/null
cmake -DSRC_DIR=/handheld-source/citra -DGIT_EXECUTABLE=/usr/bin/git -P /handheld-source/citra/CMakeModules/GenerateSCMRev.cmake
grep -q 'GIT_REV.*a0483e9abe8134ae6b290cdca7d49cb77689f05b' scm_rev.cpp
popd >/dev/null
cmake --build /handheld-build/citra --target citra_libretro -j2
binary="$(find /handheld-build/citra -type f -name citra_libretro.so -print)"
test -n "$binary" && test "$(printf '%s\n' "$binary" | wc -l)" = 1
cp "$binary" "$prefix/cores/"
cp /handheld-source/citra/license.txt "$prefix/licenses/Citra.txt"
cp /patch-citra-revision.py "$prefix/licenses/patch-citra-revision.py"
git -C /handheld-source/citra diff -- CMakeModules/GenerateBuildInfo.cmake > "$prefix/licenses/citra-scm-revision.patch"
tar --exclude=.git -czf "$prefix/licenses/citra-source.tar.gz" -C /handheld-source/citra .
cp /build-handheld.sh "$prefix/licenses/build.sh"
cp /handheld-runtime.json "$prefix/source.json"
python3 /write-handheld-manifest.py "$prefix" /handheld-runtime.json /core-catalog.json
