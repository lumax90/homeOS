# Emulator settings scopes, grouped UI and ROM metadata

Research / user requirements recorded **2026-10-21**, GAM-16/17, extending GAM-04/12/14/15.

**Status: proposal and source review only.** The live application remains **0.38.1 / schema42**. No new settings persistence, UI, metadata provider, dependency, account, API credential or runtime change has been implemented/deployed in this research slice. No user ROM was launched/read by tooling; no provider game metadata/artwork acquisition or browser automation was performed.

**Subsequent user authorization / implementation:** the user explicitly requested both settings and public metadata/artwork work. The delivered **0.39.0/42** slice uses scoped/grouped settings and a documented **public Hasheous + corrected libretro directory** path, rather than silently borrowing a keyed provider client. This document remains the earlier research record; current implementation/verification/limits are in [gaming-settings-metadata-039.md](gaming-settings-metadata-039.md).

## Current Home findings

- `NativeCoreOptions.cs` persists options and controller devices by **user/core**, with options bound to the received core binary SHA-256, not by game. Its option save captures the available option values. Current advanced player UI lists raw core options without grouping or a scope choice.
- Display fit/smoothing/volume are browser-local per-profile preferences. Streaming quality/audio filter/solar defaults are server-wide in `games/emulation-options.json`. Those different scopes are visually mixed in the preparation dialog.
- `RomArtwork.cs` already uses **libretro-thumbnails** for title-based box-art search and optional exact-normalized-title acquisition. This is not a complete game-metadata API or a hash/region/revision match. Current presentation supplies title/category/cover provenance, not verified descriptions, developers, release dates and editions.

## How RetroArch does it

Official [override documentation](https://docs.libretro.com/guides/overrides/) reviewed, dated 2026-09-25:

- Frontend config (`.cfg`) and input remaps (`.rmp`) apply **global → core → content directory → game**, with the more-specific configuration winning. Overrides/remaps can store only changed settings.
- Emulator **core options** are separate from frontend overrides, remaps and shader presets. The documented game-specific `.opt` is a complete options configuration loaded instead of the base options file; do not incorrectly describe all these formats as one sparse-merge system.
- The core defines its available options, labels/values and, with the newer options interface, categories. A universal list of fabricated resolution/GPU settings is not appropriate for every emulator.

Home should adopt the clear **scope and inheritance model**, not expose arbitrary imported RetroArch configs that could change filesystem paths, network/input/security boundaries. Our managed content filenames (`game.cue`, `game.gba`, etc.) are not stable game identity and must not key per-game settings; use the existing private ROM ID plus profile/core/binary identity.

## Proposed Home UX / persistence

1. **General preferences**: presentation, volume and shared input preferences for the profile. Owner-controlled server/network/resource limits remain separately under Server settings and cannot be overridden by game preferences.
2. **Emulator defaults**: actual settings for the selected core, shared by that profile's games using that core. Explicit action **“Save as emulator default”**, with a scope warning.
3. **This game**: selected by default in a game's settings. Store only deliberate overrides; display inherited source/value and provide **“Use emulator default”** / **“Reset this game's overrides”**. Switching/renaming metadata cannot move save partitions or rekey the ROM.

Use one reusable, grouped settings surface from both the game detail page and pause menu, rather than embedding installers, BIOS uploads and every core setting in the pause screen. Main sections: **Video, Audio, Controls, Compatibility**. Display only verified, core-relevant common controls there; the full technical catalog stays in an explicit **Advanced** section with search. Install/BIOS/firmware/provider administration belongs in the system setup page, not routine gameplay controls. Restart-only/unsafe/managed options need visible explanations; renderer/geometry/enhancement changes must not silently destabilize a live recording session.

Native defaults/categories must come from the actual installed-core interface or a version/hash-bound recorded schema. Do not invent supported options for an unopened/uninspected core. The backend remains authoritative for scope, value validation, revision checks, ownership, filesystem protection and core identity. Preserve existing core preferences as emulator defaults; no destructive migration/reset of user settings.

Acceptance: changing PS1 game A must not change PS1 game B or GBA; explicitly saving an emulator default must affect only the intended profile/core; inheritance/reset, core replacement, restart-required choices, stale commands, cancelled drafts and browser language/focus/reduced-motion behavior must be verified. Existing ROM/save IDs and paused transfers remain preserved.

## Provider comparison

| Provider | Appropriate use | Access and licence constraints |
|---|---|---|
| **ScreenScraper API v2** | Retro ROM matching by **CRC/MD5/SHA1 + size + system**, optional serial/game IDs; regional covers, synopsis, developer/publisher, release dates, players and media | Official docs require application-specific developer ID/password issued by their team. Member credentials are optional for some endpoints but affect availability/quotas. API integration is restricted to entirely free distributed apps unless their team grants permission/conditions. Site data notice is **CC BY-NC-SA 4.0**; do not assume every third-party publisher image has unrestricted rights. Home's undecided distribution licence needs an explicit access/licence review. |
| **IGDB API v4** | Broader game catalog, summaries, covers/artwork, companies, releases and platforms; useful secondary enrichment | Twitch developer app, confidential client ID/secret and client-credentials token. Official getting-started docs state free **non-commercial** use under Twitch terms and commercial partnership/contact; the FAQ wording differs, so do not claim unconditional commercial clearance. Documented **4 requests/sec / 8 open requests**. Its object `checksum` is not a ROM checksum: not a substitute for hash-based ROM matching. |
| **libretro-thumbnails** (already integrated) | Lightweight current cover fallback/manual choice | Primarily named images, not a full metadata API. Existing attribution/copyright limitations remain; no blanket metadata/data licence inferred. |

**Recommended sequence:** implement scoped/grouped settings first. For retro identification, prefer ScreenScraper if Home obtains the required developer access/licence permission; evaluate IGDB as optional additional metadata, not as a replacement ROM-hash identifier. Existing manual covers and library data remain usable when no provider is configured.

## Metadata identity and safety requirements

- Keep provider identity and retrieved metadata separate from ROM identity, core selection, saves and user-edited title/category/cover. A metadata match must never rekey saves or overwrite a locked manual field.
- Existing cartridge SHA-256 remains the integrity identity. Add provider CRC/MD5/SHA1 as **identification fingerprints**, not as security checks. Existing disc `GameRom.Sha256` hashes the Home disc manifest; **never send it as the BIN/ISO ROM hash**. Define/test the provider-compatible disc/track/directory fingerprint and serial extraction before claiming PS1/CUE/BIN identification.
- With opt-in provider setup, enqueue bounded/cancellable/recoverable enrichment after successful import. A slow/offline/limited API must not block import or Play. Ambiguous/name-only matches require user selection, with visible system/region/edition evidence and source attribution.
- Prefer the requested metadata language, with explicit English fallback when Turkish is unavailable; do not fabricate translations/ratings/game data.
- Keep credentials server-side in protected secret storage. ScreenScraper carries credentials in query strings/returned media URLs: redact logs, exceptions, provenance and UI; never hand those signed URLs to the browser. Validate provider/media destinations, redirects, content/size bounds and rate/backoff budgets. Cache only verified payloads in managed private metadata/artwork storage.
- Development verification uses original isolated API-response fixtures; actual provider setup/metadata retrieval and game/browser acceptance are user-owned. Do not download ROMs, BIOS, trailers or whole datasets to prove integration.

## Sources reviewed 2026-10-21

- RetroArch: https://docs.libretro.com/guides/overrides/
- ScreenScraper official API/auth/licence/hash fields/errors: https://www.screenscraper.fr/webapi2.php (not the unrelated `screen-scraper.com` product).
- ScreenScraper developer access process: https://www.screenscraper.fr/forumsujets.php?frub=12
- ScreenScraper site licence notice / contributing sources: https://www.screenscraper.fr/
- IGDB API/auth/rate limits/fields/terms: https://api-docs.igdb.com/
- Binding IGDB/Twitch terms reference: https://www.twitch.tv/p/legal/developer-agreement/

No wrapper library, provider dataset or code was adopted. These reviewed terms are integration gates, not a legal clearance or working-provider claim.
