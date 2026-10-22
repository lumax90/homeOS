using HomeServer.Core;
using Microsoft.Data.Sqlite;

namespace HomeServer.Infrastructure;

public sealed partial class MediaStore
{
    private const string RequestEpisodeFilter = "(r.scope_explicit=1 AND EXISTS(SELECT 1 FROM request_episode_scope scope WHERE scope.request_id=r.id AND scope.episode_id=e.id) OR r.scope_explicit=0 AND e.retired=0 AND (r.season_number IS NULL OR e.season_number=r.season_number) AND (r.episode_number IS NULL OR e.episode_number=r.episode_number))";
    public IReadOnlyList<string> RequestContentBatch(string after)
    {
        using var db = Open();
        return Query(db, "SELECT DISTINCT content_id FROM content_requests WHERE status IN ('approved','fulfilled') AND content_id>$after ORDER BY content_id LIMIT 25", r => r.GetString(0), ("$after", after));
    }
    public void ReconcileRequests(string contentId)
    {
        contentId = CanonicalContentId(contentId);
        lock (_writeGate)
        {
            using var db = Open();
            var visible = "(r.requested_by='1' OR u.role='admin' OR u.all_libraries=1 OR EXISTS(SELECT 1 FROM user_libraries ul WHERE ul.user_id=r.requested_by AND ul.library_id=l.id)) AND "
                + MediaAccess.Replace("$admin", "(r.requested_by='1' OR u.role='admin')", StringComparison.Ordinal).Replace("$maxage", "COALESCE(u.maximum_age,-1)", StringComparison.Ordinal).Replace("$unrated", "COALESCE(u.allow_unrated,1)", StringComparison.Ordinal);
            var wholeMovie = WholeMovieReady.Replace("$admin", "(r.requested_by='1' OR u.role='admin')", StringComparison.Ordinal).Replace("$maxage", "COALESCE(u.maximum_age,-1)", StringComparison.Ordinal).Replace("$unrated", "COALESCE(u.allow_unrated,1)", StringComparison.Ordinal)
                .Replace("$all", "(r.requested_by='1' OR u.role='admin' OR u.all_libraries=1)", StringComparison.Ordinal).Replace("$viewer", "r.requested_by", StringComparison.Ordinal);
            var rows = Query(db, $"""
                SELECT r.id,r.requested_by,r.status,c.title,
                  CASE WHEN c.kind='movie' THEN EXISTS(SELECT 1 FROM media_matches mm JOIN media m ON m.id=mm.media_id JOIN libraries l ON l.id=m.library_id
                    WHERE mm.content_id=c.id AND m.available=1 AND l.enabled=1 AND {visible} AND {wholeMovie})
                  ELSE EXISTS(SELECT 1 FROM episodes e WHERE e.content_id=c.id AND {RequestEpisodeFilter}
                    AND (r.monitor_mode<>'future' OR e.air_date IS NULL OR e.air_date>=substr(r.created_at,1,10)))
                    AND NOT EXISTS(SELECT 1 FROM episodes e WHERE e.content_id=c.id AND {RequestEpisodeFilter} AND (r.monitor_mode<>'future' OR e.air_date IS NULL OR e.air_date>=substr(r.created_at,1,10))
                      AND NOT EXISTS(SELECT 1 FROM media_episode_segments ms JOIN media m ON m.id=ms.media_id JOIN libraries l ON l.id=m.library_id
                        WHERE ms.episode_id=e.id AND m.available=1 AND l.enabled=1 AND {visible})) END AS ready
                FROM content_requests r JOIN content c ON c.id=r.content_id LEFT JOIN users u ON u.id=r.requested_by
                WHERE r.content_id=$content AND r.status IN ('approved','fulfilled')
                """, r => (Id: r.GetString(0), User: r.GetString(1), Status: r.GetString(2), Title: r.GetString(3), Ready: r.GetBoolean(4)), ("$content", contentId));
            using var tx = db.BeginTransaction();
            foreach (var row in rows)
            {
                var state = row.Ready ? "fulfilled" : "approved";
                if (state == row.Status) continue;
                using var update = Command(db, "UPDATE content_requests SET status=$state,updated_at=$now WHERE id=$id", ("$state", state), ("$now", Now()), ("$id", row.Id));
                update.Transaction = tx; update.ExecuteNonQuery();
                AddRequestEvent(db, tx, row.Id, "system", state, row.Ready ? "İstenen kapsam kütüphanede hazır." : "İstenen kapsamda eksik dosya var.");
                if (row.Ready) AddNotification(db, tx, row.User, "available", row.Title, "İstediğin içerik oynatmaya hazır.", contentId, row.Id + ":available");
            }
            tx.Commit();
        }
    }
    public IReadOnlyList<RequestEntry> GetRequests(Account? viewer = null, string? status = null)
    {
        using var db = Open();
        var rows = Query(db, $"""
            SELECT r.id,r.content_id,r.requested_by,COALESCE(u.display_name,'Sistem'),r.status,r.season_number,r.episode_number,
              r.monitor_mode,r.quality_profile_id,r.note,r.created_at,r.updated_at,CASE WHEN r.scope_explicit=1 THEN (SELECT COUNT(*) FROM request_episode_scope scope WHERE scope.request_id=r.id) END
            FROM content_requests r JOIN content c ON c.id=r.content_id LEFT JOIN users u ON u.id=r.requested_by
            WHERE {ContentAccess} AND ($admin=1 OR r.requested_by=$viewer) AND ($status='' OR r.status=$status) ORDER BY r.created_at DESC LIMIT 500
            """, r => (Id: r.GetString(0), ContentId: r.GetString(1), User: r.GetString(2), Name: r.GetString(3), Status: r.GetString(4),
                Season: r.IsDBNull(5) ? (int?)null : r.GetInt32(5), Episode: r.IsDBNull(6) ? (int?)null : r.GetInt32(6), Mode: r.GetString(7),
                Profile: r.GetString(8), Note: r.GetString(9), Created: r.GetString(10), Updated: r.GetString(11), ScopeCount: r.IsDBNull(12) ? (int?)null : r.GetInt32(12)), [("$status", status ?? ""), ..ViewerArgs(viewer)]);
        return rows.Select(r => new RequestEntry(r.Id, r.ContentId, r.User, r.Name, r.Status, r.Season, r.Episode, r.Mode, r.Profile, r.Note, r.Created, r.Updated, GetContent(r.ContentId, viewer)!.Metadata, r.ScopeCount)).ToArray();
    }
    public IReadOnlyList<RequestEvent> GetRequestEvents(string requestId, Account viewer)
    {
        using var db = Open();
        if (Scalar(db, "SELECT r.id FROM content_requests r JOIN content c ON c.id=r.content_id WHERE r.id=$id AND " + ContentAccess + " AND ($admin=1 OR r.requested_by=$viewer)", [("$id", requestId), ..ViewerArgs(viewer)]) is not string)
            throw new DomainException("İstek bulunamadı.", 404);
        return Query(db, """
            SELECT e.id,e.request_id,COALESCE(u.display_name,'Sistem'),e.status,e.note,e.created_at
            FROM request_events e LEFT JOIN users u ON u.id=e.actor_id WHERE e.request_id=$id ORDER BY e.created_at
            """, r => new RequestEvent(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5)), ("$id", requestId));
    }
    public void DecideRequest(string requestId, string status, string note, Account? viewer)
    {
        if (status is not ("approved" or "rejected" or "cancelled") || note.Length > 1000) throw new DomainException("İstek kararı geçersiz.");
        lock (_writeGate)
        {
            using var db = Open();
            var request = Query(db, "SELECT content_id,requested_by,status,monitor_mode,quality_profile_id FROM content_requests WHERE id=$id",
                r => (Content: r.GetString(0), User: r.GetString(1), Status: r.GetString(2), Mode: r.GetString(3), Profile: r.GetString(4)), ("$id", requestId)).FirstOrDefault();
            if (request.Content is null) throw new DomainException("İstek bulunamadı.", 404);
            if (viewer is not null && !viewer.IsAdmin && (status != "cancelled" || request.User != viewer.Id)) throw new DomainException("Bu isteği yönetme iznin yok.", 403);
            if (request.Status == status) return;
            if (status is "approved" or "rejected" && request.Status is not ("pending" or "rejected")) throw new DomainException("Bu istek artık onay beklemiyor.", 409);
            using var tx = db.BeginTransaction();
            using var command = Command(db, """
                UPDATE content_requests SET status=$status,note=$note,updated_at=$now WHERE id=$id;
                UPDATE tracking SET monitor_mode=$mode,quality_profile_id=$profile,sync_state='pending',next_sync_at=$now,revision=revision+1 WHERE content_id=$content AND $status='approved';
                UPDATE tracking SET monitor_mode='none',sync_state='idle',revision=revision+1 WHERE content_id=$content
                  AND NOT EXISTS(SELECT 1 FROM content_requests r WHERE r.content_id=$content AND r.status IN ('approved','fulfilled'));
                """, ("$id", requestId), ("$content", request.Content), ("$status", status), ("$note", note.Trim()), ("$mode", request.Mode), ("$profile", request.Profile), ("$now", Now()));
            command.Transaction = tx; command.ExecuteNonQuery();
            AddRequestEvent(db, tx, requestId, viewer?.Id ?? "1", status, note);
            var content = GetContent(request.Content)!;
            AddNotification(db, tx, request.User, "request", content.Metadata.Title,
                status == "approved" ? "İsteğin onaylandı." : status == "rejected" ? "İsteğin reddedildi. " + note : "İstek iptal edildi.", request.Content, requestId + ":" + status + ":" + Guid.NewGuid().ToString("N"));
            tx.Commit();
        }
    }
    private static void AddRequestEvent(SqliteConnection db, SqliteTransaction tx, string request, string actor, string status, string note)
    {
        using var command = Command(db, "INSERT INTO request_events(id,request_id,actor_id,status,note,created_at) VALUES($id,$request,$actor,$status,$note,$now)",
            ("$id", Guid.NewGuid().ToString("N")), ("$request", request), ("$actor", actor), ("$status", status), ("$note", note), ("$now", Now()));
        command.Transaction = tx; command.ExecuteNonQuery();
    }
    private static void AddNotification(SqliteConnection db, SqliteTransaction tx, string user, string kind, string title, string message, string? content, string dedup)
    {
        using var command = Command(db, """
            INSERT OR IGNORE INTO notifications(id,user_id,kind,title,message,content_id,created_at,dedup_key) VALUES($id,$user,$kind,$title,$message,$content,$now,$dedup)
            """, ("$id", Guid.NewGuid().ToString("N")), ("$user", user), ("$kind", kind), ("$title", title), ("$message", message), ("$content", content), ("$now", Now()), ("$dedup", dedup));
        command.Transaction = tx; command.ExecuteNonQuery();
    }
    public IReadOnlyList<NotificationItem> GetNotifications(string user)
    {
        using var db = Open();
        return Query(db, "SELECT id,kind,title,message,content_id,created_at,read_at FROM notifications WHERE user_id=$user AND (content_id IS NULL OR EXISTS(SELECT 1 FROM content c WHERE c.id=notifications.content_id AND " + ContentAccess + ")) ORDER BY created_at DESC LIMIT 100",
            r => new NotificationItem(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), Text(r, 4), r.GetString(5), Text(r, 6)), [("$user", user), ..ViewerArgs(GetAccount(user))]);
    }
    public void ReadNotification(string user, string? id)
    {
        lock (_writeGate) { using var db = Open(); Execute(db, "UPDATE notifications SET read_at=COALESCE(read_at,$now) WHERE user_id=$user AND ($id IS NULL OR id=$id)", ("$user", user), ("$id", id), ("$now", Now())); }
    }
}
