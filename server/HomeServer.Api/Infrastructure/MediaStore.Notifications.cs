using System.Text.Json;
using HomeServer.Core;
using Microsoft.Data.Sqlite;

namespace HomeServer.Infrastructure;

public sealed partial class MediaStore
{
    private const string ChannelSelect = "SELECT id,user_id,name,kind,enabled,events_json,maximum_hourly_attempts,revision,target,protected_secrets,created_at,dropped,deleted FROM notification_channels";
    private static NotificationConnection ReadChannel(SqliteDataReader r) => new(new(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetBoolean(4), JsonSerializer.Deserialize<string[]>(r.GetString(5))!, r.GetInt32(6), r.GetInt32(7), r.GetString(8), r.GetString(9).Length > 0, r.GetString(10), r.GetInt32(11), r.GetBoolean(12)), r.GetString(9));
    public NotificationConnection? GetNotificationConnection(string id) { using var db = Open(); return Query(db, ChannelSelect + " WHERE id=$id", ReadChannel, ("$id", id)).FirstOrDefault(); }
    public IReadOnlyList<NotificationChannel> GetNotificationChannels(string? userId = null)
    {
        using var db = Open(); return Query(db, ChannelSelect + " WHERE deleted=0 AND ($user IS NULL OR user_id=$user) ORDER BY created_at", ReadChannel, ("$user", userId)).Select(row => row.Channel).ToArray();
    }
    public NotificationChannel SaveNotificationChannel(string? id, NotificationChannelInput input, string protectedSecrets, string target)
    {
        lock (_writeGate)
        {
            var recipient = GetAccount(input.UserId);
            if (recipient is null || input.Enabled && !recipient.Enabled) throw new DomainException("Etkin bir bildirim alıcısı hesabı seç.", 409);
            var current = id is null ? null : GetNotificationConnection(id)?.Channel ?? throw new DomainException("Bildirim kanalı bulunamadı.", 404);
            if (current is not null && (current.Deleted || current.Revision != input.Revision || current.UserId != input.UserId || current.Kind != input.Kind)) throw new DomainException("Kanal değişti; alıcı hesabı veya kanal türü için yeni kanal oluştur.", 409);
            using var db = Open();
            if (current is null && (GetNotificationChannels(input.UserId).Count >= 8 || GetNotificationChannels().Count >= 64)) throw new DomainException("Kanal sınırı hesap başına 8, sunucuda 64.", 429);
            id ??= Guid.NewGuid().ToString("N"); using var tx = db.BeginTransaction(); using var command = Command(db, """
                INSERT INTO notification_channels(id,user_id,name,kind,enabled,events_json,maximum_hourly_attempts,revision,target,protected_secrets,created_at)
                  VALUES($id,$user,$name,$kind,$enabled,$events,$maximum,1,$target,$secret,$now)
                  ON CONFLICT(id) DO UPDATE SET name=$name,enabled=$enabled,events_json=$events,maximum_hourly_attempts=$maximum,revision=revision+1,target=$target,protected_secrets=$secret;
                UPDATE notification_deliveries SET status='cancelled',revision=revision+1,lease=NULL,finished_at=$now,result='Kanal değişti; eski gönderim iptal edildi.' WHERE channel_id=$id AND status IN ('queued','sending');
                UPDATE notification_delivery_attempts SET finished_at=$now,result='Kanal değişti; uzak sonuç geri alınamaz.' WHERE channel_id=$id AND finished_at IS NULL;
                """, ("$id", id), ("$user", input.UserId), ("$name", input.Name.Trim()), ("$kind", input.Kind), ("$enabled", input.Enabled), ("$events", JsonSerializer.Serialize(input.Events.Distinct())), ("$maximum", input.MaximumHourlyAttempts), ("$target", target), ("$secret", protectedSecrets), ("$now", Now()));
            command.Transaction = tx; command.ExecuteNonQuery(); tx.Commit(); return GetNotificationConnection(id)!.Channel;
        }
    }
    public void DeleteNotificationChannel(string id, int revision)
    {
        lock (_writeGate)
        {
            var current = GetNotificationConnection(id)?.Channel ?? throw new DomainException("Kanal bulunamadı.", 404);
            if (current.Deleted) return;
            if (current.Revision != revision) throw new DomainException("Kanal değişti; yeniden aç.", 409);
            using var db = Open(); Execute(db, """
                UPDATE notification_channels SET enabled=0,deleted=1,protected_secrets='',revision=revision+1 WHERE id=$id;
                UPDATE notification_deliveries SET status='cancelled',revision=revision+1,lease=NULL,finished_at=$now,result='Kanal silindi.' WHERE channel_id=$id AND status IN ('queued','sending');
                UPDATE notification_delivery_attempts SET finished_at=$now,result='Kanal silindi; uzak sonuç geri alınamaz.' WHERE channel_id=$id AND finished_at IS NULL;
                """, ("$id", id), ("$now", Now()));
        }
    }
    private const string DeliverySelect = """
        SELECT d.id,d.channel_id,c.name,d.user_id,d.channel_revision,d.user_version,d.notification_id,d.kind,d.title,d.message,d.content_id,d.created_at,d.is_test,d.status,d.attempts,d.revision,d.lease,d.next_attempt_at,d.finished_at,d.result
        FROM notification_deliveries d JOIN notification_channels c ON c.id=d.channel_id
        """;
    private static NotificationDelivery ReadDelivery(SqliteDataReader r) => new(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetInt32(4), r.GetInt32(5), Text(r, 6), r.GetString(7), r.GetString(8), r.GetString(9), Text(r, 10), r.GetString(11), r.GetBoolean(12), r.GetString(13), r.GetInt32(14), r.GetInt32(15), Text(r, 16), r.GetString(17), Text(r, 18), r.GetString(19));
    public NotificationDelivery? GetNotificationDelivery(string id) { using var db = Open(); return Query(db, DeliverySelect + " WHERE d.id=$id", ReadDelivery, ("$id", id)).FirstOrDefault(); }
    public IReadOnlyList<NotificationDelivery> GetNotificationDeliveries(string? userId, string? channelId = null)
    {
        using var db = Open(); return Query(db, DeliverySelect + " WHERE ($user IS NULL OR d.user_id=$user) AND ($channel IS NULL OR d.channel_id=$channel) ORDER BY d.created_at DESC LIMIT 100", ReadDelivery, ("$user", userId), ("$channel", channelId));
    }
    public IReadOnlyList<NotificationDeliveryAttempt> GetNotificationAttempts(string id)
    {
        using var db = Open(); return Query(db, "SELECT number,started_at,finished_at,result FROM notification_delivery_attempts WHERE delivery_id=$id ORDER BY number", r => new NotificationDeliveryAttempt(r.GetInt32(0), r.GetString(1), Text(r, 2), r.GetString(3)), ("$id", id));
    }
    public NotificationDelivery QueueNotificationTest(string channelId, int revision, DateTimeOffset now)
    {
        lock (_writeGate)
        {
            var channel = GetNotificationConnection(channelId)?.Channel ?? throw new DomainException("Kanal bulunamadı.", 404);
            if (channel.Deleted || channel.Revision != revision) throw new DomainException("Kanal değişti; yeniden aç.", 409);
            var user = GetAccountSecret(channel.UserId) ?? throw new DomainException("Hesap bulunamadı.", 404);
            using var db = Open();
            var existing = Scalar(db, "SELECT id FROM notification_deliveries WHERE channel_id=$channel AND is_test=1 AND status IN ('queued','sending')", ("$channel", channelId)) as string;
            if (existing is not null) return GetNotificationDelivery(existing)!;
            if (Convert.ToInt32(Scalar(db, "SELECT COUNT(*) FROM notification_deliveries WHERE channel_id=$channel AND status IN ('queued','sending')", ("$channel", channelId))) >= 1000) throw new DomainException("Kanal gönderim kuyruğu dolu.", 429);
            var id = Guid.NewGuid().ToString("N"); Execute(db, """
                INSERT INTO notification_deliveries(id,channel_id,channel_revision,user_id,user_version,kind,title,message,created_at,is_test,status,next_attempt_at)
                VALUES($id,$channel,$revision,$user,$version,'test','Home · Bildirim denemesi','Bu ileti, bildirim kanalında verdiğin deneme komutuyla gönderildi.',$now,1,'queued',$now)
                """, ("$id", id), ("$channel", channelId), ("$revision", revision), ("$user", channel.UserId), ("$version", user.Version), ("$now", now.ToString("O")));
            return GetNotificationDelivery(id)!;
        }
    }
    private bool NotificationDeliveryAllowed(NotificationDelivery job)
    {
        var channel = GetNotificationConnection(job.ChannelId)?.Channel;
        var user = GetAccountSecret(job.UserId);
        if (channel is null || channel.Deleted || !channel.Enabled && !job.IsTest || channel.Revision != job.ChannelRevision || user is not { Account.Enabled: true } || user.Version != job.UserVersion) return false;
        if (channel.Kind == "push" && !BrowserPushAllowed(channel.Id)) return false;
        if (job.NotificationId is null) return job.IsTest;
        using var db = Open(); return Scalar(db, "SELECT id FROM notifications WHERE id=$id AND user_id=$user AND (content_id IS NULL OR EXISTS(SELECT 1 FROM content c WHERE c.id=notifications.content_id AND " + ContentAccess + "))",
            [("$id", job.NotificationId), ("$user", job.UserId), ..ViewerArgs(user.Account)]) is string;
    }
    public bool NotificationLeaseCurrent(NotificationDelivery job)
    {
        lock (_writeGate)
        {
            var current = GetNotificationDelivery(job.Id);
            return current is { Status: "sending" } && current.Revision == job.Revision && current.Lease == job.Lease && NotificationDeliveryAllowed(current);
        }
    }
    public NotificationDelivery? ClaimNotificationDelivery(DateTimeOffset now)
    {
        lock (_writeGate)
        {
            using var db = Open();
            for (var i = 0; i < 64; i++)
            {
                var next = Query(db, DeliverySelect + " WHERE d.status='queued' AND d.next_attempt_at<=$now AND NOT EXISTS(SELECT 1 FROM notification_deliveries busy WHERE busy.channel_id=d.channel_id AND busy.status='sending') ORDER BY d.next_attempt_at,d.created_at LIMIT 1", ReadDelivery, ("$now", now.ToString("O"))).FirstOrDefault();
                if (next is null) return null;
                if (!NotificationDeliveryAllowed(next) || now - DateTimeOffset.Parse(next.CreatedAt) > TimeSpan.FromDays(7)) { CancelNotificationDelivery(next.Id, next.Revision, "Kanal, hesap veya bildirim erişimi değişti ya da ileti eskidi."); continue; }
                var channel = GetNotificationConnection(next.ChannelId)!.Channel;
                if (channel.Kind == "push" && Convert.ToInt32(Scalar(db, "SELECT COUNT(*) FROM notification_delivery_attempts a JOIN notification_channels c ON c.id=a.channel_id WHERE c.kind='push' AND c.user_id=$user AND a.started_at>$after", ("$user", next.UserId), ("$after", now.AddHours(-1).ToString("O")))) >= 120)
                {
                    var firstPush = (string)Scalar(db, "SELECT MIN(a.started_at) FROM notification_delivery_attempts a JOIN notification_channels c ON c.id=a.channel_id WHERE c.kind='push' AND c.user_id=$user AND a.started_at>$after", ("$user", next.UserId), ("$after", now.AddHours(-1).ToString("O")))!;
                    Execute(db, "UPDATE notification_deliveries SET next_attempt_at=$next,result='Hesabın saatlik push bütçesi bekleniyor.' WHERE user_id=$user AND channel_id IN (SELECT id FROM notification_channels WHERE kind='push') AND status='queued' AND next_attempt_at<$next", ("$user", next.UserId), ("$next", DateTimeOffset.Parse(firstPush).AddHours(1).AddSeconds(1).ToString("O"))); continue;
                }
                var attempts = Convert.ToInt32(Scalar(db, "SELECT COUNT(*) FROM notification_delivery_attempts WHERE channel_id=$channel AND started_at>$after", ("$channel", next.ChannelId), ("$after", now.AddHours(-1).ToString("O"))));
                if (attempts >= channel.MaximumHourlyAttempts)
                {
                    var first = (string)Scalar(db, "SELECT MIN(started_at) FROM notification_delivery_attempts WHERE channel_id=$channel AND started_at>$after", ("$channel", next.ChannelId), ("$after", now.AddHours(-1).ToString("O")))!;
                    Execute(db, "UPDATE notification_deliveries SET next_attempt_at=$next,result='Kanalın saatlik deneme bütçesi bekleniyor.' WHERE channel_id=$channel AND status='queued' AND next_attempt_at<$next", ("$channel", next.ChannelId), ("$next", DateTimeOffset.Parse(first).AddHours(1).AddSeconds(1).ToString("O"))); continue;
                }
                var lease = Guid.NewGuid().ToString("N"); using var tx = db.BeginTransaction(); using var command = Command(db, """
                    UPDATE notification_deliveries SET status='sending',attempts=attempts+1,revision=revision+1,lease=$lease,result='Gönderiliyor.' WHERE id=$id AND status='queued';
                    INSERT INTO notification_delivery_attempts(delivery_id,channel_id,number,started_at) SELECT id,channel_id,attempts,$now FROM notification_deliveries WHERE id=$id;
                    """, ("$id", next.Id), ("$lease", lease), ("$now", now.ToString("O")));
                command.Transaction = tx; command.ExecuteNonQuery(); tx.Commit(); return GetNotificationDelivery(next.Id);
            }
            return null;
        }
    }
    public void FinishNotificationDelivery(NotificationDelivery job, NotificationSendResult result, DateTimeOffset now)
    {
        lock (_writeGate)
        {
            if (!NotificationLeaseCurrent(job)) return;
            var retry = !result.Success && result.Retryable && job.Attempts < 8;
            var delay = result.RetryAfter ?? TimeSpan.FromSeconds(Math.Min(3600, 10 * Math.Pow(2, job.Attempts - 1)));
            delay = TimeSpan.FromSeconds(Math.Clamp(delay.TotalSeconds, 5, 86400));
            using var db = Open(); using var tx = db.BeginTransaction(); using var command = Command(db, """
                UPDATE notification_deliveries SET status=$status,lease=NULL,result=$result,next_attempt_at=$next,finished_at=$finished WHERE id=$id;
                UPDATE notification_delivery_attempts SET finished_at=$now,result=$result WHERE delivery_id=$id AND number=$attempt;
                """, ("$id", job.Id), ("$status", result.Success ? "sent" : retry ? "queued" : "failed"), ("$result", result.Message), ("$next", now.Add(delay).ToString("O")), ("$finished", retry ? null : now.ToString("O")), ("$now", now.ToString("O")), ("$attempt", job.Attempts));
            command.Transaction = tx; command.ExecuteNonQuery(); tx.Commit();
        }
    }
    public void CancelNotificationDelivery(string id, int revision, string reason = "Kullanıcı gönderimi iptal etti.")
    {
        lock (_writeGate)
        {
            var job = GetNotificationDelivery(id) ?? throw new DomainException("Gönderim bulunamadı.", 404);
            if (job.Revision != revision) throw new DomainException("Gönderim değişti; yeniden aç.", 409);
            using var db = Open(); Execute(db, """
                UPDATE notification_deliveries SET status='cancelled',revision=revision+1,lease=NULL,result=$reason,finished_at=$now WHERE id=$id AND status IN ('queued','sending');
                UPDATE notification_delivery_attempts SET finished_at=$now,result=$reason WHERE delivery_id=$id AND finished_at IS NULL;
                """, ("$id", id), ("$reason", reason), ("$now", Now()));
        }
    }
    public void RetryNotificationDelivery(string id, int revision, DateTimeOffset now)
    {
        lock (_writeGate)
        {
            var job = GetNotificationDelivery(id) ?? throw new DomainException("Gönderim bulunamadı.", 404);
            if (job.Revision != revision || job.Status != "failed" || job.Attempts >= 16 || !NotificationDeliveryAllowed(job)) throw new DomainException("Gönderim değişti veya tekrar sınırına ulaştı; güncel kanalda yeni deneme yap.", 409);
            using var db = Open();
            if (Convert.ToInt32(Scalar(db, "SELECT COUNT(*) FROM notification_deliveries WHERE channel_id=$channel AND status IN ('queued','sending')", ("$channel", job.ChannelId))) >= 1000) throw new DomainException("Kanal gönderim kuyruğu dolu.", 429);
            Execute(db, "UPDATE notification_deliveries SET status='queued',revision=revision+1,next_attempt_at=$now,finished_at=NULL,result='Kullanıcı yeniden deneme istedi.' WHERE id=$id", ("$id", id), ("$now", now.ToString("O")));
        }
    }
    public void RecoverNotificationDeliveries(DateTimeOffset now)
    {
        lock (_writeGate)
        {
            using var db = Open(); Execute(db, """
                UPDATE notification_delivery_attempts SET finished_at=$now,result='Süreç kesildi; uzak teslim sonucu bilinmiyor.' WHERE finished_at IS NULL;
                UPDATE notification_deliveries SET status=CASE WHEN attempts<8 THEN 'queued' ELSE 'failed' END,revision=revision+1,lease=NULL,next_attempt_at=$next,
                  result='Kesilen gönderimin uzak sonucu bilinmiyor; aynı ileti kimliği korunur.',finished_at=CASE WHEN attempts<8 THEN NULL ELSE $now END WHERE status='sending';
                """, ("$now", now.ToString("O")), ("$next", now.AddSeconds(30).ToString("O")));
        }
    }
    public void PruneNotificationDeliveries(DateTimeOffset now)
    {
        lock (_writeGate)
        {
            using var db = Open(); Execute(db, """
                DELETE FROM notification_deliveries WHERE status IN ('sent','failed','cancelled') AND (finished_at<$before OR id IN (SELECT id FROM notification_deliveries WHERE status IN ('sent','failed','cancelled') ORDER BY finished_at DESC LIMIT -1 OFFSET 10000))
                  AND NOT EXISTS(SELECT 1 FROM notification_delivery_attempts a WHERE a.delivery_id=notification_deliveries.id AND a.started_at>$hour);
                DELETE FROM notification_channels WHERE deleted=1 AND (kind<>'push' OR created_at<$hour) AND NOT EXISTS(SELECT 1 FROM notification_deliveries d WHERE d.channel_id=notification_channels.id);
                """, ("$before", now.AddDays(-30).ToString("O")), ("$hour", now.AddHours(-1).ToString("O")));
        }
    }
    public void PublishNotification(string userId, string kind, string title, string message, string dedup)
    {
        lock (_writeGate) { using var db = Open(); using var tx = db.BeginTransaction(); AddNotification(db, tx, userId, kind, title, message, null, dedup); tx.Commit(); }
    }
}
