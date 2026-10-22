# CasaOS registry installation — 0.43.0

**2026-10-22 — CASAOS-REGISTRY-043 / OPS-05/06 / GAM-19.** The user hit `No such image: home:0.43.0` after the bind-import correction. The earlier delivery was a verified local image tar, and the public source repository did not publish an image. The intended flow is CasaOS Import → Install → automatic image download and data-directory preparation.

## Implementation

- `.github/workflows/publish-container.yml` builds the existing Linux x64 Dockerfile, including its hash-bound native runtime proof. A Compose-level install/recreate/ownership-failure check runs before publication. The workflow publishes a stable version and a source-commit tag to **`ghcr.io/lumax90/homeos`**, records the digest and refuses to replace an existing release tag.
- The workflow runs on explicit dispatch or matching `vX.Y.Z` tags. Its repository token has `contents: read` and `packages: write`; no registry credential is committed. Third-party actions are pinned to reviewed official release commits.
- The online CasaOS recipe uses **`ghcr.io/lumax90/homeos:0.43.0`** with pulling enabled. A network-disabled, read-only, capability-scoped `home-data-init` prepares only an empty `/data` bind for **1654:1654**, then Home runs non-root. Existing nonempty data with unexpected ownership blocks startup and remains intact.
- CasaOS's upstream custom-import converter changes `restart: "no"` to `unless-stopped`. The initializer therefore uses **`on-failure:3`**, which exits after success and bounds failed retries. `depends_on: service_completed_successfully` orders Home after successful preparation. Its inherited application health check is disabled.
- Host **`/DATA/AppData/home/data`** is explicit on both services, retaining the earlier empty-source fix. A custom path must match on both. Existing named-volume deployments retain their existing mount until explicitly migrated.
- Both CasaOS recipes use long-form ports and explicit container names. The upstream converter serializes an absent short-port host IP as `null` and an absent container name as an empty string; both fail strict Compose validation. The explicit forms survive its parser/output round trip.
- The standalone tar route is retained as `docker-compose.local.yml` and `README.local.md`; the local exporter still emits its own `docker-compose.yml`/README and `load-home.sh` prepares that local bind. It is not the registry-install manifest.
- This publication uses the existing native/software-only sources, licence texts and corresponding-source archive in the image. No user database, source media, ROM, BIOS, save, signing key or paused transfer is included in the build context. The optional provider/core choices and final Home distribution licence retain their recorded status.

## Publication and verification status

- GitHub CLI now has the user-approved **`workflow` / `write:packages`** scopes required for this work.
- Local candidate Docker build and **three composed workflow checks passed**: automatic first-directory setup/healthy non-root startup, repeated initializer/container recreation with persistent bytes, and foreign-owned existing-data rejection that blocks Home while preserving data. Evidence: `artifacts/casaos-registry-043/reports/`. The initial create-phase check found that **both** bind definitions need `create_host_path: true`: Docker creates the Home container before starting its initializer, so a missing source cannot be deferred only to dependency startup. Both definitions were corrected and the full checks passed.
- **actionlint 1.7.12** (MIT, official Windows release SHA-256 `6e7241b51e6817ea6a047693d8e6fed13b31819c9a0dd6c5a726e1592d22f6e9`) passed workflow validation. Shell syntax checks passed.
- The actual CasaOS UI **parseComposeItem / outputConfigDataCommands** methods were executed from upstream commit `400a22c120e9f14b6e5c2276f9b79253acf95f35`, with its declared-library versions **lodash 4.17.21 (MIT)** and **yaml 1.10.2 (ISC)** in an isolated diagnostic dependency directory. The resulting YAML passed the **same three actual Linux install/recreate/ownership-failure checks**. The two rejected round-trip candidates are retained in the diagnostic VM; final evidence is `artifacts/casaos-registry-043/{import.json,converted-compose.yml,converted-reports/}`. CasaOS UI is **Apache-2.0**; this is verification-only source execution, not a product dependency or automated browser suite.
- The first GitHub-hosted build was **cancelled before publication** for the [repository privacy review](repository-privacy.md). Its publish step was skipped, and no GHCR package was present at the review. Its native-build dependency installation took about **2,442 seconds**; the later publication reused the verified local Linux builder's cache.
- **Published 2026-10-22:** `ghcr.io/lumax90/homeos:0.43.0` and `sha-7372b5de8ed418a6d41b9453a3b1a77b341ce084`. The input was a `git archive` of sanitized source commit **`7372b5de8ed418a6d41b9453a3b1a77b341ce084`**, built with the repository Dockerfile. The same three install/recreate/ownership-rejection checks passed against that exact image. This was a verified local-builder publication; the cancelled GitHub-hosted workflow is not represented as a successful build.
- The released **single-platform linux/amd64 OCI manifest** has digest **`sha256:017e90f2860d2f76d1a787ee7476f5c6244da4aef351f94c733f965bde9bdba0`**; config digest **`sha256:d71694cec27a6f5701ff57f3aca66c28a5e2a8627806365bd599437f88536f7d`**. The initial multi-manifest push was rejected after uploading an untagged attestation. The subsequent explicit amd64 push succeeded. The released manifest does not include the Buildx attestation index; the hash-bound native proof remains inside the image.
- Before publication, a read-only image export scanned **951,230,001 bytes**, finding no developer-specific address/profile marker, application `.env` file or populated `/data` file. Registry authentication used a disposable Docker configuration directory, removed after each attempt. No registry credential was part of the source archive or image build.
- The owner changed the new package to **Public** in GitHub's package settings. Anonymous token/manifest requests and HEAD requests for the config plus all **eight layers** returned **HTTP 200**. With a new empty Docker configuration and the registry tag removed locally, **anonymous `docker pull` restored the exact published digest**. Existing layer cache was retained. The pulled image then passed the same **three fresh-data install/recreate/ownership-rejection checks**.
- Evidence: `artifacts/casaos-registry-043/{clean-source-reports/,anonymous-reports/,home-image-privacy-043.json,home-published-image-043.json,home-anonymous-image-043.json}`. [Public package](https://github.com/users/lumax90/packages/container/package/homeos).
- The first complete GitHub-hosted build/push and package access for that workflow remain a follow-up verification gate; manual publication does not establish automatic CI release success.
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
