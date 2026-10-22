#!/usr/bin/env bash
set -euo pipefail
source_root="${1:?source directory required}"
output="${2:?output directory required}"
mkdir -p "$output/cores" "$output/licenses"
node /src/tools/patch-linux-native-input.mjs "$source_root"
node /src/tools/patch-native-host-adapter.mjs "$source_root/retroarch"
node /src/tools/patch-linux-headless-output.mjs "$source_root/retroarch"
cd "$source_root/retroarch"
chmod +x configure
./configure --prefix=/opt/home-native --enable-networkgamepad --enable-command --enable-ffmpeg \
  --enable-x11 --enable-opengl --enable-opengl_core --enable-slang --enable-builtinglslang --enable-spirv_cross --disable-vulkan --disable-wayland --disable-kms --disable-qt \
  --disable-sdl --disable-sdl2 --disable-alsa --disable-pulse --disable-jack --disable-oss \
  --disable-pipewire --disable-udev --disable-v4l2 --disable-microphone --disable-discord \
  --disable-cheevos --disable-online_updater --disable-update_cores --disable-update_core_info \
  --disable-update_assets --disable-cg \
  --disable-freetype --disable-xmb --disable-ozone --disable-materialui --enable-rgui
make -j4 GIT_VERSION=69a4f0e+home-cfb12043
install -m755 retroarch "$output/retroarch"
cp COPYING "$output/licenses/RetroArch.txt"
cmake -S "$source_root/mgba" -B /mgba-build -DCMAKE_BUILD_TYPE=Release -DBUILD_LIBRETRO=ON \
  -DSKIP_LIBRARY=ON -DBUILD_QT=OFF -DBUILD_SDL=OFF -DDISABLE_FRONTENDS=ON -DDISABLE_DEPS=ON \
  -DUSE_FFMPEG=OFF -DUSE_LUA=OFF -DUSE_LIBZIP=OFF -DUSE_LZMA=OFF -DUSE_ZLIB=OFF \
  -DUSE_PNG=OFF -DUSE_SQLITE3=OFF -DUSE_DISCORD_RPC=OFF -DENABLE_GDB_STUB=OFF
cmake --build /mgba-build --target mgba_libretro --parallel 4
install -m755 /mgba-build/mgba_libretro.so "$output/cores/"
make -C "$source_root/fceumm" -f Makefile.libretro platform=unix -j4
make -C "$source_root/snes9x/libretro" platform=unix -j4
make -C "$source_root/genesis_plus_gx" -f Makefile.libretro platform=unix -j4
make -C "$source_root/pcsx_rearmed" -f Makefile.libretro platform=unix HAVE_PHYSICAL_CDROM=0 -j4
install -m755 "$source_root/fceumm/fceumm_libretro.so" "$output/cores/"
install -m755 "$source_root/snes9x/libretro/snes9x_libretro.so" "$output/cores/"
install -m755 "$source_root/genesis_plus_gx/genesis_plus_gx_libretro.so" "$output/cores/"
install -m755 "$source_root/pcsx_rearmed/pcsx_rearmed_libretro.so" "$output/cores/"
tar -xzf "$source_root/mediamtx.tar.gz" -C "$output" mediamtx LICENSE
mv "$output/LICENSE" "$output/licenses/MediaMTX.txt"
chmod 755 "$output/mediamtx"
node /src/tools/write-linux-native-manifest.mjs "$source_root" "$output"
mkdir -p /native-test-binaries
gcc -O2 -shared -fPIC -I"$source_root/retroarch/libretro-common/include" /src/tests/native/SyntheticGpu.c -o /native-test-binaries/synthetic_gpu.so
cd "$source_root"
tar -czf "$output/licenses/corresponding-source.tar.gz" -T source-files.txt retroarch/home-core-options.c retroarch/home-host-adapter.json retroarch/home-linux-output.json \
  -C /src tools/patch-linux-native-input.mjs tools/patch-linux-headless-output.mjs tools/patch-native-host-adapter.mjs tools/native-host-options.c tools/stage-linux-native.mjs tools/write-linux-native-manifest.mjs deploy/native/build.sh deploy/native/sources.json server/HomeServer.Api/NativeCoreCatalog/catalog.json
