# Playback startup follow-up — 2026-10-25

Requirements **PLY-02/03/04/06, CLI-01-A, CAT-01/03**. Status: **local source correction and focused checks; not published**. User supplied TV screenshots and then confirmed the same `Illegal hls.js config` error in the PC browser when opening a downloaded title.

## HLS startup configuration — HLS-STARTUP-01

- The exact reported error is `Illegal hls.js config: "liveMaxLatencyDurationCount" must be greater than "liveSyncDurationCount"`.
- Home's movie player supplied `liveMaxLatencyDurationCount: Infinity` without explicitly supplying `liveSyncDurationCount`. In installed **hls.js 1.7.3**, `mergeConfig` rejects a supplied maximum when the corresponding user-config minimum is undefined. The library's default count of three does not satisfy this pre-merge check.
- The same original options reproduce the failure in the real installed constructor. `web/src/movieHlsConfig.ts` now supplies **both count values**, preserving Home's demand-paced movie policy, start/keyframe offset, memory limits, unchanged-playlist tolerance and no-live-catch-up setting. Seconds/count configuration is not mixed.
- `usePlayback` uses this tested factory. `tests/unit/movie-hls-config.test.ts` reproduces the old constructor error, creates the corrected real Hls instance, checks its merged options and destroys it without any media fetch/browser session. The test is part of the npm playback checks and the future container-publication workflow.
- This bug occurs **before decoding/media playback**, is not specific to a TV model, and is not resolved by HTTPS or selecting lower quality. The separate TV message about an undecodable stream may need additional source/codec diagnosis after this startup error is removed.

## Dolby Vision — DV-PLAYBACK-REVIEW-01

- The other reported message, `Dolby Vision dönüştürme bu profilde doğrulanmadı. Uyumlu özgün sürüm seç.`, comes from Home's planner, not from hls.js or a measured hardware failure.
- Current Home can choose original-file direct playback when the source-specific browser capability report allows it. A browser/media device supporting some Dolby Vision formats is not proof that the source's container/profile/audio combination is playable there.
- The managed HLS remux path has not verified Dolby Vision metadata preservation. If a file cannot remain direct, Home's current planner refuses Dolby Vision conversion even when ordinary HDR10/HLG tone mapping is available. This guard was **not** removed by the HLS startup patch.
- Dolby Vision is not interchangeable with HDR10. Profile and base-layer compatibility matter. FFmpeg's DOVI record exposes `dv_profile`, `dv_level`, base/enhancement/RPU flags and `dv_bl_signal_compatibility_id`; the user's particular file's values have not been provided. Neither HDR10 base-layer fallback nor DV-to-SDR conversion is represented as implemented or validated by this follow-up.
- Jellyfin documents DV P5/P8 tone mapping with **jellyfin-ffmpeg** and supported acceleration. That is a specific validated stack, not proof that Home's generic FFmpeg/software path on this host can safely reproduce it. No codec/runtime replacement, GPU enablement or new dataset was adopted.
- Added regressions confirm exact DV codec-ID probing for original direct delivery and continued rejection of unverified remux/conversion. These are parser/planner contracts, **not a real Dolby Vision playback benchmark**.

## Library report corrected

- User clarified that the manually copied films **are visible in Library**; the issue is their absence from the page they call the main/home page. This is not a missing-file/scan acceptance failure; no further scan or source-file movement is required just to prove discovery.
- `/home` uses the local catalog and its first six grouped items for `Yeni eklenenler`. `/discover` (the default landing section for discovery-enabled accounts) uses provider and personal-content shelves; unmatched local files are not automatically provider entries. Which exact page/shelf the user means is not established by the supplied playback screenshots.
- Homepage/landing visibility is tracked separately; no fictitious provider identity, download provenance, metadata or arbitrary library-default change was added to make a file appear there.

## Verification and boundaries

- **66 Node tests**, including real hls.js constructor checks, playback capabilities/transport, controller and localization contracts, passed.
- **32 backend/native/library tests**, including real generated HEVC HLS output and the new DV planner boundary, passed. Frontend/worker/PWA build and actionlint passed.
- Existing uncommitted controller actual-API probing and radio-preparation banner removal were preserved. No commit/push, household update, real-browser/device automation or internet media acquisition was performed.
- Actual TV/PC film decode after deployment, radio startup and Dolby Vision profile-specific playback remain open user/device acceptance.

Sources reviewed **2026-10-25**: installed `node_modules/hls.js/src/config.ts` (`mergeConfig`, lines 717–750), [hls.js configuration API](https://hlsjs.video-dev.org/api-docs/hls.js.hlsconfig), [FFmpeg DOVI record](https://ffmpeg.org/doxygen/trunk/structAVDOVIDecoderConfigurationRecord.html), [Jellyfin tone mapping](https://jellyfin.org/docs/general/post-install/transcoding/), [Jellyfin DV acceleration requirements](https://jellyfin.org/docs/general/post-install/transcoding/hardware-acceleration/). Context7 quota was exhausted; official documentation and installed source/types were used. External documentation is referenced, not copied into Home.
