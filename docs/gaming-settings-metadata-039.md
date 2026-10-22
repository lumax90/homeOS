# Game-specific settings and public ROM metadata — 0.39.0

Implementation / source verification **2026-10-21**, GAM-16/17, GAME-SETTINGS-02 / ROM-METADATA-01. Schema remains **42**; emulator/host binaries and save partitions are not replaced. [Earlier scope and provider research](gaming-settings-metadata-proposal.md).

## Settings implementation

- A shared **Game settings** dialog is available from native game details and the pause menu. Default editing scope is **This game**. Emulator defaults are an explicit separate scope; changing them requires exiting active gameplay.
- Sparse profile/core/game overrides take precedence over preserved profile/core defaults. Browser-local general fit/smoothing/volume remain the fallback, explicitly labelled under General preferences. Stream quality also supports game/core overrides over the server's existing default, within the same bounded recording implementation.
- Options are grouped under **Video, Audio, Controls, Compatibility**, with verified core-relevant common keys in the normal sections. The remaining actual core options are under **Advanced** with search; managed filesystem/debug options are not exposed as editable fields. No fictitious resolution/GPU option is added to an unsupported core.
- Core options and controller definitions are read from the user's actual paused core and cached per profile/core/binary hash. An unopened/unobserved core displays that the schema is not available yet; no user ROM is silently launched to discover it. Per-game options are keyed by private ROM ID, never the temporary `game.cue` filename or a downloaded title.
- Both game and core changes have revision checks. Reset removes only that game's overrides; no ROM/save/library deletion occurs. Old `values.json` and controller preferences are retained as defaults; new files live in version-bound `defaults/`, `games/` and `schemas/` subdirectories. A new core binary does not overwrite the previous version's preferences.
- UI edits are staged until Save, with separate scope labels, visible inherited values, discard/cancel behavior and reset. Fit/smoothing/volume update presentation; native core/controller/stream changes apply on relaunch, avoiding live framebuffer/recording-geometry changes.
- Configuration order was corrected: emulator starting defaults first, then profile/core/game choices, then required managed save/debug boundaries. Previously hardcoded Citra resolution/layout defaults could override saved choices; they no longer do. Required libretro save location/debug boundaries still cannot be overridden.
- General preferences, Console/core/BIOS installation and Server defaults are separate setup sections. Installation and firmware fields are not in routine game/pause settings. The original generic GBA-only solar/audio controls were removed from the universal panel, without resetting their stored values; actual core-specific options remain available where supported.

## Public metadata and artwork

### Confirmed artwork bug

The old implementation requested `.../Named_Boxarts/.index`; the public PS1 URL returned **404**. Libretro serves an Apache-style **directory listing**, not that index file. `RomArtwork` now parses bounded, decoded `.png` filenames from the actual listing, rejecting absolute URLs, encoded path escapes, folders/control characters and unrelated entries. Image requests remain fixed-origin HTTPS, bounded and hash-verified; no redirected/untrusted URL proxy is exposed.

### Chosen keyless integration

**Hasheous public MCP lookup/search/details API** (`https://hasheous.org/api/v1/Mcp`) was inspected through its official Swagger, README, API discovery and tool listing. The hosted documented tools are public/no-auth, while **ordinary IGDB metadata proxy endpoints advertise a client API key requirement**. We do not treat all Hasheous endpoints as public or reuse another application's key.

Home's original bounded HTTP adapter calls only the documented `hasheous_lookup_hashes`, `hasheous_search_games`, and `hasheous_get_game_details` contracts. No Hasheous/RomM server/plugin code, wrapper, ROM data or metadata dump is adopted. One request at a time, at least one second between requests, response/depth/count limits, cancellation and explicit source errors apply.

- After a successful user ROM import, a durable `rom-metadata` job calculates identification hashes from **verified managed source bytes**. Existing games have an explicit **Fetch game metadata and cover** command; development tools do not invoke that command to download internet artwork.
- Cartridge/image SHA-256 remains the existing integrity identity. MD5/SHA1 are separate identification fields, not security replacements. For CUE packages the first declared MODE1/MODE2 **data track** is used, never the Home manifest digest or a larger audio track. Compressed/modified/multi-disc variants can remain unmatched; no universal fingerprint-compatibility claim is made.
- Automatic confirmation requires a matching strong file hash, exact byte size and compatible platform, with only one accepted match. PS1/PS2, GB/GBC/GBA and platform variants are not conflated. CRC-only or name-only automatic matching is not used.
- Name-based candidates are shown with platform/region/year evidence for explicit selection. Selection has metadata revision checks; repeated imports/late jobs preserve a newer/manual match. Pending refreshes are coalesced. Provider failures do not block import or native Play.
- Public Hasheous signature details provide names, years, publisher, country/language and descriptions **when present**. They are **not the complete keyed IGDB/ScreenScraper dataset**; developer/player/genre/synopsis completeness and Turkish translations are not fabricated. Missing descriptions are visibly labelled.
- Covers use the repaired public libretro source for a unique confirmed title candidate; ambiguous art is selected through the existing Cover picker. Manual title/category/cover choices are preserved. Artwork publication rechecks job/user authority and current match; missing/cancelled covers never discard a confirmed metadata match.
- Provider identity, provenance and private cached metadata are stored separately under `games/metadata/<profile>/<ROM>/`. Metadata cannot rekey a ROM, change the selected emulator, move saves or alter original media. Broken metadata cannot make the normal game catalog unplayable.

No API key is required for the implemented public path. A future richer keyed provider is a separate optional feature, not claimed working now.

## Verification

**127 focused gaming backend / 16 Node checks / zero skipped**, TypeScript/worker/native-reader/Vite/PWA build passed. Backend and frontend/static asset generation were run sequentially. An earlier broad `Native` substring filter also selected unrelated media tests (136 passed / 15 conditional skips); that result is not presented as the focused gaming gate.

New original isolated fixtures verify: game A/B/core/profile isolation, sparse inheritance/reset, legacy/version preservation, stale/invalid/managed/controller transaction refusal, configuration ordering, local/CSRF/actor/ROM ownership through actual HTTP, public-provider contract without keys or ROM upload, platform/hash/size refusal, metadata/manual fields/ROM preferences preservation, CUE data-versus-audio/manifest fingerprint, pending-job coalescing, path-safe directory parsing and cancelled-authority cover refusal. Fake provider responses and image headers remain test-only; no production demo/catalog data is created.

Build initially rejected unlabeled new choice controls; accessible labels were added and the final build passed. No automated browser suite/screenshots or user ROM execution was performed. Native GPU/codec fixture measurements from earlier releases are not relabelled as real game/visual/latency acceptance of this change. SNES/Mega Drive transport and sustained game/device gates remain open.

## Sources / licences reviewed 2026-10-21

- RetroArch override/core-option distinction: https://docs.libretro.com/guides/overrides/
- Hasheous hosted API/schema: https://hasheous.org/swagger/v1/swagger.json ; `POST /api/v1/Mcp`, discovery `tools/list` returned **200 without credentials**.
- Hasheous official repository README: https://github.com/gaseous-project/hasheous/blob/main/README.MD ; source licence **AGPL-3.0**, not copied/embedded. Calling its public service is not adopting its server architecture or code.
- Hasheous `McpRequestProcessor` API response definitions were inspected, not copied: https://github.com/gaseous-project/hasheous/blob/main/hasheous-lib/Classes/Mcp/McpRequestProcessor.cs
- Public libretro filenames/art: https://thumbnails.libretro.com/ ; images retain publisher/developer copyright and attribution; no blanket open-data or commercial redistribution clearance inferred.
- Underlying Hasheous signature/IGDB data has its own source terms. Hosted free access is not a grant to redistribute whole datasets. Cache only private user-requested records; Home's public/commercial licence gates remain open.

## Publication / preservation

Signed **0.39.0 / schema42** is live at `http://localhost:5080/games`, PID **6676**, **909 files / 116,401,556 bytes**. Original trusted-key fingerprint `b6ebb7e0a2b2821afd3143dc3bd9c8b666189d9a41953aa6657cf60d789b566b`; rollback **0.38.1**, backup `2bfdf9802657477ba7428757e6112d29`. No emulator/host binary, native save partition, ROM payload or distribution/runtime stack was replaced.

Fresh pre/post-update checks preserved **4 ROMs / 10 game-state rows / 10 save rows**, all original game IDs, native enabled state, media/request/personal/collection/language data and **all three paused transfers**, with zero foreign-key violations. The actual served gaming client contains the new settings scopes, schema state and metadata commands. No gameplay/core-install/metadata acquisition/media-download command was issued by live development verification.

Evidence: `artifacts/native-preview-data-before-v039.json`, `artifacts/update-live-0.39.0.json`, `artifacts/live-native-v039.json`, `artifacts/live-core-catalog-v039.json`, `artifacts/live-game-settings-v039.json`; final focused tests `artifacts/test-results/gaming-v039-focused-final.trx`.

For existing games the user initiates **Fetch game metadata and cover**. For an unseen core, the user launches their own game and opens paused settings to capture its actual option schema. Browser/device, live provider match/cover availability, inherited-resolution result and sustained-game latency acceptance remain user-owned; API/data licence/public distribution and the earlier SNES/Mega Drive transport failures remain open. No commit/push or additional cache cleanup was performed.
