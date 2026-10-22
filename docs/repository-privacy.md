# Public repository privacy review

**2026-10-22 — REPO-PRIVACY-043 / OPS-09.** A developer-specific LAN address was published in the README and work notes. Absolute Windows profile paths also appeared in documentation and development helpers. These are inappropriate public installation details even though a private-network address is not a public internet endpoint.

## Audit findings

- The initial audit covered all **four reachable commits**, **986 tracked files** and **991 distinct file blobs**.
- No `.env` file, application database, private-key/certificate file, `.data/`, `artifacts/` or `.tools/` content appeared in those Git trees.
- **Gitleaks 8.30.1** scanned all four commits, including decoded-text checks, with redacted reporting and no inline allow-comment exemptions. It scanned approximately **9.33 MB of text changes** and reported **zero secret findings**. The tracked `server/HomeServer.Api/appsettings.json` was also reviewed: it contains application defaults, not deployment credentials.
- GitHub's repository secret-scanning API reported **zero open alerts** at the review.
- Git ignore rules were checked for root/nested environment files, local application state and signing artifacts. Raw audit receipts stay in the ignored `artifacts/repository-privacy-audit/` directory.
- The first container workflow was cancelled before its publish step; no GHCR package was present at the audit.

## Corrections

- Public README/work notes use product descriptions and installation-neutral addresses instead of a developer's running server address.
- Windows helpers resolve their existing cache paths through `LOCALAPPDATA`, and repository paths through `PSScriptRoot`. The cleanup helper requires an explicit review root when that environment variable is unavailable.
- Git ignore rules now also cover common key/certificate and SQLite file extensions. Docker context exclusions cover root/nested environment files, local state, private key/certificate formats and databases. No currently tracked source file uses those excluded sensitive-file extensions.
- The repository instructions require placeholders and environment-derived paths in public material, with raw operational receipts kept outside commits.

## Verification and history

Verification passed: **10 focused native-helper/cleanup checks**, syntax parsing of **11 changed PowerShell scripts**, Git exclusion probes, and an actual isolated Docker context export that omitted **21 private-pattern fixture paths** while retaining **four required-source fixtures**. Current-source checks find no remaining developer-specific LAN address or Windows profile path. The context fixtures are synthetic and isolated; no real credentials or user data were used.

The owner approved rewriting the published history while keeping the repository public. A local Git backup is retained before rewriting; the sanitized history will be checked before a lease-protected force-push. Editing a branch alone does **not** erase older commits, and this review does not claim that GitHub caches or third-party clones have been removed.

The audit did not identify exposed access credentials that require an emergency visibility change. This is a scoped review result, not a guarantee that arbitrary future configuration is safe to publish. The product's eventual open-source/distribution licence remains undecided.

References: [GitHub sensitive-data removal](https://docs.github.com/en/authentication/keeping-your-account-and-data-secure/removing-sensitive-data-from-a-repository), [Gitleaks v8.30.1](https://github.com/gitleaks/gitleaks/releases/tag/v8.30.1) (**MIT**, official Windows archive SHA-256 `d29144deff3a68aa93ced33dddf84b7fdc26070add4aa0f4513094c8332afc4e`).
