# Dolby Vision support plan and Home recent-items correction

Date **2026-10-25**. Requirements **PLY-02/03/05/06, CLI-01-A, CAT-01/03/05**. User confirms the missing manually copied films concern the page opened by the **Home logo**; requests broad movie-player support. **Home/analysis changes below are local and verified; new Dolby Vision playback paths are not yet implemented/validated.**

**Scope correction, 2026-10-25:** The user reiterates that the goal is **Jellyfin/Plex-level broad media compatibility**, not a fix conditional on one film's MediaInfo report. [MEDIA-COMPAT-01 / PLY-10](media-playback-compatibility.md) defines the reusable runtime, automatic policy and complete acceptance ladder. Source-profile analysis is Home's responsibility; manual household metadata may assist later device diagnosis but does not block the general implementation.

**Subsequent authorised implementation:** [MEDIA-RUNTIME-01 / MEDIA-DV-02-A](media-runtime-implementation.md) connects the pinned runtime locally and implements verified P8.1 HDR10 base-copy with real original RPU/output tests. Analysis revision is now **3**. The older diagnostic-only status below describes the previous slice; full DV rendering/tone mapping and public distribution remain open.

## Home recent items — HOME-RECENT-02

The Home screen reused the library's filtered infinite catalog query and its accumulated pagination. Library query text/type/root could therefore act as an invisible Home filter, including while navigating back. Home now requests its own **unfiltered recent catalog page** (`/catalog?offset=0&limit=60`, account access still enforced by the existing endpoint) under the `catalog` invalidation family. Its hero, total, loading/error and first six recent cards use that result, not the library's active filters.

`catalogGroups` is shared between the two views. File-only imports keep their own media identities; only known versions of the same matched title/episode are grouped. No download ID, metadata match, artwork or fabricated provider entry is required for Home's recent cards. The library's search state and pagination are preserved, not globally reset. Real-device visibility still needs user acceptance after publication; a catalogue larger than six distinct recent items is deliberately summarized by this existing shelf.

## Why VLC is not a browser-wide codec fix

Official **libVLC** is a mature native C framework with desktop/mobile/.NET bindings, **LGPL-2.1**. It owns decode/render/audio in a native application. Replacing the HTML player controls with a VLC-style UI would not give the browser those native decoders or HDMI passthrough. A separate native client would be a different product target; running decoders in WebAssembly on a weak TV also has CPU/memory costs and is not automatically a validated DV renderer.

For Home's web/PWA target, the preferred boundary remains a mature **server media runtime + source-specific client capabilities + authenticated delivery**, with Home owning selection, resource budget, timeline, cancellation and user data. A native VLC integration is not being adopted in this slice.

## Required delivery ladder — not all implemented yet

1. **Original Dolby Vision** where the exact source's DV profile, container, video/audio and the browser path are supported. Keep dynamic metadata; change only the container/audio when a validated path can preserve it.
2. **Compatible base layer** when the source actually has an HDR10/HLG/SDR-compatible base and the destination supports it. This is fallback playback, not full Dolby Vision rendering. Prefer video copying where safe; otherwise use the already appropriate base-layer tone map.
3. **True DV-to-SDR conversion** for sources such as Profile 5 with no ordinary HDR10 base. Use a DV-aware tone mapper, not a renamed HDR10 `zscale` chain. Subtitle burning and unsupported audio follow the same explicit output plan.
4. **Bounded preparation** if the host cannot keep up in real time. Offline preparation or a lower output profile is a separate planned workflow, not a promise that any 4K film plays instantly on old CPUs. User libraries remain read-only.

The application's existing blanket DV conversion guard is not removed merely because a source is marked HEVC or has PQ primaries. Profile 7 enhancement layers, Profile 8 compatibility modes and Profile 5 color mapping need distinct verification. Full home-theatre audio formats also remain subject to the browser/transport, not just the TV's hardware feature sheet.

## Mature runtime candidates and current source facts

- **jellyfin-ffmpeg v8.1.3-1**, latest non-prerelease as checked on 2026-10-25. The official Noble amd64 software archive is `jellyfin-ffmpeg8_8.1.3-1-noble_amd64.deb`, **52,574,218 bytes**, SHA-256 **`188fa8b31f7e0ded9dadd1d11d1cd05482a7f03cec4e5c0af80aae21fde44ce4`**. It is a candidate media runtime, **not installed into Home's production image by this slice**. Reuse means the FFmpeg executable/filters, not adopting Jellyfin's server architecture.
- Modern upstream FFmpeg exposes `dovi_rpu` for DV metadata processing; the configured Windows **9.0.2** executable's actual help confirms `strip`. This proves an available bitstream-filter API, **not** a working DV-to-SDR path or its availability in the current production Linux FFmpeg **6.1.1**.
- DV-aware tone mapping through a supported FFmpeg/libplacebo or the documented Jellyfin GPU paths is a candidate for Profile 5. Driver/codec/filter capability and actual output must be tested. The user's second-generation Intel/no-discrete-GPU report does not establish usable modern HEVC Main10 hardware decode or DV tone mapping.
- **quietvoid/dovi_tool 2.3.4 / MIT**, commit `614c816b6446dcd1dbaf433403d499a6026fbb5a`, is used only as an isolated development metadata generator. The official Windows x64 archive is hash-pinned in local evidence. The first generated-video experiment did **not** create DV side data, and is explicitly not counted as Dolby Vision media verification. No internet media was downloaded.
- Existing native-game runtime/source pins and settings/save identities must remain unchanged if a separate playback runtime is later added. Media/runtime licence and corresponding-source obligations remain component-specific.

## Implemented source analysis — DV-ANALYSIS-01

- `MediaTools` requests the real ffprobe DOVI profile/level, base/enhancement/RPU flags and base-layer compatibility ID. Missing/invalid optional flags remain unknown; they do not invent an HDR10 fallback.
- Analysis version **2** refreshes old cached track analysis on the next inspection without changing media IDs, progress or source bytes. This is an analysis revision, not a database migration.
- Original codec identifiers respect HEVC `dvhe`/`dvh1`, AVC `dvav`/`dva1` and AV1 `dav1` families and the observed sample entry. The underlying ordinary codec identifier is stored separately, without using it to bypass the DV guard.
- The user-opened playback **settings inspector** can show source profile/level/compatibility details even when no playback session was created. No fullscreen implementation/status banner is added. English/Turkish fields and the previously untranslated DV rejection message are covered.
- Needed next implementation evidence: real generated/authorized-source passthrough, fallback and DV tone-map output comparisons on the selected runtime across profiles. The **actual user's DV file profile/base-layer metadata** can help later household acceptance; it is not a prerequisite to develop these general paths. Parser contract tests are not substituted for output proofs.

## Verification

- **24 Node** Home/catalog grouping, discovery/navigation, installed hls.js startup and localization checks passed before the source-analysis extension. **39 backend/native** source analysis, cache-refresh, playback and library tests passed after it; final frontend/worker/PWA build passed.
- New parser tests use clearly isolated ffprobe JSON contracts. Real HEVC HLS tests use locally generated ordinary media. No real TV/browser suite, user film modification or internet media acquisition was run.
- Broad Dolby Vision playback is **not complete**. Home recent query isolation and profile diagnostics are local changes; HLS startup, gamepad actual-API correction and radio-banner removal remain preserved in this uncommitted batch. No commit/push/deployment was made.

## References

Reviewed **2026-10-25**; official documents and installed APIs were consulted because Context7 quota was exhausted.

- [libVLC native framework and bindings](https://www.videolan.org/vlc/libvlc.html).
- [Jellyfin DV/HDR acceleration requirements](https://jellyfin.org/docs/general/post-install/transcoding/hardware-acceleration/) and [tone-map cost](https://jellyfin.org/docs/general/post-install/transcoding/).
- [jellyfin-ffmpeg v8.1.3-1 release](https://github.com/jellyfin/jellyfin-ffmpeg/releases/tag/v8.1.3-1).
- [FFmpeg DOVI configuration record](https://ffmpeg.org/doxygen/trunk/structAVDOVIDecoderConfigurationRecord.html).
- [dovi_tool release](https://github.com/quietvoid/dovi_tool/releases/tag/2.3.4), [pinned generator contract](https://github.com/quietvoid/dovi_tool/blob/614c816b6446dcd1dbaf433403d499a6026fbb5a/docs/generator.md), [MIT licence](https://github.com/quietvoid/dovi_tool/blob/614c816b6446dcd1dbaf433403d499a6026fbb5a/LICENSE).
