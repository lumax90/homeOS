using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using HomeServer.Core;
using Microsoft.Data.Sqlite;

namespace HomeServer.Infrastructure;

public sealed partial class MediaStore
{
    public MusicSourceResult SaveMusicSource(IndexerInfo source, RawRelease release, MusicRelease target, MusicQualityRules rules, MusicReleaseEvaluation evaluation, string protectedUrl)
    {
        lock (_writeGate)
        {
            RequireIndexerSnapshot(source);
            using var db = Open();
            Execute(db, "DELETE FROM music_release_results WHERE download_id IS NULL AND expires_at<$now AND id NOT IN (SELECT result_id FROM music_automation_jobs WHERE result_id IS NOT NULL)", ("$now", Now()));
            if (Convert.ToInt32(Scalar(db, "SELECT COUNT(*) FROM music_release_results")) >= 20000) throw new DomainException("Müzik arama önbelleği sınırına ulaşıldı.", 429);
            var fingerprint = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(release.InfoHash?.ToLowerInvariant() ?? source.Id + ":" + release.Guid)));
            var id = Guid.NewGuid().ToString("N"); Execute(db, """
                INSERT INTO music_release_results(id,indexer_id,title,protocol,size_bytes,seeders,evaluation_json,release_json,rules_json,protected_url,info_hash,expires_at,seed_rules_json,fingerprint)
                VALUES($id,$indexer,$title,$protocol,$size,$seeders,$evaluation,$release,$rules,$url,$hash,$expires,$seedRules,$fingerprint)
                """, ("$id", id), ("$indexer", source.Id), ("$title", release.Title), ("$protocol", release.Protocol), ("$size", release.SizeBytes), ("$seeders", release.Seeders),
                ("$evaluation", JsonSerializer.Serialize(evaluation, WebJson)), ("$release", JsonSerializer.Serialize(target, WebJson)), ("$rules", JsonSerializer.Serialize(rules, WebJson)), ("$url", protectedUrl), ("$hash", release.InfoHash), ("$expires", DateTimeOffset.UtcNow.AddMinutes(30).ToString("O")), ("$seedRules", JsonSerializer.Serialize(Sources.ReleaseEvaluator.SeedRules(release), WebJson)), ("$fingerprint", fingerprint));
            return new MusicSourceResult(id, source.Id, source.Name, release.Title, release.Protocol, release.SizeBytes, release.Seeders, evaluation, null, fingerprint);
        }
    }
    public StoredMusicResult GetMusicSource(string id)
    {
        using var db = Open(); return Query(db, """
            SELECT r.id,r.indexer_id,i.name,r.title,r.protocol,r.size_bytes,r.seeders,r.evaluation_json,r.download_id,r.release_json,r.rules_json,r.protected_url,r.info_hash,r.expires_at,r.fingerprint
            FROM music_release_results r JOIN indexers i ON i.id=r.indexer_id WHERE r.id=$id
            """, r => new StoredMusicResult(new MusicSourceResult(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.IsDBNull(5) ? null : r.GetInt64(5), r.IsDBNull(6) ? null : r.GetInt32(6), JsonSerializer.Deserialize<MusicReleaseEvaluation>(r.GetString(7), WebJson)!, Text(r, 8), r.GetString(14)),
                JsonSerializer.Deserialize<MusicRelease>(r.GetString(9), WebJson)!, JsonSerializer.Deserialize<MusicQualityRules>(r.GetString(10), WebJson)!, r.GetString(11), Text(r, 12), r.GetString(13)), ("$id", id)).FirstOrDefault() ?? throw new DomainException("Müzik arama sonucu bulunamadı.", 404);
    }
    private static void AttachMusicAcquisition(SqliteConnection db, SqliteTransaction tx, string downloadId, MusicDownloadContext context)
    {
        using var current = Command(db, "SELECT r.id FROM music_release_results r JOIN indexers i ON i.id=r.indexer_id WHERE r.id=$id AND i.enabled=1 AND (r.download_id IS NOT NULL OR r.expires_at>$now)", ("$id", context.ResultId), ("$now", Now())); current.Transaction = tx;
        if (current.ExecuteScalar() is null) throw new DomainException("Müzik arama sonucu veya kaynak bağlantısı değişti; yeniden ara.", 409);
        using var find = Command(db, "SELECT release_json FROM music_download_links WHERE download_id=$id", ("$id", downloadId)); find.Transaction = tx;
        var existing = find.ExecuteScalar() as string;
        if (existing is not null && JsonSerializer.Deserialize<MusicRelease>(existing, WebJson)!.Metadata.Id != context.Release.Metadata.Id) throw new DomainException("Bu payload başka bir albüm yayınına bağlı.", 409);
        using var video = Command(db, "SELECT download_id FROM acquisition_links WHERE download_id=$id", ("$id", downloadId)); video.Transaction = tx;
        if (video.ExecuteScalar() is string) throw new DomainException("Bu payload film/dizi edinmesine bağlı.", 409);
        using var imported = Command(db, "SELECT imported_at FROM downloads WHERE id=$id", ("$id", downloadId)); imported.Transaction = tx;
        if (imported.ExecuteScalar() is string && existing is null) throw new DomainException("Bu paket zaten yayımlanmış; yerel albüm eşleştirmesini kullan.", 409);
        using var insert = Command(db, """
            INSERT OR IGNORE INTO music_download_links(download_id,result_id,release_json,rules_json,upgrade_from_json) VALUES($id,$result,$release,$rules,$floor);
            UPDATE music_release_results SET download_id=$id WHERE id=$result;
            """, ("$id", downloadId), ("$result", context.ResultId), ("$release", JsonSerializer.Serialize(context.Release, WebJson)), ("$rules", JsonSerializer.Serialize(context.Rules, WebJson)), ("$floor", context.UpgradeFrom is null ? null : JsonSerializer.Serialize(context.UpgradeFrom, WebJson)));
        insert.Transaction = tx; insert.ExecuteNonQuery();
        if (context.Automation is { } lease)
        {
            using var job = Command(db, "UPDATE music_automation_jobs SET status='grabbed',download_id=$download,grabbed_at=COALESCE(grabbed_at,$now),updated_at=$now,message='Albüm doğrulama ve import kuyruğunda.' WHERE id=$job AND revision=$revision AND status='running'",
                ("$download", downloadId), ("$now", Now()), ("$job", lease.JobId), ("$revision", lease.Revision)); job.Transaction = tx; job.ExecuteNonQuery();
        }
    }
    public MusicDownloadWork? GetMusicAcquisition(string downloadId)
    {
        using var db = Open(); return Query(db, "SELECT release_json,COALESCE(import_rules_json,rules_json),status,message,upgrade_from_json FROM music_download_links WHERE download_id=$id",
            r => new MusicDownloadWork(downloadId, JsonSerializer.Deserialize<MusicRelease>(r.GetString(0), WebJson)!, JsonSerializer.Deserialize<MusicQualityRules>(r.GetString(1), WebJson)!, r.GetString(2), r.GetString(3), r.IsDBNull(4) ? null : JsonSerializer.Deserialize<MusicAudioQuality>(r.GetString(4), WebJson)), ("$id", downloadId)).FirstOrDefault();
    }
    public void SaveMusicValidation(string downloadId, int revision, IReadOnlyList<VerifiedMusicFile> files)
    {
        lock (_writeGate)
        {
            var record = GetDownload(downloadId)!;
            if (record.Revision != revision || record.DesiredState != "running") throw new OperationCanceledException();
            using var db = Open(); using var tx = db.BeginTransaction();
            using var clear = Command(db, "DELETE FROM music_download_files WHERE download_id=$id", ("$id", downloadId)); clear.Transaction = tx; clear.ExecuteNonQuery();
            foreach (var file in files)
            {
                using var insert = Command(db, "INSERT INTO music_download_files(download_id,relative_path,size_bytes,disc_number,track_number) VALUES($id,$path,$size,$disc,$track)",
                    ("$id", downloadId), ("$path", file.RelativePath), ("$size", file.Length), ("$disc", file.Disc), ("$track", file.Position)); insert.Transaction = tx; insert.ExecuteNonQuery();
            }
            tx.Commit();
        }
    }
    public IReadOnlyList<string> PendingMusicLinks()
    {
        using var db = Open(); return Query(db, "SELECT a.download_id FROM music_download_links a JOIN downloads d ON d.id=a.download_id JOIN jobs j ON j.id=d.scan_job_id WHERE a.status='waiting' AND d.imported_at IS NOT NULL AND j.status IN ('completed','failed','cancelled') LIMIT 20", r => r.GetString(0));
    }
    public void LinkMusicAcquisition(string downloadId)
    {
        lock (_writeGate)
        {
            var download = GetDownload(downloadId) ?? throw new DomainException("İndirme bulunamadı.", 404);
            var work = GetMusicAcquisition(downloadId) ?? throw new DomainException("Müzik edinmesi bulunamadı.", 404);
            if (download.ScanJobId is null || GetJob(download.ScanJobId)?.Status != "completed") throw new DomainException("Müzik klasörü taraması tamamlanmadı.");
            using var db = Open();
            var files = Query(db, "SELECT relative_path,size_bytes,disc_number,track_number FROM music_download_files WHERE download_id=$id", r => new VerifiedMusicFile(r.GetString(0), r.GetInt64(1), r.GetInt32(2), r.GetInt32(3)), ("$id", downloadId));
            var assignments = new List<MusicTrackAssignment>(); var albums = new HashSet<string>();
            foreach (var file in files)
            {
                var edited = EditedMusicImport(downloadId, file.RelativePath);
                var row = Query(db, "SELECT m.id,mt.album_id,mm.release_track_id FROM media m JOIN music_tracks mt ON mt.media_id=m.id LEFT JOIN music_track_matches mm ON mm.media_id=m.id WHERE m.library_id=$library AND m.path_key=$path AND m.size_bytes=$size AND m.available=1",
                    r => (Id: r.GetString(0), Album: r.GetString(1), Match: Text(r, 2)), ("$library", download.ImportedLibraryId), ("$path", MediaPaths.Key(DownloadImportFile(downloadId,edited?.Path??file.RelativePath))), ("$size", edited?.Length ?? file.Length)).SingleOrDefault();
                if (row.Id is null) throw new DomainException("Doğrulanan ses dosyası katalogda bulunamadı.");
                var expected = work.Release.Tracks.Single(track => track.Disc == file.Disc && track.Position == file.Position);
                if (row.Match is not null && row.Match != expected.Id) throw new DomainException("Dosya kullanıcı tarafından başka recording'e eşlenmiş.");
                albums.Add(row.Album); assignments.Add(new MusicTrackAssignment(row.Id, file.Disc, file.Position));
            }
            if (assignments.Count == 0 || albums.Count != 1) throw new DomainException("Pakette tek albüm grubu belirlenemedi; eşleştirmeyi incele.");
            MatchMusicAlbum(albums.Single(), work.Release, GetAccount("1") ?? throw new DomainException("Sahip hesabı bulunamadı."), assignments.ToArray());
            Execute(db, "UPDATE music_download_links SET status='linked',message='Albüm ve recording kimlikleri bağlandı.' WHERE download_id=$id", ("$id", downloadId));
        }
    }
    public void MusicLinkReview(string id, string message) { lock (_writeGate) { using var db = Open(); Execute(db, "UPDATE music_download_links SET status='review',message=$message WHERE download_id=$id", ("$id", id), ("$message", message)); } }
    public void RetryMusicLink(string id)
    {
        lock (_writeGate)
        {
            var download = GetDownload(id) ?? throw new DomainException("İndirme bulunamadı.", 404);
            if (download.ImportedLibraryId is null) throw new DomainException("Albüm henüz yayımlanmadı.", 409);
            var job = QueueScan(download.ImportedLibraryId); using var db = Open();
            Execute(db, "UPDATE downloads SET scan_job_id=$job WHERE id=$id; UPDATE music_download_links SET status='waiting',message='Tarama ve recording bağlantısı bekleniyor.' WHERE download_id=$id", ("$job", job.Id), ("$id", id));
        }
    }
    public void ReviewMusicImport(string id, MusicQualityRules rules)
    {
        Music.MusicReleaseEvaluator.Validate(rules);
        lock (_writeGate)
        {
            var download = GetDownload(id) ?? throw new DomainException("İndirme bulunamadı.", 404);
            if (GetMusicAcquisition(id) is null || download.ImportedAt is not null || download.Progress < 100 || download.Status != "failed") throw new DomainException("Yalnız tamamlanan ve doğrulamada duran müzik paketi incelenebilir.", 409);
            using var db = Open(); using var tx = db.BeginTransaction(); using var command = Command(db, "UPDATE music_download_links SET import_rules_json=$rules WHERE download_id=$id; UPDATE downloads SET desired_state='running',status='queued',revision=revision+1,message='Müzik paketi yeni profille doğrulanacak.' WHERE id=$id",
                ("$id", id), ("$rules", JsonSerializer.Serialize(rules, WebJson))); command.Transaction = tx; command.ExecuteNonQuery(); tx.Commit();
        }
    }
}
