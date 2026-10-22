# CasaOS registry installation — 0.43.0

**2026-10-22 — CASAOS-REGISTRY-043 / OPS-05/06 / GAM-19.** The user hit `No such image: home:0.43.0` after the bind-import correction. The earlier delivery was a verified local image tar, and the public source repository did not publish an image. The intended flow is CasaOS Import → Install → automatic image download and data-directory preparation.

## Implementation

- `.github/workflows/publish-container.yml` builds the existing Linux x64 Dockerfile, including its hash-bound native runtime proof. A Compose-level install/recreate/ownership-failure check runs before publication. The workflow publishes a stable version and a source-commit tag to **`ghcr.io/lumax90/homeos`**, records the digest and refuses to replace an existing release tag.
- The workflow runs on explicit dispatch or matching `vX.Y.Z` tags. Its repository token has `contents: read` and `packages: write`; no registry credential is committed. Third-party actions are pinned to reviewed official release commits.
- The online CasaOS recipe uses **`ghcr.io/lumax90/homeos:0.43.0`** with pulling enabled. A network-disabled, read-only, capability-scoped `home-data-init` prepares only an empty `/data` bind for **1654:1654**, then Home runs non-root. Existing nonempty data with unexpected ownership blocks startup and remains intact.
- CasaOS's upstream custom-import converter changes `restart: "no"` to `unless-stopped`. The initializer therefore uses **`on-failure:3`**, which exits after success and bounds failed retries. `depends_on: service_completed_successfully` orders Home after successful preparation. Its inherited application health check is disabled.
- Host **`/DATA/AppData/home/data`** is explicit on both services, retaining the earlier empty-source fix. A custom path must match on both. Existing named-volume deployments retain their existing mount until explicitly migrated.
- The standalone tar route is retained as `docker-compose.local.yml` and `README.local.md`; the local exporter still emits its own `docker-compose.yml`/README and `load-home.sh` prepares that local bind. It is not the registry-install manifest.
- This publication uses the existing native/software-only sources, licence texts and corresponding-source archive in the image. No user database, source media, ROM, BIOS, save, signing key or paused transfer is included in the build context. The optional provider/core choices and final Home distribution licence retain their recorded status.

## Publication and verification status

- GitHub CLI now has the user-approved **`workflow` / `write:packages`** scopes required for this work.
- Local candidate Docker build and **three composed workflow checks passed**: automatic first-directory setup/healthy non-root startup, repeated initializer/container recreation with persistent bytes, and foreign-owned existing-data rejection that blocks Home while preserving data. Evidence: `artifacts/casaos-registry-043/reports/`. The initial create-phase check found that **both** bind definitions need `create_host_path: true`: Docker creates the Home container before starting its initializer, so a missing source cannot be deferred only to dependency startup. Both definitions were corrected and the full checks passed.
- **actionlint 1.7.12** (MIT, official Windows release SHA-256 `6e7241b51e6817ea6a047693d8e6fed13b31819c9a0dd6c5a726e1592d22f6e9`) passed workflow validation. Shell syntax checks passed.
- GitHub-hosted build, image publication and anonymous-pull verification are **in progress**. A registry reference is not considered delivered until an unauthenticated pull succeeds.
- GitHub makes a new personal container package private initially. Package visibility must be **Public** for CasaOS to pull without a registry login. GitHub documents this as a package-settings UI operation; linking a public repository alone does not establish public visibility.
- Actual CasaOS hardware/browser/TV acceptance remains **ACCEPT-43-LAN**.

## References and build-only licences

Consulted **2026-10-22** through Context7, current official release metadata and action input declarations:

| Component | Release / pinned commit | Licence |
|---|---|---|
| `actions/checkout` | v7.0.1 / `3d3c42e5aac5ba805825da76410c181273ba90b1` | MIT |
| `docker/setup-buildx-action` | v4.4.1 / `f87e5991a6d7451dcb8d9637bfbc97413f497069` | Apache-2.0 |
| `docker/login-action` | v4.6.0 / `dbcb813823bdd20940b903addbd779551569679f` | Apache-2.0 |
| `docker/build-push-action` | v7.4.0 / `c3c9e263c25d99ce0380d002d59b67737d91b0dc` | Apache-2.0 |
| `actions/upload-artifact` | v7.0.2 / `cf430e030ddbb5b0abf93d22962f4752f3646cd9` | MIT |

- [GitHub Docker publication](https://docs.github.com/en/actions/tutorials/publish-packages/publish-docker-images)
- [Container registry authentication and source labels](https://docs.github.com/en/packages/working-with-a-github-packages-registry/working-with-the-container-registry)
- [Package access and visibility](https://docs.github.com/en/packages/learn-github-packages/configuring-a-packages-access-control-and-visibility)
- [GitHub CLI scope refresh](https://cli.github.com/manual/gh_auth_refresh)
- [Compose startup dependencies](https://docs.docker.com/compose/how-tos/startup-order/)
- [CasaOS converter: restart and original-field merge](https://github.com/IceWhaleTech/CasaOS-UI/blob/400a22c120e9f14b6e5c2276f9b79253acf95f35/src/components/Apps/ComposeConfig.vue#L534-L688)
- [Existing component/data inventory](dependencies.md) and [0.43 native/Linux evidence](headless-lan-043.md)
