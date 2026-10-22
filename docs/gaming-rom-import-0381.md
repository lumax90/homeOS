# Automatic ROM import hints and CUE/BIN usability

Decision / verification **2026-10-21**, GAM-04/11/14/15. User reported that importing a PS1 game containing one CUE and one BIN failed, and asked why the console had to be selected manually.

## Findings and delivered changes

- The old dialog defaulted to **GBA**, filtered its file picker to that console, and **cleared selected files when the system changed**. These are confirmed source defects in the user workflow; no claim is made that the exact user's failed HTTP request was reproduced from logs (no matching failure message was available).
- File selection comes first, defaults to **Detect automatically**, and no longer filters out CUE/BIN or clears files when changing console. Exactly one CUE/GDI, or exactly one M3U, becomes the entry regardless of selection order. Multiple candidate entries require a choice; BIN is not offered as the entry of a descriptor-based disc package.
- Authenticated `POST /api/games/roms/detect` receives only selected-file metadata and at most **eight 64-KiB prefixes**, with a **1-MiB request limit**. Detection is read-only; no ROM record/staging/job/provider call is created. Existing CSRF, matching Home actor, acquisition permission and owning-login validity apply; the real upload retains independent filename/header/size/reference/SHA/publication validation.
- Unambiguous extensions suggest a console. Shared `.md` uses Sega cartridge console headers to distinguish Mega Drive/32X; sampled structured `BOOT` versus `BOOT2` configuration can distinguish PS1/PS2. CUE+BIN, MODE2, a game title/filename, CHD/ISO or a shared PLAYSTATION volume label are **not universal console identity**. When evidence is insufficient, the user selects the console; Home does not silently label all discs PS1.
- CUE parsing lists **missing referenced filenames before the large upload**. Both CUE and all referenced BIN tracks are submitted together, even when BIN was selected first. Console changes preserve both files.
- Imported file references now match case-insensitively while duplicate-case names remain rejected. `filename*`-only multipart filenames are accepted. Only the owned runtime descriptor copy normalizes reference casing to actual staged filenames; **imported CUE/BIN/manifest hashes and source bytes are unchanged**. Existing bounded flat-package/traversal/cycle/unused-file/conflict checks remain intact.

## Verification

**67 focused backend / 14 Node checks, zero skipped**, TypeScript/worker/Vite/PWA build passed. New original fixtures cover shared-format ambiguity, Sega headers/BOOT generations, missing tracks, UTF BOMs, unsafe references, duplicate names/count/prefix bounds, preflight CSRF/actor rejection, and actual authenticated browser-style multipart **BIN first + CUE + a 1-MiB track + case mismatch** through Home's HTTP import endpoint. The isolated imported CUE source remained byte-exact and the runtime copy referenced the actual BIN filename. Test product workers are removed to prevent internet artwork/core acquisition. No user ROM was executed or uploaded through development tooling; no browser automation was run.

Backend/static-assets and frontend build checks must run sequentially. An overlapping attempt failed while Vite replaced hashed assets; the subsequent sequential final backend run passed. The failed build was not published.

References checked: [React effect race cleanup](https://react.dev/reference/react/useEffect), [ASP.NET Core per-request body limits](https://learn.microsoft.com/aspnet/core/fundamentals/servers/kestrel/options), [libretro PCSX ReARMed disc formats](https://docs.libretro.com/library/pcsx_rearmed/). No emulator/parser library, dataset, ROM or BIOS was adopted or acquired for this change.

## User flow / remaining gates

Reload Home → **Add ROM** → select the **CUE and BIN together** → CUE is the entry. If the console cannot be determined confidently, choose **PlayStation** without reselecting files. Confirm rights → import. If the CUE references a differently named/missing BIN, the exact referenced name is shown; original files are never automatically renamed/edited.

This is import/detection usability, not new PS1 gameplay or low-latency acceptance. The previous SNES/Mega Drive host-stream failures, real game/browser/device acceptance and remote/native distribution gates remain open.

## Publication / preservation

**User acceptance update / 2026-10-21:** the user reported **“çalıştı”** after the CUE/BIN update, then noted low-resolution appearance on a large screen. This establishes user-reported successful import/initial PS1 launch, not sustained play/save/latency or visual-quality acceptance. No user game was started by tooling. Existing display controls (smoothing, original aspect/integer scaling, stream bitrate quality) and the installed PCSX ReARMed core's actually exposed `(GPU) Enhanced Resolution` option are separate controls; upstream documents the latter as 2x internal 3D rendering with performance/compatibility costs, not new 2D detail or arbitrary HD support. Its result in the user's game/Home stream has not been verified.

Signed **0.38.1 / schema42** is live at `http://localhost:5080/games`, PID **19492**. **909 files / 116,334,696 bytes**, same trusted-key fingerprint `b6ebb7e0a2b2821afd3143dc3bd9c8b666189d9a41953aa6657cf60d789b566b`; rollback **0.38.0**, backup `ed74c18262654336b3ec4809290004c4`. Current native host/core binaries and save partitions were not replaced.

Read-only verification against the new pre-update capture matched existing ROM/catalog IDs, game states/saves, media/personal/collection/language data, native enabled preference and all **three paused transfers**, with **zero foreign-key violations**. The live hashed import client contains the new detection path and prefix protocol. Evidence: `artifacts/native-preview-data-before-v0381.json`, `artifacts/update-live-0.38.1.json`, `artifacts/live-native-v0381.json`, `artifacts/live-rom-import-v0381.json`. No user ROM import/launch, provider media/ROM/BIOS acquisition, transfer resume, browser automation or commit/push was performed by development verification.
