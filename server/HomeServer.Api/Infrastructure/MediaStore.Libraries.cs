using System.Text.Json;
using HomeServer.Core;
using HomeServer.Downloads;

namespace HomeServer.Infrastructure;

public sealed partial class MediaStore
{
    public LibraryConfiguration GetLibraryConfiguration(string id)
    {
        using var db = Open(); return Query(db, "SELECT revision,options_json,last_scan_at,next_scan_at FROM libraries WHERE id=$id",
            r => new LibraryConfiguration(id, r.GetInt32(0), JsonSerializer.Deserialize<LibraryOptions>(r.GetString(1), WebJson)!, Text(r, 2), Text(r, 3)), ("$id", id)).FirstOrDefault() ?? throw new DomainException("Kütüphane bulunamadı.", 404);
    }
    public Library UpdateLibrary(string id, LibraryEdit input)
    {
        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Length > 80 || string.IsNullOrWhiteSpace(input.Path) || input.Options is null || input.Options.ScanIntervalMinutes is < 0 or > 10080
            || input.Options.ScanIntervalMinutes is > 0 and < 15 || input.Options.ExcludedPaths?.Length > 100) throw new DomainException("Kütüphane tercihleri geçersiz.");
        foreach (var excluded in input.Options.ExcludedPaths ?? []) _ = DownloadFiles.ValidateRelative(excluded);
        var current = GetLibraries(true).FirstOrDefault(library => library.Id == id) ?? throw new DomainException("Kütüphane bulunamadı.", 404);
        if (current.IsDemo && input.Enabled) throw new DomainException("Sentetik arşiv normal kütüphanede etkinleştirilemez.");
        var path = string.Equals(MediaPaths.Key(input.Path), MediaPaths.Key(current.Path), StringComparison.Ordinal) ? current.Path : MediaPaths.NormalizeRoot(input.Path);
        if (!ManagedLibraryRoot(current.Path)) ValidateExternalLibraryRoot(path);
        else if (MediaPaths.Key(path) != MediaPaths.Key(current.Path)) throw new DomainException("Yönetilen indirme kökü bu ekrandan taşınamaz.", 409);
        lock (_writeGate)
        {
            using var db = Open();
            var exclusions = Query(db, "SELECT id,relative_path FROM media WHERE library_id=$id", r => (Id: r.GetString(0), Path: r.GetString(1)), ("$id", id))
                .Select(item => (item.Id, Excluded: Scanning.LibraryTraversal.Excluded(item.Path, input.Options))).ToArray();
            var shelfExclusions=Query(db,"SELECT id,relative_path FROM shelf_assets WHERE library_id=$id",r=>(Id:r.GetString(0),Path:r.GetString(1)),("$id",id)).Select(item=>(item.Id,Excluded:Scanning.LibraryTraversal.Excluded(item.Path,input.Options))).ToArray();
            using var tx = db.BeginTransaction();
            using var command = Command(db, """
                UPDATE jobs SET cancel_requested=1,status=CASE WHEN status='queued' THEN 'cancelled' ELSE status END WHERE library_id=$id AND status IN ('queued','running');
                UPDATE libraries SET name=$name,path=$path,path_key=$key,enabled=$enabled,options_json=$options,revision=revision+1,next_scan_at=$next WHERE id=$id;
                UPDATE auto_match_jobs SET status='cancelled',message='Kütüphane tercihi değişti.' WHERE library_id=$id AND status IN ('queued','running');
                """, ("$id", id), ("$name", input.Name.Trim()), ("$path", path), ("$key", MediaPaths.Key(path)), ("$enabled", input.Enabled),
                ("$options", JsonSerializer.Serialize(input.Options, WebJson)), ("$next", input.Enabled && input.Options.ScanIntervalMinutes > 0 ? Now() : null));
            command.Transaction = tx;
            try
            {
                command.ExecuteNonQuery();
                foreach (var item in exclusions)
                {
                    using var exclude = Command(db, "UPDATE media SET excluded=$excluded WHERE id=$id", ("$id", item.Id), ("$excluded", item.Excluded)); exclude.Transaction = tx; exclude.ExecuteNonQuery();
                }
                foreach(var item in shelfExclusions)MusicSql(db,tx,"UPDATE shelf_assets SET excluded=$excluded WHERE id=$id",("$id",item.Id),("$excluded",item.Excluded));
                tx.Commit();
            }
            catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.SqliteErrorCode == 19) { throw new DomainException("Bu kök zaten başka bir kütüphaneye bağlı.", 409); }
            return GetLibraries(true).Single(library => library.Id == id);
        }
    }
    private static bool MissingPath(string root, string relative)
    {
        var path = Path.Combine(root, relative);
        if (File.Exists(path)) return false;
        var parent = Path.GetDirectoryName(path); var missingEntry = path;
        while (parent is not null && !Directory.Exists(parent) && MediaPaths.Key(parent) != MediaPaths.Key(root)) { missingEntry = parent; parent = Path.GetDirectoryName(parent); }
        if (parent is null || !Directory.Exists(parent)) return false;
        try { return !Directory.EnumerateFileSystemEntries(parent).Any(file => MediaPaths.Key(file) == MediaPaths.Key(missingEntry)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }
    public void RecordLibraryScan(string id)
    {
        lock (_writeGate)
        {
            var options = GetLibraryConfiguration(id); using var db = Open();
            Execute(db, "UPDATE libraries SET last_scan_at=$now,next_scan_at=$next WHERE id=$id", ("$id", id), ("$now", Now()),
                ("$next", options.Options.ScanIntervalMinutes > 0 ? DateTimeOffset.UtcNow.AddMinutes(options.Options.ScanIntervalMinutes).ToString("O") : null));
        }
    }
    public void ScheduleLibraryScans()
    {
        lock (_writeGate)
        {
            using var db = Open(); var due = Query(db, "SELECT id FROM libraries WHERE enabled=1 AND is_demo=0 AND next_scan_at IS NOT NULL AND next_scan_at<=$now ORDER BY next_scan_at LIMIT 10", r => r.GetString(0), ("$now", Now()));
            foreach (var id in due)
            {
                QueueScan(id); var minutes = Math.Max(15, GetLibraryConfiguration(id).Options.ScanIntervalMinutes);
                Execute(db, "UPDATE libraries SET next_scan_at=$next WHERE id=$id", ("$id", id), ("$next", DateTimeOffset.UtcNow.AddMinutes(minutes).ToString("O")));
            }
        }
    }
    public void SaveLocalMetadata(string mediaId, LocalMediaMetadata metadata)
    {
        lock (_writeGate)
        {
            using var db = Open(); Execute(db, """
                INSERT INTO local_metadata(media_id,metadata_json,title,search_text,updated_at) VALUES($id,$json,$title,$search,$now)
                ON CONFLICT(media_id) DO UPDATE SET metadata_json=$json,title=$title,search_text=$search,updated_at=$now,last_error=NULL
                """, ("$id", mediaId), ("$json", JsonSerializer.Serialize(metadata, WebJson)), ("$title", metadata.Title), ("$search", MediaPaths.SearchKey((metadata.Title ?? "") + " " + string.Join(' ', metadata.Tags))), ("$now", Now()));
        }
    }
    public LocalMediaMetadata? GetLocalMetadata(string mediaId)
    {
        using var db = Open(); var json = Scalar(db, "SELECT lm.metadata_json FROM local_metadata lm JOIN media m ON m.id=lm.media_id JOIN libraries l ON l.id=m.library_id WHERE lm.media_id=$id AND json_extract(l.options_json,'$.readLocalMetadata')=1", ("$id", mediaId)) as string;
        return json is null ? null : JsonSerializer.Deserialize<LocalMediaMetadata>(json, WebJson);
    }
    public void LocalMetadataError(string mediaId)
    {
        lock (_writeGate) { using var db = Open(); Execute(db, "INSERT INTO local_metadata(media_id,metadata_json,updated_at,last_error) VALUES($id,'null',$now,'Yerel metadata okunamadı.') ON CONFLICT(media_id) DO UPDATE SET last_error='Yerel metadata okunamadı; önceki bilgi korundu.'", ("$id", mediaId), ("$now", Now())); }
    }
    public void ClearLocalMetadata(string id) { lock (_writeGate) { using var db = Open(); Execute(db, "DELETE FROM local_metadata WHERE media_id=$id", ("$id", id)); } }
    public string? GetLocalMetadataError(string id) { using var db = Open(); return Scalar(db, "SELECT last_error FROM local_metadata WHERE media_id=$id", ("$id", id)) as string; }
    private void ValidateExternalLibraryRoot(string root)
    {
        var data = MediaPaths.Key(Path.TrimEndingDirectorySeparator(_paths.Data)); var candidate = MediaPaths.Key(root);
        if (candidate == data || candidate.StartsWith(data + Path.DirectorySeparatorChar, StringComparison.Ordinal)) throw new DomainException("Uygulamanın veri/staging alanı kaynak kütüphane olarak seçilemez.");
    }
    public string[] LibraryIgnoredRoots(string libraryRoot)
    {
        var managed = ManagedLibraryRoot(libraryRoot);
        return new[] { managed ? null : _paths.Data, _paths.Demo }.Where(path => path is not null).Cast<string>().ToArray();
    }
    private bool ManagedLibraryRoot(string path) => new[] { "library", "music-library", "live-library", "podcast-library" }.Any(name => MediaPaths.Key(path) == MediaPaths.Key(Path.Combine(_paths.Data, "downloads", name)));
}
