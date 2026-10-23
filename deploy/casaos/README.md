# Home — CasaOS / Intel-AMD x64

## Install from the registry

**Published and verified 2026-10-22:** **`ghcr.io/lumax90/homeos:0.43.0`**, Linux **amd64**. Anonymous image pull, automatic data-directory preparation, healthy startup and data-preserving recreation passed. [Package page](https://github.com/users/lumax90/packages/container/package/homeos).

Registry digest: `sha256:017e90f2860d2f76d1a787ee7476f5c6244da4aef351f94c733f965bde9bdba0`. Verification and source identity are recorded in [CASAOS-REGISTRY-043](../../docs/casaos-registry-043.md).

1. Open **CasaOS → App Store → Custom Install → Import**.
2. Import the contents of [docker-compose.yml](https://raw.githubusercontent.com/lumax90/homeOS/main/deploy/casaos/docker-compose.yml).
3. Confirm **Host `/DATA/AppData/home/data` → Container `/data`** on both services, then install. CasaOS pulls the image; `home-data-init` prepares an empty directory for UID/GID **1654:1654** and exits before Home starts.
4. Open **`http://SERVER-IP:5080`** from a phone, TV or computer and create the owner account. The server can run without a display/browser/desktop login.

The registry recipe uses `ghcr.io/lumax90/homeos:0.43.0`, not the local-only `home:0.43.0` tag. A successful `home-data-init` container is **Exited (0)**; the `home` service is the running application. The initializer uses bounded failure retries because CasaOS's custom-import form rewrites `restart: "no"` into `unless-stopped`.

## Storage

`/DATA/AppData/home/data` holds accounts, keys, database, managed downloads, ROM copies, saves and settings. Keep that directory and its mapping when updating/recreating Home. The initializer only changes ownership of an **empty** directory; it preserves a nonempty directory with unexpected ownership and reports the problem. Home itself runs as **1654:1654** with a read-only root filesystem.

If you select another Host path, use **the same path on both services**. An earlier installation using Docker volume `home_home-data` must retain its original `/data` mount until deliberately migrated; a fresh bind directory would show a fresh installation. [Local image instructions](README.local.md).

To add existing media, add a **read-only** mapping on the **home** service, e.g. the server's existing `/DATA/Media` → container `/media`, then choose `/media` when adding a Home library. UID/GID 1654 needs read/traverse access. ROM ingestion uses Home's upload/import flow.

## Network

- **5080/TCP → 8080/TCP:** Home web interface, API and authenticated game signaling/input.
- **62405/UDP + TCP → 62405:** encrypted game media. Keep the same port number on host and container.
- The address used to open Home is advertised for ordinary same-LAN WebRTC. Set `Home__Gaming__PublicHost` to the reachable LAN IP when a proxy uses a different address.
- Plain-IP HTTP supports browsing/media and receive-only WebRTC where available, plus keyboard/touch input. Browser-restricted gamepad, installable PWA and offline features can require trusted HTTPS.

## Updates

The installed recipe is pinned to **`0.43.0`**. A GitHub source commit or a change to the repository's Compose file does not update the configuration already imported into CasaOS. There is currently no automatic server-side updater in this recipe.

For an application-code update:

1. A new versioned Linux image must be built, verified and published to GHCR. The repository workflow is triggered by an explicit dispatch or a matching `vX.Y.Z` Git tag, not by ordinary pushes to `main`.
2. Update the image tag on **both `home` and `home-data-init`** in the existing CasaOS app to the same published version, then apply the update so CasaOS pulls it and recreates the containers.
3. Retain the existing `/data` mapping, custom media mounts, ports and environment settings. Accounts, library state, ROM copies and saves live in the persistent data directory. Docker can reuse unchanged image layers.

Home's executable-replacement updater is disabled inside Docker; container updates use the image lifecycle. Automatic image publication and automatic installation on the household server are separate operations.

## Troubleshooting

- **`No such image: home:0.43.0`:** re-import the current registry Compose. Both image fields must be `ghcr.io/lumax90/homeos:0.43.0` and must allow image pulling.
- **`field Source must not be empty`:** fill both services' Host fields with `/DATA/AppData/home/data`; remove unused rows with empty Host fields.
- **`home-data-init` exits with an ownership error:** check the selected existing data directory and its UID/GID. Its contents are retained; no recursive permission changes or automatic data migration are performed.

Updates use the container image lifecycle and retain the existing `/data` directory. Init, bounded logs, health checks, dropped capabilities and a three-minute graceful Home shutdown are included. Actual CasaOS hardware, TV/browser/gamepad and sustained real-game acceptance remain user-owned. This is a custom-install recipe, not an official CasaOS store entry. Component licences and source are retained under `/app/NativeRuntime/licenses/`; Home's final distribution licence remains undecided.
