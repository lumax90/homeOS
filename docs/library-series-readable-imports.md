# Series-first library and readable imports — LIBRARY-SERIES-01 / IMPORT-NAME-01

Date **2026-10-26**. Requirements **CAT-01/03/05/08/10, PLY-07/09, IMP-02/03/05, DL-02/04, CLI-01/05, OPS-04/09**. User requests one series card in Library/Home instead of a season pack filling both screens with individual episodes; opening the series must offer seasons/episode playback. User also asks why imported directories have opaque names. **Local implementation, not published or household-accepted.**

## Series-first browse

- Home/Library opt into `/catalog?grouped=true`. The original file-level catalogue API remains the default for file workflows.
- The server groups **before offset/limit**, using existing matched show/content identities. A long season pack therefore occupies one browse slot, not the first sixty file slots. Existing matched movie versions remain together; independent/unmatched films remain separate.
- Unmatched files can form a local series view only with explicit **season/episode filename evidence** or actual local `showTitle`/season/episode metadata. This is a local, library-scoped identity; it never creates a fake provider match. Bare numbered/ambiguous files are not guessed into a series. Known years remain separate.
- Library/account/age/search/root filters apply **before grouping and counts**. Disabled/inaccessible libraries do not contribute hidden episode counts or representative files. Unavailable-volume records are retained; this view does not perform a scan or delete anything.
- Cards expose a stable group key/title and file/episode/season counts while retaining the real representative media ID. React/controller focus uses the stable key. No stored media/title/artwork/watch metadata is overwritten merely to rename a card.
- Opening a series uses `/series/{mediaId}` and an access-filtered, bounded `/media/{id}/series` endpoint. It lists local seasons/files, actual episode labels, availability/progress, file information and playback through the existing media sequence/session lifecycle. Saved local series do not require a provider request just to select an episode; provider details remain a separate link where matched.
- The Home hero opens episode selection for series instead of showing one episode as the whole title. `Yeni eklenenler` and Library grid/list use series names/counts. The grouped Continue Watching view keeps the latest in-progress episode per series, without deleting other episode progress. Playback queues/file-level APIs retain actual files.

## Why the old import directories were opaque

`DownloadImporter` previously published verified payloads under the **download GUID**, not the movie/show title. That ID prevents title/edition collisions and lets a marker-backed import recover idempotently after a restart. Random staging/import-attempt names are still internal working directories, not presentation titles.

## Readable future imports

- New non-music import directories use a portable title, optional known year/season and a **short stable download-ID suffix**: e.g. `Title (Year) S01 [stable-id]`. The suffix distinguishes editions/imports; it is not a replacement media identity.
- Prefer the already selected catalogue title; otherwise use the actual download title/filename hints. Preserve Turkish/other Unicode letters, bound UTF-8 length, remove invalid path/control characters, and retain existing relative payload filenames/sidecars. No source-library files are renamed.
- Additive **schema 44 / `044_readable_imports`** adds `downloads.import_directory` and a uniqueness index. The chosen directory is reserved before filesystem publication and remains stable across retries/name changes, including a crash between `Directory.Move` and DB publication bookkeeping.
- Existing imported records/directories keep their **legacy ID locations**. Music import locations also keep their existing structure. No automatic directory migration, catalogue path rewrite or paused-transfer reset occurs.
- Catalogue acquisition linking and music tag/import path helpers now resolve the persisted import directory, with the old download ID as the fallback for legacy rows. This is necessary to keep media associations/editions/provenance valid after new named imports.
- Marker/download ID/info-hash/file-length validation and revision/cancellation fences remain. An existing conflicting file/directory is not overwritten; staging/seeding payloads and source-library files are not deleted to make a name fit.

## Verification and publication boundary

Per the user's preference, no browser suite, screenshot polish, long backend/protocol tour or internet-media acquisition is run. Regression definitions cover an 80-episode series occupying one pre-pagination slot, retained file identities/progress, filename/local-metadata identity, stable portable names/reservations and legacy schema-43 import/paused-state preservation. Existing download-path regression definitions use the resolved import location instead of assuming a GUID directory for new outputs.

Final backend/regression definitions compiled with **0 warnings / 0 errors**. Frontend TypeScript, worker, Vite/PWA and enforced English/Turkish UI translation build passed. Compilation is not presented as runtime/household acceptance of the new SQL/migration/import paths; those acceptance gates remain recorded. No test suite, browser automation, server/VM launch or real download was run in this slice.

All existing uncommitted Selkies signaling, HLS, Home visibility, controller, radio and media-runtime changes are preserved. **No release, image publication, household DB migration or download was started.** Current public stable remains **0.45.0 / schema43**; a future authorised release must account for schema44 and the separate media-runtime source/licence gate.

## References

Reviewed **2026-10-26**. Context7 quota was exhausted; official documentation and existing/installed APIs were consulted.

- [Microsoft.Data.Sqlite scalar functions](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/user-defined-functions).
- Existing `MediaStore` permission/age filters, window-based Continue Watching and file-level media APIs.
- Existing `FilenameHints`, cached/local metadata, media sequence and page-navigation contracts.
- Existing `DownloadImporter` marker/atomic-publication and `AcquisitionValidator` identity/revision boundaries.
