# Playback and game streaming batch — 0.45.0

Published and verified **2026-10-24**, with the user's approval to commit, push and build the accumulated improvements. Database schema **43**. Status: **0.45.0 is public on GHCR and promoted to `stable`**; household/device acceptance remains open.

## Included slices

| Slice | Requirements | User-facing change |
|---|---|---|
| [PLAY-04](playback-efficiency.md) | PLY-01/02/03/05/06, CLI-01-A, OPS-08 | Source-bound browser capability reports, direct/remux/audio-only selection, compatible HEVC fMP4, bounded producer/cache windows and visible playback decisions |
| [LIVE-STABILITY-01](live-stability.md) | EXT-01/02, PLY-02/03/06, USR-03, OPS-08 | Leased prepare/configure flow, compatible video copying, adaptive buffering, bounded reconnection and transient-origin recovery |
| [RADIO-VISUAL-01](radio-visual.md) | MUS-11/12, PLY-03/06, USR-03, CLI-01/05 | One radio video/audio player, prepared silent scene cache/poster and scene/intent protection |
| [SELKIES-01](selkies-streaming.md) | GAM-05/10/11/14/19, CLI-01/05, OPS-09 | Optional Selkies 2.1.0 browser publishing, SDL2 software presentation, device/profile preference, same core/save identities and demand-started processes |

The game streaming default is **Standard**. Selkies is explicitly experimental; physical TV/PC comparison determines a later default change. The deferred TV menu-gamepad issue and real-game latency acceptance remain tracked separately.

## Preservation and delivery

- Standard native runtime continues to use the exact immutable image and binary hashes in `deploy/native/runtime-lock.json`. Only the optional Selkies presentation frontend is added; its allowed core/base identities are checked before launch.
- Existing ROM/source-library files, manual metadata/artwork, settings, checkpoints and paused transfers retain their current scopes. The application version changes; no database migration is introduced by this batch.
- A verified `main` push publishes an immutable source image, the new **`0.45.0`** version image, and promotes **`stable`**. Older version/source images remain available.
- Existing CasaOS installations that already use the three-service stable/updater recipe can receive the update through its normal idle/backup flow. Version-pinned installations still need the previously documented one-time Compose transition.
- Household installation and browser/device acceptance are user-owned. Publishing the image does not establish that a particular household server has already updated.

## Verification

Each linked slice records its focused backend, client-build and isolated native/protocol evidence. For release, the combined affected backend and Node checks, production frontend build, source/manifest consistency and workflow checks are run again against **0.45.0**. GitHub then verifies installation/preservation, 4K playback, live-origin failures, automatic update/backup admission, radio/visual boundaries and the seven-system Selkies transitions before promotion.

- Local release checks: **151 backend tests and 57 Node tests passed, zero skipped**; production frontend/worker/PWA build and workflow syntax passed.
- The combined run exposed a test-server lifetime race in the delayed native-control fixture: its one-shot UDP responder could exit after answering an already-expired request. It now remains available until the client finishes, matching the persistent native command server. The complete affected set passed again.
- GitHub-hosted checks: **138 backend tests, zero failed/skipped** (14 update/backup, 27 radio, 46 native boundaries, 51 film/live), the image/client builds and all publication gates passed. Native protocol receipts confirm **seven systems / 21 standard-Selkies-standard sessions**, zero residual publishers and active-stop/container-recreation save preservation.
- Hosted production-image probes also passed **seven 4K playback checks**, **eight live-origin recovery/access checks**, **eight automatic-update/backup/preservation checks**, and the CasaOS installation/recreation/ownership checks.

Verification uses isolated, locally generated diagnostic content. No internet media download or automated browser suite is part of this release verification. Raw operational evidence is retained in ignored `artifacts/release-045/` and workflow artifacts.

## Publication record

- Application source: **`153c8a858054cc85d0b6708fde9efd7e0692b731`** — `Release 0.45.0 playback and streaming improvements`.
- Automatic push workflow: [40089647294](https://github.com/lumax90/homeOS/actions/runs/40089647294), **success**, completed in **14m54s**.
- Anonymous registry metadata checked **2026-10-24 11:38 UTC**: `ghcr.io/lumax90/homeos:stable`, `:0.45.0`, and `:sha-153c8a858054cc85d0b6708fde9efd7e0692b731` all resolve to **`sha256:89eafd568b5b96c5d43880e14789cae2427e650c7ddd213b373759f5f3e79449`**, Linux amd64, version **0.45.0**, the same application source revision.
- Immutable `0.44.0` remains **`sha256:5d179504889c21c1f0bab4b97af1f10d3e52c0f7c4a0d9d9a84b4be3a2acb11b`**. Standard native source/input pins and core/frontend hashes remain the locked 0.43 runtime.
- Staged-source verification: **101 reviewed files**, no private/build artifacts or personal workstation paths, **Gitleaks: zero findings**. Raw release/hosted evidence stays under ignored `artifacts/release-045/`.
- Device acceptance: **ACCEPT-PLAY-04, ACCEPT-LIVE-STABILITY-01, ACCEPT-RADIO-VISUAL-01, ACCEPT-SELKIES-01, ACCEPT-44-AUTO** remain open as applicable.
