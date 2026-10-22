using System.Globalization;
using HomeServer.Core;
using Microsoft.Data.Sqlite;

namespace HomeServer.Infrastructure;

public sealed partial class MediaStore
{
    private const string DownloadSelect = """
        SELECT id,info_hash,name,source_kind,desired_state,status,progress,total_bytes,download_speed,
            upload_speed,peers,message,imported_library_id,scan_job_id,imported_at,created_at,updated_at,revision,
            (SELECT a.content_id FROM acquisition_links a WHERE a.download_id=downloads.id),
            (SELECT c.title FROM acquisition_links a JOIN content c ON c.id=a.content_id WHERE a.download_id=downloads.id),
            (SELECT a.status FROM acquisition_links a WHERE a.download_id=downloads.id),
            (SELECT a.message FROM acquisition_links a WHERE a.download_id=downloads.id),priority,uploaded_bytes,received_bytes,seed_seconds,archived,payload_cleaned_at,
            (SELECT json_extract(a.release_json,'$.metadata.id') FROM music_download_links a WHERE a.download_id=downloads.id),
            (SELECT json_extract(a.release_json,'$.metadata.title') FROM music_download_links a WHERE a.download_id=downloads.id),
            (SELECT a.status FROM music_download_links a WHERE a.download_id=downloads.id),(SELECT a.message FROM music_download_links a WHERE a.download_id=downloads.id),
            owner_id,(SELECT u.display_name FROM users u WHERE u.id=downloads.owner_id),budget_origin,budget_generation,budget_charged_bytes,budget_wait_reason,budget_retry_at FROM downloads
        """;

    public IReadOnlyList<DownloadRecord> GetDownloads(bool includeArchived = false, Account? viewer = null)
    {
        using var db = Open();
        return Query(db, DownloadSelect + " WHERE (archived=0 OR $archived=1) AND ($viewer IS NULL OR $admin=1 OR owner_id=$viewer) ORDER BY created_at DESC", ReadDownload, ("$archived", includeArchived), ("$viewer", viewer?.Id), ("$admin", viewer?.IsAdmin == true));
    }

    public DownloadRecord? GetDownload(string id)
    {
        using var db = Open();
        return Query(db, DownloadSelect + " WHERE id=$id", ReadDownload, ("$id", id)).FirstOrDefault();
    }

    public DownloadSource GetDownloadSource(string id)
    {
        using var db = Open();
        var record = GetDownload(id) ?? throw new DomainException("İndirme bulunamadı.", 404);
        var source = (string)Scalar(db, "SELECT source FROM downloads WHERE id=$id", ("$id", id))!;
        return new DownloadSource(record, source);
    }
    public IndexerSecret? DownloadSourceProvider(string id)
    {
        lock (_writeGate)
        {
            using var db = Open();
            var source = Scalar(db, "SELECT i.id FROM acquisition_links a JOIN release_results r ON r.id=a.release_id JOIN indexers i ON i.id=r.indexer_id WHERE a.download_id=$id", ("$id", id)) as string;
            return source is null ? null : GetIndexer(source);
        }
    }

    public DownloadRecord AddDownload(string[] hashes, string name, string kind, string protectedSource, long size, AcquisitionContext? acquisition = null, MusicDownloadContext? music = null, DownloadPrincipal? principal = null)
    {
        if (acquisition is not null && music is not null) throw new DomainException("Aynı edinme komutu hem video hem müzik kapsamı taşıyamaz.");
        lock (_writeGate)
        {
            if (acquisition is not null) ValidateAutomationAcquisition(acquisition);
            if (acquisition is not null) ValidateCatalogAcquisition(acquisition);
            if (music is not null) ValidateMusicAutomationAcquisition(music);
            principal = ResolveDownloadPrincipal(principal, acquisition, music);
            using var db = Open();
            foreach (var hash in hashes)
            {
                var known = Scalar(db, "SELECT download_id FROM download_hashes WHERE hash=$hash", ("$hash", hash)) as string;
                if (known is not null)
                {
                    if (principal is not null) RequireDownloadAccess(known, RequireDownloadPrincipal(principal));
                    if (GetDownload(known)?.Archived == true && Convert.ToInt32(Scalar(db, "SELECT COUNT(*) FROM downloads WHERE archived=0")) >= 200)
                        throw new DomainException("Etkin indirme geçmişi sınırına ulaşıldı (200).", 409);
                    using var existingTransaction = db.BeginTransaction();
                    BudgetExecute(db, existingTransaction, "UPDATE downloads SET archived=0 WHERE id=$id", ("$id", known));
                    if (acquisition is not null) AttachAcquisition(db, existingTransaction, known, acquisition);
                    if (music is not null) AttachMusicAcquisition(db, existingTransaction, known, music);
                    existingTransaction.Commit();
                    return GetDownload(known)!;
                }
            }
            if (Convert.ToInt32(Scalar(db, "SELECT COUNT(*) FROM downloads WHERE archived=0"), CultureInfo.InvariantCulture) >= 200)
                throw new DomainException("İndirme geçmişi sınırına ulaşıldı (200).", 409);
            var id = Guid.NewGuid().ToString("N");
            using var transaction = db.BeginTransaction();
            using var insert = Command(db, """
                INSERT INTO downloads(id,info_hash,name,source_kind,source,desired_state,status,total_bytes,message,created_at,updated_at,owner_id,budget_origin)
                VALUES($id,$hash,$name,$kind,$source,'running','queued',$size,'İndirme sırası bekleniyor.',$now,$now,$owner,$origin)
                """, ("$id", id), ("$hash", hashes[0]), ("$name", name), ("$kind", kind), ("$source", protectedSource), ("$size", size), ("$now", Now()), ("$owner", principal?.UserId), ("$origin", principal?.Origin ?? "legacy"));
            insert.Transaction = transaction;
            insert.ExecuteNonQuery();
            foreach (var hash in hashes)
            {
                using var alias = Command(db, "INSERT INTO download_hashes(hash,download_id) VALUES($hash,$id)", ("$hash", hash), ("$id", id));
                alias.Transaction = transaction;
                alias.ExecuteNonQuery();
            }
            if(acquisition is not null) AttachAcquisition(db,transaction,id,acquisition);
            if(music is not null) AttachMusicAcquisition(db,transaction,id,music);
            DebitDownloadAdmission(db, transaction, principal, id, name, DateTimeOffset.UtcNow);
            transaction.Commit();
            return GetDownload(id)!;
        }
    }

    public void RegisterDownloadHashes(string id, string[] hashes)
    {
        lock (_writeGate)
        {
            using var db = Open();
            using var tx = db.BeginTransaction();
            foreach (var hash in hashes)
            {
                using var find = Command(db, "SELECT download_id FROM download_hashes WHERE hash=$hash", ("$hash", hash));
                find.Transaction = tx;
                var existing = find.ExecuteScalar() as string;
                if (existing is not null && existing != id) throw new DomainException("Bu torrent zaten başka bir indirme kaydında bulunuyor.", 409);
                using var insert = Command(db, "INSERT OR IGNORE INTO download_hashes(hash,download_id) VALUES($hash,$id)", ("$hash", hash), ("$id", id));
                insert.Transaction = tx;
                insert.ExecuteNonQuery();
            }
            tx.Commit();
        }
    }

    public DownloadRecord SetDownloadIntent(string id, string intent)
    {
        if (intent is not ("running" or "paused" or "cancelled")) throw new DomainException("Geçersiz indirme komutu.");
        lock (_writeGate)
        {
            using var db = Open();
            if (GetDownload(id) is null) throw new DomainException("İndirme bulunamadı.", 404);
            if (GetDownload(id)!.Archived) throw new DomainException("İndirmeyi önce arşivden çıkar.", 409);
            Execute(db, """
                UPDATE downloads SET desired_state=$intent,status=$status,message=$message,revision=revision+1,
                    download_speed=0,upload_speed=0,peers=0,budget_wait_reason=NULL,budget_retry_at=NULL,updated_at=$now WHERE id=$id
                """, ("$id", id), ("$intent", intent), ("$status", intent == "running" ? "queued" : intent),
                ("$message", intent == "running" ? "İndirme yeniden sıraya alındı." : intent == "paused" ? "İndirme duraklatıldı." : "İndirme iptal edildi. Kütüphanedeki dosyalar korunur."), ("$now", Now()));
            return GetDownload(id)!;
        }
    }

    public void UpdateDownload(string id, int revision, string status, string message, double progress, long size, long down = 0, long up = 0, int peers = 0, string? name = null)
    {
        lock (_writeGate)
        {
            using var db = Open();
            // A stale engine observation may never overwrite an explicit pause/cancel command.
            Execute(db, """
                UPDATE downloads SET status=$status,message=$message,progress=$progress,total_bytes=$size,
                    download_speed=$down,upload_speed=$up,peers=$peers,name=COALESCE($name,name),updated_at=$now
                WHERE id=$id AND desired_state='running' AND revision=$revision
                """, ("$id", id), ("$revision", revision), ("$status", status), ("$message", message),
                ("$progress", double.IsFinite(progress) ? Math.Clamp(progress, 0, 100) : 0),
                ("$size", size), ("$down", down), ("$up", up), ("$peers", peers), ("$name", name), ("$now", Now()));
        }
    }

    public void RecordImport(string id, string libraryId, string scanJobId)
    {
        lock (_writeGate)
        {
            using var db = Open();
            Execute(db, "UPDATE downloads SET imported_library_id=$library,scan_job_id=$scan,imported_at=COALESCE(imported_at,$now),updated_at=$now WHERE id=$id",
                ("$id", id), ("$library", libraryId), ("$scan", scanJobId), ("$now", Now()));
        }
    }

    public void SaveResolvedTorrent(string id, string encryptedMetadata, int? expectedRevision = null)
    {
        lock (_writeGate)
        {
            if (expectedRevision is { } expected)
            {
                var current = GetDownload(id);
                if (current is null || current.DesiredState != "running" || current.Revision != expected) throw new OperationCanceledException();
            }
            using var db = Open();
            Execute(db, "UPDATE downloads SET source_kind='torrent',source=$source WHERE id=$id", ("$id", id), ("$source", encryptedMetadata));
        }
    }

    public void RecoverDownloads()
    {
        lock (_writeGate)
        {
            using var db = Open();
            Execute(db, """
                UPDATE downloads SET download_speed=0,upload_speed=0,peers=0;
                UPDATE downloads SET status='queued',message='Kesilen indirme yeniden doğrulanacak.'
                  WHERE desired_state='running' AND status NOT IN ('failed','budget-waiting');
                """);
        }
    }

    public Library EnsureDownloadLibrary(string path, bool music = false)
    {
        lock (_writeGate)
        {
            var existing = GetLibraries(true).FirstOrDefault(l => MediaPaths.Key(l.Path) == MediaPaths.Key(path));
            if (existing is { Enabled: false }) throw new DomainException("İndirilenler kütüphanesi arşivde. İçe aktarma için yeniden etkinleştir.", 409);
            return existing ?? AddLibrary(new AddLibrary(music ? "İndirilen müzik" : "İndirilenler", path, music ? "music" : "movies"), managed: true);
        }
    }

    public ScanJob? TryPublishImport(string downloadId, int revision, string libraryId, Action publish)
    {
        lock (_writeGate)
        {
            using var db = Open();
            var download = GetDownload(downloadId)!;
            if (download.DesiredState != "running" || download.Revision != revision) throw new OperationCanceledException();
            var active = Scalar(db, "SELECT id FROM jobs WHERE library_id=$library AND status IN ('queued','running') LIMIT 1", ("$library", libraryId));
            if (active is not null) return null;
            // Directory publication and queuing are serialized with every scan claim/request.
            // A publication marker makes recovery idempotent if the process exits between them.
            publish();
            var job = QueueScan(libraryId);
            RecordImport(downloadId, libraryId, job.Id);
            return job;
        }
    }

    private static DownloadRecord ReadDownload(SqliteDataReader r) => new(
        r.GetString(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetString(4),r.GetString(5),r.GetDouble(6),r.GetInt64(7),
        r.GetInt64(8),r.GetInt64(9),r.GetInt32(10),r.GetString(11),Text(r,12),Text(r,13),Text(r,14),r.GetString(15),r.GetString(16),r.GetInt32(17),
        r.IsDBNull(18) ? null : new AcquisitionStatus(r.GetString(18),r.GetString(19),r.GetString(20),r.GetString(21)),r.GetInt32(22),r.GetInt64(23),r.GetInt64(24),r.GetInt64(25),r.GetBoolean(26),Text(r,27),
        r.IsDBNull(28) ? null : new MusicDownloadStatus(r.GetString(28),r.GetString(29),r.GetString(30),r.GetString(31)),
        new DownloadOwnership(Text(r,32),Text(r,33),r.GetString(34),r.GetInt32(35),r.GetInt64(36),Text(r,37),Text(r,38)));

    private void AttachAcquisition(SqliteConnection db,SqliteTransaction transaction,string downloadId,AcquisitionContext context)
    {
        using var music = Command(db, "SELECT download_id FROM music_download_links WHERE download_id=$id", ("$id", downloadId)); music.Transaction=transaction;
        if(music.ExecuteScalar() is string) throw new DomainException("Bu payload müzik edinmesine bağlı.",409);
        using var existing=Command(db,"SELECT content_id,season_number,episode_number FROM acquisition_links WHERE download_id=$id",("$id",downloadId));existing.Transaction=transaction;
        using(var reader=existing.ExecuteReader())
        {
            if(reader.Read())
            {
                if(reader.GetString(0)!=context.ContentId || (reader.IsDBNull(1)?(int?)null:reader.GetInt32(1))!=context.Season || (reader.IsDBNull(2)?(int?)null:reader.GetInt32(2))!=context.Episode)
                    throw new DomainException("Bu torrent başka bir içerik veya bölüm kapsamına bağlı.",409);
            }
            else
            {
                reader.Close();
                using var imported=Command(db,"SELECT imported_at FROM downloads WHERE id=$id",("$id",downloadId));
                imported.Transaction=transaction;
                if(imported.ExecuteScalar() is string)throw new DomainException("Bu payload daha önce yayımlanmış. Kütüphaneden içerik eşleştirmesini kullan.",409);
            }
        }
        using var link=Command(db,"""
            INSERT OR IGNORE INTO acquisition_links(download_id,content_id,season_number,episode_number,release_id,created_at)
            VALUES($download,$content,$season,$episode,$release,$now);
            UPDATE release_results SET download_id=$download WHERE id=$release;
            """,("$download",downloadId),("$content",context.ContentId),("$season",context.Season),("$episode",context.Episode),("$release",context.ReleaseId),("$now",Now()));
        link.Transaction=transaction;link.ExecuteNonQuery();
        var numbering = GetContent(context.ContentId)?.Metadata.Kind == "show" ? GetEpisodeNumbering(context.ContentId) : null;
        var releaseScope = AcquisitionScope(context, numbering);
        using var scope = Command(db, "UPDATE acquisition_links SET episodes_json=$scope,numbering_json=$numbering WHERE download_id=$download AND numbering_json IS NULL",
            ("$download", downloadId), ("$scope", System.Text.Json.JsonSerializer.Serialize(releaseScope, WebJson)), ("$numbering", numbering is null ? null : System.Text.Json.JsonSerializer.Serialize(numbering, WebJson))); scope.Transaction = transaction; scope.ExecuteNonQuery();
        if (context.Automation is { } automatic)
        {
            using var job = Command(db, "UPDATE automation_jobs SET status='grabbed',download_id=$download,grabbed_at=COALESCE(grabbed_at,$now),updated_at=$now,message='Doğrulama ve kütüphaneye alma bekleniyor.' WHERE id=$job AND rule_revision=$revision AND status='running'",
                ("$download", downloadId), ("$now", Now()), ("$job", automatic.JobId), ("$revision", automatic.Revision));
            job.Transaction = transaction; job.ExecuteNonQuery();
        }
    }
}
