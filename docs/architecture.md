# Architecture decision — 2026-10-12

## Accepted direction

An independent, integrated media server. The application owns the catalog, policies, library, acquisition workflows, subtitles, playback decisions and user experience. These are internal modules sharing one data model and durable job history.

- **Backend:** C# + ASP.NET Core, .NET 10 LTS.
- **Application data:** SQLite on the server's local disk. Media stays in user-selected folders, which may be mounted storage.
- **Frontend:** React, TypeScript, Vite, HeroUI v3, Tailwind CSS v4 and Motion.
- **Media primitives:** FFmpeg / ffprobe under the application's process lifecycle management.
- **Acquisition module:** MonoTorrent 3.0.2 is embedded directly. 0.3 adds standard znab source search, quality evaluation, file-independent tracking and user-selected acquisition; 0.4.1 adds explicit import-profile review. Automatic RSS/upgrades and real indexer acceptance remain open.
- **Deployment:** one application package, initially Linux x64/ARM64 and Windows development. The compiled frontend is served by ASP.NET Core. Linux/GPU support needs its own actual verification.

## Rationale

.NET provides a coherent API, configuration, authentication, hosting, logging and diagnostics foundation for a broad media product. Go remains a strong compact-server option; Rust offers more direct resource control at greater initial development complexity; TypeScript offers language sharing. The decision prioritizes total development and maintenance cost for a small team over unmeasured microbenchmark differences.

No comparative CPU or memory benchmark has been performed. Most video-processing work is delegated to native codecs/FFmpeg; language choice alone does not establish playback performance.

## First vertical slice

1. Local owner setup and cookie authentication.
2. Explicitly selected media folders.
3. Persistent scan jobs, bounded execution, cancellation and restart recovery.
4. Read-only catalog ingestion, basic filename-derived titles, optional ffprobe metadata and thumbnails.
5. Search, details, direct file streaming with HTTP range support, saved playback progress.
6. Content-first HeroUI interface, glass navigation and Motion transitions.

Metadata matching and torrent downloads were added in 0.2; [0.3](milestone-03.md) adds real discovery/tracking/source selection; [0.4](milestone-04.md) adds track/subtitle inventory and bounded software HLS playback. Automatic acquisition, subtitle providers, HDR/GPU, remote access, multi-user accounts and TV clients remain on the [execution board](product/execution.md).

## Data and workflow rules

- A library is an explicitly selected filesystem root. Source files are never renamed, moved or deleted by this milestone.
- Paths are normalized; scans and playback must reject reparse points/symlinks that cross filesystem boundaries.
- A file identity is scoped to its library and relative path. Repeated scans update that identity.
- The first `media` table is a physical file inventory. Canonical content identities, editions and tracks will relate to it; a film/episode is not assumed to equal a single file.
- Only a fully successful scan may mark unseen files unavailable. Failed or cancelled scans preserve prior availability.
- An offline folder is a failed scan, never evidence that the catalog should be emptied.
- Jobs live in SQLite. The hosted worker is an executor, not the persistence layer.
- Playback refers to a catalog ID, never an arbitrary browser-supplied filesystem path.
- Credentials are hashed; sessions use HTTP-only cookies and state-changing browser requests require CSRF validation.

## Sources checked on 2026-10-12

- [.NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core): official; .NET 10 LTS support through 2028-11-14.
- [Self-contained deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/): official; the runtime can be included in the product package.
- [ASP.NET Core hosted services](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services): official; execution/lifecycle primitives, not automatic durable jobs.
- [SQLite appropriate uses](https://sqlite.org/whentouse.html): official; device-local application/server storage, one writer at a time.
- [Jellyfin 12.2 server project](https://github.com/jellyfin/jellyfin/blob/v12.2/Jellyfin.Server/Jellyfin.Server.csproj): source verified; ASP.NET Core / net10.0.
- [Jellyfin database architecture](https://github.com/jellyfin/jellyfin/blob/v12.2/src/Jellyfin.Database/readme.md): source verified; SQLite currently supported.
- [Emby 4.9 release](https://github.com/MediaBrowser/Emby.Releases/releases/tag/4.9.1.80): official release notes verify .NET 8, SQLite and FFmpeg. Current full server implementation is proprietary.
- [Plex database](https://support.plex.tv/articles/repair-a-corrupted-database/): official; customized SQLite. [C++ server description](https://www.plex.tv/blog/the-plex-penguin-friendly-media-server/) is historical (2011), not a source-level audit of current Plex.
- [MonoTorrent](https://github.com/alanmcgovern/monotorrent): release-v3.0.2 integration verified with controlled loopback torrent/magnet transfers.
- [HeroUI v3](https://heroui.com/en/docs/react/getting-started): official; React 19+, Tailwind 4, CSS styles package, React Aria foundation.
- [Apple materials](https://developer.apple.com/design/human-interface-guidelines/materials): official; glass as a floating navigation/control layer above content.
