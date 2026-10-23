using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HomeServer.Core;
using Microsoft.Data.Sqlite;

namespace HomeServer.Infrastructure;

public sealed partial class MediaStore
{
    private const string OfflineSelect = "SELECT id,user_id,device_id,media_id,source_fingerprint,source_catalog_bytes,source_catalog_modified,match_revision,user_version,policy_revision,input_json,status,revision,lease,progress,message,created_at,expires_at,reserved_bytes,manifest_json,last_sequence FROM offline_exports";
    private static OfflineExport ReadOffline(SqliteDataReader r) => new(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetInt64(5), r.GetString(6), r.GetInt32(7), r.GetInt32(8), r.GetInt32(9), JsonSerializer.Deserialize<OfflineInput>(r.GetString(10), WebJson)!, r.GetString(11), r.GetInt32(12), Text(r, 13), r.GetInt32(14), r.GetString(15), r.GetString(16), r.GetString(17), r.GetInt64(18), r.IsDBNull(19) ? null : JsonSerializer.Deserialize<OfflineManifest>(r.GetString(19), WebJson), r.GetInt64(20));
    public string OfflineServerId
    {
        get { lock (_writeGate) { var id = GetSetting("offline.server-id"); if (id is null) { id = Guid.NewGuid().ToString("N"); SetSetting("offline.server-id", id); } return id; } }
    }
    public OfflinePolicy GetOfflinePolicy(string userId)
    {
        using var db = Open(); return Query(db, "SELECT enabled,maximum_items,maximum_bytes,lease_days,revision FROM user_offline_policies WHERE user_id=$user", r => new OfflinePolicy(r.GetBoolean(0), r.GetInt32(1), r.GetInt64(2), r.GetInt32(3), r.GetInt32(4)), ("$user", userId)).FirstOrDefault() ?? new();
    }
    public OfflinePolicy SaveOfflinePolicy(string userId, OfflinePolicy input)
    {
        if (input.MaximumItems is < 1 or > 100 || input.MaximumBytes is < 268435456 or > 274877906944 || input.LeaseDays is < 1 or > 7) throw new DomainException("Çevrimdışı sınırı 1–100 dosya, 256 MiB–256 GiB ve 1–7 gün olmalı.");
        lock (_writeGate)
        {
            _ = GetAccount(userId) ?? throw new DomainException("Hesap bulunamadı.", 404);
            var current = GetOfflinePolicy(userId); if (current.Revision != input.Revision) throw new DomainException("Çevrimdışı hesap sınırı değişti.", 409);
            using var db = Open(); using var tx = db.BeginTransaction(); using var command = Command(db, """
                INSERT INTO user_offline_policies(user_id,enabled,maximum_items,maximum_bytes,lease_days,revision) VALUES($user,$enabled,$items,$bytes,$days,$revision)
                  ON CONFLICT(user_id) DO UPDATE SET enabled=$enabled,maximum_items=$items,maximum_bytes=$bytes,lease_days=$days,revision=$revision;
                UPDATE offline_exports SET status='revoked',lease=NULL,revision=revision+1,message='Çevrimdışı hesap sınırı değişti.' WHERE user_id=$user AND status NOT IN ('cancelled','revoked');
                """, ("$user", userId), ("$enabled", input.Enabled), ("$items", input.MaximumItems), ("$bytes", input.MaximumBytes), ("$days", input.LeaseDays), ("$revision", current.Revision + 1));
            command.Transaction = tx; command.ExecuteNonQuery(); tx.Commit(); return input with { Revision = current.Revision + 1 };
        }
    }
    public OfflineExport? GetOfflineExport(string id) { using var db = Open(); return Query(db, OfflineSelect + " WHERE id=$id", ReadOffline, ("$id", id)).FirstOrDefault(); }
    public IReadOnlyList<OfflineExport> GetOfflineExports(PlaybackActor actor)
    {
        lock (_writeGate)
        {
            _ = RequirePlaybackActor(actor); ReconcileOfflineExports(actor.UserId);
            using var db = Open(); return Query(db, OfflineSelect + " WHERE user_id=$user AND device_id=$device ORDER BY created_at DESC LIMIT 200", ReadOffline, ("$user", actor.UserId), ("$device", actor.DeviceId));
        }
    }
    public OfflineExport QueueOfflineExport(PlaybackActor actor, AnalyzedMedia media, OfflineInput input, long reservation, DateTimeOffset now)
    {
        _ = AppFiles.Id(input.ClientId);
        lock (_writeGate)
        {
            var viewer = RequirePlaybackActor(actor); var current = PlaybackSource(media.Media.Id, viewer);
            if (current.Fingerprint != media.Fingerprint || current.Media.MatchRevision != media.Media.MatchRevision || input.ExpectedMatchRevision is { } expected && expected != current.Media.MatchRevision) throw new DomainException("Dosya sürümü değişti; çevrimdışı isteği yeniden aç.", 409);
            var policy = GetOfflinePolicy(actor.UserId); if (!policy.Enabled) throw new DomainException("Bu hesapta çevrimdışı kopya kapalı.", 403);
            using var db = Open(); var existing = Query(db, OfflineSelect + " WHERE user_id=$user AND device_id=$device AND client_id=$client", ReadOffline, ("$user", actor.UserId), ("$device", actor.DeviceId), ("$client", input.ClientId)).FirstOrDefault();
            if (existing is not null) return existing.MediaId == media.Media.Id && existing.Input == input ? RequireOfflineExport(existing.Id, actor, true) : throw new DomainException("Bu çevrimdışı komut başka bir dosya/tercih için kullanıldı.", 409);
            ReconcileOfflineExports(actor.UserId);
            var values = Query(db, "SELECT reserved_bytes FROM offline_exports WHERE user_id=$user AND status IN ('queued','running','ready') AND expires_at>$now", r => r.GetInt64(0), ("$user", actor.UserId), ("$now", now.ToString("O")));
            if (values.Count >= policy.MaximumItems || reservation > policy.MaximumBytes - values.Sum()) throw new DomainException("Hesabın çevrimdışı dosya/boyut sınırına ulaşıldı.", 429);
            if (Convert.ToInt32(Scalar(db, "SELECT COUNT(*) FROM offline_exports WHERE status IN ('queued','running')")) >= 32 || Convert.ToInt32(Scalar(db, "SELECT COUNT(*) FROM offline_exports")) >= 5000) throw new DomainException("Çevrimdışı hazırlık kuyruğu dolu.", 429);
            var id = Guid.NewGuid().ToString("N"); Execute(db, """
                INSERT INTO offline_exports(id,user_id,device_id,media_id,client_id,source_fingerprint,source_catalog_bytes,source_catalog_modified,match_revision,user_version,policy_revision,input_json,status,created_at,expires_at,reserved_bytes)
                  VALUES($id,$user,$device,$media,$client,$fp,$sourceBytes,$modified,$match,$version,$policy,$json,'queued',$now,$expires,$reserved)
                """, ("$id", id), ("$user", actor.UserId), ("$device", actor.DeviceId), ("$media", media.Media.Id), ("$client", input.ClientId), ("$fp", media.Fingerprint), ("$sourceBytes", current.Media.SizeBytes), ("$modified", current.Media.ModifiedAt), ("$match", current.Media.MatchRevision), ("$version", GetAccountSecret(actor.UserId)!.Version), ("$policy", policy.Revision), ("$json", JsonSerializer.Serialize(input, WebJson)), ("$now", now.ToString("O")), ("$expires", now.AddDays(policy.LeaseDays).ToString("O")), ("$reserved", reservation));
            return GetOfflineExport(id)!;
        }
    }
    private bool OfflineScopeCurrent(OfflineExport job)
    {
        var user = GetAccountSecret(job.UserId); var policy = GetOfflinePolicy(job.UserId);
        if (user is not { Account.Enabled: true } || user.Version != job.UserVersion || !policy.Enabled || policy.Revision != job.PolicyRevision) return false;
        var media = GetMedia(job.MediaId, user.Account);
        // Unavailable volumes do not revoke a completed device copy; explicit ACL/association/source catalogue changes do.
        return media is not null && media.MatchRevision == job.MatchRevision && media.SizeBytes == job.SourceCatalogBytes && media.ModifiedAt == job.SourceCatalogModified;
    }
    public OfflineExport RequireOfflineExport(string id, PlaybackActor actor, bool allowExpired = false)
    {
        _ = AppFiles.Id(id);
        lock (_writeGate)
        {
            _ = RequirePlaybackActor(actor); var job = GetOfflineExport(id);
            if (job is null || job.UserId != actor.UserId || job.DeviceId != actor.DeviceId) throw new DomainException("Çevrimdışı kopya bu hesap/cihaza ait değil.", 404);
            if (!OfflineScopeCurrent(job)) { RevokeOfflineExport(id, "Dosya veya çevrimdışı erişimi değişti."); throw new DomainException("Çevrimdışı kopyanın erişimi iptal edildi.", 403); }
            if (job.Status is "cancelled" or "revoked") throw new DomainException("Çevrimdışı kopya iptal edildi.", 410);
            if (!allowExpired && DateTimeOffset.Parse(job.ExpiresAt) <= DateTimeOffset.UtcNow) throw new DomainException("Çevrimdışı erişim süresi yenilenmeli.", 410);
            return job;
        }
    }
    public bool OfflineWorkCurrent(OfflineExport job)
    {
        lock (_writeGate) { var current = GetOfflineExport(job.Id); return current is { Status: "running" } && current.Revision == job.Revision && current.Lease == job.Lease && DateTimeOffset.Parse(current.ExpiresAt) > DateTimeOffset.UtcNow && OfflineScopeCurrent(current); }
    }
    public OfflineExport? ClaimOfflineExport()
    {
        lock (_writeGate)
        {
            if (ContainerUpdateBlocked) return null;
            using var db = Open();
            Execute(db, "UPDATE offline_exports SET status='failed',revision=revision+1,lease=NULL,message='Hazırlık izni sona erdi; yeniden dene.' WHERE status='queued' AND expires_at<=$now", ("$now", Now()));
            var job = Query(db, OfflineSelect + " WHERE status='queued' AND expires_at>$now ORDER BY created_at LIMIT 1", ReadOffline, ("$now", Now())).FirstOrDefault();
            if (job is null) return null;
            if (!OfflineScopeCurrent(job)) { RevokeOfflineExport(job.Id, "Çevrimdışı erişim değişti."); return null; }
            Execute(db, "UPDATE offline_exports SET status='running',revision=revision+1,lease=$lease,progress=0,message='Cihaz kopyası hazırlanıyor.' WHERE id=$id", ("$id", job.Id), ("$lease", Guid.NewGuid().ToString("N"))); return GetOfflineExport(job.Id);
        }
    }
    public void OfflineProgress(OfflineExport job, int progress)
    {
        lock (_writeGate) { using var db = Open(); Execute(db, "UPDATE offline_exports SET progress=$progress WHERE id=$id AND revision=$revision AND lease=$lease AND status='running'", ("$id", job.Id), ("$revision", job.Revision), ("$lease", job.Lease), ("$progress", Math.Clamp(progress, 0, 99))); }
    }
    public void CompleteOfflineExport(OfflineExport job, OfflineManifest manifest, Action publish)
    {
        lock (_writeGate)
        {
            if (!OfflineWorkCurrent(job)) throw new OperationCanceledException("Çevrimdışı hazırlık iptal edildi.");
            if (PlaybackSource(job.MediaId, GetAccount(job.UserId)!).Fingerprint != job.SourceFingerprint) throw new DomainException("Yayın öncesinde kaynak dosya değişti.", 409);
            if (manifest.Assets.Sum(asset => asset.Bytes) > job.ReservedBytes) throw new DomainException("Hazırlanan dosya ayrılan boyut sınırını aştı.", 413);
            using var db = Open(); using var tx = db.BeginTransaction(); publish();
            using var command = Command(db, "UPDATE offline_exports SET status='ready',lease=NULL,progress=100,message='Cihaza aktarılmaya hazır.',manifest_json=$json WHERE id=$id AND revision=$revision", ("$id", job.Id), ("$revision", job.Revision), ("$json", JsonSerializer.Serialize(manifest, WebJson)));
            command.Transaction = tx; command.ExecuteNonQuery(); tx.Commit();
        }
    }
    public void FailOfflineExport(OfflineExport job, string message)
    {
        lock (_writeGate) { using var db = Open(); Execute(db, "UPDATE offline_exports SET status='failed',lease=NULL,message=$message WHERE id=$id AND revision=$revision AND status='running'", ("$id", job.Id), ("$revision", job.Revision), ("$message", message)); }
    }
    private void RevokeOfflineExport(string id, string message)
    {
        using var db = Open(); Execute(db, "UPDATE offline_exports SET status='revoked',lease=NULL,revision=revision+1,message=$message WHERE id=$id AND status NOT IN ('revoked','cancelled')", ("$id", id), ("$message", message));
    }
    public void ReconcileOfflineExports(string? userId = null)
    {
        lock (_writeGate)
        {
            using var db = Open(); foreach (var job in Query(db, OfflineSelect + " WHERE status IN ('queued','running','ready') AND ($user IS NULL OR user_id=$user)", ReadOffline, ("$user", userId)))
                if (!OfflineScopeCurrent(job)) RevokeOfflineExport(job.Id, "Hesap, dosya veya çevrimdışı politikası değişti.");
        }
    }
    public OfflineExport RenewOfflineExport(string id, int revision, PlaybackActor actor)
    {
        lock (_writeGate)
        {
            var job = RequireOfflineExport(id, actor, true); if (job.Revision != revision || job.Status != "ready") throw new DomainException("Çevrimdışı kopya değişti veya henüz hazır değil.", 409);
            RequireOfflineCapacity(job);
            using var db = Open(); Execute(db, "UPDATE offline_exports SET expires_at=$expires WHERE id=$id", ("$id", id), ("$expires", DateTimeOffset.UtcNow.AddDays(GetOfflinePolicy(actor.UserId).LeaseDays).ToString("O"))); return GetOfflineExport(id)!;
        }
    }
    public void CancelOfflineExport(string id, int revision, PlaybackActor actor)
    {
        lock (_writeGate)
        {
            _ = RequirePlaybackActor(actor); var job = GetOfflineExport(id) ?? throw new DomainException("Çevrimdışı kopya bulunamadı.", 404);
            if (job.UserId != actor.UserId || job.DeviceId != actor.DeviceId) throw new DomainException("Çevrimdışı kopya bu cihaza ait değil.", 404);
            if (job.Status is "cancelled" or "revoked") return;
            if (job.Revision != revision) throw new DomainException("Çevrimdışı hazırlık değişti; yeniden aç.", 409);
            using var db = Open(); Execute(db, "UPDATE offline_exports SET status='cancelled',lease=NULL,revision=revision+1,message='Kullanıcı çevrimdışı kopyayı kaldırdı.' WHERE id=$id", ("$id", id));
        }
    }
    public OfflineExport RetryOfflineExport(string id, int revision, PlaybackActor actor)
    {
        lock (_writeGate)
        {
            var job = RequireOfflineExport(id, actor, true); if (job.Revision != revision || job.Status is not ("failed" or "ready")) throw new DomainException("Çevrimdışı hazırlık değişti.", 409);
            RequireOfflineCapacity(job);
            using var db = Open();
            if (Convert.ToInt32(Scalar(db, "SELECT COUNT(*) FROM offline_exports WHERE status IN ('queued','running')")) >= 32) throw new DomainException("Çevrimdışı hazırlık kuyruğu dolu.", 429);
            Execute(db, "UPDATE offline_exports SET status='queued',lease=NULL,revision=revision+1,progress=0,manifest_json=NULL,expires_at=$expires,message='Hazırlık yeniden sıraya alındı.' WHERE id=$id", ("$id", id), ("$expires", DateTimeOffset.UtcNow.AddDays(GetOfflinePolicy(actor.UserId).LeaseDays).ToString("O"))); return GetOfflineExport(id)!;
        }
    }
    public void RecoverOfflineExports()
    {
        lock (_writeGate) { using var db = Open(); Execute(db, "UPDATE offline_exports SET status='queued',lease=NULL,revision=revision+1,progress=0,message='Kesilen cihaz hazırlığı yeniden başlayacak.' WHERE status='running'"); ReconcileOfflineExports(); }
    }
    private void RequireOfflineCapacity(OfflineExport job)
    {
        var policy = GetOfflinePolicy(job.UserId); using var db = Open();
        var others = Query(db, "SELECT reserved_bytes FROM offline_exports WHERE user_id=$user AND id<>$id AND status IN ('queued','running','ready') AND expires_at>$now", r => r.GetInt64(0), ("$user", job.UserId), ("$id", job.Id), ("$now", Now()));
        if (others.Count >= policy.MaximumItems || job.ReservedBytes > policy.MaximumBytes - others.Sum()) throw new DomainException("Hesabın çevrimdışı kopya kotası dolu.", 429);
    }
    public IReadOnlyList<OfflineExport> OfflineCleanupCandidates()
    {
        using var db = Open(); return Query(db, OfflineSelect + " WHERE status IN ('failed','revoked','cancelled') OR expires_at<$now", ReadOffline, ("$now", Now()));
    }
    public void PruneOfflineExports()
    {
        lock (_writeGate) { using var db = Open(); Execute(db, "DELETE FROM offline_exports WHERE expires_at<$before AND status<>'running'", ("$before", DateTimeOffset.UtcNow.AddDays(-30).ToString("O"))); }
    }
    public string ProgressStamp(string mediaId, string userId)
    {
        var media = GetMedia(mediaId, GetAccount(userId)) ?? throw new DomainException("Medya bulunamadı.", 404); using var db = Open(); return ProgressStamp(db, null, media, userId);
    }
    private static string ProgressStamp(SqliteConnection db, SqliteTransaction? tx, MediaItem media, string userId)
    {
        var rows = new List<object>();
        foreach (var sql in new[] { "SELECT position_seconds,watched,updated_at,event_client,event_sequence FROM user_watch_state WHERE user_id=$user AND media_id=$media",
            "SELECT episode_key,position_seconds,watched,updated_at,event_client,event_sequence FROM content_watch_state WHERE user_id=$user AND content_id=$content AND (episode_key='' OR episode_key IN (SELECT value FROM json_each($episodes))) ORDER BY episode_key" })
        {
            using var command = Command(db, sql, ("$user", userId), ("$media", media.Id), ("$content", media.Content?.Id), ("$episodes", JsonSerializer.Serialize(media.Episodes?.Select(episode => episode.EpisodeId) ?? []))); command.Transaction = tx;
            using var reader = command.ExecuteReader(); while (reader.Read()) { var values = new object[reader.FieldCount]; reader.GetValues(values); rows.Add(values.Select(value => value is DBNull ? null : value).ToArray()); }
        }
        return Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(rows, WebJson)));
    }
    public OfflineProgressResult SyncOfflineProgress(string id, OfflineProgressInput input, PlaybackActor actor)
    {
        _ = AppFiles.Id(input.EventId);
        if (input.ExpectedUserId != actor.UserId || input.Sequence is < 1 or > 9_007_199_254_740_991 || !double.IsFinite(input.PositionSeconds) || input.PositionSeconds < 0 || input.ExpectedStamp is not { Length: 64 }) throw new DomainException("Çevrimdışı konum olayı geçersiz.");
        lock (_writeGate)
        {
            var job = RequireOfflineExport(id, actor, true);
            if (job.Status != "ready" || job.Manifest is null || input.PositionSeconds > job.Manifest.DurationSeconds + 5) throw new DomainException("Çevrimdışı konum bu dosyanın sınırları dışında.", 409);
            var hash = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(input, WebJson))); using var db = Open();
            var prior = Query(db, "SELECT request_hash,result_json FROM offline_progress_receipts WHERE grant_id=$id AND event_id=$event", r => (Hash: r.GetString(0), Json: r.GetString(1)), ("$id", id), ("$event", input.EventId)).FirstOrDefault();
            if (prior.Hash is not null) return prior.Hash == hash ? JsonSerializer.Deserialize<OfflineProgressResult>(prior.Json, WebJson)! : throw new DomainException("Konum olay kimliği farklı veriyle tekrar kullanıldı.", 409);
            var media = GetMedia(job.MediaId, GetAccount(actor.UserId))!; var stamp = ProgressStamp(db, null, media, actor.UserId);
            if (input.Sequence <= job.LastSequence) return new("stale", stamp, media.PositionSeconds, job.Manifest.DurationSeconds, media.IsWatched, job.LastSequence);
            if (stamp != input.ExpectedStamp) return new("conflict", stamp, media.PositionSeconds, job.Manifest.DurationSeconds, media.IsWatched, job.LastSequence);
            OfflineProgressResult? result = null;
            WriteProgress(job.MediaId, new(input.PositionSeconds, job.Manifest.DurationSeconds, job.Id, input.Sequence, actor.UserId, job.MatchRevision), GetAccount(actor.UserId), (connection, transaction) =>
            {
                var next = ProgressStamp(connection, transaction, media, actor.UserId); var position = Math.Min(input.PositionSeconds, media.DurationSeconds ?? job.Manifest.DurationSeconds);
                result = new("applied", next, position, job.Manifest.DurationSeconds, position >= job.Manifest.DurationSeconds * .95, input.Sequence);
                using var command = Command(connection, """
                    UPDATE offline_exports SET last_sequence=$sequence WHERE id=$id;
                    INSERT INTO offline_progress_receipts(grant_id,event_id,sequence,request_hash,result_json,created_at) VALUES($id,$event,$sequence,$hash,$result,$now);
                    DELETE FROM offline_progress_receipts WHERE grant_id=$id AND sequence<$sequence-32;
                    """, ("$id", id), ("$event", input.EventId), ("$sequence", input.Sequence), ("$hash", hash), ("$result", JsonSerializer.Serialize(result, WebJson)), ("$now", Now())); command.Transaction = transaction; command.ExecuteNonQuery();
            });
            return result!;
        }
    }
}
