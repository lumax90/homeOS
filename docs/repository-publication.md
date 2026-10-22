# Source repository

User-approved source publication target, **2026-10-22**:

- Repository: https://github.com/lumax90/homeOS
- Branch: `main`
- Initial source baseline: **Home 0.43.0 / schema43**.
- Commit identity: the owner's GitHub username and GitHub-provided no-reply address.

The source snapshot includes application code, tests, deployment/build tooling, documentation, dependency notices and the application's existing bundled assets. Local application data, ROM/save storage, signing keys, package archives, installed dependencies, build outputs and test reports remain excluded by `.gitignore`. Environment-file variants and Python caches are excluded too; environment examples can be tracked.

The initial inventory was checked against Git's ignore rules for oversized files, credential/key files and recognizable private-key/token formats. The remote was empty and the authenticated owner had push permission. Publication uses an ordinary upstream-setting push to `main`.

`.gitattributes` keeps source text in LF form and preserves manifest/digest-bound runtime assets byte for byte, including on Windows checkouts.

Container registry publication, GitHub Actions automation and a hosted CasaOS store definition are subsequent distribution work. Source publication does not change the recorded dependency licences or the project's still-undecided distribution licence. See [dependency inventory](dependencies.md) and [0.43 verification](headless-lan-043.md).
