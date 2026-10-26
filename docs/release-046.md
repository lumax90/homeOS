# Home 0.46.0 release batch

Published and anonymously verified **2026-10-26**, with explicit user approval to commit, push and publish all accumulated changes together. Schema **44**. **0.46.0 is public on GHCR and promoted to `stable`.** Household/browser/device acceptance belongs to the user; publication does not prove that the household server has updated.

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

Source qualification, frontend/worker/PWA/backend compilation, the focused new-data subset, staged privacy review and hosted publication completed.

- Application change commit **`9c2f06bbfa610448798af874110552a141de4321`**; client-dependency workflow correction/publication source **`e2a37eb2b56490057893ab225d7d06e00a5e13e3`**. Both pushed to `main`.
- Workflow [40302109156](https://github.com/lumax90/homeOS/actions/runs/40302109156) **succeeded**, publish job **18m50s**. The obsolete first run was deliberately cancelled after the workflow correction.
- Hosted gates passed **176 backend test executions** plus **61 source-built-runtime native checks**, zero failed/skipped; **45 Node** guards, client/image builds, source archive validation, installation/recreation, movie/live range/access/recovery, automatic-update backup/busy gates and native binary identity checks. The long 21-session Selkies comparison was not rerun.
- Anonymous `stable`, `0.46.0` and `sha-e2a37eb2b56490057893ab225d7d06e00a5e13e3` all resolve to **`sha256:3a296aaa53a9abdbb849a0af6b219d7e770588945349d5fa9afc8dfea3b6c170`**, Linux amd64, version **0.46.0**, source **e2a37eb**. Configured media paths point to the source-built `MediaRuntime/bin` executables.
- Immutable **0.45.0** remains **`sha256:89eafd568b5b96c5d43880e14789cae2427e650c7ddd213b373759f5f3e79449`**. Native runtime/core/save identities remain locked.
- **93 reviewed release files**, staged Gitleaks zero findings. Raw source/build/hosted receipts remain ignored under `artifacts/release-046/`; local verification VM is shut down.
- Existing updater installations tracking `stable` can receive this image through the idle/backup flow. Version-pinned installations require an explicit image-version change/update. No household mount, database or server was directly changed by this publication.

## Remaining scope

Other DV profiles/original-DV remux/true DV tone mapping, full hardware pipelines and wider audio/subtitle output combinations remain open. Real CasaOS TV/PC/radio/controller/latency behaviour remains user-owned acceptance after the update. This release is not a whole-product completion claim.
