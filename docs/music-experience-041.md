# Music workspace, radio and visual listening — 0.41.0

Implemented **2026-10-21** for **MUS-06/09/10/11**, **CLI-01/05**, **USR-03**, **OPS-03**. Requested scope: a Spotify-inspired alternative for a personal server, grouped settings, optional internet metadata/artwork, categorized radio and an artwork-free full-screen listening mode for mobile/TV.

## Library and player

- Content-first Music workspace with Listen, Albums, Artists, Songs, Liked songs, Playlists, Radio and explicit MusicBrainz discovery. Desktop side navigation becomes a horizontal touch/remote bar on small screens. Empty/loading/error states use actual catalog results; decorative records and scene illustrations are not synthetic albums.
- Real personal recently-played and liked-song pages, paged search, album artwork and quick play, add-to-queue, queue-to-playlist saving and playlist contents. Library scope, age restrictions and private history remain enforced by Home's catalog queries. Existing follow, monitoring, acquisition, tag/undo and analysis tools are grouped under the Music gear.
- The existing `GaplessEngine`/server PCM path is retained. The new persistent music dock keeps browsing available; its expanded view groups queue, lyrics and sound settings. Appending tracks preserves scheduled samples and the active lease, including paused queues. Appending after the queue finishes opens a fresh lease only on Play. User playback selection, shuffle/repeat, ReplayGain, volume, speed and compatible Video.js/shared-room access remain available.
- Radio, ordinary media, podcasts, Live TV and games relinquish local playback when the user selects a different source. Local music and radio share the same monotonic browser intent, server/account/device quotas and account playback management. Stale radio responses cannot resurrect an older source or borrow another login's stream.

## Optional metadata and cover sources

- Per-profile settings: automatic MusicBrainz library enrichment, Cover Art Archive images, Radio Browser discovery, online scene media, chosen scene, track-title visibility and default station country. New online metadata/covers/stock switches default **off**. Radio directory browsing defaults on; it does not start a stream. Sound defaults reuse existing profile playback preferences.
- Existing embedded/manual tags and embedded artwork stay authoritative. Enrichment stores private sidecars; it does not retag, rename, move or replace music. Album covers also serve songs and the player. Existing recording IDs, credits, lyrics and manual release/video relationships keep their current mature implementation.
- Automatic album identification accepts a tagged MusicBrainz ID or one exact album **and** artist match from a complete search page. Live editions, remasters, guest-artist differences, ambiguous duplicates and truncated searches are not guessed. Album type/date/provider identity supplement local details. A full track/recording identity for untagged files is not fabricated.
- A bounded sweep reads 60 albums/page and processes at most **4 per 12-second pass**, round-robin across opted-in accounts. Per-album fingerprints/generations/results and the sweep cursor persist across restart. Shared MusicBrainz requests are spaced **1.1 seconds** apart. Unmatched results retry after 7 days, matched after 30 days, transient errors after 30 minutes; the next completed collection sweep starts after 10 minutes. Settings expose counts and rescan. Disabling rechecks authority before publication and stops further work; an already-sent HTTP request remains bounded by its normal timeout.
- Existing CAA host allowlist, byte bounds, 512 MiB/2,000-image cache and missing-cover backoff are reused. Concurrent requests recheck the cache after admission. Turning fetches off permits cached images. Successful visible images are not refetched every time an enrichment batch finishes.
- Profile settings and saved station/category JSON are included in bounded backup/restore paths. Rebuildable metadata/cover caches are separate from source files and personal playlists/history.

## Radio

Home's original C# Radio Browser adapter reads the public station directory with genre/name/country filters, 48-result pages, bounded responses, 15-minute cache and randomized public HTTPS mirror fallback. Favorites retain actual provider UUIDs and user-defined categories. Favoriting or searching reads metadata only. Play reports the documented station click on a best-effort basis.

The real public adapter probe caught an important contract detail: `countrycode=` means **unknown/empty country**, not all countries. Empty filters are now omitted. The final probe returned **46 usable worldwide jazz stations from a 48-entry provider page** and **48 Turkish stations**, retaining pagination even when unsafe/broken entries are filtered. It also found the exact `Kind of Blue / Miles Davis` MusicBrainz group **`8e8a594f-2175-38c7-a871-abb68ec363e7`**, date **1959-08-17**. No stream, cover or stock-media body was requested. Evidence: `artifacts/public-music-sources-v041-final.json`.

Play creates a login/device-owned, process-local lease; the same-origin relay uses the existing public-address-pinned HTTP transport. Redirects and every HLS key/segment/init URI retain the network boundary. Opaque paths include Home's path base. Byte ranges, reader limits, playlist/node bounds, idle deadlines and cancellation are explicit. Sliding HLS node entries expire so a long-running station does not fail after its first fixed batch. Heartbeats expire after 90 seconds; shutdown, close, revocation and fatal client errors release the source. Pause ends the live relay; resume reconnects at the live edge. Stations and preferences survive restart; radio never auto-resumes on server startup.

Existing **hls.js 1.7.3** is used for MSE audio HLS, with native HLS fallback; ordinary supported MP3/AAC/Ogg streams use the browser audio element. Radio is live relay playback, not a recorder or acquisition/import job. Codec, station outage and autoplay failures are shown with explicit reconnect/Play controls. No promise of universal station or TV codec support is made.

## Visual listening

The same optional full-screen modal works over local music, compatible/shared audio and radio without replacing their audio element/clock. Album covers are absent. Scene choices: **Fireplace, Rain, Aquarium, Underwater, Art gallery**. Source playback controls, volume, scene choice, close/fullscreen and source credits remain keyboard/remote accessible. HeroUI handles modal focus/return; the portal host is stable when fullscreen changes. Controls fade after inactivity, remain available to focused keyboard/controller input and reappear on interaction. Screen Wake Lock is requested only while playing when supported and released on exit.

Original offline Home illustrations run at at most 20 fps on a 1600×900-bounded, aspect-correct canvas. Reduced motion renders a still scene and disables online looping video/gallery movement. With online visuals enabled, reviewed Wikimedia videos are muted, looped and cropped, and the gallery cycles three public-domain Monet artworks every 30 seconds. Sources load only after the user opens visual mode. Home illustrations remain beneath loading/failed visuals. No stock media is bundled, preloaded on library pages or fetched during development verification.

### Reviewed visual provenance — 2026-10-21

| Scene | Source / author | License |
|---|---|---|
| Fireplace | [Fantastic fireplace…](https://commons.wikimedia.org/wiki/?curid=77473809), **free video library** | [CC BY 3.0](https://creativecommons.org/licenses/by/3.0/); use the Commons machine-readable licence, not the video's informal “free/CC0” description |
| Rain | [Radevormwald – Raindrops on a window 10 (1)](https://commons.wikimedia.org/wiki/?curid=33649328), **Frank Vincentz** | [CC BY-SA 3.0](https://creativecommons.org/licenses/by-sa/3.0/) |
| Aquarium | [Aquarium fishes (1)](https://commons.wikimedia.org/wiki/?curid=95224319), **Gpkp** | [CC BY-SA 4.0](https://creativecommons.org/licenses/by-sa/4.0/) |
| Underwater | [Underwater Videos 6](https://commons.wikimedia.org/wiki/File:Underwater_Videos_6.webm), **Rob Bixby** | [CC BY 2.0](https://creativecommons.org/licenses/by/2.0/) |
| Art gallery | Art Institute of Chicago: **Water Lilies (16568)**, **Arrival of the Normandy Train, Gare Saint-Lazare (16571)**, **Stacks of Wheat (End of Summer) (64818)**; Claude Monet | All three returned `is_public_domain: true`; museum open-access image/selected metadata **CC0**. API descriptive prose is not imported |

Exact public URLs, authors and license/source links are in `web/src/music/sceneSources.ts` and the visual-mode information panel. The display notes looping/muting/cropping. Scene media remain under their individual licenses; Home's eventual distribution licence remains undecided.

## Documentation checked

- [MusicBrainz API](https://musicbrainz.org/doc/MusicBrainz_API), [rate limiting](https://musicbrainz.org/doc/MusicBrainz_API/Rate_Limiting), [data license](https://musicbrainz.org/doc/About/Data_License), [CAA API](https://musicbrainz.org/doc/Cover_Art_Archive/API).
- [Radio Browser API](https://docs.radio-browser.info/) and [public-domain station contributions](https://www.radio-browser.info/add): no API key; community directory metadata is public domain, broadcaster audio rights remain separate. Its GPL/AGPL server software is not embedded in Home.
- [Art Institute public API](https://www.artic.edu/open-access/public-api), [API/IIIF docs](https://api.artic.edu/docs/), Commons `videoinfo/extmetadata/derivatives` responses for every selected video.
- Current HeroUI v3 Modal/Button, TanStack Query cancellation/infinite-query, Motion reduced-motion and hls.js attachment/error/disposal docs via Context7, plus installed declarations. No new third-party package or dataset dump was installed.

## Verification and release status

Initial focused gate: **59 backend/native checks**, zero skipped, using self-contained `win-x64` tests. The existing local framework-only SDK still lacks `Microsoft.AspNetCore.WebSockets.dll`; the packaged runtime contains it. This environment issue did not cause boundary tests to be skipped. **64 Node checks**, zero skipped, cover audio clocks/append/pause/end/revocation, radio grouping, all UI translations/selection controls, controller scope and HLS routing. Evidence: `artifacts/test-results/music-experience-v041-selfcontained.trx`, `artifacts/music-experience-v041-node.tap`.

Final metadata/radio/backup gate: **24 checks passed, zero skipped**, including opt-out during a provider response, logout during directory lookup, raw-provider pagination/empty-country behavior and preference/favorite-category backup restoration. Together with the initial suite this is **64 distinct backend/native cases**. Evidence: `artifacts/test-results/music-experience-v041-final.trx`. Final TypeScript, worker, native-reader, Vite and PWA build passed; PWA shell **76 public files / `efa6ea2f47aa2d15`**. The existing large-chunk/react-compiler directive build notices remain informational.

## Signed publication — verified 2026-10-21

**0.41.0 / schema42** is live at **http://localhost:5080/music**, PID **18044**. Package `artifacts/packages/home-0.41.0-win-x64.zip`: **909 verified files / 116,602,277 bytes**, unchanged trusted-key fingerprint **`b6ebb7e0a2b2821afd3143dc3bd9c8b666189d9a41953aa6657cf60d789b566b`**. Rollback **0.40.1**, backup **`276043d3fba941d1b5a00cd2b42fe934`**.

Windows PowerShell's buffered package upload timed out before application. The existing streaming Node updater then applied the same signed package through the authenticated update API, with fresh idle/game/transfer/restore checks. Owner authentication survived restart, with **zero resumed transfers**. Evidence: `artifacts/update-live-0.41.0.json`.

Read-only pre/post checks preserved all existing music tables/preferences, **one saved queue / three user-progress rows**, **4 ROMs / 10 game-state rows / 10 save rows**, native enable state, media/request/personal/collection data and **three paused transfers**; **zero foreign-key violations**. The live library currently contains **zero indexed music tracks**; no synthetic collection was added. Live Music APIs returned valid private settings/pages and **18 real Turkish jazz station records**. Served client checks confirmed the workspace, gear, visual mode, radio dock and control endpoints.

Evidence: `artifacts/music-data-before-v041.json`, `artifacts/live-music-experience-v041.json`, `artifacts/native-preview-data-before-v041.json`, `artifacts/live-native-v041.json`. Verification started **zero radio streams**, fetched **zero artwork/stock-media bodies**, and issued **zero acquisition/gameplay commands**. Real browser/mobile/TV audio, long-running broadcasts, visual quality, fullscreen/autoplay and controller acceptance remain user-owned. Changes are local; no commit/push was made.
