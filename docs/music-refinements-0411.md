# Music follow-up — 0.41.1

Implemented **2026-10-21** for **MUS-09/11/12**, **CLI-01/05/01-K**. User feedback: general Music layout looks good; remove the hand-drawn fireplace/rain/water animations, add more freely licensed stock clips, use the library's record as fallback, bound long card titles, clarify/live-update discovery search and refine the existing gamepad navigation.

## Visual mode

- Deleted `web/src/music/AmbientCanvas.tsx` and its runtime/CSS references. There are no procedural fireplace, rain, fish, underwater or abstract-gallery fallback branches left.
- Library hero and visual fallback share the original `VinylRecord` component. Visual mode slowly turns the record while music/radio plays, pauses it with playback and respects reduced motion. It also appears during stock loading, after a load failure, when online visuals are disabled, or when Vinyl is selected explicitly.
- Stock catalog grows from **4 to 10 videos**: 2 fireplace, 3 rain, 3 aquarium and 2 underwater choices. Existing selections map to their original clips. The new `sceneVideo` preference is profile-private/revisioned and backward compatible with 0.41 JSON; only curated IDs select URLs. The selected clip appears in settings and the visual-mode selector. A visible **Enable online visuals** control explains the vinyl fallback when the opt-in is off.
- Per-resource load/error state remounts with a selected video/artwork, preventing stale readiness events from hiding the new fallback. Credits include source, author, license and loop/mute/crop notice; failed resources can be retried. Audio clocks, leases and source playback remain owned by their existing players.

### Additional free stock provenance

All ten source licenses and selected derivative URLs were checked against the **Commons `videoinfo/extmetadata/derivatives` text API** on 2026-10-21. Original/source dimensions are metadata, not a claim that the derivative is streamed at the original resolution. No video or image body was fetched during verification.

| Added clip | Author | License | Source |
|---|---|---|---|
| Fireplace II | Fastily | CC BY-SA 4.0 | [Fireplace 1 2021-02-14](https://commons.wikimedia.org/wiki/?curid=99849838) |
| Night lights — Las Palmas | slavikfi / Vjatseslav Robtsenkov | CC0 | [Raindrops against the window in the night city](https://commons.wikimedia.org/wiki/?curid=66989018) |
| Raindrops on glass | Frank Vincentz | CC BY-SA 3.0 | [Radevormwald window 07](https://commons.wikimedia.org/wiki/?curid=33649027) |
| Boka aquarium | BuhaM | CC0 | [Aquarium Boka — species 01](https://commons.wikimedia.org/wiki/?curid=149148404) |
| Planted aquarium | Lucas Joahay | CC BY-SA 4.0 | [Aquário com plantas aquáticas](https://commons.wikimedia.org/wiki/?curid=92845837) |
| Underwater dive II | Rob Bixby | CC BY 2.0 | [Underwater Videos 5](https://commons.wikimedia.org/wiki/File:Underwater_Videos_5.webm) |

Prior clips and the three Monet images keep their [0.41 provenance](music-experience-041.md). Exact reviewed records: `artifacts/music-visual-source-review-v0411.json`; reproducible text-only verifier: `tools/verify-music-visual-sources.mjs`. Media remains user-selected external content, with no stock binaries or new dependencies bundled.

## Card bounds and discovery

Music/radio/artist/playlist/provider cards now reserve at most two title lines with safe word wrapping and bounded secondary text. Hero/player/visual headings and track rows are also bounded. The full provider/user text remains in the DOM/accessible labels, with title tooltips on key cards; media identity and stored names are not truncated. This removes long-title-driven grid stretching.

Discovery now has a prominent, labelled **artist/band search field inside the section**, immediately adjacent to visible **MusicBrainz** attribution. The old empty-state paragraph resembling a search field is removed. Typing provides immediate status feedback, a 220 ms debounce, clear control, native Enter submission, Arrow Down into results, live counts/loading/no-match states and cancellation of superseded requests. Changing the query exits a previous artist; Back returns to the selected result/followed artist.

The keyless MusicBrainz endpoint supports an explicit autocomplete mode: boost the literal phrase and search artist/alias fields using literal earlier tokens plus one trailing-token wildcard. Names/operators are not accepted as arbitrary Lucene syntax. Existing exact-search callers keep their contract. The common 1.1-second provider lane, cache and cancellation remain in force; “live search” does not promise a network round trip per keystroke.

Actual public C# probes returned **Metallica** for `Metallic` and **Daft Punk** for `Daft P`, using the new adapter mode with no media requests. Evidence: `artifacts/music-artist-prefix-probe-v0411.json`. Official syntax: [MusicBrainz indexed search](https://musicbrainz.org/doc/Indexed_Search_Syntax); TanStack Query cancellation/current installed hook declarations and HeroUI modal declarations were reviewed.

## Targeted controller refinements

- Bounded the previous **1,000-pixel alignment penalty** so a very distant aligned item does not always beat a nearby off-column control. Sideways controls within the same row are excluded from an unrelated vertical move, and vice versa.
- Reset remembered column position after pointer/programmatic focus changes and account resets. Direction-driven focus retains its intended column. Focus reveal is immediate to avoid scoring intermediate smooth-scroll geometry during 125 ms held-repeat input.
- Fully transparent ancestors are excluded from navigation. Controller scroll margins reveal page targets above the fixed player rather than behind it. Horizontal navigation inside the dock stays in its own controls.
- Added **R3/right-stick press → player / press again → prior page target** on the standard pad. The dock's Play button receives focus without being clicked; after moving within the dock, that control is remembered. Returning resolves a replaced DOM node by its stable identity. Dock focus does not overwrite page-return history.
- Added a remappable `focusplayer` action and standard-layout hint. Existing face/D-pad/shoulder mappings, neutral gating, repeats, custom bindings, keyboard/native controls, modal/menu priority and gameplay ownership keep their existing roles. Custom mapping objects are not silently rewritten; R3 never escapes an open modal/fullscreen scope or active game input owner.

## Verification

- **23 backend/native checks, zero skipped**: prefix/quoted-operator/legacy search, active/waiting cancellation, old/new visual preferences, metadata opt-in/identity, music catalog access and backup restoration. Self-contained Windows test output is used because the existing framework-only development runtime is incomplete.
- **74 Node checks, zero skipped**: near/far and same-row geometry, R3 neutral/once-per-press behavior, primary focus and exact return, modal/game ownership, replaced target identity, transparency/column reset, previous controller/navigation/audio guarantees, all UI translations and selection controls, scene ID boundaries.
- Evidence: `artifacts/test-results/music-refinements-v0411.trx`, `artifacts/music-refinements-v0411-node.tap` and the two real public-text receipts above.
- No automated browser suite or screenshot iteration was run. Device feel, real stock-video appearance/codec support and visual acceptance are user-owned. The general 0.41 interface received positive user feedback; these specific refinements await refresh/device acceptance.

## Publication — verified 2026-10-21

Final frontend/worker/native-reader/Vite/PWA build passed; **76 public PWA files**, shell hash **`1cc7cf890bf15bf4`**. Signed **0.41.1 / schema42** is live at **http://localhost:5080/music**, PID **12364**. Package `artifacts/packages/home-0.41.1-win-x64.zip`: **909 verified files / 116,616,249 bytes**; trusted-key fingerprint **`b6ebb7e0a2b2821afd3143dc3bd9c8b666189d9a41953aa6657cf60d789b566b`**. Rollback **0.41.0**, backup **`87ea342be96747ee8137ca5541640782`**. The existing streaming updater preserved owner authentication and resumed no transfers.

Fresh pre/post read-only checks preserved existing music preferences, radio-favorite files, saved queue/history and catalog rows; **4 ROMs / 10 game-state rows / 10 saves**, native enable state and **3 paused transfers** remain intact, with **zero foreign-key violations**. The current library still has 0 indexed music tracks. Live verification also confirmed the new prefix API (`Metallic` includes Metallica), served vinyl/search/R3/new-clip client markers, and absence of the removed canvas class. Tooling started no radio stream, user ROM, acquisition, image or stock-video request.

Evidence: `artifacts/update-live-0.41.1.json`, `artifacts/music-data-before-v0411.json`, `artifacts/live-music-experience-v0411.json`, `artifacts/native-preview-data-before-v0411.json`, `artifacts/live-native-v0411.json`. User-owned visual/device acceptance remains open. Changes are local; no commit/push was made.
