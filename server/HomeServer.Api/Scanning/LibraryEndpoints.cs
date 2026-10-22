using HomeServer.Core;
using HomeServer.Identity;
using HomeServer.Infrastructure;
using HomeServer.Metadata;

namespace HomeServer.Scanning;

public static class LibraryEndpoints
{
    public static void MapLibraryAdministration(this RouteGroupBuilder api)
    {
        api.MapGet("/libraries/manage", (MediaStore store) => Results.Ok(store.GetLibraries(true))).RequireAuthorization("admin");
        api.MapGet("/libraries/{id}/configuration", (string id, MediaStore store) => Results.Ok(store.GetLibraryConfiguration(id))).RequireAuthorization("admin");
        api.MapPut("/libraries/{id}", (string id, LibraryEdit input, MediaStore store, ScanWorker worker, EventHub events) =>
        {
            var jobs = store.GetJobs().Where(job => job.LibraryId == id && job.Status is "queued" or "running").ToArray();
            var result = store.UpdateLibrary(id, input);
            foreach (var job in jobs) worker.Cancel(job.Id);
            events.Publish("catalog.changed"); return Results.Ok(result);
        }).RequireAuthorization("admin");
        api.MapGet("/library/auto-matches", (MediaStore store) => Results.Ok(store.GetAutoMatchJobs())).RequireAuthorization("admin");
        api.MapGet("/filesystem/directories", (string? path) =>
        {
            if (string.IsNullOrWhiteSpace(path)) return Results.Ok(new { path = "", parent = (string?)null,
                directories = DriveInfo.GetDrives().Where(drive => drive.IsReady).Select(drive => new { name = drive.Name, path = drive.RootDirectory.FullName }).ToArray(), truncated = false });
            var root = MediaPaths.NormalizeRoot(path);
            var directories = Directory.EnumerateDirectories(root).Where(value => (File.GetAttributes(value) & (FileAttributes.ReparsePoint | FileAttributes.System)) == 0).Take(301).ToArray();
            return Results.Ok(new { path = root, parent = Directory.GetParent(root)?.FullName, directories = directories.Take(300).Order(StringComparer.OrdinalIgnoreCase).Select(value => new { name = Path.GetFileName(value), path = value }).ToArray(), truncated = directories.Length > 300 });
        }).RequireAuthorization("admin");
        api.MapPost("/media/{id}/auto-match", (string id, CurrentAccount actor, MediaStore store) =>
        {
            var media = actor.RequireMedia(id); var config = store.GetLibraryConfiguration(media.LibraryId);
            if (!config.Options.AutoMatch) throw new DomainException("Kütüphanenin otomatik eşleştirmesini önce etkinleştir.", 409);
            store.QueueAutoMatch(id, config.Revision, true); return Results.Accepted();
        }).RequireAuthorization("admin");
        api.MapGet("/media/{id}/local-metadata", (string id, CurrentAccount actor, MediaStore store) =>
        { actor.RequireMedia(id); return Results.Ok(new { metadata = store.GetLocalMetadata(id), error = store.GetLocalMetadataError(id) }); });
    }
    public static IResult? LocalArtwork(string id, string kind, MediaStore store)
    {
        var local = store.GetLocalMetadata(id); var relative = kind == "poster" ? local?.Poster : local?.Backdrop;
        if (relative is null) return null;
        var media = store.GetMedia(id); var library = media is null ? null : store.GetLibrary(media.LibraryId);
        if (library is null || !File.Exists(Path.Combine(library.Path, relative))) return null;
        var file = MediaPaths.ResolveFile(library.Path, relative); var mime = LocalMetadataReader.ImageMime(file);
        return mime is null ? null : Results.File(file, mime, lastModified: new FileInfo(file).LastWriteTimeUtc);
    }
}
