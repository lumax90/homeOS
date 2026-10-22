# Basic ROM selection and development-cache cleanup

Decision / sources reviewed **2026-10-21**, GAM-04/05/10/11/13.

The user narrowed Games from the full RetroArch emulator catalogue to a dependable basic selection: **Game Boy, Game Boy Color, GBA, NES, SNES, Mega Drive and PlayStation 1**. PS3-class systems are not a release target. Existing Citra/3DS ROMs, saves and installed cores are retained; a narrower default view does not remove them. Other metadata remains available under advanced catalogue access, not advertised as verified support.

## Selected mature cores / licences

| System | Core | Upstream licence | Adoption status |
|---|---|---|---|
| GB / GBC / GBA | Released mGBA 0.10.5 | MPL-2.0 | Retain reviewed installed binary, no replacement |
| NES | FCEUmm | GPLv2 | Explicit Home install, upstream Windows binary pinned on receipt |
| SNES | Snes9x | Non-commercial | Explicit separate local acquisition; no commercial redistribution decision |
| Mega Drive | Genesis Plus GX | Non-commercial | Explicit separate local acquisition; no commercial redistribution decision |
| PS1 | PCSX ReARMed | GPLv2 | Explicit Home install; user BIOS recommended for compatibility |

No ROM/BIOS/game dataset is acquired. Additional emulators are software installations authorized by the user's request; they are not silently bundled into the Home installer. Snes9x/Genesis Plus GX restrictions must be revisited before any commercial/public distribution. Existing catalogue entries disclose the received upstream-nightly channel and immutable binary fingerprint.

References: [FCEUmm](https://docs.libretro.com/library/fceumm/), [Snes9x](https://docs.libretro.com/library/snes9x/), [Genesis Plus GX](https://docs.libretro.com/library/genesis_plus_gx/), [PCSX ReARMed](https://docs.libretro.com/library/pcsx_rearmed/), [mGBA](https://docs.libretro.com/library/mgba/). Official metadata remains pinned at `5a74858ab2f7a50cebb5a6330895bc38899531c0`.

## Cache cleanup

`tools/prune-native-development-builds.mjs` only considers immediate `retroarch-backport-build-<32 hex>` directories under the approved source-review cache. It hashes and protects every referenced profile/candidate/GPU asset, retains each attempt's receipts/logs/changed C/C++ source, refuses links and verifies preservation afterward. Original reviewed source trees, compiler/toolchains, ROMs and saves are never cleanup targets.

The retained legacy frontend's exact-executable firewall rule still binds to its original path, so that build is preserved rather than moving it and weakening the guard. The published GPU host already lives in managed persistent state.

Applied receipt `artifacts/native-build-cleanup-v038.json`: **27 → 1 complete development build**, 26 obsolete attempts / **6,033,438,093 bytes** removed; **198 source/receipt/log files / 26,325,989 bytes** retained in `frontend-build-history/`. Net reclaimed **6,007,112,104 bytes (~6.0 GB)**. All three profile files and **210 referenced asset entries** were verified unchanged. Those 27 folders were repeated full development builds, not emulator cores.

Software-install receipt `artifacts/basic-native-cores-v038.json`: only four new DLLs, total **23,990,715 bytes (~24 MB)**. Original mGBA 0.10.5 and Citra binary/save identities are not replaced. Existing game catalog and library are not filtered by the new import/settings defaults.

## Verification and readiness gates

**70 focused backend / 28 Node checks, 0 skipped**, plus TypeScript/worker/Vite/PWA build passed. New tests cover exactly seven systems/five cores without changing advanced routing, selected-advanced import retention, no implicit installation, and cleanup integrity/preflight refusals. An older source assertion was updated to the existing bounded 16-button ABI and combined guarded-port inspection; no input/firewall runtime change was made to satisfy it.

`tools/Home.BasicCoreProbe/` runs only original isolated CPU diagnostics. It never accepts a user ROM or acquires a BIOS, never adds a fixture to the live catalog, and acquires the same exclusive native-host lock before local streaming tests. Direct core tests separate emulated video/input/state/SRAM from the existing custom RetroArch → CPU libx264/Opus → MediaMTX/WHEP path. The live host/cores are not rewritten.

Initial diagnostic failures are retained rather than treated as a pass. Some assumptions are demonstrably **harness-specific**: mGBA's real V2 GB default palette is DMG Green (the legacy minimal harness used grayscale), some serialized state containers are not byte-identical after unserialize, and released mGBA exposes GBA WRAM through memory-map descriptors rather than `RETRO_MEMORY_SYSTEM_RAM`. The diagnostic now reads the actual interface. Native transport/SPS/input failures remain open unless a subsequent matching probe passes; adjusting a diagnostic decoder alone does not prove the production path fixed.

Only the isolated fixture's GB palette/MD three-button controller and diagnostic decoder are adjusted. Actual GBA/PS1 state restoration must recover the exposed game RAM and a counter that demonstrably advanced. Received state/SRAM blobs in Home must still round-trip byte-exactly. These do not constitute a browser transport or latency fix.

Browser/device and real-ROM acceptance belongs to the user. A successful diagnostic is not a guarantee of lag-free gameplay. Linux/CasaOS/Umbrel and TV remote clients remain open; no Windows-only preview is relabelled as a clean multi-platform installation.

## Current measured matrix — 2026-10-21

Full receipts: `artifacts/basic-native-proofs-v038.json`, with immutable core hashes and paths to each isolated original probe. **7/7 direct core checks passed; 5/7 host stream checks passed; 2 open.**

| System | Software installed | Original core video/input/state + Home snapshot | Actual existing-host encoded input + H264/Opus WHEP | Real game/browser acceptance |
|---|---|---|---|---|
| GB | mGBA 0.10.5, unchanged | Passed | Passed, explicitly selected diagnostic grayscale palette | Open |
| GBC | Same mGBA | Passed | Passed | Open |
| GBA | Same mGBA | Passed; full WRAM/IWRAM/counter restore via actual memory map | Passed | Open |
| NES | FCEUmm, new | Passed | Passed | Open |
| SNES | Snes9x, new | Passed | **Open/failed:** diagnostic decoder did not obtain H264 geometry/frames, including with bounded normal probing | Open |
| Mega Drive | Genesis Plus GX, new | Passed | **Open/failed:** video packets/pixels arrived, but the original pressed transition was not observed; explicit 3-button selection did not resolve it | Open |
| PS1 | PCSX ReARMed, new | Passed; complete 2-MB RAM/counter restore, memory-card blob snapshot | Passed, original PS-X diagnostic + core HLE (no BIOS acquired) | Open; user BIOS/disc compatibility separate |

These failed transport checks have **not** been relabelled as harmless harness errors or fixed gameplay. Their causes are not isolated. No further emulator breadth is pursued before the basic host path is dependable. The former reported 3DS freezes and browser end-to-end latency are not resolved by this scope/UI/setup update.

The actual frontend state files also passed exact Home blob snapshot restoration on the five successful host checks. Native state containers for GBA/PS1 are not byte-identical after unserialize; actual game-memory restoration was separately verified rather than weakening Home's stored-blob integrity requirement. The original GBA fixture uses autodetected save RAM; this is not acceptance of every commercial game's save hardware. Audio callback/codec presence is verified, not physical sound/gamepad/sustained gameplay.

## Publication / preservation

Signed **0.38.0 / schema42** is live at `http://localhost:5080/games`, PID **19716**, original trusted key fingerprint `b6ebb7e0a2b2821afd3143dc3bd9c8b666189d9a41953aa6657cf60d789b566b`. Package **909 files / 116,322,303 bytes**; the four emulator DLLs remain outside the ZIP. Existing native host SHA `c1b9dd158da2a41db79a774c0ec31865b889433b196800099a4c9ad1cfb854f9` and both original mGBA identities are unchanged. Rollback is **0.37.0**, backup `c6cabb4e22444b53b0b1b4e7f1785f89`.

Read-only pre/post-publication verification matched the freshly captured 0.37 user-data baseline (`artifacts/native-preview-data-before-v038.json`), including **3 ROMs / 9 game-state rows / 8 save rows**, all original game IDs, media/personal metadata/language/collections, native enabled preference and **three paused transfers**; **zero foreign-key violations**, no native processes remain after probes. No user game was launched; no ROM/BIOS/provider-media acquisition, download resume or desktop capture was performed. Evidence: `artifacts/update-live-0.38.0.json`, `artifacts/live-native-v038.json`, `artifacts/live-core-catalog-v038.json`.

The current seven-system/five-core **focus and installation slice is delivered**, but BASIC-ROM-01 remains open for the two host failures plus real-game/device/latency acceptance. Fresh-host native setup, public licensing/distribution and LAN/TV/Linux gates remain open. No commit/push or runtime-stack migration was made.
