# DS/3DS packages and transparent supported systems

**2026-10-27 / HANDHELD-01 / GAM-23, GAM-24**. User authorized making DS and 3DS ready and requested a supported-device dropdown next to Games settings. User also reported markedly lower lag after Selkies and described the experience as good. That is household feedback, not a new benchmark or blanket game/TV acceptance.

## Decisions and preservation

- Keep canonical **DeSmuME / `nds`** and **Citra / `3ds`** identities. Existing ROM ids, selected-core receipts, settings, Windows Citra snapshots, firmware/user Mii data, and paused downloads are not migrated or deleted. Source-built Linux cores are separate, pinned packages; no existing seven-system frontend/core binary is replaced.
- DeSmuME **95b4d798731caa809125b6c3c11d17cc332ff6ef**: mature DS libretro path, native dual-screen/software rasterizer/JIT, original DS saves and stylus input. Firmware remains user-supplied/optional; not downloaded during development.
- Citra libretro **a0483e9abe8134ae6b290cdca7d49cb77689f05b**: retain the current core identity rather than silently switching existing content to another engine. Native dual screens, OpenGL 3.3 context, original save/user-system-data paths and Home input. **3DS remains hardware-dependent/experimental** in the support view even after a package is qualified. Decrypted, lawfully user-supplied content is required; some titles need data from the owner's own console.
- Reuse the current Standard publisher and optional Selkies only with matching core/backend proofs. Do not run both publishers, replace save identities or infer a general GPU/hardware guarantee. No repeated seven-system/session latency tour and no automated browser suite.
- Supported-systems UI is an informational popover, not an emulator installer or catalog-as-support assertion. It reads real package/qualification state and clearly separates supported, experimental and not-ready systems. Missing package/runtime capability stays visible as a limitation rather than a fabricated success. English/Turkish and keyboard/controller dismiss/focus behavior are required.

## Status/gates

**Component implementation/qualification passed; approved for 0.48 publication on 2026-10-27.** The clean-release workflow and real-game/device acceptance remain separate; [release status](release-048.md). PSP/uninstall and scene-still/trailer work are included in the approved batch.

The support popover/API is implemented beside Games settings; frontend types and **10 focused Node/localization contracts passed**, and backend compilation had **zero warnings/errors**. DS Selkies presentation uses the native 256×384 dual-screen canvas; hardware-context cores retain OpenGL rather than being forced into the SDL2 software driver. Core/manifest qualification remains separate from these UI/adapter checks.

- Actual Standard H.264/Opus frame decoding, held button/stylus input and native save-state RAM restoration passed: DS **42 → 96 → 42**; final deterministic Citra **41 → 67 → 41**. Only original isolated diagnostics were used. DS core SHA-256 `d4b5120de77809d1c8040920bdb4aef06c5f50a4c60131704dacefb2b28745e7`; final Citra `ac90beb0bdaee636b4b0ffc7fbac4f5c57bb975a38f8113e8ef7e22da2cb8fc6`.
- Selkies frontend presentation, publisher health, actual native button/stylus counters and save restoration passed for both cores. This adds matching core hashes to its manifest; it is **not a new browser/WebRTC/device latency benchmark**. Preserved Standard frontend hash `285e1197…` and Selkies frontend `ead118c0…` are unchanged.
- A Citra upstream script-mode/out-of-source lookup produced `GITDIR-NOTFOUND`, breaking state revision validation. The newly packaged core now records the exact pin and source-commit build timestamp; adaptation, original source, recipe and notices are retained. Citra's larger synchronous state operations use a bounded **15-second** command budget; other cores keep **3 seconds**, and gameplay input is unaffected.
- Exact source closure passed: **13 linked binary packages / 10 source identities / 84 indexed upstream notices**, with core/submodule sources, dependency descriptors, build/manifest recipes and the revision patch. Source hashes/descriptors/notices are checked read-only. Component flags are true; image construction regenerates proof against its own binaries.
- Isolated read-only/no-network/non-root application package passed health and authenticated core/support API checks: DS ready/software, 3DS ready/experimental, Standard/Selkies exact-hash bindings and unsupported PS3/Steam. Existing binaries/partitions/settings were not migrated.

Development VM storage was expanded safely (shutdown, qcow2/partition/filesystem grow) when the isolated build disk filled. No source libraries, household settings or prior evidence/images were pruned. This was not a household CasaOS storage failure.

User owns real-game/device acceptance. Never acquire an internet ROM, BIOS, Mii data or media fixture for verification. Original diagnostics belong only to isolated tests. Previous 3DS freeze reports are not declared fixed by adopting a newer binary or by successful compilation alone.

## Source/licence references

Reviewed **2026-10-27**. Context7 was requested but still quota-exhausted; pinned official source and installed UI types are used.

- [DeSmuME libretro source](https://github.com/libretro/desmume/tree/95b4d798731caa809125b6c3c11d17cc332ff6ef): core Makefile/options, COPYING and individual libretro-common/source notices. **GPL-2.0-or-later**, with dependency/data notices retained.
- [Citra libretro source](https://github.com/libretro/citra/tree/a0483e9abe8134ae6b290cdca7d49cb77689f05b): core CMake/options, license.txt and pinned submodules (Boost, Dynarmic, Crypto++, shader/audio/archive libraries). **GPL-2.0-or-later**, with dependent notices. Full corresponding-source closure is a publication gate.
- [HeroUI v3 Popover](https://heroui.com/en/docs/react/components/popover): controlled disclosure, trigger/content/dialog, placement and focus behavior; no new UI dependency is adopted.
- Azahar was considered as a maintained standalone 3DS engine, but no silent engine/save-format migration is authorized here. Citra's existing Home identity is retained; compatibility/performance stays reviewable.
