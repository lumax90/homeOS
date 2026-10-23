# Home — CasaOS / Intel-AMD x64

## Automatic-update channel

**Published and verified 2026-10-23:** the recipe tracks **`ghcr.io/lumax90/homeos:stable`**, Linux **amd64**, starting with **0.44.0**. The immutable `0.44.0` and source-commit tags are also available. Publication, upgrade and automatic-update receipts are recorded in [AUTO-UPDATE-044](../../docs/container-auto-updates-044.md). [Package page](https://github.com/users/lumax90/packages/container/package/homeos).

## First installation

1. Open **CasaOS → App Store → Custom Install → Import**.
2. Import the contents of [docker-compose.yml](https://raw.githubusercontent.com/lumax90/homeOS/main/deploy/casaos/docker-compose.yml).
3. Confirm **Host `/DATA/AppData/home/data` → Container `/data`** on `home` and `home-data-init`. The `home-updater` service has the separate Docker-socket mapping. CasaOS pulls the image; `home-data-init` prepares an empty directory for UID/GID **1654:1654** and exits before Home starts.
4. Open **`http://SERVER-IP:5080`** from a phone, TV or computer and create the owner account. The server can run without a display/browser/desktop login.

A successful `home-data-init` container is **Exited (0)**; `home` and `home-updater` are the running services. The initializer uses bounded failure retries because CasaOS's custom-import form rewrites `restart: "no"` into `unless-stopped`.

## One-time upgrade from the version-pinned recipe

Update the **existing Home app's Compose configuration** to this three-service recipe while Home is idle. Transfer the current data/media mappings, ports and custom environment values into the updated configuration before applying it. The Docker-socket mount belongs only on `home-updater`.

This changes the tracked image from `0.43.0` to `stable`, adds the updater, and enables Home's update guard. Changing only the image tag does not configure automatic updates. After this initial transition, normal application updates do not require editing the tag again. The updates page shows whether automatic setup is present and lets the owner pause/resume it.

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

Application-code pushes to `main` start the release workflow. Backend admission/backup checks, the image/native proof, fresh installation and actual automatic-update tests must pass before a candidate can move `stable`. Documentation-only changes do not redeploy the application. Immutable version/source tags remain available.

The scoped updater checks **at startup and every 15 minutes**:

1. It pulls a changed image and runs Home's private pre-update control.
2. Active games, media/radio/TV playback, transfers, recording and running jobs defer the update. API admission/background claims are fenced while a consistent application backup is prepared.
3. Home is replaced with the new image using its existing configuration, mounts and three-minute shutdown allowance. The updater can also replace itself.

The **three most recent automatic application backups** are retained; manually created backups are preserved. Files, ROM copies and saves remain in the existing data directory. Docker reuses unchanged image layers. An interrupted preparation expires and reopens the application if the updater does not complete the stop.

The updater has its own process health check. It ignores Compose startup-only dependencies while doing rolling updates: the completed `home-data-init` helper is not an application that needs restarting on every update.

Automatic database rollback is not enabled. For an unexpected failed deployment, stop the updater, use the prior immutable source/version image and the pre-update backup as appropriate, and pause automatic updates until the problem is resolved. The backup has the existing application-state scope, not all media payloads. The Home service has no Docker socket; management access is confined to the updater service and its named/scoped selection.

## Troubleshooting

- **`No such image: home:0.43.0`:** apply the current registry Compose to the existing app. The automatic recipe uses `ghcr.io/lumax90/homeos:stable` and allows image pulling.
- **`field Source must not be empty`:** fill both services' Host fields with `/DATA/AppData/home/data`; remove unused rows with empty Host fields.
- **`home-data-init` exits with an ownership error:** check the selected existing data directory and its UID/GID. Its contents are retained; no recursive permission changes or automatic data migration are performed.
- **A new image exists but Home stays on its current revision:** check the updater's log, the owner's automatic-update preference, and active playback/game/job activity. Busy/preparation failures leave Home running for a later retry.

Updates use the container image lifecycle and retain the existing `/data` directory. Init, bounded logs, health checks, dropped capabilities and a three-minute graceful Home shutdown are included. Actual CasaOS hardware, TV/browser/gamepad and sustained real-game acceptance remain user-owned. This is a custom-install recipe, not an official CasaOS store entry. Component licences and source are retained under `/app/NativeRuntime/licenses/`; Home's final distribution licence remains undecided.
