#!/usr/bin/env bash
set -uo pipefail
docker run --rm --read-only --user 1654:1654 --tmpfs /work:rw,exec,uid=1654,gid=1654 --tmpfs /tmp \
  --mount "type=bind,source=$HOME/home-verification-tools/native-probe,target=/probe,readonly" \
  --mount "type=bind,source=$HOME/home-lan-fixtures,target=/fixtures,readonly" \
  --entrypoint /bin/bash home-final:0.43.0 -c '
    /probe/Home.NativeLinuxProbe --prepare-debug /work
    cp /app/NativeRuntime/cores/pcsx_rearmed_libretro.so /work/cores/
    cp /app/NativeRuntime/cores/pcsx_rearmed_libretro.info /work/info/
    cp /fixtures/ORIGINAL-ISOLATED-ps1.cue /fixtures/ORIGINAL-ISOLATED-ps1.bin /work/
    mv /work/ORIGINAL-ISOLATED-ps1.cue /work/game.cue
    timeout 15s /app/NativeRuntime/retroarch --verbose --config /work/retroarch.cfg \
      -L /work/cores/pcsx_rearmed_libretro.so --record /work/record.ts --recordconfig /work/record.cfg /work/game.cue
  ' >"$HOME/home-lan-reports/ps1-isolated.log" 2>&1
result=$?
tail -n 95 "$HOME/home-lan-reports/ps1-isolated.log"
printf '\nDiagnostic exit: %s\n' "$result"
