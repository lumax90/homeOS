#!/usr/bin/env bash
set -euo pipefail
source_root="${1:?reviewed native corresponding source directory}"
output="${2:?output directory}"
cd "$source_root/retroarch"
test -f home-host-adapter.json
test -f home-linux-output.json
chmod +x configure
# SDL owns its X connection. RetroArch's direct-X11 screensaver callback can
# otherwise dereference the uninitialised global Display when using SDL2.
python3 - gfx/common/x11_common.c <<'PY'
from pathlib import Path
import sys
path = Path(sys.argv[1])
text = path.read_text()
before = "if (video_driver_display_type_get() != RARCH_DISPLAY_X11)"
after = "if (!g_x11_dpy || video_driver_display_type_get() != RARCH_DISPLAY_X11)"
if text.count(before) != 1:
    raise SystemExit("Reviewed SDL/X11 screensaver source no longer matches")
path.write_text(text.replace(before, after))
PY
# Only the presentation frontend is rebuilt. The immutable native runtime's
# exact core binaries, metadata, and save/settings identities are reused.
./configure --prefix=/opt/home-selkies --enable-networkgamepad --enable-command --enable-ffmpeg \
  --enable-x11 --enable-opengl --enable-opengl_core --enable-slang --enable-builtinglslang --enable-spirv_cross --disable-vulkan --disable-wayland --disable-kms --disable-qt \
  --disable-sdl --enable-sdl2 --disable-alsa --enable-pulse --disable-jack --disable-oss \
  --disable-pipewire --disable-udev --disable-v4l2 --disable-microphone --disable-discord \
  --disable-cheevos --disable-online_updater --disable-update_cores --disable-update_core_info \
  --disable-update_assets --disable-cg \
  --disable-freetype --disable-xmb --disable-ozone --disable-materialui --enable-rgui
make -j4 GIT_VERSION=69a4f0e+home-selkies
mkdir -p "$output/licenses"
install -m755 retroarch "$output/retroarch"
cp COPYING "$output/licenses/RetroArch.txt"
cp gfx/common/x11_common.c "$output/licenses/RetroArch-x11_common.c"
