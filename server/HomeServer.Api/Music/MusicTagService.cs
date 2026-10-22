using System.Text.Json;
using HomeServer.Core;
using HomeServer.Downloads;
using HomeServer.Infrastructure;
using HomeServer.Playback;

namespace HomeServer.Music;

public sealed class MusicTagService(MediaStore store, MediaAnalysisService analysis, MediaTools tools, DownloadFiles files, CoverArtCache artwork)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public async Task<MusicTagPreview> PreviewAsync(string id, bool rename, CancellationToken token)
    {
        var media = await analysis.InspectAsync(id, token);
        var tags = media.Probe.Music ?? throw new DomainException("Dosyanın müzik etiketleri okunamadı.", 422);
        var album = store.MusicAlbumForMedia(id); var release = album is null ? null : store.MatchedMusicRelease(album);
        var tracks = album is null ? [] : store.PreviewMusicMatch(album, release ?? new(new("", "", "", "", null, null, null, [], 0), []), store.GetAccount("1")!).Files;
        var match = release?.Tracks.FirstOrDefault(track => tracks.Any(file => file.MediaId == id && file.ReleaseTrackId == track.Id));
        var proposed = release is not null && match is not null ? tags with { Title = match.Title, Artist = match.Artist, AlbumArtist = release.Metadata.Artist, Album = release.Metadata.Title,
            Disc = match.Disc, Track = match.Position, Date = release.Metadata.Date, ReleaseMbid = release.Metadata.Id, ReleaseGroupMbid = release.Metadata.GroupId, RecordingMbid = match.RecordingId,
            ArtistCredits = MusicCredits.Normalize(match.ArtistCredits, match.Artist), AlbumArtistCredits = MusicCredits.Normalize(release.Metadata.ArtistCredits, release.Metadata.Artist),
            ArtistMbid = match.ArtistCredits is { Length: 1 } artists ? artists[0].Mbid : null, AlbumArtistMbid = release.Metadata.ArtistCredits is { Length: 1 } albumArtists ? albumArtists[0].Mbid : null } : tags;
        var writable = store.IsManagedMusic(media.Media) && MusicTagFile.Supports(media.Path) && match is not null;
        var target = rename ? Path.Combine(Path.GetDirectoryName(media.Media.RelativePath) ?? "", $"{proposed.Disc:00}-{proposed.Track:00} {SafeName(proposed.Title)}{Path.GetExtension(media.Path).ToLowerInvariant()}") : media.Media.RelativePath;
        _ = DownloadFiles.ValidateRelative(target);
        return new(id, media.Fingerprint, writable, media.Media.RelativePath, target, tags, proposed,
            !store.IsManagedMusic(media.Media) ? "Kaynak müzik klasörleri salt okunur." : !MusicTagFile.Supports(media.Path) ? "Etiket yazımı FLAC, MP3, M4A, Ogg/Vorbis, Opus, WAV ve AIFF için desteklenir." : match is null ? "Önce albümü bir MusicBrainz yayınıyla eşleştir." : null,
            release is not null && match is not null ? MediaStore.MusicTagMetadataHash(release, match.Id) : null);
    }
    public async Task<MusicTask> QueueAsync(string id, MusicTagInput input, CancellationToken token)
    {
        var preview = await PreviewAsync(id, input.Rename, token);
        if (!preview.Writable) throw new DomainException(preview.Reason!, 403);
        if (preview.Fingerprint != input.Fingerprint) throw new DomainException("Etiket önizlemesi eskidi.", 409);
        if (preview.MetadataHash != input.ExpectedMetadataHash || input.ExpectedMetadataHash is null) throw new DomainException("Yayın/katkı önizlemesi değişti; güncel etiketleri yeniden incele.", 409);
        var media = store.GetMedia(id)!; var library = store.GetLibrary(media.LibraryId)!;
        var target = Path.Combine(library.Path, preview.ProposedPath);
        if (MediaPaths.Key(preview.CurrentPath) != MediaPaths.Key(preview.ProposedPath) && File.Exists(target)) throw new DomainException("Önerilen dosya adı başka bir dosyayla çakışıyor.", 409);
        var plan = new MusicTagPlan(id, library.Id, store.GetLibraryConfiguration(library.Id).Revision, preview.CurrentPath, preview.ProposedPath, preview.Fingerprint, preview.Proposed, input.Artwork, preview.Proposed.ReleaseMbid, MetadataHash: preview.MetadataHash);
        return store.QueueMusicTask(id, "tags", plan);
    }
    public async Task<MusicTask> RestoreAsync(string jobId, CancellationToken token)
    {
        var job = store.GetMusicTask(jobId) ?? throw new DomainException("Etiket geçmişi bulunamadı.", 404);
        if (!job.HistoryAvailable || job.PlanJson is null) throw new DomainException("Geri alma kopyası bulunan tamamlanmış bir etiket işi seç.", 409);
        var old = JsonSerializer.Deserialize<MusicTagPlan>(job.PlanJson, Json)!; var media = await analysis.InspectAsync(job.MediaId, token);
        if (!store.IsManagedMusic(media.Media) || media.Media.RelativePath != old.TargetPath || StateFolders.Hash(media.Path) != old.TargetHash) throw new DomainException("Dosya bu etiket işleminden sonra değişti; eski sürüm üzerine yazılmadı.", 409);
        var backup = BackupPath(job.Id, old.OriginalPath); AppFiles.RejectLink(backup);
        if (StateFolders.Hash(backup) != old.OriginalHash) throw new DomainException("Etiket geri alma kopyası doğrulanamadı.", 409);
        var tags = (await tools.ProbeAsync(backup, token))?.Music ?? throw new DomainException("Eski etiketler okunamadı.", 422);
        var plan = new MusicTagPlan(job.MediaId, media.Media.LibraryId, store.GetLibraryConfiguration(media.Media.LibraryId).Revision, old.TargetPath, old.OriginalPath,
            media.Fingerprint, tags, false, tags.ReleaseMbid, RestoreJobId: job.Id);
        return store.QueueMusicTask(job.MediaId, "restore-tags", plan);
    }
    private string DirectoryFor(string id) => AppFiles.DirectoryIn(files.ImportArea, "music-tags", AppFiles.Id(id));
    private string BackupPath(string id, string original) => Path.Combine(DirectoryFor(id), "original" + Path.GetExtension(original));
    public void PruneHistory(string id) => store.PruneMusicTagHistory(id, () => AppFiles.DeleteOwnedDirectory(DirectoryFor(id)));
    private static string SafeName(string value)
    {
        var text = new string(value.Select(character => character < 32 || "<>:\"/\\|?*".Contains(character) ? '_' : character).ToArray()).Trim().TrimEnd('.');
        return string.IsNullOrWhiteSpace(text) ? "Parça" : text[..Math.Min(100, text.Length)];
    }
    public async Task ExecuteAsync(MusicTask task, CancellationToken token)
    {
        var plan = JsonSerializer.Deserialize<MusicTagPlan>(task.PlanJson!, Json)!;
        if (task.Status != "publishing" && plan.RestoreJobId is null && (plan.MetadataHash is null || plan.MetadataHash != store.GetMusicTagMetadataHash(task.MediaId))) throw new DomainException("Etiket işi için onaylanan yayın/katkı eşlemesi değişti.", 409);
        var directory = DirectoryFor(task.Id); var temporary = Path.Combine(directory, "replacement" + Path.GetExtension(plan.TargetPath)); var backup = BackupPath(task.Id, plan.OriginalPath);
        var library = store.GetLibraries(true).FirstOrDefault(item => item.Id == plan.LibraryId) ?? throw new DomainException("Müzik kütüphanesi bulunamadı.", 404);
        var source = Path.Combine(library.Path, DownloadFiles.ValidateRelative(plan.OriginalPath)); var target = Path.Combine(library.Path, DownloadFiles.ValidateRelative(plan.TargetPath));
        if (task.Status != "publishing")
        {
            _ = MediaPaths.ResolveFile(library.Path, plan.OriginalPath);
            if (MediaAnalysisService.Fingerprint(new FileInfo(source)) != plan.Fingerprint) throw new DomainException("Etiket dosyası iş sırası beklerken değişti.", 409);
            var originalHash = StateFolders.Hash(source);
            if (File.Exists(temporary)) { AppFiles.RejectLink(temporary); File.Delete(temporary); }
            if (plan.RestoreJobId is { } previous)
            {
                var old = JsonSerializer.Deserialize<MusicTagPlan>(store.GetMusicTask(previous)!.PlanJson!, Json)!;
                var original = BackupPath(previous, old.OriginalPath); AppFiles.RejectLink(original);
                if (StateFolders.Hash(original) != old.OriginalHash) throw new DomainException("Geri alma kopyası değişti.", 409);
                File.Copy(original, temporary);
            }
            else
            {
                (string Path, string Mime)? cover = plan.Artwork && plan.ReleaseMbid is not null ? await artwork.GetAsync("release", plan.ReleaseMbid, token) : null;
                if (plan.Artwork && cover is null) throw new DomainException("Seçilen yayın için kapak bulunamadı.", 404);
                await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.Asynchronous))
                await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.Asynchronous))
                    await input.CopyToAsync(output, token);
                MusicTagFile.WriteCopy(temporary, plan.Tags, cover, token);
                if (Path.GetExtension(temporary).ToLowerInvariant() is ".ogg" or ".oga" or ".opus") await OggTagBoundary.RepairAsync(temporary, token);
                if (await AudioHashAsync(source, token) != await AudioHashAsync(temporary, token)) throw new DomainException("Etiket yazımı ses payload'ını değiştirdi; yayın durduruldu.", 422);
                var written = (await tools.ProbeAsync(temporary, token))?.Music;
                if (written is null || written.Title != plan.Tags.Title || written.Album != plan.Tags.Album || written.Track != plan.Tags.Track || written.Disc != plan.Tags.Disc || written.ReleaseMbid != plan.Tags.ReleaseMbid
                    || plan.Tags.Date is not null && written.Date != plan.Tags.Date
                    || written.ReleaseGroupMbid != plan.Tags.ReleaseGroupMbid || written.RecordingMbid != plan.Tags.RecordingMbid || !MusicTagFile.SameCredits(plan.Tags.ArtistCredits, written.ArtistCredits)
                    || !MusicTagFile.SameCredits(plan.Tags.AlbumArtistCredits, written.AlbumArtistCredits) || plan.Artwork && !written.HasArtwork)
                    throw new DomainException("Yazılan müzik etiketleri doğrulanamadı.", 422);
            }
            if (MediaAnalysisService.Fingerprint(new FileInfo(source)) != plan.Fingerprint || StateFolders.Hash(source) != originalHash) throw new DomainException("Ses dosyası etiket hazırlığı sırasında değişti.", 409);
            if (File.Exists(backup)) { AppFiles.RejectLink(backup); if (StateFolders.Hash(backup) != originalHash) throw new DomainException("Önceki etiket yedeği farklı; üzerine yazılmadı.", 409); }
            else
            {
                var historyRoot = AppFiles.DirectoryIn(files.ImportArea, "music-tags");
                if (Directory.EnumerateFiles(historyRoot, "original.*", SearchOption.AllDirectories).Sum(path => new FileInfo(path).Length) + new FileInfo(source).Length > 20L * 1024 * 1024 * 1024) throw new DomainException("Etiket geri alma alanı 20 GB sınırına ulaştı.", 409);
                var partial = backup + ".partial"; if (File.Exists(partial)) { AppFiles.RejectLink(partial); File.Delete(partial); }
                await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.Asynchronous))
                await using (var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.Asynchronous))
                { await input.CopyToAsync(output, token); await output.FlushAsync(token); output.Flush(true); }
                if (StateFolders.Hash(partial) != originalHash) throw new DomainException("Etiket geri alma kopyası eksik.", 422);
                File.Move(partial, backup);
            }
            plan = plan with { OriginalHash = originalHash, TargetHash = StateFolders.Hash(temporary) };
        }
        token.ThrowIfCancellationRequested();
        while (!store.TryPublishMusicTags(task, plan, () => Publish(library.Path, plan, temporary, backup, source, target)))
        { await Task.Delay(500, token); token.ThrowIfCancellationRequested(); }
        if (store.GetLibrary(library.Id) is not null) await analysis.InspectAsync(task.MediaId, token);
    }
    private static void Publish(string root, MusicTagPlan plan, string temporary, string backup, string source, string target)
    {
        AppFiles.RejectLink(backup); if (StateFolders.Hash(backup) != plan.OriginalHash) throw new DomainException("Etiket yedeği doğrulanamadı.", 409);
        var same = MediaPaths.Key(plan.OriginalPath) == MediaPaths.Key(plan.TargetPath);
        if (File.Exists(target))
        {
            _ = MediaPaths.ResolveFile(root, plan.TargetPath);
            var currentHash = StateFolders.Hash(target);
            if (currentHash == plan.TargetHash)
            {
                if (!same && File.Exists(source))
                { _ = MediaPaths.ResolveFile(root, plan.OriginalPath); if (StateFolders.Hash(source) != plan.OriginalHash) throw new DomainException("Eski etiket yolu başka bir dosya içeriyor.", 409); File.Delete(source); }
                return;
            }
            if (!same || currentHash != plan.OriginalHash) throw new DomainException("Etiket hedefi değişti; çakışan dosya korunuyor.", 409);
        }
        _ = MediaPaths.ResolveFile(root, plan.OriginalPath);
        if (StateFolders.Hash(source) != plan.OriginalHash) throw new DomainException("Etiket kaynağı değişti; yayın durduruldu.", 409);
        AppFiles.RejectLink(temporary); if (StateFolders.Hash(temporary) != plan.TargetHash) throw new DomainException("Etiketli kopya doğrulanamadı.", 409);
        if (same) File.Move(temporary, target, overwrite: true); // Replaces the directory entry, never a hardlinked payload's bytes.
        else { File.Move(temporary, target); File.Delete(source); }
    }
    private async Task<string> AudioHashAsync(string path, CancellationToken token) => (await MediaProcess.RunAsync(tools.FfmpegExecutable,
        ["-hide_banner", "-loglevel", "error", "-nostdin", "-protocol_whitelist", "file,pipe", "-format_whitelist", MediaProcess.MediaFormats, "-i", path, "-map", "0:a:0", "-c:a", "copy", "-f", "hash", "-hash", "sha256", "-"], TimeSpan.FromMinutes(2), token)).Trim();
}
