using System.Security.Cryptography;
using System.Text.Json;
using HomeServer.Core;
using HomeServer.Playback;

namespace HomeServer.Infrastructure;

public sealed partial class MediaStore
{
    private const string ReceiverSelect = "SELECT id,user_id,login_id,playback_id,target_id,target_name,mode,expires_at,closed,kind,revision,artifact_json,device_json,observation_json FROM receiver_grants";
    private static ReceiverGrant ReadReceiver(Microsoft.Data.Sqlite.SqliteDataReader r) => new(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), Text(r,4), r.GetString(5), r.GetString(6), r.GetString(7), r.GetBoolean(8), r.GetString(9), r.GetInt32(10),
        r.IsDBNull(11) ? null : JsonSerializer.Deserialize<ReceiverCopy>(r.GetString(11), WebJson), r.IsDBNull(12) ? null : JsonSerializer.Deserialize<RenderDevice>(r.GetString(12), WebJson), r.IsDBNull(13) ? null : JsonSerializer.Deserialize<ReceiverObservation>(r.GetString(13), WebJson));
    public ReceiverGrant? GetReceiver(string id) { using var db = Open(); return Query(db, ReceiverSelect + " WHERE id=$id", ReadReceiver, ("$id", id)).FirstOrDefault(); }
    public IReadOnlyList<ReceiverGrant> Receivers(string? user = null)
    {
        using var db = Open(); return Query(db, ReceiverSelect + " WHERE ($user IS NULL OR user_id=$user) AND closed=0 ORDER BY created_at DESC LIMIT 100", ReadReceiver, ("$user", user));
    }
    public ReceiverGrant OwnReceiver(string id, PlaybackActor actor)
    {
        _ = RequirePlaybackActor(actor);
        var grant = GetReceiver(id) ?? throw new DomainException("Alıcı oturumu bulunamadı.", 404);
        if (grant.UserId != actor.UserId || grant.LoginId != actor.LoginId) throw new DomainException("Alıcı oturumu bu girişe ait değil.", 403);
        return grant;
    }
    public ReceiverGrant RequireReceiver(string id, PlaybackActor actor)
    {
        var grant = OwnReceiver(id, actor);
        if (grant.Closed || DateTimeOffset.Parse(grant.ExpiresAt) <= DateTimeOffset.UtcNow) throw new DomainException("Alıcı oturumu sona erdi.", 410);
        _ = RequirePlaybackLease(grant.PlaybackId, actor, DateTimeOffset.UtcNow);
        return grant;
    }
    public ReceiverGrant CreateReceiver(PlaybackActor actor, string playbackId, string token, string mode, string name, string kind, RenderDevice? device, ReceiverCopy? artifact)
    {
        lock (_writeGate)
        {
            var lease = RequirePlaybackLease(playbackId, actor, DateTimeOffset.UtcNow);
            if (token.Length != 64 || kind is not ("cast" or "airplay" or "dlna") || (kind == "dlna") != (device is not null) || mode is not ("copy" or "original" or "direct" or "hls")) throw new DomainException("Alıcı izni geçersiz.");
            if (mode == "copy" && (artifact is null || artifact.MediaId != lease.MediaId || artifact.Fingerprint != lease.Fingerprint || artifact.MatchRevision != lease.MatchRevision || artifact.ArtifactFingerprint is null || artifact.Sha256?.Length != 64)) throw new DomainException("Alıcı kopyasının doğrulaması değişti.", 409);
            ReconcilePlaybackLeases(DateTimeOffset.UtcNow);
            using var db = Open();
            Execute(db, "UPDATE receiver_grants SET closed=1,revision=revision+1 WHERE closed=0 AND expires_at<=$now; DELETE FROM receiver_grants WHERE closed=1 AND expires_at<$old", ("$now", Now()), ("$old", DateTimeOffset.UtcNow.AddDays(-7).ToString("O")));
            if (device is not null && Scalar(db, "SELECT id FROM receiver_grants WHERE target_id=$target AND closed=0", ("$target", device.Id)) is string) throw new DomainException("Bu alıcıda başka bir Home oturumu açık; önce onu bitir.", 409);
            var id = Guid.NewGuid().ToString("N");
            Execute(db, """
                INSERT INTO receiver_grants(id,token_hash,user_id,login_id,playback_id,target_id,target_name,mode,expires_at,created_at,kind,artifact_json,device_json)
                VALUES($id,$hash,$user,$login,$playback,$target,$name,$mode,$expires,$now,$kind,$artifact,$device)
                """, ("$id", id), ("$hash", TokenHash(token)), ("$user", actor.UserId), ("$login", actor.LoginId), ("$playback", playbackId), ("$target", device?.Id), ("$name", name), ("$mode", mode),
                ("$expires", DateTimeOffset.UtcNow.AddMinutes(20).ToString("O")), ("$now", Now()), ("$kind", kind), ("$artifact", artifact is null ? null : JsonSerializer.Serialize(artifact, WebJson)), ("$device", device is null ? null : JsonSerializer.Serialize(device, WebJson)));
            return GetReceiver(id)!;
        }
    }
    public ReceiverGrant RequireReceiverToken(string token)
    {
        if (token.Length != 64) throw new DomainException("Alıcı izni bulunamadı.", 404);
        using var db = Open(); var row = Query(db, ReceiverSelect + " WHERE token_hash=$hash AND closed=0 AND expires_at>$now", ReadReceiver, ("$hash", TokenHash(token)), ("$now", Now())).FirstOrDefault() ?? throw new DomainException("Alıcı izni sona erdi.", 410);
        return RequireReceiver(row.Id, GetPlaybackActor(row.UserId, row.LoginId));
    }
    public bool ReceiverOwnsUrl(ReceiverGrant grant, string? address)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || uri.Query.Length > 0 || uri.Fragment.Length > 0) return false;
        var parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3 || parts[^3] != "receiver" || parts[^2].Length != 64 || parts[^1] != (grant.Mode == "hls" ? "index.m3u8" : "media")) return false;
        using var db = Open(); return Scalar(db, "SELECT id FROM receiver_grants WHERE id=$id AND token_hash=$hash", ("$id", grant.Id), ("$hash", TokenHash(parts[^2]))) is string;
    }
    public ReceiverStatus ReceiverStatus(ReceiverGrant grant)
    {
        var lease = GetPlaybackLease(grant.PlaybackId); var closed = grant.Closed || lease?.Status is null or "closed" || DateTimeOffset.Parse(grant.ExpiresAt) <= DateTimeOffset.UtcNow;
        return new(grant.Id, grant.Revision, closed, grant.Observation?.PositionSeconds ?? lease?.PositionSeconds ?? 0, lease?.Session?.DurationSeconds ?? 0,
            grant.Observation?.Paused ?? lease?.PausedAt is not null, grant.Observation?.Volume, closed ? "closed" : grant.Observation?.State ?? "connecting", grant.Observation?.ObservedAt, grant.Observation?.Error);
    }
    public ReceiverStatus ObserveReceiver(ReceiverGrant expected, PlaybackActor actor, ReceiverObservation observation)
    {
        lock (_writeGate)
        {
            var grant = RequireReceiver(expected.Id, actor);
            if (grant.Revision != expected.Revision) throw new DomainException("Alıcı komutu değişti; durum yeniden okunmalı.", 409);
            var lease = GetPlaybackLease(grant.PlaybackId)!; var sequence = (grant.Observation?.Sequence ?? 0) + 1;
            observation = observation with { ObservedAt = Now(), Sequence = sequence };
            if (observation.PositionSeconds is { } position && observation.Paused is { } paused)
            {
                _ = TouchPlayback(lease.Id, actor, new(position, paused), DateTimeOffset.UtcNow);
                WriteProgress(lease.MediaId, new(position, lease.Session?.DurationSeconds, grant.Id, sequence, actor.UserId, lease.MatchRevision), GetAccount(actor.UserId)!, Save);
            }
            else { _ = TouchPlayback(lease.Id, actor, null, DateTimeOffset.UtcNow); using var db = Open(); using var tx = db.BeginTransaction(); Save(db, tx); tx.Commit(); }
            return ReceiverStatus(GetReceiver(grant.Id)!);
            void Save(Microsoft.Data.Sqlite.SqliteConnection db, Microsoft.Data.Sqlite.SqliteTransaction tx) => MusicSql(db, tx,
                "UPDATE receiver_grants SET observation_json=$observation,expires_at=$expires WHERE id=$id AND closed=0", ("$id", grant.Id), ("$observation", JsonSerializer.Serialize(observation, WebJson)), ("$expires", DateTimeOffset.UtcNow.AddMinutes(20).ToString("O")));
        }
    }
    public void ReceiverError(ReceiverGrant expected, string message)
    {
        lock (_writeGate)
        {
            var grant = GetReceiver(expected.Id); if (grant is null || grant.Closed || grant.Revision != expected.Revision) return;
            var observation = grant.Observation ?? new(null, null, null, null, "connecting", Now());
            using var db = Open(); Execute(db, "UPDATE receiver_grants SET observation_json=$json WHERE id=$id", ("$id", grant.Id), ("$json", JsonSerializer.Serialize(observation with { Error = message }, WebJson)));
        }
    }
    public (ReceiverGrant Grant, ReceiverStatus? Result) BeginReceiverCommand(string id, ReceiverControl input, PlaybackActor actor)
    {
        _ = AppFiles.Id(input.CommandId ?? throw new DomainException("Alıcı komutu kimliği gerekli.", 428));
        var hash = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(input, WebJson)));
        lock (_writeGate)
        {
            var grant = OwnReceiver(id, actor); using var db = Open();
            var receipt = Query(db, "SELECT request_hash,result_json FROM receiver_commands WHERE receiver_id=$id AND command_id=$command", r => (Hash: r.GetString(0), Result: Text(r,1)), ("$id", id), ("$command", input.CommandId)).FirstOrDefault();
            if (receipt.Hash is not null)
            {
                if (receipt.Hash != hash) throw new DomainException("Alıcı komutu kimliği farklı bir istekle kullanılmış.", 409);
                return receipt.Result is null ? throw new DomainException("Önceki komutun sonucu kesinleşmedi; alıcı durumunu yenile.", 409) : (grant, JsonSerializer.Deserialize<ReceiverStatus>(receipt.Result, WebJson)!);
            }
            if (input.ExpectedRevision != grant.Revision) throw new DomainException("Alıcı durumu değişti; kumandayı yenile.", 409);
            if (grant.Closed && input.Action != "stop") throw new DomainException("Alıcı oturumu sona erdi.", 410);
            if (input.Action != "stop") _ = RequireReceiver(id, actor);
            if (Convert.ToInt32(Scalar(db, "SELECT COUNT(*) FROM receiver_commands WHERE receiver_id=$id", ("$id", id))) >= 4096) throw new DomainException("Alıcı komut sınırı doldu; yeni bir oturum başlat.", 429);
            using var tx = db.BeginTransaction();
            MusicSql(db, tx, "INSERT INTO receiver_commands(receiver_id,command_id,request_hash,created_at) VALUES($id,$command,$hash,$now); UPDATE receiver_grants SET revision=revision+1 WHERE id=$id",
                ("$id", id), ("$command", input.CommandId), ("$hash", hash), ("$now", Now()));
            tx.Commit(); return (GetReceiver(id)!, null);
        }
    }
    public void CompleteReceiverCommand(string id, string commandId, ReceiverStatus result)
    {
        lock (_writeGate) { using var db = Open(); Execute(db, "UPDATE receiver_commands SET result_json=$json WHERE receiver_id=$id AND command_id=$command AND result_json IS NULL", ("$id", id), ("$command", commandId), ("$json", JsonSerializer.Serialize(result, WebJson))); }
    }
    public void CloseReceiver(string id)
    {
        lock (_writeGate)
        {
            var grant = GetReceiver(id); if (grant is null) return;
            EndPlaybackLease(grant.PlaybackId, "receiver-closed", DateTimeOffset.UtcNow);
            using var db = Open(); Execute(db, "UPDATE receiver_grants SET closed=1,revision=revision+1 WHERE id=$id AND closed=0", ("$id", id));
        }
    }
    public ReceiverCopy? GetReceiverCopy(string mediaId)
    {
        using var db = Open(); return Query(db, "SELECT media_id,fingerprint,match_revision,path,size_bytes,artifact_fingerprint,sha256 FROM receiver_copies WHERE media_id=$id",
            r => new ReceiverCopy(r.GetString(0), r.GetString(1), r.GetInt32(2), r.GetString(3), r.GetInt64(4), Text(r,5), Text(r,6)), ("$id", mediaId)).FirstOrDefault();
    }
    public bool ReceiverCopyReferenced(string path)
    {
        using var db = Open(); return Scalar(db, "SELECT media_id FROM receiver_copies WHERE path=$path UNION ALL SELECT id FROM receiver_grants WHERE closed=0 AND json_extract(artifact_json,'$.path')=$path LIMIT 1", ("$path", path)) is string;
    }
    public void PublishReceiverCopy(ProductJob job, ReceiverCopy copy, Action promote)
    {
        lock (_writeGate)
        {
            var actor = RequireProductJob(job); var source = PlaybackSource(copy.MediaId, actor);
            if (MediaPaths.Key(copy.Path) != MediaPaths.Key(Path.Combine(_paths.Data, "receiver-copies", AppFiles.Id(job.Id), "media.mp4"))) throw new DomainException("Alıcı kopyası yayın kökü dışında.", 403);
            if (job.ScopeId != copy.MediaId || copy.Fingerprint != source.Fingerprint || copy.MatchRevision != source.Media.MatchRevision || copy.ArtifactFingerprint is null || copy.Sha256?.Length != 64 || copy.SizeBytes <= 0) throw new DomainException("Alıcı kopyasının kaynağı veya doğrulaması değişti.", 409);
            promote();
            if (MediaAnalysisService.Fingerprint(new FileInfo(copy.Path)) != copy.ArtifactFingerprint) throw new DomainException("Alıcı kopyası yayın sırasında değişti.", 409);
            using var db = Open(); using var tx = db.BeginTransaction();
            MusicSql(db, tx, """
                INSERT INTO receiver_copies(media_id,fingerprint,match_revision,path,size_bytes,created_at,artifact_fingerprint,sha256) VALUES($media,$fp,$revision,$path,$size,$now,$artifact,$hash)
                ON CONFLICT(media_id) DO UPDATE SET fingerprint=$fp,match_revision=$revision,path=$path,size_bytes=$size,created_at=$now,artifact_fingerprint=$artifact,sha256=$hash;
                UPDATE product_jobs SET status='completed',result_json=$result,progress=100,message='Uyumlu alıcı kopyası doğrulandı.',updated_at=$now,lease=NULL WHERE id=$job AND revision=$jobRevision AND lease=$lease AND status='running';
                """, ("$media", copy.MediaId), ("$fp", copy.Fingerprint), ("$revision", copy.MatchRevision), ("$path", copy.Path), ("$size", copy.SizeBytes), ("$now", Now()), ("$artifact", copy.ArtifactFingerprint), ("$hash", copy.Sha256),
                ("$result", JsonSerializer.Serialize(new { mediaId = copy.MediaId }, WebJson)), ("$job", job.Id), ("$jobRevision", job.Revision), ("$lease", job.Lease));
            tx.Commit();
        }
    }
}
