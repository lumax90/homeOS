using System.Globalization;
using System.Text.Json;
using HomeServer.Core;
using Microsoft.Data.Sqlite;

namespace HomeServer.Infrastructure;

public sealed partial class MediaStore(HomePaths paths)
{
    private readonly HomePaths _paths = paths;
    private readonly object _writeGate = new();
    private readonly string _connectionString = new SqliteConnectionStringBuilder
    {
        DataSource = Path.Combine(paths.State, "home.db"),
        ForeignKeys = true,
        DefaultTimeout = 5,
        Pooling = true
    }.ToString();

    private static readonly string MediaSelect = $"""
        SELECT m.id, m.library_id, l.name,
               CASE WHEN e.id IS NOT NULL THEN COALESCE(cp.title,c.title) || ' · ' || printf('S%02dE%02d',e.season_number,e.episode_number)
                    ELSE COALESCE(cp.title,c.title,lm.title,mtm.title,mt.title,m.title) END,
               m.kind, m.relative_path,
               m.mime_type, m.size_bytes, m.duration_seconds, m.width, m.height,
               m.video_codec, m.audio_codec, m.available, m.added_at, m.modified_at,
                  CASE WHEN json_extract(lm.metadata_json,'$.poster') IS NOT NULL THEN 1 ELSE m.has_thumbnail END, {MediaPosition}, {MediaWatched}, l.is_demo,
               m.title,c.id,c.kind,COALESCE(cp.title,c.title),c.original_title,COALESCE(cp.overview,c.overview),c.release_date,c.poster_url,c.backdrop_url,
               c.provider,c.provider_id,c.provider_url,c.genres_json,
               e.season_number,e.episode_number,e.title,e.overview,e.still_url,
               mm.edition,mm.matched_at,
                (SELECT COUNT(*) FROM media_matches versions JOIN media vm ON vm.id=versions.media_id JOIN libraries vl ON vl.id=vm.library_id
                  WHERE versions.content_id=mm.content_id AND (versions.episode_id IS NULL AND mm.episode_id IS NULL OR EXISTS(SELECT 1 FROM media_episode_segments va JOIN media_episode_segments vb ON vb.episode_id=va.episode_id WHERE va.media_id=m.id AND vb.media_id=vm.id)) AND vl.enabled=1 AND {MediaAccessFor("vm")}
                    AND ($all=1 OR EXISTS(SELECT 1 FROM user_libraries ul WHERE ul.user_id=$viewer AND ul.library_id=vl.id))),m.association_revision,
                (SELECT json_group_array(json_object('episodeId',s.episode_id,'season',se.season_number,'number',se.episode_number,'title',se.title,'position',s.position,'startSeconds',s.start_seconds,'endSeconds',s.end_seconds))
                  FROM media_episode_segments s JOIN episodes se ON se.id=s.episode_id WHERE s.media_id=m.id ORDER BY s.position),
                (SELECT json_object('editionId',p.edition_id,'name',ed.name,'number',p.part_number,'count',ed.part_count) FROM movie_parts p JOIN movie_editions ed ON ed.id=p.edition_id WHERE p.media_id=m.id)
        FROM media m JOIN libraries l ON l.id = m.library_id
        LEFT JOIN user_watch_state w ON w.media_id=m.id AND w.user_id=$viewer
        LEFT JOIN media_matches mm ON mm.media_id=m.id
        LEFT JOIN content c ON c.id=mm.content_id
        LEFT JOIN content_presentation cp ON cp.content_id=c.id
        LEFT JOIN episodes e ON e.id=mm.episode_id
        LEFT JOIN content_watch_state cw ON cw.content_id=mm.content_id AND cw.episode_key=COALESCE(mm.episode_id,'') AND cw.user_id=$viewer
        LEFT JOIN music_tracks mt ON mt.media_id=m.id
        LEFT JOIN music_track_matches mtm ON mtm.media_id=m.id
        LEFT JOIN local_metadata lm ON lm.media_id=m.id AND json_extract(l.options_json,'$.readLocalMetadata')=1
        """;
    private const string LibraryAccess = "($all=1 OR EXISTS(SELECT 1 FROM user_libraries ul WHERE ul.user_id=$viewer AND ul.library_id=l.id))";
    private static bool AllLibraries(Account? viewer) => viewer is null || viewer.IsAdmin || viewer.AllLibraries;
    private static (string, object?)[] ViewerArgs(Account? viewer) => [("$viewer", viewer?.Id ?? "1"), ("$all", AllLibraries(viewer)), ("$admin", viewer is null || viewer.IsAdmin), ("$maxage", viewer?.MaximumAge ?? -1), ("$unrated", viewer?.AllowUnrated ?? true), ("$discover", viewer?.CanDiscover ?? true)];
    private const string JobSelect = """
        SELECT j.id, j.library_id, l.name, j.status, j.processed, j.added,
               j.attempt, j.message, j.created_at, j.started_at, j.finished_at, j.cancel_requested
        FROM jobs j JOIN libraries l ON l.id = j.library_id
        """;

    public void Initialize()
    {
        Directory.CreateDirectory(_paths.Data);
        Directory.CreateDirectory(_paths.Thumbnails);
        using var db = Open();
        Execute(db, "PRAGMA journal_mode=WAL;");
        var version = Convert.ToInt32(Scalar(db, "PRAGMA user_version;"), CultureInfo.InvariantCulture);
        string[] migrations = ["001_initial", "002_metadata", "003_downloads", "004_product_catalog", "005_media_playback", "006_import_review", "007_household", "008_automation", "009_operations", "010_music", "011_subtitle_provider", "012_usenet", "013_personal_catalog", "014_transfer_policies", "015_library_lifecycle", "016_music_releases", "017_music_acquisition", "018_music_automation", "019_music_audio", "020_catalog_identity", "021_subtitle_versions", "022_subtitle_sync", "023_playback_quotas", "024_notification_delivery", "025_web_push", "026_offline_media", "027_watch_rooms", "028_download_budgets", "029_episode_catalog", "030_music_catalog"];
        migrations=[..migrations,"031_product_work","032_playback_segments","033_receivers","034_operations","035_source_adapters","036_live_tv","037_shelves","038_integration_lifecycle","039_source_catalog","040_games","041_native_roms","042_multisystem_roms","043_live_experience"];
        if (version > migrations.Length) throw new InvalidOperationException("Veritabanı bu Home sürümünden daha yeni.");
        for (var index = version; index < migrations.Length; index++)
        {
            var rebuilding = migrations[index] is "012_usenet" or "025_web_push" or "029_episode_catalog" or "037_shelves";
            if (rebuilding) Execute(db, "PRAGMA foreign_keys=OFF;");
            try
            {
            using var transaction = db.BeginTransaction();
            using var stream = typeof(MediaStore).Assembly.GetManifestResourceStream(
                $"HomeServer.Infrastructure.Migrations.{migrations[index]}.sql")
                ?? throw new InvalidOperationException("Veritabanı şeması bulunamadı.");
            using var reader = new StreamReader(stream);
            using var command = db.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = reader.ReadToEnd() + $"\nPRAGMA user_version={index + 1};";
            command.ExecuteNonQuery();
            if (rebuilding)
            {
                using var check = Command(db, "PRAGMA foreign_key_check;"); check.Transaction = transaction;
                using var violations = check.ExecuteReader();
                if (violations.Read()) throw new InvalidOperationException("Şema yükseltmesinde ilişkili veri kontrolü başarısız.");
            }
            transaction.Commit();
            }
            finally { if (rebuilding) Execute(db, "PRAGMA foreign_keys=ON;"); }
        }
        if (version < 7)
            foreach (var user in Query(db, "SELECT id,display_name FROM users", r => (Id: r.GetString(0), Name: r.GetString(1))))
                Execute(db, "UPDATE users SET name_key=$key WHERE id=$id", ("$id", user.Id), ("$key", MediaPaths.SearchKey(user.Name)));
    }

    public Owner? GetOwner()
    {
        using var db = Open();
        using var cmd = Command(db, "SELECT display_name, password_hash FROM owner WHERE id=1");
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? new Owner(reader.GetString(0), reader.GetString(1)) : null;
    }

    public void CreateOwner(Owner owner)
    {
        lock (_writeGate)
        {
            using var db = Open();
            try
            {
                using var transaction = db.BeginTransaction();
                using var command = Command(db, """
                    INSERT INTO owner(id,display_name,password_hash) VALUES(1,$name,$hash);
                    INSERT INTO users(id,display_name,name_key,password_hash,role,can_acquire,auto_approve,monthly_request_limit,created_at)
                    VALUES('1',$name,$key,$hash,'admin',1,1,0,$now);
                    """, ("$name", owner.DisplayName), ("$key", MediaPaths.SearchKey(owner.DisplayName)), ("$hash", owner.PasswordHash), ("$now", Now()));
                command.Transaction = transaction; command.ExecuteNonQuery(); transaction.Commit();
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
            { throw new DomainException("Bu sunucunun kurulumu zaten tamamlandı.", 409); }
        }
    }

    public IReadOnlyList<Library> GetLibraries(bool includeArchived = false, Account? viewer = null)
    {
        using var db = Open();
        return Query(db, $"""
            SELECT l.id, l.name, l.path, l.kind, l.is_demo, l.created_at, SUM(CASE WHEN m.id IS NOT NULL AND {MediaAccess} THEN 1 ELSE 0 END)+(SELECT COUNT(*) FROM shelf_assets a WHERE a.library_id=l.id AND {ShelfAccess}),l.enabled
            FROM libraries l LEFT JOIN media m ON m.library_id=l.id
            WHERE (l.enabled=1 OR $archived=1) AND {LibraryAccess}
            GROUP BY l.id ORDER BY l.created_at
            """, r => new Library(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3),
                r.GetBoolean(4), r.GetString(5), r.GetInt32(6),r.GetBoolean(7)),[("$archived",includeArchived), ..ViewerArgs(viewer)]);
    }

    public Library? GetLibrary(string id, Account? viewer = null) => GetLibraries(viewer: viewer).FirstOrDefault(l => l.Id == id);

    public Library AddLibrary(AddLibrary input, bool managed = false)
    {
        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Trim().Length > 80)
            throw new DomainException("Kütüphane adı 1–80 karakter olmalı.");
        if (input.Kind is not ("movies" or "shows" or "music" or "photos" or "books" or "audiobooks"))
            throw new DomainException("Geçerli bir kütüphane türü seç.");
        var root = MediaPaths.NormalizeRoot(input.Path);
        if (!managed) ValidateExternalLibraryRoot(root);
        var isDemo = _paths.Demo is not null && MediaPaths.Key(root) == MediaPaths.Key(Path.TrimEndingDirectorySeparator(_paths.Demo));
        if (isDemo) throw new DomainException("Sentetik test klasörü normal kütüphane olarak kullanılamaz. Gerçek medya klasörünü seç.");
        var library = new Library(Guid.NewGuid().ToString("N"), input.Name.Trim(), root, input.Kind, isDemo, Now(), 0);
        lock (_writeGate)
        {
            if (GetLibraries(true).Count >= 100) throw new DomainException("En fazla 100 kütüphane bağlanabilir.");
            using var db = Open();
            try
            {
                Execute(db, """
                    INSERT INTO libraries(id,name,path,path_key,kind,is_demo,created_at)
                    VALUES($id,$name,$path,$key,$kind,$demo,$now)
                    """, ("$id", library.Id), ("$name", library.Name), ("$path", root),
                    ("$key", MediaPaths.Key(root)), ("$kind", input.Kind), ("$demo", isDemo), ("$now", library.CreatedAt));
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
            { throw new DomainException("Bu klasör zaten kütüphanende.", 409); }
        }
        return library;
    }

    public CatalogPage GetCatalog(string? search, string? kind, string? libraryId, int offset, int limit, Account? viewer = null)
    {
        offset = Math.Max(0, offset);
        limit = Math.Clamp(limit, 1, 120);
        var filter = " WHERE l.enabled=1 AND " + LibraryAccess + " AND " + MediaAccess + " AND ($q='' OR m.search_text LIKE $pattern ESCAPE '~' OR c.search_text LIKE $pattern ESCAPE '~' OR mt.search_text LIKE $pattern ESCAPE '~' OR mtm.search_text LIKE $pattern ESCAPE '~' OR cp.search_text LIKE $pattern ESCAPE '~' OR lm.search_text LIKE $pattern ESCAPE '~' OR EXISTS(SELECT 1 FROM content_aliases ca WHERE ca.content_id=c.id AND ca.search_text LIKE $pattern ESCAPE '~') OR EXISTS(SELECT 1 FROM content_names cn WHERE cn.content_id=c.id AND cn.search_text LIKE $pattern ESCAPE '~')) AND ($kind='' OR m.kind=$kind) AND ($library='' OR m.library_id=$library)";
        var q = MediaPaths.SearchKey(search?.Trim() ?? "");
        var escaped = q.Replace("~", "~~").Replace("%", "~%").Replace("_", "~_");
        (string, object?)[] args = [("$q", q), ("$pattern", "%" + escaped + "%"), ("$kind", kind ?? ""),
            ("$library", libraryId ?? ""), ("$limit", limit), ("$offset", offset), ..ViewerArgs(viewer)];
        using var db = Open();
        var total = Convert.ToInt32(Scalar(db, "SELECT COUNT(*) FROM media m JOIN libraries l ON l.id=m.library_id LEFT JOIN media_matches mm ON mm.media_id=m.id LEFT JOIN content c ON c.id=mm.content_id LEFT JOIN content_presentation cp ON cp.content_id=c.id LEFT JOIN music_tracks mt ON mt.media_id=m.id LEFT JOIN music_track_matches mtm ON mtm.media_id=m.id LEFT JOIN local_metadata lm ON lm.media_id=m.id AND json_extract(l.options_json,'$.readLocalMetadata')=1" + filter, args), CultureInfo.InvariantCulture);
        var items = Query(db, MediaSelect + filter + " ORDER BY m.added_at DESC, m.id LIMIT $limit OFFSET $offset", ReadMedia, args);
        return new CatalogPage(items, total, offset, limit);
    }

    public MediaItem? GetMedia(string id, Account? viewer = null)
    {
        using var db = Open();
        return Query(db, MediaSelect + " WHERE m.id=$id AND l.enabled=1 AND " + LibraryAccess + " AND " + MediaAccess, ReadMedia, [("$id", id), ..ViewerArgs(viewer)]).FirstOrDefault();
    }

    public IReadOnlyList<MediaItem> GetContinueWatching(Account? viewer = null)
    {
        using var db = Open();
        var select = MediaSelect.Replace("FROM media m JOIN libraries l", """
            ,COALESCE(cw.updated_at,w.updated_at) AS playback_updated,
             ROW_NUMBER() OVER(PARTITION BY COALESCE(cw.source_media_id,mm.content_id,m.id),CASE WHEN cw.source_media_id IS NULL THEN COALESCE(mm.episode_id,'') ELSE '' END
               ORDER BY CASE WHEN cw.source_media_id=m.id THEN 0 ELSE 1 END,m.height DESC,m.id) AS resume_rank
            FROM media m JOIN libraries l
            """, StringComparison.Ordinal);
        return Query(db, "WITH resume AS (" + select + $" WHERE ({MediaPosition})>2 AND ({MediaWatched})=0 AND m.available=1 AND l.enabled=1 AND "
            + LibraryAccess + " AND " + MediaAccess + ") SELECT * FROM resume WHERE resume_rank=1 ORDER BY playback_updated DESC LIMIT 12", ReadMedia, ViewerArgs(viewer));
    }

    public LibraryStats GetStats(Account? viewer = null)
    {
        using var db = Open();
        using var cmd = Command(db, $"""
            SELECT (SELECT COUNT(*) FROM libraries l WHERE enabled=1 AND {LibraryAccess}), COUNT(*)+(SELECT COUNT(*) FROM shelf_assets a JOIN libraries l ON l.id=a.library_id WHERE l.enabled=1 AND {LibraryAccess} AND {ShelfAccess}),
                   COALESCE(SUM(m.kind='video'),0), COALESCE(SUM(m.kind='audio'),0),
                   COALESCE(SUM(CASE WHEN m.available=1 THEN m.size_bytes ELSE 0 END),0)+(SELECT COALESCE(SUM(a.size_bytes),0) FROM shelf_assets a JOIN libraries l ON l.id=a.library_id WHERE l.enabled=1 AND a.available=1 AND {LibraryAccess} AND {ShelfAccess}),
                   (SELECT COUNT(*) FROM jobs j JOIN libraries l ON l.id=j.library_id WHERE l.enabled=1 AND {LibraryAccess} AND j.status IN ('queued','running'))
              FROM media m JOIN libraries l ON l.id=m.library_id WHERE l.enabled=1 AND {LibraryAccess} AND {MediaAccess}
            """, ViewerArgs(viewer));
        using var r = cmd.ExecuteReader();
        r.Read();
        return new LibraryStats(r.GetInt32(0), r.GetInt32(1), r.GetInt32(2), r.GetInt32(3), r.GetInt64(4), r.GetInt32(5));
    }

    public (string Id, bool Added, bool NeedsProbe) UpsertFile(string libraryId, SourceFile file, string scanToken, string? filesystemId = null)
    {
        lock (_writeGate)
        {
            using var db = Open();
            var key = MediaPaths.Key(file.RelativePath);
            using var find = Command(db, "SELECT id,size_bytes,modified_at,duration_seconds,has_thumbnail FROM media WHERE library_id=$library AND path_key=$path",
                ("$library", libraryId), ("$path", key));
            string? id = null;
            var changed = true;
            var needsProbe = true;
            using (var reader = find.ExecuteReader())
            {
                if (reader.Read())
                {
                    id = reader.GetString(0);
                    changed = reader.GetInt64(1) != file.Size || reader.GetString(2) != file.ModifiedAt;
                    needsProbe = changed || reader.IsDBNull(3) || (file.Kind == "video" && !reader.GetBoolean(4));
                }
            }
            if (id is null && filesystemId is not null)
            {
                var root = GetLibrary(libraryId)!.Path;
                var candidates = Query(db, "SELECT id,relative_path,size_bytes,modified_at,duration_seconds,has_thumbnail FROM media WHERE library_id=$library AND filesystem_id=$fs", r => (Id: r.GetString(0), Path: r.GetString(1), Size: r.GetInt64(2), Modified: r.GetString(3), Probed: !r.IsDBNull(4), Thumbnail: r.GetBoolean(5)), ("$library", libraryId), ("$fs", filesystemId))
                    .Where(candidate => MissingPath(root, candidate.Path)).ToArray();
                if (candidates.Length == 1)
                {
                    id = candidates[0].Id;
                    changed = candidates[0].Size != file.Size || candidates[0].Modified != file.ModifiedAt;
                    needsProbe = changed || !candidates[0].Probed || file.Kind == "video" && !candidates[0].Thumbnail;
                    Execute(db, "UPDATE media SET relative_path=$relative,path_key=$key WHERE id=$id", ("$id", id), ("$relative", file.RelativePath), ("$key", key));
                }
            }
            var added = id is null;
            id ??= Guid.NewGuid().ToString("N");
            Execute(db, """
                INSERT INTO media(id,library_id,relative_path,path_key,title,search_text,kind,mime_type,
                    size_bytes,modified_at,added_at,last_seen_scan,filesystem_id)
                VALUES($id,$library,$relative,$path,$title,$search,$kind,$mime,$size,$modified,$now,$scan,$fs)
                ON CONFLICT(library_id,path_key) DO UPDATE SET
                    title=$title,search_text=$search,size_bytes=$size,modified_at=$modified,
                    available=1,excluded=0,last_seen_scan=$scan,filesystem_id=COALESCE($fs,filesystem_id),
                    duration_seconds=CASE WHEN $changed THEN NULL ELSE duration_seconds END,
                    width=CASE WHEN $changed THEN NULL ELSE width END,
                    height=CASE WHEN $changed THEN NULL ELSE height END,
                    video_codec=CASE WHEN $changed THEN NULL ELSE video_codec END,
                    audio_codec=CASE WHEN $changed THEN NULL ELSE audio_codec END,
                    has_thumbnail=CASE WHEN $changed THEN 0 ELSE has_thumbnail END
                """, ("$id", id), ("$library", libraryId), ("$relative", file.RelativePath), ("$path", key),
                ("$title", file.Title), ("$search", MediaPaths.SearchKey(file.Title)), ("$kind", file.Kind),
                ("$mime", file.MimeType), ("$size", file.Size), ("$modified", file.ModifiedAt),
                ("$now", Now()), ("$scan", scanToken), ("$changed", changed), ("$fs", filesystemId));
            return (id, added, needsProbe);
        }
    }

    public void SaveProbe(string id, ProbeResult? probe, bool hasThumbnail)
    {
        lock (_writeGate)
        {
            using var db = Open();
            Execute(db, """
                UPDATE media SET duration_seconds=$duration,width=$width,height=$height,
                    video_codec=$video,audio_codec=$audio,has_thumbnail=$thumb WHERE id=$id
                """, ("$id", id), ("$duration", probe?.Duration), ("$width", probe?.Width),
                ("$height", probe?.Height), ("$video", probe?.VideoCodec), ("$audio", probe?.AudioCodec), ("$thumb", hasThumbnail));
            if (probe?.Music is { } music) SaveMusicTags(id, music);
        }
    }

    public void FinishScan(string libraryId, string scanToken, string jobId, int processed, int added)
    {
        lock (_writeGate)
        {
            using var db = Open();
            if (Convert.ToInt32(Scalar(db, "SELECT cancel_requested FROM jobs WHERE id=$id", ("$id", jobId)), CultureInfo.InvariantCulture) == 1)
                throw new OperationCanceledException("Tarama iptal edildi.");
            using var tx = db.BeginTransaction();
            // Availability and completion are one transaction, and are never run on failure/cancellation.
            using var cmd = Command(db, """
                UPDATE media SET available=0 WHERE library_id=$library AND last_seen_scan<>$scan;
                UPDATE jobs SET status='completed',processed=$processed,added=$added,
                    message=$message,finished_at=$now WHERE id=$id;
                """, ("$library", libraryId), ("$scan", scanToken), ("$processed", processed),
                ("$added", added), ("$message", $"{processed} medya dosyası tarandı. {added} yeni içerik eklendi."),
                ("$now", Now()), ("$id", jobId));
            cmd.Transaction = tx;
            cmd.ExecuteNonQuery();
            tx.Commit();
        }
    }

    public void SaveProgress(string mediaId, PlaybackProgress progress, Account? viewer = null)
        => WriteProgress(mediaId, progress, viewer);
    private void WriteProgress(string mediaId, PlaybackProgress progress, Account? viewer, Action<SqliteConnection, SqliteTransaction>? completing = null)
    {
        if (progress.ExpectedUserId is not null && progress.ExpectedUserId != (viewer?.Id ?? "1")) throw new DomainException("Profil değişti; eski oynatıcı konumu uygulanmadı.", 409);
        if (!double.IsFinite(progress.PositionSeconds) || progress.PositionSeconds < 0 || progress.PositionSeconds > 31_536_000)
            throw new DomainException("Geçersiz oynatma konumu.");
        if (progress.ClientId is not null && (!Guid.TryParseExact(progress.ClientId, "N", out _) || progress.Sequence is null or < 0)
            || progress.ClientId is null && progress.Sequence is not null)
            throw new DomainException("Oynatma olayı sırası geçersiz.");
        var item = GetMedia(mediaId, viewer) ?? throw new DomainException("İçerik bulunamadı.", 404);
        if (progress.ExpectedMatchRevision is { } expected && expected != item.MatchRevision) throw new DomainException("Dosya eşleştirmesi değişti; eski oynatıcı konumu uygulanmadı.", 409);
        var duration = item.DurationSeconds ?? progress.DurationSeconds;
        if (duration is not null && (!double.IsFinite(duration.Value) || duration < 0 || duration > 31_536_000))
            throw new DomainException("Geçersiz medya süresi.");
        var position = duration is > 0 ? Math.Min(progress.PositionSeconds, duration.Value) : progress.PositionSeconds;
        var watched = duration is > 0 && position >= duration * 0.95;
        lock (_writeGate)
        {
            if (GetMedia(mediaId, viewer)?.MatchRevision != item.MatchRevision) throw new DomainException("Oynatma sırasında dosya eşleştirmesi değişti.", 409);
            using var db = Open();
            using var tx = db.BeginTransaction();
            using var command = Command(db, """
                INSERT INTO user_watch_state(user_id,media_id,position_seconds,watched,updated_at,event_client,event_sequence) VALUES($user,$id,$position,$watched,$now,$client,$sequence)
                ON CONFLICT(user_id,media_id) DO UPDATE SET position_seconds=$position,watched=$watched,updated_at=$now,event_client=$client,event_sequence=$sequence
                WHERE $client IS NULL OR user_watch_state.event_client IS NOT $client OR user_watch_state.event_sequence<$sequence;
                """, ("$user", viewer?.Id ?? "1"), ("$id", mediaId), ("$position", position), ("$watched", watched), ("$now", Now()), ("$client", progress.ClientId), ("$sequence", progress.Sequence));
            command.Transaction = tx; command.ExecuteNonQuery(); SaveComposedProgress(db, tx, item, progress, viewer?.Id ?? "1", position, duration); completing?.Invoke(db, tx); tx.Commit();
        }
    }

    public IReadOnlyList<ScanJob> GetJobs()
    {
        using var db = Open();
        return Query(db, JobSelect + " WHERE l.enabled=1 ORDER BY j.created_at DESC LIMIT 50", ReadJob);
    }

    public ScanJob? GetJob(string id)
    {
        using var db = Open();
        return Query(db, JobSelect + " WHERE j.id=$id", ReadJob, ("$id", id)).FirstOrDefault();
    }

    public ScanJob QueueScan(string libraryId)
    {
        _ = GetLibrary(libraryId) ?? throw new DomainException("Kütüphane bulunamadı.", 404);
        lock (_writeGate)
        {
            using var db = Open();
            var existing = Query(db, JobSelect + " WHERE j.library_id=$library AND j.status IN ('queued','running')",
                ReadJob, ("$library", libraryId)).FirstOrDefault();
            if (existing is not null) return existing;
            var id = Guid.NewGuid().ToString("N");
            Execute(db, "INSERT INTO jobs(id,library_id,status,message,created_at) VALUES($id,$library,'queued','Tarama sırası bekleniyor.',$now)",
                ("$id", id), ("$library", libraryId), ("$now", Now()));
            return GetJob(id)!;
        }
    }

    public ScanJob? TryClaimJob()
    {
        lock (_writeGate)
        {
            using var db = Open();
            var id = Scalar(db, "SELECT j.id FROM jobs j JOIN libraries l ON l.id=j.library_id WHERE j.status='queued' AND l.enabled=1 AND NOT EXISTS(SELECT 1 FROM music_tasks mt WHERE mt.library_id=j.library_id AND mt.status='publishing') AND NOT EXISTS(SELECT 1 FROM dvr_recordings r WHERE r.status='publishing' AND json_extract(r.publication_json,'$.libraryId')=j.library_id) ORDER BY j.created_at LIMIT 1") as string;
            if (id is null) return null;
            Execute(db, """
                UPDATE jobs SET status='running',attempt=attempt+1,processed=0,added=0,
                    message='Klasör inceleniyor.',started_at=$now,finished_at=NULL WHERE id=$id
                """, ("$id", id), ("$now", Now()));
            return GetJob(id);
        }
    }

    public void RecoverInterruptedJobs()
    {
        lock (_writeGate)
        {
            using var db = Open();
            Execute(db, """
                UPDATE jobs SET status='cancelled',message='Tarama iptal edildi.',finished_at=$now
                    WHERE status IN ('running','queued') AND cancel_requested=1;
                UPDATE jobs SET status='queued',message='Kesilen tarama yeniden başlatılacak.',started_at=NULL
                    WHERE status='running';
                """, ("$now", Now()));
        }
    }

    public void UpdateJobProgress(string id, int processed, int added, string message)
    {
        lock (_writeGate)
        {
            using var db = Open();
            Execute(db, "UPDATE jobs SET processed=$processed,added=$added,message=$message WHERE id=$id AND status='running'",
                ("$id", id), ("$processed", processed), ("$added", added), ("$message", message));
        }
    }

    public void EndJob(string id, string status, string message)
    {
        if (status is not ("failed" or "cancelled" or "queued")) throw new ArgumentOutOfRangeException(nameof(status));
        lock (_writeGate)
        {
            using var db = Open();
            Execute(db, "UPDATE jobs SET status=$status,message=$message,finished_at=$end WHERE id=$id",
                ("$id", id), ("$status", status), ("$message", message), ("$end", status == "queued" ? null : Now()));
        }
    }

    public void RequestCancellation(string id)
    {
        lock (_writeGate)
        {
            using var db = Open();
            Execute(db, """
                UPDATE jobs SET cancel_requested=1,
                  status=CASE WHEN status='queued' THEN 'cancelled' ELSE status END,
                  message='Tarama iptal ediliyor.',
                  finished_at=CASE WHEN status='queued' THEN $now ELSE finished_at END
                WHERE id=$id AND status IN ('queued','running')
                """, ("$id", id), ("$now", Now()));
        }
    }

    private SqliteConnection Open()
    {
        var db = new SqliteConnection(_connectionString);
        db.Open();
        return db;
    }

    private static SqliteCommand Command(SqliteConnection db, string sql, params (string Name, object? Value)[] args)
    {
        var command = db.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in args) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return command;
    }

    private static int Execute(SqliteConnection db, string sql, params (string, object?)[] args)
    {
        using var cmd = Command(db, sql, args);
        return cmd.ExecuteNonQuery();
    }

    private static object? Scalar(SqliteConnection db, string sql, params (string, object?)[] args)
    {
        using var cmd = Command(db, sql, args);
        return cmd.ExecuteScalar();
    }

    private static List<T> Query<T>(SqliteConnection db, string sql, Func<SqliteDataReader, T> map, params (string, object?)[] args)
    {
        using var cmd = Command(db, sql, args);
        using var reader = cmd.ExecuteReader();
        var result = new List<T>();
        while (reader.Read()) result.Add(map(reader));
        return result;
    }

    private static string? Text(SqliteDataReader reader, int i) => reader.IsDBNull(i) ? null : reader.GetString(i);
    private static MediaItem ReadMedia(SqliteDataReader r) => new(
        r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5), r.GetString(6),
        r.GetInt64(7), r.IsDBNull(8) ? null : r.GetDouble(8), r.IsDBNull(9) ? null : r.GetInt32(9),
        r.IsDBNull(10) ? null : r.GetInt32(10), Text(r, 11), Text(r, 12), r.GetBoolean(13),
        r.GetString(14), r.GetString(15), r.GetBoolean(16), r.GetDouble(17), r.GetBoolean(18), r.GetBoolean(19),
        r.GetString(20), r.IsDBNull(21) ? null : new ContentMatch(
            r.GetString(21),r.GetString(22),r.GetString(23),r.GetString(24),r.GetString(25),Text(r,26),
            Text(r,27),Text(r,28),r.GetString(29),r.GetInt32(30),r.GetString(31),
            JsonSerializer.Deserialize<string[]>(r.GetString(32)) ?? [],
            r.IsDBNull(33) ? null : r.GetInt32(33),r.IsDBNull(34) ? null : r.GetInt32(34),
            Text(r,35),Text(r,36),Text(r,37),r.GetString(38),r.GetString(39),r.GetInt32(40)),r.GetInt32(41),
        JsonSerializer.Deserialize<MediaEpisodeSegment[]>(r.GetString(42), WebJson)?.OrderBy(segment => segment.Position).ToArray(),r.IsDBNull(43) ? null : JsonSerializer.Deserialize<MoviePart>(r.GetString(43), WebJson));
    private static ScanJob ReadJob(SqliteDataReader r) => new(
        r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetInt32(4), r.GetInt32(5),
        r.GetInt32(6), r.GetString(7), r.GetString(8), Text(r, 9), Text(r, 10), r.GetBoolean(11));
    private static string Now() => DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
}
