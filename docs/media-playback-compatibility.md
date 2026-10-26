# Broad media playback compatibility — MEDIA-COMPAT-01

Requirement clarified **2026-10-25**. Related requirements: **PLY-01..06, PLY-10, SUB-01/05, MUS-03, EXT-01, CLI-01/04, OPS-08/09**.

## Product objective

Home must target the broad, automatic media compatibility provided by **Jellyfin/Plex**, not merely repair one reported film or require the user to identify codecs before playback. The server analyses sources itself and combines that analysis with the actual client/transport capabilities. A manually supplied MediaInfo report can help diagnose a particular failure later; **it is not a prerequisite for implementing this compatibility programme**.

**Execution preference, 2026-10-25:** The user will try actual playback/games and report failures. Continue implementation with focused build checks instead of repeatedly running long backend/native/protocol suites or device benchmarks. Keep the existing test definitions and safety boundaries; do not make manual acceptance a prerequisite to develop the remaining paths. Publication authority and source/licence distribution gates remain separate.

The user selects media and presses Play. Home prefers original playback, then lossless container repackaging, then audio-only conversion, then necessary video conversion with correct colour/subtitle processing. A successful fallback must retain the selected language, subtitle timing and saved viewing position. Broad source compatibility is distinct from native output of every HDR or home-theatre audio feature on every browser.

**Current status: [first runtime/base-output slice implemented locally](media-runtime-implementation.md), not completed parity.** Existing Home direct/remux/audio/video, HDR10/HLG, subtitle and lifecycle implementations are retained. DV profile diagnostics alone do not satisfy the playback requirement; P8.1 HDR10 base-copy now has a distinct implemented path and original output tests. Full DV/HWA/audio/subtitle coverage and publication gates remain open.

## Mature implementation reuse

- Reuse **jellyfin-ffmpeg** as the preferred candidate decode/filter/encode runtime instead of implementing codec or Dolby Vision colour algorithms from scratch. Reviewed release **v8.1.3-1**, source commit **`253db2a7b0a8045c54ce68ce33d7f601229b1822`**; package pin is recorded in [the DV plan](dolby-vision-playback-plan.md). Its GPL build and separately licensed bundled components require retained notices and corresponding-source availability before distribution.
- Merely installing that executable does not integrate its capabilities. Home must supply the appropriate source/client selection policy, filters, hardware-device setup, subtitle handling, authentication and bounded session lifecycle. The existing DV guard cannot be removed without working replacement paths.
- Jellyfin's codec/acceleration documentation and observable policy are references. No Jellyfin server, duplicate catalogue/database, background media-manager service, or copied Jellyfin application code is adopted here. Any later code reuse needs a specific component/source/licence review. Plex is a behavioural/acceptance reference, not a redistributable codec implementation.
- Retain the existing Video.js/hls.js controls/delivery where appropriate. Changing the player skin or embedding a native VLC API does not automatically add codecs, Dolby Vision rendering or HDMI passthrough to HTML playback.

## Coverage and evidence programme

These are **test targets**, not a claim that every listed combination is currently supported. Track original output and compatible fallback separately.

| Area | Target coverage | Required evidence / present gap |
|---|---|---|
| Source/container | MP4/M4V/MOV, MKV/WebM, AVI, MPEG/TS/M2TS/VOB, ASF/WMV/3GP; common standalone audio containers | Scanning, constrained local demux and playback must agree; audit extension/MIME/format allowlists, not just FFmpeg's advertised demuxers. Do not broaden network/protocol access. |
| SDR video | AVC/H.264 including High10, HEVC/Main10, AV1, VP8/VP9, MPEG-2/MPEG-4 and legacy WMV/VC-1 | Original where supported; verified compatible output otherwise. Real decoded output, seek, aspect ratio, frame rate and cancellation. Codec presence in `-decoders` alone is insufficient. |
| HDR/DV | HDR10/HDR10+, HLG; DV P5, P7 and P8 with their actual layer/compatibility variants | Verified metadata-preserving delivery where possible, compatible base-layer fallback where valid, and DV-aware SDR tone mapping otherwise. P7 enhancement-layer reconstruction is not implied by base-layer playback. Current Home DV fallback/conversion is open. |
| Audio | AAC/HE-AAC, AC-3/E-AC-3, DTS/DTS-HD, TrueHD, FLAC/ALAC, MP3/MP2, Opus/Vorbis and PCM | Compatible source audio is preserved; unsupported audio gets an explicit compatible output/downmix without unnecessarily re-encoding video. Language/default selection, A/V sync and channel mapping need output tests. TrueHD/Atmos and DTS:X bitstream/object passthrough are separate client/transport features, not an AAC downmix claim. |
| Subtitles | Embedded/sidecar SRT, WebVTT, ASS/SSA, mov_text; PGS/DVD/DVB bitmap subtitles | Text extraction/rendering where possible, necessary styled/bitmap burning otherwise. Fonts, forced/HI selection, offset, seek and HDR/DV filter ordering must be verified together. |
| Acceleration | Intel QSV, Intel/AMD VA-API on Linux, NVIDIA decode/encode; appropriate software fallback | Probe actual supported operations/device access. Current Home proves NVENC encoding with CPU decode/filter only; it does not implement the full Jellyfin hardware ladder. Never infer Main10/DV acceleration from a CPU/GPU name or a listed encoder. |
| Delivery/lifecycle | Source range, remux/audio-only/video HLS, track/quality changes, pause, seek, reconnect, bounded cache and cleanup | Authenticated account-owned sessions, cancellation, no stale response takeover, source read-only, resume/exit/restart preservation. Reuse the existing protocol tests and extend them to the new runtime. |
| Music/Live TV | Apply suitable compatibility policy to real music/radio/live streams without replacing their integrated Home lifecycle | The movie runtime change must not silently alter games, live leases, radio visual/audio ownership, or user settings/saves. |

## Ordered implementation slices

1. **MEDIA-RUNTIME-01:** verify the pinned Jellyfin-FFmpeg package, notices/source/build configuration, representative real output and compatibility with Home's existing protocol arguments. Keep the canonical gaming/runtime identities intact. Production default replacement remains gated by that evidence.
2. **MEDIA-DV-02:** implement and verify DV-preserving repackaging, valid compatible-base fallback, and P5/P8 DV-aware SDR paths; the current diagnostic-only analysis is not the finish line. Use generated/authorised isolated sources without development internet-media acquisition. Do not wait for the household film's manual report to begin this work.
3. **MEDIA-AUDIO-SUB-02:** broaden negotiated audio output/downmix and complete source/subtitle interactions, especially HEVC/HDR/DV plus TrueHD/DTS and bitmap/ASS subtitles.
4. **MEDIA-HWA-02:** integrate actual Intel/AMD/NVIDIA device/driver pipelines with tested fallback and cancellation. Benchmark separately from codec coverage; preparation/lower quality on slow hosts is not yet an implemented workflow.
5. **ACCEPT-MEDIA-COMPAT-01:** output/colour/sync/seek/preservation matrix plus user-owned PC/TV/phone sustained-use acceptance. Do not label Jellyfin/Plex parity complete while any required path lacks implementation or evidence.

Full-quality playback on a native client and compatible playback in a browser are different acceptance tracks. Web/PWA remains the stable first client target; native client targets are not cancelled or treated as implemented. The server must not require a separate player simply to get an otherwise supported compatible stream.

## References and verification

Reviewed **2026-10-25**. Official documentation was consulted after Context7 reported its quota exhausted; these references are not copied documentation/data incorporated into Home.

- [Jellyfin codec/client compatibility](https://jellyfin.org/docs/general/clients/codec-support/): direct playback goal, source-specific fallback and client-dependent HDR/audio/subtitle support.
- [Jellyfin transcoding](https://jellyfin.org/docs/general/post-install/transcoding/): ordered direct/remux/audio/video paths and HDR software cost.
- [Jellyfin hardware acceleration](https://jellyfin.org/docs/general/post-install/transcoding/hardware-acceleration/): modified FFmpeg runtime, full vs partial acceleration, documented P5/P8 tone mapping.
- [Pinned Jellyfin-FFmpeg source/release](https://github.com/jellyfin/jellyfin-ffmpeg/tree/253db2a7b0a8045c54ce68ce33d7f601229b1822), [upstream licence/build conditions](https://github.com/jellyfin/jellyfin-ffmpeg/blob/253db2a7b0a8045c54ce68ce33d7f601229b1822/LICENSE.md).
- [Plex automatic direct/stream/transcode behaviour](https://support.plex.tv/articles/200250387-streaming-media-direct-play-and-direct-stream/) and [HDR tone-map requirements/limitations](https://support.plex.tv/articles/hdr-to-sdr-tone-mapping/).

The scope clarification itself changed documentation only. The subsequently authorised first runtime/base-output implementation is tracked separately in [MEDIA-RUNTIME-01](media-runtime-implementation.md); it is local/unpublished, does not prove full DV rendering/GPU support, and does not touch household media/data. Existing local Home/HLS/controller/radio fixes remain preserved.
