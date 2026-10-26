# Home 0.47.0 — owner-approved container updates

Prepared **2026-10-26**, with explicit user approval to commit, push and publish the update-settings slice. Schema **44** is unchanged. **Publication qualification in progress; public stable remains 0.46.0 until promotion succeeds.**

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

Commit, hosted workflow, image digest, final verification and anonymous `stable`/version/source-tag identity are recorded here when publication completes. No household deployment is claimed by registry publication alone.
