# Mature media runtime integration — MEDIA-RUNTIME-01 / MEDIA-DV-02-A

Date **2026-10-25**. Requirements **PLY-01/02/03/05/06/10, CAT-01/03, SUB-01/05, EXT-01, MUS-03, OPS-08/09**. User authorised implementation of [broad Jellyfin/Plex-style media compatibility](media-playback-compatibility.md), not a per-film manual diagnostic dependency.

**2026-10-26 publication qualification:** [0.46.0](release-046.md) uses the same pinned FFmpeg core **built from source** with an explicit Home/Ubuntu shared-library profile. This avoids publishing the initial vendor bundle before its external-source qualification. Exact linked-library source packages/notices and the build recipe are retained in the image. The Linux executable path becomes `/app/MediaRuntime/bin/ffmpeg`; earlier official-package tests below remain historical evidence, not a claim of identical final binary/configuration. Source completion/publication is recorded separately in the release note.

## Implemented locally

- **Jellyfin-FFmpeg 8.1.3-1** is the container's configured media executable/probe, under `/usr/lib/jellyfin-ffmpeg`. Official Noble amd64 software and its source/build-recipe archive are size/SHA-256 pinned in `deploy/media/runtime.json`; official Windows x64 software is pinned for isolated development tests. No Jellyfin server/catalogue/UI or internet media is downloaded.
- `stage-media-runtime.mjs` refuses HTTP failure, oversized/truncated/wrong-digest payloads and existing destinations. The install script verifies the actual `8.1.3-Jellyfin` executable, rejects `--enable-nonfree`, retains licence/copyright and codec/filter inventories. **Inventories alone are not playback proof.**
- The image builds a separate **media-tests** stage against the actual pinned Linux runtime and retains its test proof. Existing app authentication, bounded HLS producer/cache, account/track binding, cancellation, save/progress and read-only source boundaries remain in Home.
- The system FFmpeg stays separately installed for existing native-runtime/system dependencies and diagnostic tooling. Native/Selkies frontend/core/image pins, save namespaces and controller ownership are not rewritten; game core/host identity is separately checked against the existing lock.
- MPEG/MPG/VOB, M2TS/MTS, WMV/ASF, 3GP/3G2, AAC and WMA extensions/MIMEs are accepted consistently by scanning/catalogue/client policy. The constrained **local** demux allowlist adds only `asf` and `aac`; it is not changed to arbitrary protocols/formats. Actual generated MPEG-2/MP2, WMV2/WMAv2, MPEG-TS, 3GP H.264/AAC, ADTS AAC and WMA outputs are tested through the manager. This is not a proof of every codec those containers can carry.
- Analysis revision **3** refreshes old cached probe results, including prior revision 2, on next inspection. This is not a database migration or media/progress identity reset.

## First implemented Dolby Vision fallback

**MEDIA-DV-02-A:** single-layer **HEVC DV P8.1 with confirmed HDR10-compatible base** (`profile=8`, compatibility ID `1`, BL/RPU present, EL absent, BT.2020/PQ). Home now probes the **ordinary base HEVC identifier separately** and reports distinct `BaseOriginal`/`BaseVideoCopy` support; it does not pretend that an HDR10-capable browser supports original DV.

If original direct playback is supported it still wins. Otherwise, when the exact base output is supported and the runtime actually exposes `hevc_metadata=remove_dovi`, Home can retain the HDR10 base video through fMP4 HLS and remove DV RPU/configuration. Unsupported TrueHD is converted to AAC stereo; supported audio can remain copied. The actual mode/reasons explicitly identify this as **HDR10 base-layer fallback, not full Dolby Vision**.

Quality/bitrate limits, subtitle burning and changed source/track binding still apply. P5, P7/EL, P8.4/HLG, unknown/missing flags, unsupported base decoding and missing strip-filter support **do not borrow this path**. The DV rejection remains for paths not yet implemented/verified; no blanket guard deletion.

## Original DV fixture and output evidence

`DolbyVisionFixture` creates an isolated HEVC Main10 PQ/BT.2020 video using FFmpeg, inserts an original constant P8.1 RPU generated locally with **MIT dovi_tool 2.3.4**, and supplies the corresponding MP4 DOVI record. It is clearly test-only and is never placed in the normal Home catalogue. No provider film, user film or internet media fixture is acquired.

Tests check **real decoded frame RPU data**, not just synthetic ffprobe JSON. The HLS output must have no DV configuration or frame RPU metadata, preserve Main10/PQ/BT.2020/dimensions, and have the **identical decoded base-video SHA-256**. Two real cases cover **TrueHD 5.1 → AAC stereo** and **AAC stereo copied**, alongside source-byte preservation, account denial and process/cache cleanup. The test generator's TrueHD encoder does not support 7.1; 7.1 planning contracts are not represented as native decode/output evidence. This proves base-copy integrity on these original fixtures; it is **not a P5 colour-rendering or P7 enhancement-layer reconstruction proof**.

## Publication and remaining work

- Source/build recipes and package licence are retained. The packaged external libraries/drivers require complete corresponding-source/notices validation before public distribution; `distribution.correspondingSourceVerified` is deliberately **false** and the publication workflow fails closed **before uploading/promoting an image** while that gate is open. An upstream source archive alone is not represented as the complete corresponding source of every bundled component.
- No commit, push, household update or public image publication was authorised/performed in this slice. Prior Home/HLS/controller/radio corrections remain in the uncommitted batch. CasaOS stable remains unchanged.
- **MEDIA-DV-02-B/C:** original DV-preserving remux, DV-aware P5/P8 SDR tone mapping and further base modes remain open.
- **MEDIA-AUDIO-SUB-02:** negotiated additional audio outputs/channel layouts and full HDR/DV + styled/bitmap subtitle interactions remain open.
- **MEDIA-HWA-02:** actual Intel QSV/Intel-AMD VAAPI/NVIDIA decoder-filter-encoder pipelines remain open; a packaged driver/encoder is not a working HWA claim. Slow-host preparation and native passthrough/client targets remain separate.
- Full Jellyfin/Plex compatibility and user-owned PC/TV/phone sustained-use acceptance are **not complete**.

## Verification

Verification is tracked in ignored `artifacts/media-runtime-01/`:

- **117 Windows backend/native** tests passed against the actual pinned Windows runtime: 109 media/lifecycle/radio/live/library/Selkies checks plus 8 native-adapter checks run in isolation.
- **61 Linux backend/native** tests passed, **0 skipped**, inside the image's pinned-runtime media-tests stage. These include the original DV RPU/base-pixel and actual TrueHD **5.1** output checks, legacy/audio sources, cache revision, manager lifecycle/backpressure and source preservation.
- The final Linux image passed **5 runtime/identity checks**, **7 movie HTTP checks** and **8 live HTTP checks**: configured runtime, retained notices/native proof, unchanged game binary identities, actual 4K/Main10/HDR preservation, audio-only conversion, ranges/authentication, producer suspend/resume/close, temporary-origin recovery, revocation, source bytes and login preservation.
- **80 Node** capability/startup/controller/live/radio/localisation/software-integrity checks and frontend/worker/PWA build passed; actionlint, diff formatting and local documentation references passed.
- An initial test-generation attempt requested TrueHD 7.1, which the installed **encoder** does not support; the original fixture uses its explicitly supported **5.1(side)** layout instead. No 7.1 native decode/output proof is claimed. One native UDP startup test timed out in the initial expanded batch; all 8 adapter tests subsequently passed in isolation without a gaming-code change. Its transient cause was not established and is not labelled a newly fixed gaming issue.
- Only root C# test sources/project are newly allowed through Docker context; test outputs, fixtures, state, keys and operational evidence are not broadly admitted. The build-only test stage is not a new runtime service.
- **3 Linux installation/preservation checks** passed: completed automatic initializer/healthy non-root app, recreation with identical data bytes, and fail-closed handling of foreign-owned existing data without overwriting it.
- No browser suite, screenshot acceptance, household media modification or internet-media acquisition was run. Completed protocol/build/install receipts were retained locally; fixture containers were cleaned up and the diagnostic VM was shut down. No background verification remains.

## Sources

Reviewed **2026-10-25**. Context7 quota was exhausted; official upstream source/APIs and actual installed executable help were used.

- [Pinned Jellyfin-FFmpeg release](https://github.com/jellyfin/jellyfin-ffmpeg/releases/tag/v8.1.3-1), [source/build recipes](https://github.com/jellyfin/jellyfin-ffmpeg/tree/253db2a7b0a8045c54ce68ce33d7f601229b1822), [licensing conditions](https://github.com/jellyfin/jellyfin-ffmpeg/blob/253db2a7b0a8045c54ce68ce33d7f601229b1822/LICENSE.md).
- [Jellyfin client/codec policy](https://jellyfin.org/docs/general/clients/codec-support/) and [hardware/DV processing requirements](https://jellyfin.org/docs/general/post-install/transcoding/hardware-acceleration/).
- [Original MIT RPU generator](https://github.com/quietvoid/dovi_tool/blob/614c816b6446dcd1dbaf433403d499a6026fbb5a/docs/generator.md). Its generator code/runtime is not bundled in Home.
- [FFmpeg bitstream filters](https://ffmpeg.org/ffmpeg-bitstream-filters.html), actual Jellyfin-FFmpeg `-h bsf=hevc_metadata`, `-h bsf=dovi_rpu`, `-h filter=libplacebo` output. Presence of `apply_dolbyvision` in help is not counted as an implemented/verified Home tone-mapper.
