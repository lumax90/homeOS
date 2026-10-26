using System.Text.Json;
using HomeServer.Core;
using HomeServer.Downloads;

namespace HomeServer.Infrastructure;

public sealed partial class MediaStore
{
    public static string MusicTagMetadataHash(MusicRelease release, string trackId) => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { release, trackId }, WebJson)));
    public string? GetMusicTagMetadataHash(string mediaId)
    {
        using var db = Open(); var row = Query(db, "SELECT cache.metadata_json,mt.release_track_id FROM music_track_matches mt JOIN music_album_matches am ON am.album_id=mt.album_id JOIN music_release_cache cache ON cache.release_mbid=am.release_mbid WHERE mt.media_id=$id",
            r => (Release: JsonSerializer.Deserialize<MusicRelease>(r.GetString(0), WebJson)!, Track: r.GetString(1)), ("$id", mediaId)).FirstOrDefault();
        return row.Release is null ? null : MusicTagMetadataHash(row.Release, row.Track);
    }
    public bool TryPublishMusicTags(MusicTask task, MusicTagPlan plan, Action publish)
    {
        lock (_writeGate)
        {
            var current = GetMusicTask(task.Id) ?? throw new DomainException("Etiket işi bulunamadı.", 404);
            if (current.Revision != task.Revision || current.Status is not ("running" or "publishing")) throw new OperationCanceledException();
            var library = GetLibraries(true).FirstOrDefault(item => item.Id == plan.LibraryId) ?? throw new DomainException("Etiket kütüphanesi bulunamadı.", 404);
            if (MediaPaths.Key(library.Path) != MediaPaths.Key(Path.Combine(_paths.Data, "downloads", "music-library"))) throw new DomainException("Etiket hedefi yönetilen müzik alanında değil.", 403);
            if (current.Status != "publishing" && (!library.Enabled || GetLibraryConfiguration(library.Id).Revision != plan.LibraryRevision)) throw new DomainException("Kütüphane etiket işi sırasında değişti.", 409);
            using var db = Open();
            if (Scalar(db, "SELECT id FROM jobs WHERE library_id=$library AND status='running' LIMIT 1", ("$library", library.Id)) is string) return false;
            if (plan.OriginalHash is null || plan.TargetHash is null) throw new DomainException("Etiket yayın günlüğü eksik.", 409);
            if (current.Status != "publishing")
            {
                if (plan.RestoreJobId is null && (plan.MetadataHash is null || plan.MetadataHash != GetMusicTagMetadataHash(task.MediaId))) throw new DomainException("Albüm/yayın eşlemesi etiket işi sırasında değişti; yeni önizleme gerekli.", 409);
                var media = GetMedia(task.MediaId) ?? throw new DomainException("Etiket dosyası bulunamadı.", 404);
                if (media.RelativePath != plan.OriginalPath) throw new DomainException("Etiket dosyası taşınmış.", 409);
                var sourcePath = MediaPaths.ResolveFile(library.Path, DownloadFiles.ValidateRelative(plan.OriginalPath));
                if (Playback.MediaAnalysisService.Fingerprint(new FileInfo(sourcePath)) != plan.Fingerprint || StateFolders.Hash(sourcePath) != plan.OriginalHash) throw new DomainException("Etiket kaynağı arada değişti; üzerine yazılmadı.", 409);
                if (MediaPaths.Key(plan.OriginalPath) != MediaPaths.Key(plan.TargetPath) && File.Exists(Path.Combine(library.Path, DownloadFiles.ValidateRelative(plan.TargetPath)))) throw new DomainException("Etiket hedefi başka bir dosyayla çakışıyor.", 409);
                if (Scalar(db, "SELECT id FROM media WHERE library_id=$library AND path_key=$path AND id<>$id", ("$library", library.Id), ("$path", MediaPaths.Key(plan.TargetPath)), ("$id", media.Id)) is string)
                    throw new DomainException("Etiket adı başka bir katalog kaydıyla çakışıyor.", 409);
                Execute(db, "UPDATE music_tasks SET status='publishing',plan_json=$plan,message='Doğrulanmış etiketler atomik yayımlanıyor.',updated_at=$now WHERE id=$id",
                    ("$id", task.Id), ("$plan", JsonSerializer.Serialize(plan, WebJson)), ("$now", Now()));
            }
            // A durable publishing state fences scan claims; recovery repeats this exact journal.
            publish();
            var path = MediaPaths.ResolveFile(library.Path, DownloadFiles.ValidateRelative(plan.TargetPath)); var file = new FileInfo(path);
            using var tx = db.BeginTransaction();
            using var write = Command(db, """
                UPDATE media SET relative_path=$path,path_key=$key,size_bytes=$size,modified_at=$modified,filesystem_id=$fs,has_thumbnail=0,title=$title,search_text=$search WHERE id=$media;
                DELETE FROM media_analysis WHERE media_id=$media;
                DELETE FROM music_sonic_analysis WHERE media_id=$media;
                DELETE FROM music_index_state WHERE media_id=$media;
                UPDATE music_tasks SET status='completed',message='Etiketler doğrulandı; önceki dosya geri alma için saklandı.',updated_at=$now WHERE id=$id;
                """, ("$id", task.Id), ("$media", task.MediaId), ("$path", plan.TargetPath), ("$key", MediaPaths.Key(plan.TargetPath)), ("$size", file.Length),
                ("$modified", new DateTimeOffset(file.LastWriteTimeUtc).ToString("O")), ("$fs", StableFileIdentity.Read(path)), ("$title", plan.Tags.Title), ("$search", MediaPaths.SearchKey(plan.Tags.Title)), ("$now", Now()));
            write.Transaction = tx; write.ExecuteNonQuery(); tx.Commit();
            if (library.Enabled) QueueScan(library.Id);
            return true;
        }
    }
    public void MusicPublishingError(string id, string message)
    {
        lock (_writeGate) { using var db = Open(); Execute(db, "UPDATE music_tasks SET message=$message,updated_at=$now WHERE id=$id AND status='publishing'", ("$id", id), ("$message", message), ("$now", Now())); }
    }
    public void PruneMusicTagHistory(string id, Action remove)
    {
        lock (_writeGate)
        {
            var task = GetMusicTask(id) ?? throw new DomainException("Etiket geçmişi bulunamadı.", 404);
            if (task.Kind == "sonic" || task.Status is "queued" or "running" or "publishing") throw new DomainException("Etkin müzik işinin geri alma dosyaları temizlenemez.", 409);
            using var db = Open();
            if (Scalar(db, "SELECT id FROM music_tasks WHERE json_extract(plan_json,'$.restoreJobId')=$id AND status IN ('queued','running','publishing') LIMIT 1", ("$id", id)) is string)
                throw new DomainException("Bu kopyayla geri alma işlemi sürüyor.", 409);
            remove(); Execute(db, "UPDATE music_tasks SET history_pruned=1,message='Etiket geri alma kopyası kullanıcı tarafından temizlendi.' WHERE id=$id", ("$id", id));
        }
    }
    public (string Path, long Length)? EditedMusicImport(string downloadId, string originalRelative)
    {
        using var db = Open();var directory=DownloadImportDirectory(downloadId);var original = DownloadImportFile(downloadId,originalRelative);
        var id = Scalar(db, "SELECT media_id FROM music_tasks WHERE kind='tags' AND status='completed' AND json_extract(plan_json,'$.originalPath')=$original ORDER BY created_at LIMIT 1", ("$original", original)) as string;
        if (id is null) return null;
        var media = GetMedia(id); if (media is null) return null;
        var journal = Query(db, MusicTaskSelect + " WHERE media_id=$media AND kind IN ('tags','restore-tags') AND status='completed' ORDER BY updated_at DESC,id DESC LIMIT 1", ReadMusicTask, ("$media", id)).FirstOrDefault();
        if (journal?.PlanJson is null) return null;
        var plan = JsonSerializer.Deserialize<MusicTagPlan>(journal.PlanJson, WebJson)!;
        if (media.RelativePath != plan.TargetPath || !media.RelativePath.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.Ordinal)) return null;
        var path = MediaPaths.ResolveFile(GetLibrary(media.LibraryId)!.Path, media.RelativePath);
        return StateFolders.Hash(path) == plan.TargetHash ? (media.RelativePath[(directory.Length + 1)..], new FileInfo(path).Length) : null;
    }
}
