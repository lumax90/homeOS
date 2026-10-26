using System.Text.Json;
using HomeServer.Core;
using HomeServer.Infrastructure;
using HomeServer.Metadata;

namespace HomeServer.Downloads;

public sealed class DownloadImporter(DownloadFiles paths, MediaStore store, EventHub events, IMediaProbe? mediaTools = null, MetadataProvider? metadata = null)
{
    private readonly SemaphoreSlim _imports = new(1, 1);
    private sealed record Manifest(string DownloadId, string InfoHash, ManifestFile[] Files);
    private sealed record ManifestFile(string Path, long Length);

    public async Task ImportAsync(DownloadRecord download, ITorrentSession session, CancellationToken token)
    {
        await _imports.WaitAsync(token);
        string? temporary = null;
        try
        {
            var music = store.GetMusicAcquisition(download.Id);
            var media = music is null ? await AcquisitionValidator.ValidateAsync(download, session, store, mediaTools, metadata, token)
                : await Music.MusicAcquisitionValidator.ValidateAsync(download, session, music, store, mediaTools, token);
            if (media.Length == 0) throw new DomainException("İndirilen pakette desteklenen medya dosyası yok.");
            var libraryRoot = music is null ? paths.LibraryRoot : paths.MusicLibraryRoot;
            var library = store.EnsureDownloadLibrary(libraryRoot, music is not null);
            var legacy=Path.Combine(libraryRoot,DownloadFiles.ValidId(download.Id));
            var title=DownloadImportNaming.Title(download,store,out var year,out var season);
            // Published legacy locations are never renamed. The reservation also
            // survives a crash after Directory.Move but before RecordImport.
            var proposed=music is not null||download.ImportedAt is not null||Directory.Exists(legacy)?download.Id:DownloadImportNaming.DirectoryName(title,download.Id,year,season);
            var directory=store.ReserveDownloadImportDirectory(download.Id,download.Revision,proposed);
            var target=Path.Combine(libraryRoot,directory);
            if(File.Exists(target))throw new DomainException("Hedef klasörde başka bir dosya var. Üzerine yazılmadı.",409);
            if (Directory.Exists(target)) await ValidatePublishedAsync(target, download, media, token);
            else
            {
                temporary = DownloadFiles.EnsureDirectory(paths.ImportArea, download.Id + "-" + Guid.NewGuid().ToString("N"));
                foreach (var file in media)
                {
                    token.ThrowIfCancellationRequested();
                    var relative = DownloadFiles.ValidateRelative(file.RelativePath);
                    var source = MediaPaths.ResolveFile(session.Directory, relative);
                    if (!string.Equals(Path.GetFullPath(source), Path.GetFullPath(file.FullPath), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
                        || new FileInfo(source).Length != file.Length)
                        throw new DomainException("İndirilen dosya doğrulaması başarısız.");
                    var parent = Path.GetDirectoryName(relative);
                    if (!string.IsNullOrEmpty(parent)) DownloadFiles.EnsureDirectory(temporary, parent);
                    var destination = Path.Combine(temporary, relative);
                    if (!DownloadFiles.TryHardLink(source, destination))
                    {
                        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
                        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.Asynchronous);
                        await input.CopyToAsync(output, token);
                        await output.FlushAsync(token);
                    }
                    if (new FileInfo(destination).Length != file.Length) throw new DomainException("Dosya kütüphaneye eksik kopyalandı.");
                }
                var manifest = new Manifest(download.Id, download.InfoHash, media.Select(f => new ManifestFile(f.RelativePath, f.Length)).ToArray());
                await File.WriteAllTextAsync(Path.Combine(temporary, ".home-import.json"), JsonSerializer.Serialize(manifest), token);
            }

            ScanJob? scan;
            do
            {
                token.ThrowIfCancellationRequested();
                scan = store.TryPublishImport(download.Id, download.Revision, library.Id, () =>
                {
                    token.ThrowIfCancellationRequested();
                    if (temporary is not null) { Directory.Move(temporary, target); temporary = null; }
                });
                if (scan is null) await Task.Delay(350, token);
            } while (scan is null);
            events.Publish("catalog.changed");
        }
        finally
        {
            // This is a fresh random import-attempt directory created by this call only.
            if (temporary is not null && Directory.Exists(temporary)) Directory.Delete(temporary, true);
            _imports.Release();
        }
    }

    private async Task ValidatePublishedAsync(string target, DownloadRecord download, TorrentFileSnapshot[] expected, CancellationToken token)
    {
        _ = MediaPaths.NormalizeRoot(target);
        var marker = MediaPaths.ResolveFile(target, ".home-import.json");
        var manifest = JsonSerializer.Deserialize<Manifest>(await File.ReadAllTextAsync(marker, token));
        if (manifest is null || manifest.DownloadId != download.Id || manifest.InfoHash != download.InfoHash
            || manifest.Files.Length != expected.Length)
            throw new DomainException("Hedef klasör bu indirmeye ait değil. Mevcut dosyalar üzerine yazılmadı.", 409);
        foreach (var file in expected)
        {
            var edited = store.EditedMusicImport(download.Id, file.RelativePath);
            if (!manifest.Files.Any(f => f.Path == file.RelativePath && f.Length == file.Length)
                || new FileInfo(MediaPaths.ResolveFile(target, DownloadFiles.ValidateRelative(edited?.Path ?? file.RelativePath))).Length != (edited?.Length ?? file.Length))
                throw new DomainException("Kütüphanedeki indirme çıktısı doğrulanamadı.", 409);
        }
    }
}
