# Games workspace, automatic metadata and notification/focus fixes — 0.40.0 / 0.40.1

Implemented / verified **2026-10-21**, GAM-15/16/17/18, CLI-01/05/01-F/01-J. User requested all items together: selecting an identified game must work, large collections must enrich automatically, replace the library's manual cover button with Games settings, repair automatic console detection, themed console collections, renewed game details/settings layouts, anchored notifications and an unclipped controller focus effect.

## 1. Actual metadata failure and recovery

The user's cached PS1 candidates included Hasheous IDs **598584** and **937669** (the same title from separate signature catalogs); the 3DS candidate was **640909**. Read-only public `hasheous_get_game_details` calls returned HTTP **200** with JSON-RPC error **-32000 / `The node already has a parent.`** for all three. This is a provider details-tool failure; Home previously collapsed it into “The game metadata response is invalid.”

`HasheousMetadataClient` now distinguishes correlated remote JSON-RPC errors from malformed responses. For that details-tool failure it reuses the documented public search contract and requires **the exact same provider ID and matching console**. It never silently swaps in a similarly named edition. Unavailable long descriptions are kept empty, and partial details are recorded. Search results whose “description” only repeats the title no longer appear as a fabricated synopsis.

The real updated client was exercised with `tools/Home.MetadataProbe` against ID **598584**, recovering `Tony Hawk's Pro Skater 2 / Sony - PlayStation` successfully. This probe read public text metadata only: **no ROM access, image request, gameplay or live-state mutation**. Evidence: `artifacts/hasheous-details-recovery-v040.json`.

## 2. Automatic collection enrichment

- The user's explicit automatic-collection request is implemented for **existing and future ROMs**. Per-profile Games settings expose automatic enrichment, exact title/console fallback and cover-region preference. It defaults on; the user can pause it without affecting transfers or gameplay.
- `RomMetadataAutomation` scans **64 owned in-library ROM records per page**, refills at most **8 jobs per pass**, holds at most **12 pending metadata jobs**, and reserves global durable-queue capacity. The existing one-lane provider client still limits requests to at most one per second. The collection does not need to be open in a browser.
- Per-ROM pipeline/generation/outcome/retry receipts make restart rescans idempotent and prevent immediate retries for failures or ambiguous matches. HTTP timeouts and transient failures have exponential retry delay; unmatched/missing-art results are revisited later. A collection rescan is available inside settings, without a “find missing covers” button on the main page.
- Disabling automation cancels queued/running automatic jobs, including legacy import jobs, and authority is rechecked before metadata/cover publication. Explicit manual work remains explicit. Queue pressure no longer changes a successfully imported ROM into an HTTP upload failure; the refill worker picks it up later.
- Exact hash/size/platform duplicates with the same identity are resolved automatically. If the hash is unknown, optional **exact title + console** matching is used; sequels, near names, special-edition/demo ambiguity are not guessed. Match method remains visible (`hash`, `name`, `manual`).
- Artwork selects an exact-title public cover by confirmed/filename/preferred region, with deterministic regional fallback. Multiple regional covers for the same game no longer force hundreds of manual choices. Custom cover/title/category and all ROM/save IDs stay intact.

Public lookup and image availability are not guarantees of complete metadata or Turkish descriptions. The implementation still uses public Hasheous and libretro, with no borrowed developer key or new ROM/content provider.

## 3. Automatic import detection

The old detector sampled only the first 64 KiB and searched for a boot string there. PS1/PS2 `SYSTEM.CNF` commonly lives beyond that prefix. The new bounded, read-only detector follows an **ISO9660 primary volume descriptor → root directory → SYSTEM.CNF** path for 2048-byte ISO, 2352-byte Mode1 and Mode2 sectors.

The server requests only small byte ranges from the already selected browser files; the client checks file identity, offsets, lengths, total **512-KiB sample budget**, repeated requests and a four-round maximum. No filesystem path, unselected file or external game server is read. Structured `BOOT` versus `BOOT2` discriminates PS1/PS2. Conventional explicitly named cartridge extensions also stay useful even when generic core metadata advertises overlapping formats. Upload header/reference/hash checks remain independent.

The UI shows the detected console. Truly ambiguous formats show console-choice buttons and **“Choose a console first”**, rather than an unexplained disabled import button. Manual selection preserves the files and can be used if only the optional detection request failed.

## 4. Games presentation and settings

- Main Games heading now has a **gear/settings control**. Its panel has **Library and metadata / Emulation settings / Consoles and cores** sections, including background progress, automatic matching and regional cover settings. The obsolete bottom preparation panel and manual missing-cover action were removed from this page.
- Console groups have distinct color treatments and **original decorative vector console silhouettes** (PlayStation-style disc console, dual-screen handheld, Game Boy-style handheld, other console families). These are UI decoration, not downloaded publisher logos, game screenshots, synthetic catalog entries or newly licensed datasets.
- Native **and browser-game** detail pages now share a content-first cover/hero treatment, separated primary actions, saved-progress/runtime facts, readable metadata and file/core management cards. Source hashes and firmware/core administration are not mixed into the hero text.
- Game settings have an actual **880px-bounded outer frame** instead of a wide inner element trapped inside the old 530px modal. Games settings use a larger fixed frame with independently scrolling body. Component-scoped `.games-surface` fields, descriptions, buttons and checkbox/range styles avoid generic `.form-note` flex/negative margins and full-width 45px checkbox rules.
- Display and core option groups remain scoped to the game/core/profile. A dirty editor retains its original revision snapshot through background refetches, so another tab cannot silently replace a draft or make an old edit overwrite newer settings. Saved changes retain the current scope rather than remounting into game scope.
- Shared dialogs now portal into the fullscreen element when necessary, like the existing select/controller overlays, so in-game settings remain in the visible fullscreen/focus boundary.

## 5. Notifications and controller focus

The header bell opens a **HeroUI v3 anchored, dismissible non-modal popover**, not the full-screen blurred modal. It includes unread/all filters, individual/all read actions, distinct loading/error/empty states, bounded list scrolling and a link to notification settings. Delivery administration/history remains in Settings. Escape/outside interaction/controller Back dismiss the popup; push-click opening and account-isolated APIs remain intact.

Controller card focus no longer relies on an exterior four-pixel-offset outline that the discovery rail clips. It uses an **inset poster ring, subtle elevation/image emphasis and brighter title**, with enough rail padding for the effect. Generic controls use an inward outline. The styling remains dormant in mouse/keyboard mode, and reduced-motion preference removes transforms/transitions. Input mapping, neutral gating, gameplay ownership and navigation-return identity are unchanged.

## Verification

- **138 focused backend checks, zero skipped**: existing native/import/settings/save coverage plus actual provider-error fallback contract, automatic exact match/region policy, **205-ROM** bounded refill/disable/restart, durable rescan/backoff, and structurally original ISO/Mode1/Mode2 boot-file range fixtures and malformed sample refusal.
- **79 Node checks, zero skipped**: sample-range ownership/size/repetition bounds, game display/selection rules, all UI messages and all discovered selection controls, controller scope/neutral/Back/keyboard/navigation restoration, and notification push account binding. These are isolated DOM/model checks, not automated browser/device gameplay acceptance.
- TypeScript/worker/native-reader/Vite/PWA build passed. Backend/static-assets and frontend build ran sequentially. The old exact-count control inventory assertion (92 controls/38 files) was replaced with nonempty discovery; every individual accessibility/renderer assertion remains. No runtime was modified just to satisfy an obsolete count.
- Public metadata fallback was exercised by the actual C# client. No user ROM was launched, no automated browser suite/screenshots or internet game/BIOS acquisition was performed. Development checks did not request internet cover images. The production automation now follows the user's requested collection enrichment after deployment; it may fetch public cover artwork as its real workflow, not as a synthetic test.

Sources reviewed: [Hasheous public contracts](https://hasheous.org/swagger/v1/swagger.json), [official Hasheous README](https://github.com/gaseous-project/hasheous/blob/main/README.MD), [libretro thumbnail server](https://thumbnails.libretro.com/), [HeroUI v3 Popover](https://heroui.com/docs/react/components/popover), [React effects/race cleanup](https://react.dev/reference/react/useEffect). Installed HeroUI/React Aria types were checked. No new dependency, provider key or dataset was installed. Public provider/data/illustration provenance continues in `docs/dependencies.md`.

## Final artwork follow-up — 0.40.1

The first production automatic pass completed the Mario cover and recovered Tony Hawk metadata, but Tony's cover was still absent. Read-only inspection of the real libretro directory exposed another parser defect: `href=["']([^"']+)` incorrectly truncated **apostrophes inside a double-quoted href**, discarding those `.png` links. The parser now matches the opening/closing quote type independently and still rejects path/URL escapes. Regression cases include literal apostrophes, percent-escaped apostrophes, and HTML entities under both quote styles.

The actual C# public-text probe then parsed **9,339** PS1 directory entries and selected **`Tony Hawk's Pro Skater 2 (USA).png`**, without requesting an image. Evidence: `artifacts/libretro-apostrophe-recovery-v0401.json`. **15 targeted metadata/automation backend checks, zero skipped**, and the complete frontend/worker/PWA build passed after the fix. The metadata pipeline revision was advanced so the existing collection automatically retries affected records; no manual per-game retry is required.

## Publication and real production outcome

Signed **0.40.1 / schema42** is live at `http://localhost:5080/games`, PID **5184**, **909 verified files / 116,466,427 bytes**. Trusted-key fingerprint is unchanged: `b6ebb7e0a2b2821afd3143dc3bd9c8b666189d9a41953aa6657cf60d789b566b`. Rollback **0.40.0**, backup `c9384c914a194f33b717c5ee1fba4433`; the preceding 0.40.0 rollout retained 0.39.0 and backup `52872be5a78e48018be4c95ce13d9ff5`.

Both fresh pre/post-update data checks preserved **4 ROMs / 10 game-state rows / 10 save rows**, native enabled state, existing core/ROM/game IDs, media/request/personal/collection/language data, and **three paused transfers**; **zero foreign-key violations**. No native emulator/MediaMTX process was running at final read-only inspection. Runtime binaries/save partitions were not replaced, and no user ROM was executed by development tools.

The user's requested **production automation** completed metadata and public cover artwork for **Tony Hawk's Pro Skater 2** and **Super Mario Advance 4 — Super Mario Bros. 3**, with **zero failed jobs** at final observation. **Mario Kart 7 Demo/Kiosk** and the modified **Castlevania Definitive Edition** remained unconfirmed rather than silently receiving retail-game identities. Those genuine ambiguity cases remain reviewable; they do not stop the rest of the collection. The user's manual title/cover choices are preserved. The read-only verification script itself issued no import/game/enrichment/media-download commands.

Evidence: `artifacts/update-live-0.40.0.json`, `artifacts/update-live-0.40.1.json`, `artifacts/native-preview-data-before-v040.json`, `artifacts/native-preview-data-before-v0401.json`, `artifacts/live-native-v0401.json`, `artifacts/live-core-catalog-v0401.json`, `artifacts/live-games-experience-v040.json`, `artifacts/live-games-experience-v0401.json`. Served client checks confirmed the gear, console groups, detail layout, automatic detection/settings protocol and anchored notification panel.

Real device/visual/game/latency acceptance remains user-owned; earlier SNES/Mega Drive host-stream and multi-platform release gates are not relabelled by this UI/workflow slice. No commit/push, automated browser suite or screenshot iteration was performed.
