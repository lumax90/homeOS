# Headless LAN / CasaOS server — 0.43 work log

Started **2026-10-22**. User's target is an **Intel/AMD x64 CasaOS host**, left headless after installation, with Home reached from TVs, phones and computers by IP. Plain IP access is the initial requirement; DNS/mDNS discovery is deferred.

Requirements: **OPS-05/06, CLI-01/02/05, GAM-04/05/10/11/19**. Status: **0.43.0 delivered locally; x64 container/native/LAN protocol gates passed.** User's physical CasaOS/TV/browser acceptance remains open.

Decisions:

- Standard non-root Docker/Compose deployment, persistent application data and read-only media binds; Home serves frontend/API at the same origin. Container image lifecycle replaces executables; accounts, keys, queues, ROMs/saves/settings persist across recreation.
- Reuse RetroArch 1.22.2 + the existing Home adapter/upstream input fix, FFmpeg and MediaMTX 1.21.2. Only encrypted WebRTC ICE transport is published for games; management, MPEG-TS ingestion and emulator input/control remain loopback. No desktop/client-side emulator requirement.
- WHEP uses the browser's IP/host (or an explicit advertised address under Docker NAT) with matching UDP/TCP media ports. Account/login/ticket/reader ownership is enforced across devices. A second device must not silently replace an active game and its pending save.
- Replace secure-context-only UUID calls with `crypto.getRandomValues` IDs for HTTP LAN. Receive-only WebRTC is capability-gated. Browser Gamepad/PWA/Web Crypto offline features require trusted HTTPS on browsers that restrict them; keyboard/touch and ordinary online media retain IP HTTP access.

Documentation consulted 2026-10-22: [ASP.NET Core Kestrel endpoints](https://learn.microsoft.com/aspnet/core/fundamentals/servers/kestrel/endpoints?view=aspnetcore-10.0), [Docker port publishing](https://docs.docker.com/engine/network/port-publishing/), [Compose services](https://docs.docker.com/reference/compose-file/services/), [MediaMTX 1.21.2 WebRTC](https://github.com/bluenviron/mediamtx/blob/v1.21.2/docs/2-features/25-webrtc-specific-features.md), [RetroArch 1.22.2](https://github.com/libretro/RetroArch/tree/v1.22.2). Native component licences remain GPL-3.0 RetroArch, MPL-2.0 mGBA, MIT MediaMTX and build-specific FFmpeg/system packages; public redistribution review remains open.

This workstation has Docker client binaries but no running Linux daemon/WSL. Linux/container verification will use a separate local diagnostic VM if available. Any original native fixtures stay isolated; no user's ROM is launched and no internet ROM/BIOS/media download is used.

## Implementation and verification notes

- A separate **Ubuntu 24.04 x64 diagnostic VM** is available via QEMU/WHPX; no WSL/host OS feature activation was needed. Docker Engine **29.1.3** runs inside it. Its disk, SSH key and bootstrap files live under the approved temporary workspace, not Home's live data. The tools never connect to the user's actual CasaOS host.
- Root and deploy Compose recipes now agree on **5080→8080 TCP** plus **62405 UDP/TCP**, non-root **1654:1654**, persistent `/data`, read-only rootfs, scoped `/tmp`, dropped capabilities, bounded logs, init and three-minute shutdown. Existing media is an optional explicit read-only bind.
- The Docker web build now copies all current build helpers and licence notices; the native build uses a platform-specific ELF validator rather than Windows DLL assumptions. Linux bundle descriptors contain relative, hash-bound paths; their private enable flag survives image recreation and is included in application-state backup. Windows profiles and their save-version hash calculation are retained.
- Native software is compiled from fixed commits with the existing `home-host-1` adapter. The raw software-only source cache verifies Git blob hashes, omits ROM/BIOS/media payload formats, and packages the relevant corresponding source and build helpers with the image. No frontend install action acquires a ROM or BIOS.
- The Linux image includes the five basic cores; Citra's official Linux x64 package endpoint was confirmed with **HEAD 200 / 10,852,434 bytes** (no body fetched). Citra remains an optional explicit core install; actual 3DS/game/device acceptance is separate. Hardware-context requests use isolated **Xvfb + Mesa software OpenGL**, not a logged-in desktop or physical monitor; no GPU encoding claim.
- A native build gate runs original ARM and OpenGL diagnostic fixtures, checking held input, loopback-only command sockets, pause/save/restore, four controller ports/options, H.264/Opus WHEP negotiation and decoded headless GL output. A separate **aiortc 1.15.0** protocol client checks the published non-loopback ICE route and WebSocket commands; this is not browser automation.
- Finalized owned session work is removed only when a marker and absence of pending checkpoint journals establish ownership. Unmarked old work and failed/uncommitted recovery state are retained. Normal shutdown checkpoints before stopping the engine; source ROMs and immutable saves remain outside work cleanup.
- Focused Windows-host gates so far: **89 distinct backend cases** (initial 86 passing, two historical-test corrections, one new cleanup/recovery case), **68 Node checks**, frontend/worker/PWA build. The old schema-40 regression fixture was corrected to construct a genuine schema-40 database instead of just lowering `user_version` on a schema-43 database. Final Linux/protocol/publication outcomes are recorded below once complete.

Additional references: [React pointer events](https://react.dev/reference/react-dom/components/common#pointerevent-handler), [aiortc API](https://aiortc.readthedocs.io/en/latest/api.html), [official CasaOS app manifest example](https://github.com/IceWhaleTech/CasaOS-AppStore/blob/main/Apps/Jellyfin/docker-compose.yml), [QEMU Windows distribution](https://qemu.weilnetz.de/w64/), [Ubuntu Noble 20261017 cloud image checksums](https://cloud-images.ubuntu.com/noble/20261017/SHA256SUMS). Context7 quota was exhausted during the later documentation lookups; official installed/API documentation was consulted directly.

### Linux software source pins

| Component | Commit / version | Licence |
|---|---|---|
| RetroArch | `69a4f0ea1e8aaf442ae4858f2e7f2b31a1776576` / 1.22.2, input fix `cfb1204314dd6b061a7b1cda77221735939bf5ba` | GPL-3.0 |
| mGBA | `26b7884bc25a5933960f3cdcd98bac1ae14d42e2` / 0.10.5 | MPL-2.0 |
| FCEUmm | `ac0d00a7fc01166f84c7bac01d1084ebfd2b865e` | GPL-2.0 |
| Snes9x | `fae2fea08f74180759ef540ee94259213f503480` | Snes9x non-commercial terms |
| Genesis Plus GX | `aa74d79634cd230f65cce4180e92688e53883d2f` | Non-commercial terms; component exceptions |
| PCSX ReARMed | `c8816799b50388e61cfe237fe2cdbb7d8175f20a` | GPL-2.0 |
| MediaMTX | 1.21.2 Linux amd64, release SHA-256 `121be6e00397e473a95d83c75f4fcd3819a6af3b0e1a50889cba43ab4fab6a27` | MIT |

This is a **local, personal-use image delivery**, not a public registry/store release or a claim of commercial redistribution rights. Full distribution licensing remains undecided. The verification-only QEMU (GPL-2.0), 7-Zip extractor (LGPL/BSD components), Ubuntu image/package licences, aiortc (BSD-3-Clause) and aiohttp (Apache-2.0/MIT) are not added as Home product dependencies.

## Final corrections found by real execution

1. **Headless AV reporting:** RetroArch 1.22.2's `GET_AUDIO_VIDEO_ENABLE` reports video disabled for the null display even while FFmpeg is consuming frames. Snes9x stopped producing video; Genesis Plus GX repeatedly supplied a stale frame. The narrow `headless-output-1` Linux adapter reports the actual active recorder as an AV consumer, preserving the later netplay/runahead masks. Both systems then passed decoded-video input transitions. The final Linux bundle is **`home-linux-host-1`**. The existing Windows frontend binary was preserved, so the historical Windows SNES/Mega Drive host gate is not silently closed by the Linux result.
2. **CUE entry / first binary names:** PCSX's first-track path opened the 84-byte renamed `game.cue` as its payload when its first BIN retained a different basename. A separate original-disc reproduction confirmed this. Runtime-only canonical CUE/BIN names now agree (`game.cue`/`game.bin`, subsequent tracks `game-track-NNN.ext`), with rewritten references in **BOM-free UTF-8**. Import manifests/source bytes remain identical. This retains the established **`game.state` / `game.srm`** save partition. A short-lived candidate using CLI save-path overrides was rejected by native verification and archived under `artifacts/packages/candidate-0.43.0-disc-review`; it was not activated.
3. **Isolated probe fidelity:** The initial native probe omitted core info/binary staging and queried asynchronous state loading too early. It now stages exactly as the real runner and observes restored RAM while preserving the paused state. Actual frame counters are recorded (normal single post-load frame allowed), rather than inferring restore success from file existence.
4. **OpenGL build:** The released configure script disables GLCore if its shader-tool prerequisites are disabled. Enabling the pinned builtin glslang/SPIRV-Cross path produced real Mesa GLCore output under Xvfb; no default/physical desktop was used.
5. **Lifecycle:** Global core-option handling and override/remap/shader lookup are explicitly scoped; owned display shutdown uses SIGTERM to let Xvfb remove its socket. The expanded VM disk preserved build evidence after its original diagnostic overlay filled; this changed only the temporary VM, not the Home data or host OS configuration.

## Completed verification

- **109 distinct focused backend cases passed**, plus **68 Node checks**, zero skipped. Final aggregation: `artifacts/headless-lan-v043-verification.json`. Genuine schema-40/41 fixtures replaced tests that merely lowered a newer database's `user_version`; existing ROM IDs, preferences and save partitions were verified across migration.
- Frontend/worker/PWA build passed: **80 public files**, Windows shell **`d850071724958854`**. Linux Docker build, non-root native build proof, and the actual local image loader/checksum/Compose validation all passed.
- **All seven original-system probes passed over the Docker host's non-loopback `172.17.0.1:62405` ICE route**: GB, GBC, GBA, NES, SNES, Mega Drive and PS1. A real aiortc client decoded H.264 video and Opus audio, observed press/release color changes, used authenticated WebSocket input, rejected other-login WHEP/takeover and stale input epochs, saved and relaunched each game. These were original diagnostic programs, not user/acquired ROMs or BIOS. Observed diagnostic input-to-decoded-frame samples were **46–82 ms**; this is not a physical TV/browser/gamepad latency guarantee. TCP ICE fallback is configured from MediaMTX's supported interface; the decoded-media probe used UDP.
- Online video/audio **HTTP 206**, same-login lease ownership, software HLS and source-byte preservation passed against a read-only media mount. No provider television/music/game payload was acquired.
- A running original GBA diagnostic was left active for **normal container shutdown**, then the container was **removed and recreated with the same volume**. Its save revision advanced; all seven ROM identities, favorites and saves remained. New marked work directories were cleaned. `container-before.json` / `container-after.json` record UID **1654**, read-only root, mounts and published ports.
- Evidence: `artifacts/headless-lan-v043-linux/{protocol.json,recreation.json,server.log,container-before.json,container-after.json}`, `artifacts/headless-lan-v043-linux-build.log`, and the packaged `native-proof.json`. Earlier failed results remain in `previousErrors`/`previousCases`; only the final `cases` and empty `errors` establish completion.

## Local delivery

**CasaOS folder:** `artifacts/home-casaos-0.43.0/`

- `home-0.43.0-linux-amd64.tar` — **404,893,184 bytes**
- SHA-256 **`953810c10023484b8e1f3c3ae46bf39abf448654f0652c8abc511d4e39004eee`** (rechecked after copying across the VM boundary)
- `load-home.sh`, `docker-compose.yml`, `README.md`, `image.json`, `native-manifest.json`, `native-proof.json`
- Load once with **`bash load-home.sh`**, then use **CasaOS → Custom Install → Import → docker-compose.yml**. Access **`http://CASAOS-SERVER-IP:5080`**. A terminal Compose installation is also described in the bundled README. No official store/registry entry was published.

Final Linux frontend SHA-256 **`285e1197d59af4715cfbbbc6c45edb19d458cc329218224fee2f93b127c12ba5`**, mGBA **`1f313f3c40a3d3c56eabd19133048076b41864367274941535b51f9536ccc24f`**, MediaMTX **`0ffb7f18210d08ee38cb16668c4f3a2c96622e69d401119c0026d078a2b89523`**.

**Existing Windows Home:** signed **0.43.0 / schema43**, PID **5412**, now listens on **`0.0.0.0:5080`** and was authenticated/verified at **http://SERVER-IP:5080**. Non-loopback native status reports `available=true`, `localOnly=false`, `clientLocal=false`. Current Windows firewall rules already permit the active API executable and MediaMTX on the current Public network profile; they were inspected without replacing user-created rules. The optional `tools/enable-home-lan-firewall.ps1` configures LocalSubnet-only port rules for installations that need them.

Windows package `artifacts/packages/home-0.43.0-win-x64.zip`: **921 verified files / 116,767,131 bytes**, existing trust fingerprint **`b6ebb7e0a2b2821afd3143dc3bd9c8b666189d9a41953aa6657cf60d789b566b`**. Rollback **0.42.0**, backup **`da6c7f7ecd4a413881a7e76d91571bcc`**. Publication preserved **4 ROMs / 10 game-state rows / 10 saves**, native enabled state and **3 paused transfers**, with **zero foreign-key violations**. Receipts: `artifacts/update-live-0.43.0.json`, `artifacts/live-native-v043.json`, `artifacts/live-lan-v043.json`, `artifacts/lan-configuration-v043.json`.

## Remaining acceptance / scope

User's actual CasaOS hardware, TV/browser controls, real ROM/BIOS/IPTV and sustained performance remain **ACCEPT-43-LAN**. One native game lease runs at a time; another device cannot silently replace it. Plain-IP HTTP supports normal online media and keyboard/touch; browser-gated Gamepad/PWA/offline/camera functions still require trusted HTTPS where enforced. Existing Windows Citra/ROM/save files are preserved; optional Linux Citra/game behavior, ARM64 native runtime, an Umbrel app-store package, automatic local DNS and public distribution/licensing are still open. No commits or pushes were made.
