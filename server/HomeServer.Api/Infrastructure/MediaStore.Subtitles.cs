using System.Text.Json;
using HomeServer.Core;

namespace HomeServer.Infrastructure;

public sealed partial class MediaStore
{
    public void SetSubtitleProfile(SubtitleLanguageProfile profile)
    {
        lock (_writeGate)
        {
            using var db = Open(); using var tx = db.BeginTransaction();
            using var command = Command(db, """
                INSERT INTO settings(key,value) VALUES('subtitles.profile',$profile) ON CONFLICT(key) DO UPDATE SET value=$profile;
                INSERT INTO settings(key,value) VALUES('subtitles.profile.revision','1') ON CONFLICT(key) DO UPDATE SET value=CAST(value AS INTEGER)+1;
                UPDATE subtitle_wanted SET next_run=$now,status='waiting',policy_json=NULL,lease_token=NULL,lease_until=NULL,profile_revision=(SELECT CAST(value AS INTEGER) FROM settings WHERE key='subtitles.profile.revision');
                """, ("$profile", JsonSerializer.Serialize(profile)), ("$now", Now()));
            command.Transaction = tx; command.ExecuteNonQuery(); tx.Commit();
        }
    }
    public SubtitleCandidate SaveSubtitleCandidate(SubtitleCandidate candidate, AnalyzedMedia media, int? connectionRevision = null)
    {
        lock (_writeGate)
        {
            using var db = Open();
            var connection = GetSubtitleConnectionSnapshot().Revision;
            if (connectionRevision is { } expected && connection != expected) throw new DomainException("Altyazı bağlantısı arama sırasında değişti.", 409);
            var existing = Scalar(db, "SELECT id FROM subtitle_candidates WHERE media_id=$media AND fingerprint=$fp AND provider_file_id=$file", ("$media", media.Media.Id), ("$fp", media.Fingerprint), ("$file", candidate.FileId)) as string;
            Execute(db, "DELETE FROM subtitle_candidates WHERE downloaded_at IS NULL AND expires_at<$now AND id IS NOT $existing", ("$now", Now()), ("$existing", existing));
            if (existing is null && Convert.ToInt32(Scalar(db, "SELECT COUNT(*) FROM subtitle_candidates")) >= 20000) throw new DomainException("Altyazı arama önbelleği sınırı dolu.", 429);
            var id = existing ?? Guid.NewGuid().ToString("N"); candidate = candidate with { Id = id };
            Execute(db, """
                INSERT INTO subtitle_candidates(id,media_id,content_id,fingerprint,provider_file_id,language,candidate_json,managed_id,created_at,expires_at,association_revision,connection_revision)
                VALUES($id,$media,$content,$fp,$file,$language,$json,$managed,$now,$expires,$revision,$connection)
                ON CONFLICT(id) DO UPDATE SET content_id=$content,candidate_json=$json,expires_at=$expires,association_revision=$revision,
                  protected_ticket=CASE WHEN subtitle_candidates.connection_revision=$connection THEN protected_ticket END,
                  ticket_expires_at=CASE WHEN subtitle_candidates.connection_revision=$connection THEN ticket_expires_at END,connection_revision=$connection
                """, ("$id", id), ("$media", media.Media.Id), ("$content", media.Media.Content?.Id), ("$fp", media.Fingerprint), ("$file", candidate.FileId), ("$language", candidate.Language),
                ("$json", JsonSerializer.Serialize(candidate, WebJson)), ("$managed", Guid.NewGuid().ToString("N")), ("$now", Now()), ("$expires", DateTimeOffset.UtcNow.AddHours(1).ToString("O")), ("$revision", media.Media.MatchRevision), ("$connection", connection));
            Execute(db, "DELETE FROM subtitle_candidates WHERE downloaded_at IS NULL AND expires_at<$now", ("$now", Now()));
            return candidate;
        }
    }
    public StoredSubtitleCandidate GetSubtitleCandidate(string id)
    {
        using var db = Open(); return Query(db, "SELECT candidate_json,fingerprint,content_id,managed_id,expires_at,association_revision,connection_revision,protected_ticket,ticket_expires_at FROM subtitle_candidates WHERE id=$id",
            r => new StoredSubtitleCandidate(JsonSerializer.Deserialize<SubtitleCandidate>(r.GetString(0), WebJson)!, r.GetString(1), Text(r, 2), r.GetString(3), r.GetString(4), r.GetInt32(5), r.GetInt32(6), Text(r, 7), Text(r, 8)), ("$id", id)).FirstOrDefault() ?? throw new DomainException("Altyazı sonucu bulunamadı; aramayı yenile.", 404);
    }
    public void CompleteSubtitleDownload(string id)
    {
        lock (_writeGate) { using var db = Open(); Execute(db, "UPDATE subtitle_candidates SET downloaded_at=COALESCE(downloaded_at,$now) WHERE id=$id", ("$id", id), ("$now", Now())); }
    }
    public int SubtitleDownloadsToday()
    {
        using var db = Open(); return Convert.ToInt32(Scalar(db, "SELECT COUNT(*) FROM subtitle_candidates WHERE downloaded_at>=strftime('%Y-%m-%dT00:00:00.0000000+00:00','now')"));
    }
    public IReadOnlyList<string> SubtitleMediaBatch(string after)
    {
        using var db = Open(); return Query(db, "SELECT m.id FROM media m JOIN libraries l ON l.id=m.library_id WHERE l.enabled=1 AND m.available=1 AND m.excluded=0 AND m.kind='video' AND m.id>$after ORDER BY m.id LIMIT 20", r => r.GetString(0), ("$after", after));
    }
    public void QueueSubtitle(string media, string language, int revision)
    {
        lock (_writeGate) { using var db = Open(); Execute(db, "INSERT OR IGNORE INTO subtitle_wanted(media_id,language,profile_revision,next_run) VALUES($media,$language,$revision,$now)", ("$media", media), ("$language", language), ("$revision", revision), ("$now", Now())); }
    }
    public IReadOnlyList<SubtitleWanted> GetSubtitleWanted()
    {
        using var db = Open(); return Query(db, "SELECT w.media_id,m.title,w.language,w.status,w.message,w.next_run FROM subtitle_wanted w JOIN media m ON m.id=w.media_id ORDER BY w.next_run LIMIT 200",
            r => new SubtitleWanted(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5)));
    }
    public SubtitleWanted? ClaimSubtitle(int revision)
    {
        lock (_writeGate)
        {
            if (ContainerUpdateBlocked) return null;
            using var db = Open();
            var item = Query(db, "SELECT w.media_id,m.title,w.language,w.status,w.message,w.next_run FROM subtitle_wanted w JOIN media m ON m.id=w.media_id WHERE w.profile_revision=$revision AND w.next_run<=$now ORDER BY w.next_run LIMIT 1",
                r => new SubtitleWanted(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5)), ("$revision", revision), ("$now", Now())).FirstOrDefault();
            if (item is not null) UpdateSubtitleWanted(item.MediaId, item.Language, "searching", "Altyazı aranıyor.", DateTimeOffset.UtcNow.AddMinutes(10));
            return item;
        }
    }
    public void UpdateSubtitleWanted(string media, string language, string status, string message, DateTimeOffset next, int? revision = null)
    {
        lock (_writeGate) { using var db = Open(); Execute(db, "UPDATE subtitle_wanted SET status=$status,message=$message,next_run=$next WHERE media_id=$media AND language=$language AND ($revision IS NULL OR profile_revision=$revision)", ("$media", media), ("$language", language), ("$status", status), ("$message", message), ("$next", next.ToString("O")), ("$revision", revision)); }
    }
    public void NotifySubtitle(string mediaId, string message, string dedup)
    {
        var media = GetMedia(mediaId); if (media is null) return;
        lock (_writeGate)
        {
            using var db = Open(); using var tx = db.BeginTransaction();
            foreach (var admin in GetAccounts().Where(account => account.Enabled && account.IsAdmin)) AddNotification(db, tx, admin.Id, "subtitle", media.Title, message, media.Content?.Id, dedup);
            tx.Commit();
        }
    }
}
