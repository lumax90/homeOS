#!/bin/sh
set -eu

if [ "$(stat -c %u:%g /data)" != 1654:1654 ]; then
  entries="$(find /data -mindepth 1 -maxdepth 1 -print -quit)"
  if [ -n "$entries" ]; then
    echo 'Existing Home /data is not owned by 1654:1654. Its files were preserved; check the selected data directory and permissions.' >&2
    exit 1
  fi
  chown 1654:1654 /data
fi

mode="$(stat -c %a /data)"
if [ "$((0$mode & 0300))" -ne "$((0300))" ]; then
  echo 'Home /data needs owner write and traverse permissions for UID 1654.' >&2
  exit 1
fi
printf 'Home data directory ready for UID/GID 1654:1654.\n'
