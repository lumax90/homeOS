# Automatic container updates — 0.44

Started **2026-10-23**. Requirement **OPS-10**, with **OPS-03/04/05/09 and GAM-19**: application code changes should reach an existing CasaOS server automatically after verification.

## Implementation in progress

- Application-code pushes to `main` build and verify a source-commit image. Only a verified build of the current `main` revision may move the **`stable`** channel; immutable source/version tags remain available. Documentation-only pushes do not trigger an application deployment.
- The CasaOS recipe uses `stable` and includes a scoped updater. Existing version-pinned installations need one initial Compose update, retaining their current data/media mounts and settings.
- Reuse the maintained **nicholas-fedor/watchtower 1.23.2** fork, **Apache-2.0**, pinned release commit `cd5946be205ea0a60dd31235a11f04ac9851ebf6` and Linux amd64 archive SHA-256 `e8429673b67a61ff00cd6891c781b744a39f479ada9d8bfddc89c324d11c7ae8`. The original containrrr repository is archived. The unmodified binary and licence are packaged in the Home image; the updater runs with its own entrypoint.
- The updater checks every **15 minutes**, selecting only the named Home application/updater containers carrying the Home scope and enable labels. The Docker socket is mounted only in the updater; the main application remains UID/GID **1654**, read-only root, and has no Docker socket.
- Before replacing Home, Watchtower executes Home's pre-update hook. A **private Unix socket** (directory 0700, socket 0600) communicates with the running process; there is no public control endpoint or embedded update credential. Temporary failure returns **75** to defer replacement.
- API admission and background-work claims are gated around preparation. Active games, playback/radio/TV/podcasts, transfers, DVR/current-near recordings and running background work defer an update. A consistent application backup must succeed before stopping the container. The existing three-minute shutdown and durable recovery paths remain the process lifecycle.
- The automatic-backup history is bounded to **three**; retention checks the backup manifest's explicit purpose so manually created backups are preserved. A failed preparation reopens admission. An eight-minute lease expiry recovers from a missing updater callback. Successful startup records the new source revision.
- Owners can pause/resume the application update policy in the updates screen. Image deployment and recovery use Docker; the existing signed executable updater remains disabled in containers.
- Native ROM/source/save data stays in persistent managed storage. Application backups cover the existing database/key/configuration scope; they are not a media-library backup. Candidate health/startup and data preservation are verified before promotion. Automatic database rollback is not implemented; a failed deployment retains the pre-update backup and can be recovered with the prior immutable image and the existing restore mechanism.
- Application-only images reuse the **immutable, previously verified native runtime** recorded in `deploy/native/runtime-lock.json`. The first fresh hosted rebuild changed frontend/PCSX binary hashes despite unchanged source pins, which would change existing hash-bound save/settings identities. That candidate was not promoted. The runtime lock preserves the original binaries and checks the native source inputs; changing emulator sources requires a separately verified compatible runtime release. The `native-proof` build target remains available for that work.

## Verification

- **132 focused backend checks passed, zero skipped**, including automatic-update guards, backup/restore, games, transfer budgets, radio/Live TV and playback ownership. Windows checks use the complete self-contained runtime pack: the local shared SDK runtime lacked assemblies required by existing HTTP tests. A historical migration test that merely lowered `user_version` was corrected to construct a genuine schema-39 database.
- Frontend/worker/PWA build passed; the final owner-control change also passed type checking. The updates screen supports pause/resume and identifies installations that still need the one-time Compose setup.
- Real Linux Docker verification passed **private socket permissions, active-game deferral, automatic backup/replacement, cookie-session and paused-transfer preservation, read-only source/data mount preservation, unrelated-container exclusion, idempotent rescans and updater self-replacement**. Game content was not executed; only its isolated server-side lease was opened. Evidence: `artifacts/auto-update-044/` and the isolated VM's per-tree reports.
- The bundled release has no `--version` flag; its verified archive and successful `--help` execution establish the selected binary input. Actual startup/update execution is covered by the Docker probe.
- The actual pinned CasaOS parser/output round trip preserves all three stable images, data/socket mounts, scope/hook labels, updater command, ports and permissions. Receipt: `artifacts/auto-update-044/casaos-import.json`.
- In progress: the first complete GitHub `GITHUB_TOKEN` publication/promotion. The owner confirmed granting this repository **Write** access under the GHCR package's **Manage Actions access** settings. The first candidate is verified on an implementation branch before `main` promotion.

No user ROM is launched and no internet media is acquired by verification. The integration probe uses an isolated local registry, private fixture database, an unexecuted server-side game lease, and a synthetic source-preservation file.

## References

Reviewed **2026-10-23** using Context7, installed types and pinned upstream source:

- [Watchtower 1.23.2 release](https://github.com/nicholas-fedor/watchtower/releases/tag/v1.23.2), [Apache-2.0 licence](https://github.com/nicholas-fedor/watchtower/blob/cd5946be205ea0a60dd31235a11f04ac9851ebf6/LICENSE.md)
- [Lifecycle hooks and exit code 75](https://watchtower.nickfedor.com/advanced-features/lifecycle-hooks/)
- [Container selection](https://watchtower.nickfedor.com/getting-started/container-selection/), [scoped instances](https://watchtower.nickfedor.com/advanced-features/running-multiple-instances/)
- [GitHub package workflow access](https://docs.github.com/en/packages/learn-github-packages/configuring-a-packages-access-control-and-visibility#ensuring-workflow-access-to-your-package)
- [actions/setup-dotnet v6.0.0](https://github.com/actions/setup-dotnet/releases/tag/v6.0.0), **MIT**, pinned commit `a98b56852c35b8e3190ac28c8c2271da59106c68`; .NET SDK 10.0.401 is used for CI admission/backup checks.
- [Compose deployment lifecycle](https://docs.docker.com/compose/how-tos/production/), HeroUI v3 Button/current installed React types

The official Distribution `registry:3` image is an **Apache-2.0 verification-only** dependency. It is not an application service in the CasaOS deployment.
