# RetroArch multi-system integration — 0.36.0 / schema 42

2026-10-20 · GAM-04/05/10/11/13 · user requests **all RetroArch emulation possibilities**, not just GBA. **The full request remains open.** This slice removes the single-core/single-extension architecture, implements multi-system content/core/firmware routing and the software-framebuffer adapter. It does not pretend that catalog entries alone provide playable GPU/special-device emulation.

## Official scope, not an invented support list

Pinned **libretro-core-info `5a74858ab2f7a50cebb5a6330895bc38899531c0`** (2026-09-16): **326 core definitions / 246 Emulator-category definitions**. Original `.info` text and digest are retained inside the bundled catalog; metadata uses **MIT**, original `COPYING` included. `tools/build-retroarch-catalog.mjs` fetches only official source metadata and software package **names**, never binaries/game assets/ROM/BIOS. Builds do not refresh it automatically.

Core settings distinguish installed, installable software adapter and integration-pending; filter/search/paged rendering, actual metadata/firmware/license/hardware requirements. PS2 **LRPS2** and **Dolphin** require a different GPU-frame path; **xemu** is actually in today's official catalog, but experimental, no savestate, HDD-based save path, OpenGL requirement even though `.info.hw_render=false`. It is explicitly blocked by save/adapter requirements. **Switch is not in this pinned official core catalog.** A download directory name without `.info` provenance is not adopted as a supported system.

## Implemented adapter and import

- Software framebuffer + upstream FFmpeg/MediaMTX/WHEP pipeline stays unchanged. No emulator/streaming engine, virtual-input engine or desktop capture was invented. Native runtime/control/launch now resolves the chosen core, its verified binary and `.info`, ROM entrypoint, required firmware and version-bound checkpoint. Old GBA mGBA **0.10.5** frontend/core fingerprint and **`native-mgba-*` partition are unchanged**.
- Released mGBA is also routed for **GB/GBC**; other implemented software adapter routes include NES/Famicom, SNES, Sega 8/16-bit/32X/CD, PS1, Saturn, PC Engine, Atari 2600/7800/Lynx/Jaguar, Neo Geo Pocket, WonderSwan, Virtual Boy, ColecoVision and Pokémon Mini. Alternative cores use real compatible database/extension metadata. Binding the adapter is not an assertion that every commercial game/core combination passed device acceptance.
- **35 system import definitions**: explicit system choice because `.bin/.iso/.rom` are ambiguous. Per-system extensions and size bounds (GBA still 32MiB, cartridges smaller, large-disc systems up to 64GiB), authenticated bounded streaming, cancellation, content-addressed verified publication, no host executable/ELF importer. Unsupported rendering/input systems may be stored in the library but their Play action stays disabled with a specific reason.
- Flat **CUE/BIN, GDI and M3U multi-disc packages** streamed with `MultipartReader`: <=128 files, <=64KiB descriptors, bounded total content, safe portable basenames, exact-case references, duplicate/cycle/external/unused-file rejection. Hash includes canonical entrypoint and complete sorted file manifest. Original CUE text/tracks are preserved; runtime has its own copy. **Interactive disc switching and resuming a changed disc index remain open**; do not claim this slice accepts a whole multi-disc game's progression.
- Complete **16 digital RetroPad buttons** through existing reviewed upstream Remote RetroPad ABI, including X/Y/L2/R2/L3/R3. GBA mapping/default validation is retained; full masks propagate through client and bounded server edge queue. Analog/touch/pointer/microphone/lightgun/wheel/multiplayer mappings remain open, not faked as digital support for N64/Wii/DS.
- State/save partitions are core+binary+frontend+transport specific; changing core does not erase old checkpoints. Managed snapshots now also preserve standard `.mcr/.bkr/.bcr/.smpc` and PCSX's optional second card; generic software cores flush actual upstream **`SAVE_FILES`** before snapshot publication. Special external save disks/NAND/HDD are not routed.

## Explicit installation, not silent nightly promotion

Home owns a durable **`native-core-install`** job; user chooses a compatible implemented core in Emulation settings, reads its actual license, and explicitly accepts the **official upstream nightly channel**. No automatic bulk downloads, automatic upgrades, manual DLL/EXE upload or replacement of the reviewed released mGBA. This acknowledges the current official Windows core downloader distribution channel; it is not described as a released-core build.

Download targets are constructed only from validated packaged core IDs at `https://buildbot.libretro.com/nightly/windows/x86_64/latest/`; redirects/proxy disabled, <=256MiB, one expected DLL ZIP entry, symlink/extra/executable entries rejected, native PE x64 DLL checked, immutable SHA-256 archive/binary/metadata receipt, publication only under the idle native host gate. Same version/receipt retry verifies rather than upgrading. The hash pins the **received** binary, not an independently supplied upstream signature or reviewed source commit. Runtime boot stays owner/local/non-admin and keeps exact-EXE firewall checks out of the input hot loop. Full binary/source-license distribution/sandbox gates remain open.

## User BIOS and artwork

Core-specific personal firmware store: exact declared names, <=64MiB per file, own upload only, known upstream MD5 checked when provided, immutable SHA-256 payload/manifest, current login/owner/local/idle requirements, no cross-user read or conflicting overwrite. Required entries and content-dependent FDS/Sega CD/PCE CD requirements block Play. Full BIOS **directory packs**, large DSi NAND/Xbox HDD and external core resources remain open; filename presence alone is not claimed to verify those systems. mGBA retains its prior automatic/HLE BIOS config for old save compatibility.

Cover lookup now uses each supported system's **real libretro thumbnail database**, rather than always GBA: bounded 8-index cache, exact HTTPS target, original provenance/rights restrictions, manual ambiguous selection and upload remain unchanged. No provider acquisition was issued during development verification.

## Meaningful focused verification only

No automated browser tests/screenshots, user ROM launches, internet media/ROM/BIOS downloads, elevated processes, new drivers, commits or pushes. Focused data/protocol checks are required for the migration/filesystem/save boundary changes; no full regression suite or playability claim.

- **58 backend checks / 0 skipped**: original isolated cartridge/disc fixtures, 41→42 preservation/idempotence, individual system copies, path/executable/descriptor-reference boundaries including tab-separated CUE attacks, private firmware integrity/conflict, revision-bound core selection, complete RetroPad queue, legacy input guard/control/save/ownership checks. `tests/HomeServer.Tests/TestResults/native-multisystem-v036.trx`.
- Local framework-only test runtime lacks `Microsoft.AspNetCore.WebSockets.dll` already used by 0.35. The same focused checks passed using self-contained `win-x64` test output, no OS/runtime modifications. This is an environment issue, not a reason to skip the API boundary checks.
- Frontend/worker/PWA/localization/accessibility gates passed. Pending publication and read-only live preservation/capability verification will be recorded below.
- **12 focused Node checks / 0 skipped** passed for full RetroPad mapping, preserved GBA mapping/edge buffering, the existing WHEP reader/auth/origin adapter and honest browser buffer/decode counters. No automated browser engine was started.

## References and remaining completion gates

Checked 2026-10-20: official core-info/COPYING; Context7 current RetroArch/Libretro core list, LRPS2/Dolphin/xemu requirements, Windows nightly/core downloader, PCSX and Beetle save layouts; exact pinned upstream `command.c`/`command.h` `SAVE_FILES` ACK, Remote RetroPad 16-button parser in the already backported receiver; installed frontend/ASP.NET Core multipart types.

**ALL-RETROARCH acceptance stays open:** per-core real native audio/video/input/save verification, user game/device acceptance, GPU context/frame path without personal desktop capture, analog/touch/peripherals/multiple ports, interactive disc switching, complete BIOS/resources packs, special save-disk persistence, stable/source-pinned binary packaging/fresh-host setup/native sandbox/licensing. Hardware is the user's choice; these are missing product integrations, not judgments about this server's performance.

## Live publication / preservation

**0.36.0 / schema 42** published through the normal signed/streamed update API, **PID 2032**, **909 package files / 116241464 bytes**, unchanged signing fingerprint `b6ebb7e0a2b2821afd3143dc3bd9c8b666189d9a41953aa6657cf60d789b566b`. Prior **0.35.0** rollback backup `7e77eddb9ac54d778b3a8f5b2ae5f499`; `artifacts/update-live-0.36.0.json`.

Pre-apply snapshot explicitly rejected active games; no game was active. Post-apply `artifacts/live-native-v036.json` matched all prior media/request/personal/collection/game-state/game-save/ROM digests, **0 FK violations**, native **enabled/available=true** and original **`native-mgba-7abcc80ad4dac4a5e6f46e9d0279ac4d`** unchanged. All **three transfers remain paused**, no resume/acquisition/game command.

Read-only `artifacts/live-core-catalog-v036.json` verified **326 definitions / 246 emulators / 33 software-adapter-ready entries / 35 import systems** served by the live API. **No extra cores installed**: current reviewed mGBA remains the only installed core; software installation is a user command. LRPS2/Dolphin/xemu remain integration-pending in the real API. Client `/assets/index-B18iY_M9.js`, PWA `024ccaf35c372a52`. Actual non-GBA core/game/firmware/browser acceptance has not been exercised by development tooling.
