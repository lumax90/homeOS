#!/usr/bin/env bash
set -euo pipefail
cd "$HOME/home-source"
docker build --target build -t home-build:0.43.0 . >"$HOME/home-build-debug-tools.log" 2>&1
docker build -f tools/verification/Dockerfile.native-debug -t home-native-debug:0.43.0 . >"$HOME/home-native-debug-build.log" 2>&1
docker run --rm --entrypoint /bin/bash home-native-debug:0.43.0 -c '
  /native-probe/Home.NativeLinuxProbe --prepare-debug /tmp/owned-diagnostic
  timeout 15s gdb -batch -ex "set pagination off" -ex "set disable-randomization off" -ex run -ex "thread apply all bt" --args \
    /native/retroarch --verbose --config /tmp/owned-diagnostic/retroarch.cfg -L /native/cores/mgba_libretro.so \
    --record /tmp/owned-diagnostic/record.ts --recordconfig /tmp/owned-diagnostic/record.cfg /tmp/owned-diagnostic/game.gba
' >"$HOME/home-native-gdb.log" 2>&1
tail -n 100 "$HOME/home-native-gdb.log"
