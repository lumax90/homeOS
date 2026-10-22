#!/usr/bin/env bash
set -euo pipefail
cd "$HOME/home-source"
image=home-final:0.43.0
name=home-lan-verification
volume=home-lan-verification-v043
fixtures="$HOME/home-lan-fixtures"
reports="$HOME/home-lan-reports"
resume="${1:-}"
if [[ -z "$resume" ]] && { docker container inspect "$name" >/dev/null 2>&1 || docker volume inspect "$volume" >/dev/null 2>&1; }; then
  echo 'A diagnostic container or volume already exists; preserve it and inspect its result before retrying.' >&2
  exit 1
fi
mkdir -p "$fixtures/media/movies" "$fixtures/media/music" "$reports"
if [[ -z "$resume" ]]; then
docker build --target verification-tools --output "type=local,dest=$HOME/home-verification-tools" . >"$reports/tools-export.log" 2>&1
probe="$HOME/home-verification-tools/native-probe/Home.NativeLinuxProbe"
"$probe" --fixtures "$fixtures"
uid="$(id -u):$(id -g)"
docker run --rm --user "$uid" --entrypoint /usr/bin/ffmpeg -v "$fixtures:/fixtures" "$image" \
  -v error -f lavfi -i 'color=c=navy:s=320x180:r=24' -f lavfi -i 'sine=frequency=440:sample_rate=48000' \
  -t 8 -c:v libx264 -threads 1 -pix_fmt yuv420p -c:a aac -ac 2 -movflags +faststart /fixtures/media/movies/ORIGINAL-VIDEO.mp4
docker run --rm --user "$uid" --entrypoint /usr/bin/ffmpeg -v "$fixtures:/fixtures" "$image" \
  -v error -f lavfi -i 'sine=frequency=220:sample_rate=48000' -t 5 -c:a pcm_s16le -ac 2 /fixtures/media/music/ORIGINAL-AUDIO.wav
fi
start() {
  docker run -d --name "$name" --label home.isolated-verification=043 --init --restart unless-stopped \
    --read-only --cap-drop ALL --security-opt no-new-privileges --tmpfs /tmp:rw,nosuid,size=512m \
    --stop-timeout 180 -p 5080:8080 -p 62405:62405/udp -p 62405:62405/tcp \
    -e HOME=/data -e XDG_CACHE_HOME=/data/cache -e MESA_SHADER_CACHE_MAX_SIZE=128M \
    -e Home__Games__Diagnostics=true \
    --mount "type=volume,source=$volume,target=/data" --mount "type=bind,source=$fixtures/media,target=/media,readonly" "$image" >/dev/null
  for i in $(seq 1 90); do if curl -fsS http://172.17.0.1:5080/api/health >/dev/null 2>&1; then return; fi; sleep 1; done
  docker logs "$name" >"$reports/startup-failed.log" 2>&1
  exit 1
}
if [[ -n "$resume" ]]; then
  test "$(docker inspect -f '{{ index .Config.Labels "home.isolated-verification" }}' "$name")" = 043
  if [[ "$resume" == refresh ]]; then
    docker stop --time 180 "$name" >/dev/null
    docker rm "$name" >/dev/null
    start
  fi
else
  start
fi
test "$(docker exec "$name" id -u)" = 1654
docker inspect "$name" >"$reports/container-before.json"
if ! "$HOME/protocol-env/bin/python" tools/verification/verify-lan.py --base http://172.17.0.1:5080 --fixtures "$fixtures" --output "$reports/protocol.json" --leave-active ${resume:+--continue-isolated} >"$reports/protocol.log" 2>&1; then
  docker logs "$name" >"$reports/server.log" 2>&1
  cat "$reports/protocol.log"
  echo "Diagnostic state retained in volume $volume." >&2
  exit 1
fi
docker stop --time 180 "$name" >/dev/null
docker logs "$name" >"$reports/server.log" 2>&1
test "$(docker inspect -f '{{ index .Config.Labels "home.isolated-verification" }}' "$name")" = 043
docker rm "$name" >/dev/null
start
"$HOME/protocol-env/bin/python" tools/verification/verify-lan.py --base http://172.17.0.1:5080 --fixtures "$fixtures" --resume "$reports/protocol.json" --output "$reports/recreation.json"
docker exec "$name" sh -c 'test -z "$(find /data/games/native-work -name .home-native-work -print -quit)"'
docker inspect "$name" >"$reports/container-after.json"
docker stop --time 180 "$name" >/dev/null
cat "$reports/protocol.log"
printf '\nProtocol, native save-on-shutdown and data-volume recreation passed. Reports: %s\n' "$reports"
