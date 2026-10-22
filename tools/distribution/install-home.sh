#!/usr/bin/env bash
set -euo pipefail
source_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
install_root="${1:-$HOME/.local/share/home}"
data_root="${2:-$install_root/data}"
port="${3:-5080}"
[[ "$port" =~ ^[0-9]+$ ]] && ((port > 0 && port < 65536)) || { echo 'Invalid port' >&2; exit 1; }
chmod +x "$source_dir/HomeServer.Api"
"$source_dir/HomeServer.Api" --verify-package "$source_dir" "$source_dir/update-trust.pem"
version="$(sed -n 's/.*"Version":"\([0-9.]*\)".*/\1/p' "$source_dir/.home-package.json")"
[[ -n "$version" ]] || { echo 'Package version missing' >&2; exit 1; }
target="$install_root/releases/$version"
[[ ! -e "$target" ]] || { echo 'Release directory exists; use the Home updater.' >&2; exit 1; }
mkdir -p -- "$target" "$data_root"
cp -a -- "$source_dir/." "$target/"
{
  printf '#!/usr/bin/env bash\nset -euo pipefail\n'
  printf 'cd -- %q\n' "$target"
  printf 'exec %q --Home:DataPath %q --urls %q\n' "$target/HomeServer.Api" "$data_root" "http://0.0.0.0:$port"
} > "$install_root/start-home.sh"
chmod 700 "$install_root/start-home.sh"
printf 'Installed Home %s. Run: %s/start-home.sh\nData: %s\n' "$version" "$install_root" "$data_root"
