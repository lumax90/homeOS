# Home 0.47.0 — owner-approved container updates

Published and anonymously verified **2026-10-26**, with explicit user approval to commit, push and publish the update-settings slice. Schema **44** is unchanged. **0.47.0 is public on GHCR and promoted to `stable`.** Household installation and device acceptance remain separate.

## Included changes

[UPDATE-CONSENT-01](managed-updates.md), OPS-10/03/04/05/09, CLI-01/05, GAM-19:

- Background release-metadata checking and an owner-only update indicator.
- **Download and update → Confirm**; exact immutable digest approval, cancellation and restart recovery.
- Separate **automatic installation when idle**, default **off**. Legacy enabled-check preferences do not imply unattended installation consent.
- Connected-helper heartbeat, release/source information and approximate new-layer transfer size.
- A scoped helper pulls only the approved image and invokes the pinned, unmodified Watchtower with `--run-once --no-pull`; existing mounts/configuration and backup/busy guards remain.
- Cancelled automatic candidates are not immediately reapproved; installation cannot start merely because a new release exists.

## Preservation and limits

The application schema, native/core/save identities, data/media mounts and paused-transfer intent are unchanged by this slice. Direct upgrading from 0.45 still includes the additive schema43→44 migration delivered in 0.46; the same data path must be retained.

Ordinary future updates use Home's approval screen after a **one-time existing-CasaOS configuration transition**. Installing only an image tag does not add the helper. The revised helper cannot be run from the older 0.46 image. Do not replace an existing app with a fresh empty data directory or discard its media mappings/custom settings.

Docker reuses unchanged image layers; this is not a file-level delta-package implementation or a promise of 30–40 MB per update. The helper does not self-update in this slice. Automatic database rollback is not implemented; failures retain application backups and require controlled recovery if schema has changed.

## Verification/publication

Previous local slice checks: **21 backend, 8 Python bridge, 3 Node** passed; client/worker/PWA/localization and backend compilation passed. Device testing belongs to the user. New real Docker approval/backup/replacement checks are required by the hosted publication workflow before stable promotion; no browser automation or multi-system gaming benchmark is repeated.

- Application implementation commit **`b7cb8e1cdbf044646096caabd9872eafae5ab9e0`**; corrected and published source **`1e50abf9dc1fe9c5de7c7dd7a6a75e5e7feb3518`**. Both pushed to `main`. The English README/documentation commits do not change the qualified application snapshot.
- Workflow [40335777351](https://github.com/lumax90/homeOS/actions/runs/40335777351) **succeeded in 8m6s**. Hosted checks passed **187 backend test executions, 62 pinned-runtime native checks, 48 Node guards and 8 isolated Python bridge checks**, zero failed/skipped, plus builds/source validation, installation/recreation and actual movie/live HTTP checks.
- Actual Docker proof passed **nine checks**: private socket, no-consent/no-replacement, active-game deferral, approved digest/backup/replacement, same login and paused transfer, same data/read-only source mounts, unrelated-container exclusion, idempotence and separate legacy Watchtower self-replacement. This is not a claim that the new bridge self-updates.
- Anonymous `stable`, `0.47.0` and `sha-1e50abf9dc1fe9c5de7c7dd7a6a75e5e7feb3518` all resolve to **`sha256:be1f4cf3028fbaaf663718c075591cb47c80ae74321dffecb52404a8aa99b115`**, Linux amd64, version **0.47.0**, source **1e50abf**.
- Immutable **0.46.0** remains **`sha256:3a296aaa53a9abdbb849a0af6b219d7e770588945349d5fa9afc8dfea3b6c170`**; **0.45.0** remains **`sha256:89eafd568b5b96c5d43880e14789cae2427e650c7ddd213b373759f5f3e79449`**. Native/core/save identities remain locked.
- [Mount-preserving configuration overlay](../deploy/casaos/managed-updates.override.yml) is available at documentation commit **`b62f69e4a7c7c727f45497458a3ea53b75467e4c`**, file SHA-256 **`69b6c0fcb860208b3aafa887dedb4ed7416e01b0cecb6c829be01d93a79a5399`**. It adds update flags/labels and the helper without replacing Home's volumes, ports, CPU/memory or custom environment.
- Raw build/protocol/registry receipts remain ignored under `artifacts/release-047/`. Staged source/documentation privacy checks found no secrets. No browser suite, household media acquisition or multi-system gaming benchmark was run.

**No household deployment is claimed by registry publication.** The user-provided existing two-service CasaOS configuration still needs one backup/merge/deployment transition. Its data path, media bindings and resource settings must remain unchanged; helper connection, real PC/TV playback/game controls and interrupted-helper recovery remain user/deployment acceptance.

### First qualification attempt

Workflow **40327937133** failed closed before publication on the existing movie resume probe. Its heartbeat left one full mux target unconsumed; in-flight encoder progress plus an audio-frame fraction can keep that position above the resume hysteresis. A focused window regression reproduces this contract. The probe now advances the simulated consumer to within 0.25 seconds of the published edge; it still requires new production, renewed backpressure, account/range/source preservation and cleanup. Production media logic is unchanged. Failed waits retain their last observed state for diagnosis.

The same run's roughly 32-minute radio step was dominated by a slow hosted Ubuntu package transfer, not the radio tests (longest test about 5.8 seconds). Hosted checks now prefer already installed FFmpeg tools and bound fallback package-transfer timeouts; the released media runtime remains source/hash pinned. The corrected source **1e50abf** subsequently passed all mandatory gates and was promoted.
