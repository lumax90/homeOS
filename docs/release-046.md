# Home 0.46.0 release batch

Prepared **2026-10-26**, with explicit user approval to commit, push and publish all accumulated changes together. Schema **44**. **Preparation in progress; not yet published.** Household/browser/device acceptance belongs to the user.

## Included changes

- [HLS-STARTUP-01](playback-hls-followup.md): movie hls.js constructor configuration correction.
- [SELKIES-SIGNAL-02](selkies-signalling-followup.md): released browser trailing-slash signalling and plain HTTP reconnect probe.
- [LIBRARY-SERIES-01 / IMPORT-NAME-01](library-series-readable-imports.md): pre-pagination series grouping, local season/episode page, readable new import directories and retained legacy paths/progress/paused state.
- [DETAIL-MEDIA-01](detail-media-cast-trailers.md): one initial credits row, expandable credits/gallery and explicit-click YouTube trailers within Home.
- [MEDIA-RUNTIME-01 / MEDIA-DV-02-A](media-runtime-implementation.md): pinned Jellyfin-FFmpeg core, broader local input formats, P8.1 HDR10 base preservation and unsupported-audio conversion. This is not full DV/HWA/parity.
- [LAN-GAMEPAD-02](lan-gamepad-manual-library.md), Home recent-query isolation and radio preparation-banner removal.

## Media component source distribution

The initially evaluated official Noble FFmpeg package carries additional vendor libraries/drivers whose complete source/notices record was not ready for Home publication. The user chose to include the media update, not defer it. The release therefore builds the **same pinned Jellyfin-FFmpeg 8.1.3-1 source** with an explicit Home build profile and ordinary Ubuntu shared codec libraries, instead of distributing that unqualified vendor bundle.

The FFmpeg source archive, its **pinned upstream Jellyfin Debian patch series**, build recipe, exact dynamically linked Ubuntu package versions/copyright notices, and signed Ubuntu source packages for non-system dependencies are retained inside `MediaRuntime/licenses/corresponding-source.tar.gz`. The upstream patch series is applied just as in the published Jellyfin build; it includes the required `remove_dovi` bitstream filter. libc/compiler runtime is identified separately as a system component. Autodetection is disabled; absent vendor hardware pipelines are not advertised as supported. Native-game frontend/core/source pins and save namespaces remain unchanged.

Source qualification completed **2026-10-26**: the pinned core built successfully, **37 linked source-package identities** were recorded (glibc/compiler runtime identified separately), and the complete source archive's hashes plus every non-system package's exact source/version descriptor passed `verify-source.sh`. The final image repeats that verification before publication. Original official binary pins remain development reference/Windows test descriptors, not the released Linux media binary identity.

## Release verification / preservation

- The new catalogue/import/schema/trailer parsing boundary subset passed **8 focused backend tests**, zero failed/skipped. These isolated tests protect data and path behaviour, not device playback acceptance.
- Previously recorded **117 Windows / 61 Linux / 80 Node** and protocol receipts qualify the earlier media slice, not every final release change. Final source-built runtime/build checks are reported separately.
- No automated browser suite or repeated multi-system benchmark is requested. The long 21-session Selkies comparison is now an explicit opt-in workflow dispatch, not automatic on every release push. Mandatory short data/authentication/build/publication gates remain.
- Existing media/ROM source bytes, user metadata/artwork, library paths, checkpoints/save namespaces, navigation state and paused transfers must not be reset. Schema44 is additive and keeps legacy GUID import paths; automatic upgrades remain backup/idle guarded.
- No internet media acquisition is used for development verification. Software/source archives are component acquisition; generated diagnostic media remains isolated.

## Publication

Source qualification, frontend/worker/PWA/backend compilation, the focused new-data subset and staged privacy review completed. Source commit/push and successful hosted publication are next. Planned public images: `ghcr.io/lumax90/homeos:0.46.0`, `:stable`, immutable `:sha-{application-commit}`. Do not overwrite older version images.

## Remaining scope

Other DV profiles/original-DV remux/true DV tone mapping, full hardware pipelines and wider audio/subtitle output combinations remain open. Real CasaOS TV/PC/radio/controller/latency behaviour remains user-owned acceptance after the update. This release is not a whole-product completion claim.
