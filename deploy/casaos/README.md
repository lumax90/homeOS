# Home — CasaOS / Intel-AMD x64

1. Copy this release folder to your CasaOS server. Run **`bash load-home.sh`** once in its terminal to verify/load the supplied Docker image and prepare the persistent data directory for UID/GID **1654:1654**.
2. In **CasaOS → App Store → Custom Install → Import**, paste/import **`docker-compose.yml`**. Confirm that **Volumes → Host** is **`/DATA/AppData/home/data`** and **Container** is **`/data`**, then install.
3. Open **`http://SERVER-IP:5080`** on another device. Create the owner account on first use. The server needs no browser, monitor or desktop login.

The image is supplied locally; `home:0.43.0` is **not a published registry image**. Loading the included `.tar` first is required. No registry account, ROM or BIOS download is needed to install Home.

## Storage

The server directory **`/DATA/AppData/home/data`** is mounted read-write at **`/data`**. It contains accounts/keys, database, managed downloads, ROM copies, saves and settings. Keep this directory and the same mapping when updating or recreating the app. `load-home.sh` prepares an empty directory and verifies writes as UID/GID **1654:1654**. It does not recursively change existing files' ownership.

**Earlier named-volume installations:** the previous recipe used Docker volume **`home_home-data`**. If Home already has accounts, ROMs or saves there, retain its original Compose/mount. Changing to the new empty bind directory would show a fresh installation. The updated loader stops if it finds data in that old default volume; data migration is not automatic. For custom project names, check the existing container's `/data` mount before updating.

To add existing media in CasaOS, add a **read-only** volume: e.g. the server's actual `/DATA/Media` → container `/media`. Select `/media` when adding a Home library. The folder must already exist and UID/GID **1654** needs read/traverse permission. Use `deploy/compose.media.example.yaml` when running Compose from the source distribution.

## Fix: `field Source must not be empty`

CasaOS's custom-import form can turn `home-data:/data` with an empty top-level volume definition into a bind mount with an empty host path. The supplied Compose now uses explicit `type`, `source` and `target` fields.

Re-import the updated file, or fill the existing form's **Volumes** row with:

| Host (server) | Container | Access |
|---|---|---|
| `/DATA/AppData/home/data` | `/data` | Read/write |

Remove any unused volume row whose Host field is empty. If you already loaded the image without using the updated loader, prepare the directory on the CasaOS server before the first installation:

```sh
sudo install -d -o 1654 -g 1654 -m 0750 /DATA/AppData/home/data
```

This command sets the directory's ownership, not the ownership of existing files below it. An existing installation must continue to use its actual data location. If you choose another host path in CasaOS, prepare that exact directory instead.

## Network and games

- **5080/TCP → 8080/TCP:** web, login, API, media and authenticated game signaling/input.
- **62405/UDP + TCP → the same 62405:** encrypted WebRTC game media. Keep this port number equal on host/container. API/encoder/control ports are private.
- The IP used to open Home is automatically advertised for WebRTC. If a proxy uses another address, set **`Home__Gaming__PublicHost`** to the server's reachable LAN IP. No internet STUN/TURN service is required for ordinary same-LAN use.
- mGBA (GB/GBC/GBA), FCEUmm (NES), Snes9x, Genesis Plus GX and PCSX ReARMed run in the container. OpenGL cores use an isolated Xvfb/Mesa software display when required. Performance depends on the host/core/game; no GPU encoding is assumed.
- Plain IP HTTP supports normal browsing/media and receive-only WebRTC where the browser exposes it, plus keyboard/touch input. Some browsers restrict **Gamepad API, installable PWA, offline Web Crypto and microphone/camera** to trusted HTTPS. These browser restrictions are not bypassed; Home's HTTPS settings can be used with an IP-SAN certificate trusted by your devices.

Real TV/browser, own ROM/BIOS, IPTV and host performance acceptance are user-owned. This package is not an official CasaOS store listing. The source/build/protocol evidence and component licences are under `docs/headless-lan-043.md` and the image's `/app/NativeRuntime/licenses/`.

## Update / restart

Use CasaOS/Docker image updates while retaining the same data volume; executable replacement inside a container is disabled. `restart: unless-stopped`, a health check, an init process, bounded logs, non-root UID, read-only rootfs and a three-minute graceful shutdown are included. Normal shutdown checkpoints an active game; crash recovery retains the journal and last verified save.

For a terminal-only installation after loading the image: `docker compose -f docker-compose.yml up -d --wait`.
