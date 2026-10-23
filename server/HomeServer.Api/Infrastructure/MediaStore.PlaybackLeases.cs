using System.Text.Json;
using HomeServer.Core;
using HomeServer.Playback;
using Microsoft.Data.Sqlite;

namespace HomeServer.Infrastructure;

public sealed partial class MediaStore
{
    public static readonly TimeSpan PlaybackIdleTimeout = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan PlaybackPausedTimeout = TimeSpan.FromMinutes(15);
    private const string LeaseSelect = "SELECT id,user_id,login_id,device_id,client_id,client_revision,media_id,fingerprint,match_revision,title,mode,status,created_at,last_seen_at,paused_at,position_seconds,ended_at,end_reason,session_json,global_revision,user_revision,device_revision,watch_room_id,watch_member_id,watch_epoch FROM playback_leases";
    private static PlaybackLease ReadLease(SqliteDataReader r) => new(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetInt64(5), r.GetString(6), r.GetString(7), r.GetInt32(8), r.GetString(9), r.GetString(10), r.GetString(11), r.GetString(12), r.GetString(13), Text(r, 14), r.GetDouble(15), Text(r, 16), Text(r, 17), r.IsDBNull(18) ? null : JsonSerializer.Deserialize<PlaybackSession>(r.GetString(18), WebJson), r.GetInt32(19), r.GetInt32(20), r.GetInt32(21), Text(r, 22), Text(r, 23), r.IsDBNull(24) ? null : r.GetInt32(24));
    public PlaybackLease? GetPlaybackLease(string id) { using var db = Open(); return Query(db, LeaseSelect + " WHERE id=$id", ReadLease, ("$id", id)).FirstOrDefault(); }
    public IReadOnlyList<PlaybackLease> ActivePlaybackLeases() { using var db = Open(); return Query(db, LeaseSelect + " WHERE status<>'closed' ORDER BY created_at", ReadLease); }
    public (MediaItem Media, string Path, string Fingerprint) PlaybackSource(string mediaId, Account viewer)
    {
        var media = GetMedia(mediaId, viewer) ?? throw new DomainException("Medya bulunamadı veya erişimin değişti.", 404);
        if (!media.Available) throw new DomainException("Medya şu anda çevrimdışı.", 409);
        var library = GetLibrary(media.LibraryId) ?? throw new DomainException("Kütüphane çevrimdışı.", 409);
        try
        {
            var path = MediaPaths.ResolveFile(library.Path, media.RelativePath); var info = new FileInfo(path);
            if (!info.Exists) throw new DomainException("Medya dosyasına erişilemiyor.", 404);
            return (media, path, MediaAnalysisService.Fingerprint(info));
        }
        catch (IOException) { throw new DomainException("Medya dosyasına erişilemiyor.", 404); }
    }
    private Account RequirePlaybackActor(PlaybackActor actor)
    {
        var current = GetPlaybackActor(actor.UserId, actor.LoginId);
        if (current.DeviceId != actor.DeviceId) throw new DomainException("Cihaz oturumu değişti.", 401);
        return GetAccount(actor.UserId)!;
    }
    public PlaybackAdmission AdmitPlayback(PlaybackActor actor, string clientId, long revision, string mediaId, string mode, string requestHash, DateTimeOffset now, string? replaceId = null, WatchPlaybackBinding? watch = null)
    {
        _ = AppFiles.Id(clientId);
        if (revision is < 1 or > 9_007_199_254_740_991 || mode is not ("preparing" or "pcm")) throw new DomainException("Oynatma komutu geçersiz.");
        lock (_writeGate)
        {
            RequireContainerWorkAllowed();
            var viewer = RequirePlaybackActor(actor); var source = PlaybackSource(mediaId, viewer);
            if (watch is not null) RequireWatchPlayback(watch, actor, clientId, mediaId, now);
            if (mode == "pcm" && source.Media.Kind != "audio") throw new DomainException("Kesintisiz müzik için ses dosyası seç.");
            ReconcilePlaybackLeases(now);
            using var db = Open();
            var intent = Query(db, "SELECT revision,closed,request_hash FROM playback_intents WHERE login_id=$login AND client_id=$client",
                r => (Revision: r.GetInt64(0), Closed: r.GetBoolean(1), Hash: r.GetString(2)), ("$login", actor.LoginId), ("$client", clientId)).ToArray();
            if (intent.Length > 0 && (intent[0].Revision > revision || intent[0].Revision == revision && (intent[0].Closed || intent[0].Hash != requestHash))) throw new DomainException("Daha yeni oynatma komutu zaten uygulandı.", 409);
            var active = ActivePlaybackLeases(); var previous = active.Where(item => item.LoginId == actor.LoginId && item.ClientId == clientId).ToArray();
            if (intent.Length > 0 && intent[0].Revision == revision)
            {
                var existing = previous.FirstOrDefault(item => item.ClientRevision == revision);
                return existing is null ? throw new DomainException("Bu oynatma komutu sona erdi; yeni komut gönder.", 410) : new(existing, true, []);
            }
            if (replaceId is not null && GetPlaybackLease(replaceId) is { } replace && (replace.LoginId != actor.LoginId || replace.ClientId != clientId))
                throw new DomainException("Yalnız aynı cihaz oynatıcısının oturumu değiştirilebilir.", 403);
            var server = GetPlaybackLimits(); var user = GetUserPlaybackLimits(actor.UserId); var device = GetPlaybackDevice(actor.DeviceId)!;
            var others = active.Where(item => !previous.Any(old => old.Id == item.Id)).ToArray();
            var replacingRadio=ActiveMusicRadioSessions().Where(item=>item.LoginId==actor.LoginId&&item.ClientId==clientId).ToArray();
            var replacingLive=ActiveLiveSessions().Where(item=>item.LoginId==actor.LoginId&&item.ClientId==clientId).ToArray();
            var transient=ActiveLiveSessions().Where(item=>!replacingLive.Any(old=>old.Id==item.Id)).Select(item=>(item.UserId,item.DeviceId)).Concat(ActivePodcastSessions().Select(item=>(item.UserId,item.DeviceId))).Concat(ActiveMusicRadioSessions().Where(item=>!replacingRadio.Any(old=>old.Id==item.Id)).Select(item=>(item.UserId,item.DeviceId))).ToArray();
            if (user.MaximumStreams == 0 || device.MaximumStreams == 0) throw new DomainException("Bu hesap veya cihazda oynatma kapalı.", 403);
            if (others.Count(item => item.UserId == actor.UserId)+transient.Count(item=>item.UserId==actor.UserId) >= user.MaximumStreams) throw new DomainException($"Hesabının {user.MaximumStreams} eşzamanlı oynatma sınırına ulaşıldı. Açık oynatmalardan birini kapat.", 429);
            if (others.Where(item => item.UserId == actor.UserId).Select(item => item.DeviceId).Concat(transient.Where(item=>item.UserId==actor.UserId).Select(item=>item.DeviceId)).Append(actor.DeviceId).Distinct().Count() > user.MaximumDevices) throw new DomainException($"Hesabın en fazla {user.MaximumDevices} cihazda aynı anda oynatabilir.", 429);
            var deviceLimit = device.MaximumStreams ?? server.MaximumStreamsPerDevice;
            if (others.Count(item => item.DeviceId == actor.DeviceId)+transient.Count(item=>item.DeviceId==actor.DeviceId) >= deviceLimit) throw new DomainException($"Bu cihazın {deviceLimit} eşzamanlı oynatma sınırına ulaşıldı.", 429);
            if (others.Length+transient.Length >= server.MaximumStreams) throw new DomainException($"Sunucunun {server.MaximumStreams} eşzamanlı oynatma sınırına ulaşıldı.", 429);
            if (intent.Length == 0) RequirePlaybackIntentCapacity(db, actor.LoginId, now);
            var id = Guid.NewGuid().ToString("N"); var stamp = now.ToString("O");
            using var tx = db.BeginTransaction(); using var command = Command(db, """
                UPDATE playback_leases SET status='closed',ended_at=$now,end_reason='replaced' WHERE login_id=$login AND client_id=$client AND status<>'closed';
                INSERT INTO playback_intents(login_id,client_id,revision,closed,request_hash,updated_at) VALUES($login,$client,$revision,0,$hash,$now)
                  ON CONFLICT(login_id,client_id) DO UPDATE SET revision=$revision,closed=0,request_hash=$hash,updated_at=$now;
                INSERT INTO playback_leases(id,user_id,login_id,device_id,client_id,client_revision,media_id,fingerprint,match_revision,title,mode,status,created_at,last_seen_at,global_revision,user_revision,device_revision,watch_room_id,watch_member_id,watch_epoch)
                  VALUES($id,$user,$login,$device,$client,$revision,$media,$fp,$match,$title,$mode,'starting',$now,$now,$global,$policy,$deviceRevision,$room,$member,$epoch);
                UPDATE playback_devices SET last_seen_at=$now WHERE id=$device;
                DELETE FROM playback_leases WHERE id IN (SELECT id FROM playback_leases WHERE status='closed' ORDER BY ended_at DESC LIMIT -1 OFFSET 10000);
                """, ("$id", id), ("$user", actor.UserId), ("$login", actor.LoginId), ("$device", actor.DeviceId), ("$client", clientId), ("$revision", revision), ("$hash", requestHash), ("$media", mediaId), ("$fp", source.Fingerprint), ("$match", source.Media.MatchRevision), ("$title", source.Media.Title), ("$mode", mode), ("$now", stamp), ("$global", server.Revision), ("$policy", user.Revision), ("$deviceRevision", device.Revision), ("$room", watch?.RoomId), ("$member", watch?.MemberId), ("$epoch", watch?.Epoch));
            command.Transaction = tx; command.ExecuteNonQuery(); tx.Commit();
            foreach(var radio in replacingRadio)CloseMusicRadio(radio.Id);
            foreach(var live in replacingLive)SetLiveSession(live.Id,"closed");
            return new(GetPlaybackLease(id)!, false, previous.Select(item => item.Id).ToArray());
        }
    }
    private string? InvalidPlayback(PlaybackLease lease, DateTimeOffset now)
    {
        if (now - DateTimeOffset.Parse(lease.LastSeenAt) >= PlaybackIdleTimeout || now - DateTimeOffset.Parse(lease.CreatedAt) >= TimeSpan.FromHours(24)) return "expired";
        if (lease.PausedAt is { } paused && now - DateTimeOffset.Parse(paused) >= PlaybackPausedTimeout) return "paused-timeout";
        if (ValidateLoginSession(lease.LoginId, lease.UserId) is not { } viewer) return "access-revoked";
        if (lease.WatchRoomId is not null && !WatchPlaybackCurrent(lease)) return "watch-room-ended";
        if (GetPlaybackLimits().Revision != lease.GlobalRevision || GetUserPlaybackLimits(lease.UserId).Revision != lease.UserRevision || GetPlaybackDevice(lease.DeviceId)?.Revision != lease.DeviceRevision) return "limits-changed";
        try
        {
            var source = PlaybackSource(lease.MediaId, viewer);
            if (source.Fingerprint != lease.Fingerprint || source.Media.MatchRevision != lease.MatchRevision) return "source-changed";
        }
        catch (Exception ex) when (ex is DomainException or IOException or UnauthorizedAccessException) { return "access-revoked"; }
        return null;
    }
    public PlaybackLease RequirePlaybackLease(string? id, PlaybackActor actor, DateTimeOffset now, string? mode = null, string? mediaId = null)
    {
        if (id is null) throw new DomainException("Önce bir oynatma oturumu başlat.", 428);
        _ = AppFiles.Id(id);
        lock (_writeGate)
        {
            var lease = GetPlaybackLease(id) ?? throw new DomainException("Oynatma oturumu sona erdi; yeniden oynat.", 410);
            if (lease.UserId != actor.UserId || lease.LoginId != actor.LoginId || lease.DeviceId != actor.DeviceId) throw new DomainException("Bu oynatma farklı bir giriş oturumuna ait.", 403);
            if (lease.Status == "closed") throw new DomainException("Oynatma oturumu kapatıldı; yeniden oynat.", 410);
            if (InvalidPlayback(lease, now) is { } reason) { EndPlaybackLease(id, reason, now); throw new DomainException("Oynatma erişimi veya sınırları değişti; yeniden oynat.", 410); }
            if (mode is not null && mode != lease.Mode || mediaId is not null && mediaId != lease.MediaId && lease.Mode != "pcm") throw new DomainException("Oynatma oturumu bu akışa ait değil.", 403);
            return lease;
        }
    }
    public PlaybackLease BindPlayback(PlaybackLease lease, PlaybackSession session, DateTimeOffset now)
    {
        lock (_writeGate)
        {
            _ = RequirePlaybackLease(lease.Id, new(lease.UserId, lease.LoginId, lease.DeviceId), now);
            using var db = Open(); Execute(db, "UPDATE playback_leases SET session_json=$json,mode=$mode,status='active',last_seen_at=$now WHERE id=$id AND status='starting'",
                ("$json", JsonSerializer.Serialize(session, WebJson)), ("$mode", session.Mode), ("$id", lease.Id), ("$now", now.ToString("O")));
            return GetPlaybackLease(lease.Id)!;
        }
    }
    public PlaybackLease TouchPlayback(string id, PlaybackActor actor, PlaybackHeartbeat? input, DateTimeOffset now)
    {
        lock (_writeGate)
        {
            var lease = RequirePlaybackLease(id, actor, now);
            var mediaId = input?.MediaId ?? lease.MediaId; PlaybackMediaGrant? grant = null;
            if (lease.Mode == "pcm" && input is not null)
            {
                using var check = Open();
                if (mediaId != lease.MediaId || Scalar(check, "SELECT media_id FROM playback_media WHERE lease_id=$lease AND media_id=$media", ("$lease", id), ("$media", mediaId)) is not null)
                    grant = RequirePlaybackMedia(id, actor, mediaId, null, now);
            }
            else if (mediaId != lease.MediaId) throw new DomainException("Oynatma medyası değişti.", 409);
            var duration = grant?.Frames / 48000d ?? lease.Session?.DurationSeconds ?? 86400;
            if (input is not null && (!double.IsFinite(input.PositionSeconds) || input.PositionSeconds < 0 || input.PositionSeconds > (duration > 0 ? duration : 86400) + 5)) throw new DomainException("Oynatma konumu geçersiz.");
            var pausedAt = input is null ? lease.PausedAt : input.Paused ? lease.PausedAt ?? now.ToString("O") : null;
            using var db = Open(); Execute(db, """
                UPDATE playback_leases SET last_seen_at=$now,paused_at=$paused,position_seconds=$position,media_id=$media,fingerprint=$fp,match_revision=$match,title=$title WHERE id=$id;
                UPDATE playback_devices SET last_seen_at=$now WHERE id=$device;
                """, ("$id", id), ("$now", now.ToString("O")), ("$paused", pausedAt), ("$position", input?.PositionSeconds ?? lease.PositionSeconds), ("$media", mediaId), ("$fp", grant?.Fingerprint ?? lease.Fingerprint), ("$match", grant?.MatchRevision ?? lease.MatchRevision), ("$title", mediaId == lease.MediaId ? lease.Title : GetMedia(mediaId)!.Title), ("$device", actor.DeviceId));
            return GetPlaybackLease(id)!;
        }
    }
    public PlaybackMediaGrant AuthorizePlaybackMedia(string id, PlaybackActor actor, string mediaId, DateTimeOffset now)
    {
        lock (_writeGate)
        {
            _ = RequirePlaybackLease(id, actor, now, "pcm"); var source = PlaybackSource(mediaId, RequirePlaybackActor(actor));
            if (source.Media.Kind != "audio") throw new DomainException("PCM kuyruğunda yalnız ses dosyaları hazırlanabilir.");
            using var db = Open(); using var tx = db.BeginTransaction(); using var command = Command(db, """
                INSERT INTO playback_media(lease_id,media_id,fingerprint,match_revision,touched_at) VALUES($lease,$media,$fp,$match,$now)
                  ON CONFLICT(lease_id,media_id) DO UPDATE SET frames=CASE WHEN fingerprint=$fp THEN frames ELSE NULL END,fingerprint=$fp,match_revision=$match,touched_at=$now;
                DELETE FROM playback_media WHERE lease_id=$lease AND media_id NOT IN (SELECT media_id FROM playback_media WHERE lease_id=$lease ORDER BY (media_id=$media) DESC,touched_at DESC,media_id LIMIT 3)
                  AND media_id<>(SELECT media_id FROM playback_leases WHERE id=$lease);
                """, ("$lease", id), ("$media", mediaId), ("$fp", source.Fingerprint), ("$match", source.Media.MatchRevision), ("$now", now.ToString("O")));
            command.Transaction = tx; command.ExecuteNonQuery(); tx.Commit(); return new(mediaId, source.Fingerprint, source.Media.MatchRevision, null);
        }
    }
    public PlaybackMediaGrant RequirePlaybackMedia(string id, PlaybackActor actor, string mediaId, string? fingerprint, DateTimeOffset now)
    {
        lock (_writeGate)
        {
            _ = RequirePlaybackLease(id, actor, now, "pcm");
            var grant = GetPlaybackMedia(id, mediaId) ?? throw new DomainException("Bu ses izi oynatma kuyruğunda yeniden hazırlanmalı.", 410);
            var source = PlaybackSource(mediaId, RequirePlaybackActor(actor));
            if (source.Fingerprint != grant.Fingerprint || source.Media.MatchRevision != grant.MatchRevision || fingerprint is not null && fingerprint != grant.Fingerprint) throw new DomainException("Ses dosyası veya erişimi hazırlıktan sonra değişti.", 409);
            return grant;
        }
    }
    public PlaybackMediaGrant? GetPlaybackMedia(string id, string mediaId)
    {
        using var db = Open(); return Query(db, "SELECT media_id,fingerprint,match_revision,frames FROM playback_media WHERE lease_id=$lease AND media_id=$media",
            r => new PlaybackMediaGrant(r.GetString(0), r.GetString(1), r.GetInt32(2), r.IsDBNull(3) ? null : r.GetInt64(3)), ("$lease", id), ("$media", mediaId)).FirstOrDefault();
    }
    public void CompletePlaybackMedia(string id, PlaybackActor actor, MusicPcmInfo info, DateTimeOffset now)
    {
        lock (_writeGate)
        {
            _ = RequirePlaybackMedia(id, actor, info.MediaId, info.Fingerprint, now);
            using var db = Open(); Execute(db, "UPDATE playback_media SET frames=$frames WHERE lease_id=$lease AND media_id=$media", ("$frames", info.Frames), ("$lease", id), ("$media", info.MediaId));
        }
    }
    public void EndPlaybackLease(string id, string reason, DateTimeOffset now)
    {
        lock (_writeGate)
        {
            using var db = Open(); Execute(db, """
                UPDATE playback_leases SET status='closed',ended_at=$now,end_reason=$reason WHERE id=$id AND status<>'closed';
                UPDATE playback_intents SET closed=1,updated_at=$now WHERE EXISTS(SELECT 1 FROM playback_leases p WHERE p.id=$id AND p.login_id=playback_intents.login_id AND p.client_id=playback_intents.client_id AND p.client_revision=playback_intents.revision);
                DELETE FROM playback_leases WHERE id IN (SELECT id FROM playback_leases WHERE status='closed' ORDER BY ended_at DESC LIMIT -1 OFFSET 10000);
                """, ("$id", id), ("$now", now.ToString("O")), ("$reason", reason));
        }
    }
    public string[] ClosePlaybackClient(PlaybackActor actor, string clientId, long revision, DateTimeOffset now)
    {
        _ = AppFiles.Id(clientId);
        if (revision is < 0 or > 9_007_199_254_740_991) throw new DomainException("Oynatma revizyonu geçersiz.");
        lock (_writeGate)
        {
            _ = RequirePlaybackActor(actor); using var db = Open();
            if (Scalar(db, "SELECT client_id FROM playback_intents WHERE login_id=$login AND client_id=$client", ("$login", actor.LoginId), ("$client", clientId)) is null) RequirePlaybackIntentCapacity(db, actor.LoginId, now);
            Execute(db, """
                INSERT INTO playback_intents(login_id,client_id,revision,closed,request_hash,updated_at) VALUES($login,$client,$revision,1,'',$now)
                ON CONFLICT(login_id,client_id) DO UPDATE SET revision=$revision,closed=1,updated_at=$now WHERE revision<=$revision;
                """, ("$login", actor.LoginId), ("$client", clientId), ("$revision", revision), ("$now", now.ToString("O")));
            var leases = ActivePlaybackLeases().Where(item => item.LoginId == actor.LoginId && item.ClientId == clientId && item.ClientRevision <= revision).ToArray();
            foreach (var lease in leases) EndPlaybackLease(lease.Id, "closed", now); return leases.Select(item => item.Id).ToArray();
        }
    }
    public string[] ReconcilePlaybackLeases(DateTimeOffset now)
    {
        lock (_writeGate)
        {
            var closed = new List<string>(); foreach (var lease in ActivePlaybackLeases())
                if (InvalidPlayback(lease, now) is { } reason) { EndPlaybackLease(lease.Id, reason, now); closed.Add(lease.Id); }
            return closed.ToArray();
        }
    }
    private static void RequirePlaybackIntentCapacity(SqliteConnection db, string loginId, DateTimeOffset now)
    {
        Execute(db, "DELETE FROM playback_intents WHERE NOT EXISTS(SELECT 1 FROM user_sessions s WHERE s.id=playback_intents.login_id AND s.revoked_at IS NULL AND s.expires_at>$now)", ("$now", now.ToString("O")));
        if (Convert.ToInt32(Scalar(db, "SELECT COUNT(*) FROM playback_intents WHERE login_id=$login", ("$login", loginId))) >= 1024 || Convert.ToInt32(Scalar(db, "SELECT COUNT(*) FROM playback_intents")) >= 20000)
            throw new DomainException("Oynatıcı komut kaydı sınırına ulaşıldı; eski giriş oturumlarını kapatıp yeniden giriş yap.", 429);
    }
    public void RecoverPlaybackLeases(DateTimeOffset now)
    {
        lock (_writeGate) { foreach (var lease in ActivePlaybackLeases()) EndPlaybackLease(lease.Id, "server-restarted", now); PrunePlaybackHistory(now); }
    }
    public void PrunePlaybackHistory(DateTimeOffset now)
    {
        lock (_writeGate)
        {
            using var db = Open(); Execute(db, """
                DELETE FROM playback_leases WHERE status='closed' AND (ended_at<$before OR id IN (SELECT id FROM playback_leases WHERE status='closed' ORDER BY ended_at DESC LIMIT -1 OFFSET 10000));
                DELETE FROM playback_intents WHERE NOT EXISTS(SELECT 1 FROM user_sessions s WHERE s.id=playback_intents.login_id AND s.revoked_at IS NULL AND s.expires_at>$now);
                """, ("$before", now.AddDays(-90).ToString("O")), ("$now", now.ToString("O")));
        }
    }
    public IReadOnlyList<PlaybackActivity> GetPlaybackActivity(string? userId, string deviceId, bool history)
    {
        using var db = Open(); return Query(db, """
            SELECT p.id,p.user_id,u.display_name,p.device_id,d.name,p.media_id,p.title,p.mode,p.status,p.created_at,p.last_seen_at,p.paused_at,p.position_seconds,p.ended_at,p.end_reason
            FROM playback_leases p JOIN users u ON u.id=p.user_id JOIN playback_devices d ON d.id=p.device_id
            WHERE ($user IS NULL OR p.user_id=$user) AND CASE WHEN $history=1 THEN p.status='closed' ELSE p.status<>'closed' END
            ORDER BY COALESCE(p.ended_at,p.created_at) DESC LIMIT 100
            """, r => new PlaybackActivity(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5), r.GetString(6), r.GetString(7), r.GetString(8), r.GetString(9), r.GetString(10), !r.IsDBNull(11), r.GetDouble(12), Text(r, 13), Text(r, 14), r.GetString(3) == deviceId), ("$user", userId), ("$history", history));
    }
}
