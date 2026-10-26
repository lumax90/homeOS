# Managed updates with owner approval

Date **2026-10-26**. Slice **UPDATE-CONSENT-01**; requirements **OPS-10, OPS-03/04/05/09, CLI-01/05, GAM-19**. User asked to configure updates after distinguishing the updater shipped in source from the absent helper in their existing CasaOS installation.

**0.47.0 release preparation authorised; publication qualification in progress.** Public `stable` remains **0.46.0** until the new image passes promotion. Do not apply the revised recipe against that older image: it does not contain the new approval bridge. One initial deployment of the qualifying 0.47 image is required; it must preserve the existing Compose data/media mounts, ports, environment and settings. [Release record](release-047.md).

## User-facing policy

- Default: **automatically check, require owner approval to download/install**. Legacy `{enabled:true}` preferences do not imply consent to automatic installation. Explicitly paused checks remain paused.
- Metadata check at startup/every 15 minutes, plus owner **Check for updates**. No layers are downloaded merely to detect a release.
- Small owner-only header control opens existing maintenance settings. Release version, source changes, last check and an approximate compressed-layer download size are displayed.
- **Download and update** opens the existing accessible/focus-managed dialog. Confirmation binds an immutable Linux amd64 manifest digest, not the moving `stable` tag. Approval survives process restart; another channel release cannot overwrite it. Cancellation revokes installation consent, including after a download; it does not delete media or cached Docker layers.
- Automatic idle installation is a separate, explicit opt-in with its own confirmation. It may be turned off without disabling metadata checks.
- During download Home remains running. Installation waits for active game/playback/transfer/recording/job activity to finish. The existing request/background admission gate and verified application backup run immediately before replacement. No forced interruption option is provided.

## Components and security boundary

`ContainerReleaseSource` reads fixed HTTPS GHCR token/manifest/config endpoints. Metadata is capped at 1 MiB/request, platform recursion at one index, layer counts/sizes are bounded and source/version/platform/digests checked. It never fetches layer payloads. Transient errors retain the last known candidate and the running server. Size estimates compare the running image's recorded digest against candidate compressed blobs; other local cache entries may make actual transfers smaller. This is Docker layer reuse, **not a promised 30–40 MiB binary-delta patch**.

Owner routes stay under `/operations`' existing admin authorization and global session/CSRF boundary. Availability/approval records are atomically written under the existing data directory; no database migration or media/save namespace change is required. Corrupt records are retained and fail closed rather than being reset.

The existing `home-updater` service becomes a small standard-library Python bridge. It is the **only service with the Docker control socket**. No update control HTTP port or shared credential is added. It executes a bounded, argument-array CLI in Home as UID/GID 1654; that CLI communicates with the existing private 0700-directory/0600 Unix control socket. The bridge reports a heartbeat, including during downloads, so the UI distinguishes an environment flag from an actually connected helper. Revoking consent aborts the pull on subsequent progress and forbids replacement; an explicitly cancelled automatic candidate is not immediately reapproved.

The bridge pulls only the approved repository/digest, validates its actual Docker image identity, aliases that verified cached image to local `stable`, and invokes the **unmodified pinned Watchtower 1.23.2** with `--run-once --no-pull` and an explicit Home name/scope. Watchtower retains the mature configuration/mount/network-preserving replacement behaviour. Remote `stable` movement cannot change the selected release. A Docker administrator must not perform concurrent local retag/recreation operations during this managed installation; Docker-socket access is itself administrative authority, not a read-only API despite the bind's `read_only` attribute.

The completed initializer and unrelated containers are not replaced. The bridge does not request image/volume cleanup. In this slice the helper **does not self-update**; protocol 1 remains usable across ordinary Home application updates. Helper upgrades require a separate compatible recipe deployment. Historical Watchtower self-update regression coverage is retained as component evidence, not a claim that the new bridge self-updates.

## Failure/recovery

Approval alone cannot authorize a stop: the helper must report the matching digest as downloaded/ready and the pre-update backup must succeed. Busy/error conditions retain the running process or defer it. Expired preparation reopens admission and requires fresh approval. Successful startup must match the approved source revision; the bridge additionally checks actual Docker image ID and health. Application backup history remains bounded to three automatic backups; manual backups remain untouched.

**Automatic database rollback remains unimplemented.** If a new version fails after migrating data, do not blindly start an older image against the newer database. Retain the candidate/backup and use the existing controlled restore/recovery flow. A health failure is not represented as a verified rollback.

## Verification/status

- Focused approval/default-policy/restart/cancellation/metadata integrity plus existing backup/admission subset: **21 backend tests passed**, zero failed/skipped. Registry fixtures are isolated; no software/media payload is acquired by these tests.
- Bridge contracts: **8 isolated Python tests passed**, covering no-consent/no-pull, paused/busy behaviour, exact digest/no-pull invocation, wrong source/scope/mount denial and bounded Docker exec framing. No live Docker daemon is used by these contracts.
- Client approval/phase helpers: **3 Node tests passed**. Final frontend/worker/PWA/localization build passed. Final backend/regression compilation: **zero warnings/errors**. Workflow syntax, Python integration-definition parsing and source Gitleaks/diff checks passed; zero secret findings.
- Existing real Docker regression definitions now exercise the new bridge for no-consent/no-replacement and approved-digest busy/backup/replacement cases, using an isolated local-registry fixture override unavailable in production. Legacy Watchtower self-update coverage remains separate component evidence. These definitions were parsed, **not rerun locally**. Real Linux bridge/Compose/interrupted-helper and household browser acceptance remain **publication/deployment gates**, not claimed completed by unit tests.
- No browser suite, game benchmark, Linux VM restart, household modification, or internet media download was performed for this slice.
- Image-layer splitting/reproducibility improvements for smaller ordinary releases are separate remaining work. The 0.45→0.46 registry descriptors showed approximately **428 MB new /306 MB reused** compressed layers; that observation does not guarantee the next release's transfer size.

## References

Reviewed **2026-10-26**. Context7 was requested but its quota was exhausted; official documentation, pinned source and installed HeroUI types were used.

- [Docker image pulls and layer reuse](https://docs.docker.com/reference/cli/docker/image/pull/)
- [Docker Compose replacement preserves mounted volumes](https://docs.docker.com/reference/cli/docker/compose/up/)
- [Docker Engine API 1.45](https://docs.docker.com/reference/api/engine/version/v1.45/)
- [Pinned Watchtower lifecycle/source](https://github.com/nicholas-fedor/watchtower/tree/cd5946be205ea0a60dd31235a11f04ac9851ebf6), **Apache-2.0**, unmodified executable; existing dependency pin retained.
- Installed HeroUI v3 `modal.d.ts` and the existing `Dialog` wrapper; no new UI/deployment dependency adopted. The bridge uses system Python already in the image and retains its distribution notices.
