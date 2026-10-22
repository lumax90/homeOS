# Home — independent media server

- Root: `<REPOSITORY_ROOT>`. This is an independent product with its own architecture, API, database, jobs and frontend.
- Backend: C# / ASP.NET Core on .NET 10 LTS; SQLite for device-local application data. Use a modular monolith with durable, bounded background work.
- Frontend: React + TypeScript + Vite + HeroUI v3 + Tailwind CSS v4 + Motion. Read current library documentation and installed types before using APIs.
- Visual direction: Apple-inspired content-first interface, refined typography, restrained glass controls above content, fluid and interruptible motion. Respect reduced-motion preferences and keyboard focus.
- Product languages: English and Turkish, selectable in the interface; English is the default. Working name: Home. No final branding decision has been made.
- Core product logic belongs here. Protocol and codec libraries may be embedded or packaged; external media management applications are not the product architecture.
- User-selected media folders are read-only in the first milestone. Scans must be repeatable and must not remove media records when a volume is unavailable or a scan fails.
- Downloads may write only in the application's managed staging/import/library directories. Publish verified payloads idempotently; never overwrite a conflicting target or delete source-library files.
- Keep authentication, filesystem boundaries, HTTP range streaming, cancellation, restart recovery, and database migrations explicit and verified with meaningful tests.
- Synthetic or demonstration content must be clearly labeled. Never claim provider integration, transcoding or GPU support without a working implementation and verification.
- Record decisions, source references, verification dates and implementation status under `docs/`.
- Product scope and acceptance criteria are tracked in `docs/product/`. Update the requirement IDs and execution board with each implemented slice; do not label the whole product complete while release gates remain open.
- The user owns browser/device acceptance testing. Do not run automated browser suites or spend time on repeated screenshot polish unless explicitly requested. Run focused backend/build checks for data, protocol and workflow changes.
- Runtime screens must use real provider/catalog data. Archive existing synthetic libraries from normal use; synthetic data belongs only in isolated automated tests. Never initiate an internet media download during development verification; acquisition is a user command.
- First stable client target is web + PWA. Distribution license is undecided; record dependency/data licenses before adopting code or datasets.
