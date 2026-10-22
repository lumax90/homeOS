using HomeServer.Core;
using HomeServer.Infrastructure;
using HomeServer.Metadata;

namespace HomeServer.Scanning;

public sealed class LibraryScanner(MediaStore store, MediaTools mediaTools, EventHub events)
{
    public async Task ScanAsync(ScanJob job, CancellationToken cancellationToken)
    {
        var library = store.GetLibrary(job.LibraryId) ?? throw new DomainException("Kütüphane bulunamadı.", 404);
        if(library.Kind is "photos" or "books"){await Shelves.ShelfScanner.ScanAsync(store,events,library,job,cancellationToken);return;}
        var root = MediaPaths.NormalizeRoot(library.Path);
        var preferences = store.GetLibraryConfiguration(library.Id);
        // Every attempt gets its own token, including recovered jobs with the same job ID.
        var token = Guid.NewGuid().ToString("N");
        var processed = 0;
        var added = 0;
        var lastUpdate = DateTimeOffset.MinValue;
        foreach (var path in LibraryTraversal.Files(root, preferences.Options, cancellationToken, store.LibraryIgnoredRoots(root)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = MediaPaths.Inspect(root, path);
            if (source is null) continue;
            var result = store.UpsertFile(library.Id, source, token, StableFileIdentity.Read(path));
            if (preferences.Options.ReadLocalMetadata)
            {
                try { if (await LocalMetadataReader.ReadAsync(library, source, cancellationToken) is { } local) store.SaveLocalMetadata(result.Id, local); else store.ClearLocalMetadata(result.Id); }
                catch (Exception ex) when (ex is DomainException or IOException or UnauthorizedAccessException) { store.LocalMetadataError(result.Id); }
            }
            if (result.NeedsProbe)
            {
                var probe = await mediaTools.ProbeAsync(path, cancellationToken);
                var thumbnail = source.Kind == "video" ? await mediaTools.ThumbnailAsync(path, result.Id, probe?.Duration, cancellationToken)
                    : probe?.Music?.HasArtwork == true && await mediaTools.AudioArtworkAsync(path, result.Id, cancellationToken);
                store.SaveProbe(result.Id, probe, thumbnail);
                if(library.Kind=="audiobooks"&&probe?.Music is{}book)store.SetAudiobookTitle(result.Id,book.Title);
                if (probe?.Streams is not null)
                {
                    var info = new FileInfo(path);
                    if (info.Length == source.Size && new DateTimeOffset(info.LastWriteTimeUtc).ToString("O", System.Globalization.CultureInfo.InvariantCulture) == source.ModifiedAt)
                        store.SaveAnalysis(result.Id, Playback.MediaAnalysisService.Fingerprint(info), probe);
                }
            }
            processed++;
            if (preferences.Options.AutoMatch && source.Kind == "video") store.QueueAutoMatch(result.Id, preferences.Revision);
            if (result.Added) added++;
            if (DateTimeOffset.UtcNow - lastUpdate > TimeSpan.FromMilliseconds(500))
            {
                if (store.GetLibraryConfiguration(library.Id).Revision != preferences.Revision) throw new OperationCanceledException();
                store.UpdateJobProgress(job.Id, processed, added, $"{processed} dosya işlendi · {source.Title}");
                events.Publish("catalog.changed");
                lastUpdate = DateTimeOffset.UtcNow;
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        // A detached/unavailable root must never be reconciled as an empty library.
        _ = MediaPaths.NormalizeRoot(root);
        if (store.GetLibraryConfiguration(library.Id).Revision != preferences.Revision) throw new OperationCanceledException();
        store.FinishScan(library.Id, token, job.Id, processed, added);
        store.RecordLibraryScan(library.Id);
        events.Publish("catalog.changed");
    }
}
