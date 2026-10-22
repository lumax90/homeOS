using System.Text.Json;
using HomeServer.Core;
using Microsoft.Data.Sqlite;

namespace HomeServer.Infrastructure;

public sealed partial class MediaStore
{
    public PushServerSnapshot GetPushServerSnapshot()
    {
        lock (_writeGate) return new(GetSetting("push.configuration") is { } json ? JsonSerializer.Deserialize<PushServerConfiguration>(json, WebJson)! : new(), GetSetting("push.private-key"));
    }
    public PushServerConfiguration SavePushServerConfiguration(int expectedRevision, PushServerConfiguration configuration, string? protectedKey)
    {
        lock (_writeGate)
        {
            var previous = GetPushServerSnapshot().Configuration;
            if (previous.Revision != expectedRevision) throw new DomainException("Push sunucu ayarı değişti; yeniden aç.", 409);
            using var db = Open(); using var tx = db.BeginTransaction(); using var command = Command(db, """
                INSERT INTO settings(key,value) VALUES('push.configuration',$json) ON CONFLICT(key) DO UPDATE SET value=$json;
                INSERT INTO settings(key,value) VALUES('push.private-key',$secret) ON CONFLICT(key) DO UPDATE SET value=$secret;
                """, ("$json", JsonSerializer.Serialize(configuration, WebJson)), ("$secret", protectedKey ?? ""));
            command.Transaction = tx; command.ExecuteNonQuery(); tx.Commit();
            if (!configuration.Enabled || configuration.KeyRevision != previous.KeyRevision)
                foreach (var binding in GetBrowserPushBindings()) RetireBrowserPush(binding.ChannelId, "Push sunucusu kapatıldı veya imza anahtarı yenilendi.");
            return configuration;
        }
    }
    private const string PushBindingSelect = "SELECT channel_id,user_id,login_id,device_id,endpoint_hash,keys_hash,binding_id,key_revision,expires_at FROM browser_push_subscriptions";
    private static BrowserPushBinding ReadPushBinding(SqliteDataReader r) => new(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5), r.GetString(6), r.GetInt32(7), r.GetString(8));
    private IReadOnlyList<BrowserPushBinding> GetBrowserPushBindings() { using var db = Open(); return Query(db, PushBindingSelect, ReadPushBinding); }
    public BrowserPushBinding? GetBrowserPushBinding(string channelId) { using var db = Open(); return Query(db, PushBindingSelect + " WHERE channel_id=$id", ReadPushBinding, ("$id", channelId)).FirstOrDefault(); }
    private bool BrowserPushAllowed(string channelId)
    {
        var binding = GetBrowserPushBinding(channelId); if (binding is null) return false;
        var config = GetPushServerSnapshot().Configuration;
        return config.Enabled && config.KeyRevision == binding.KeyRevision && DateTimeOffset.Parse(binding.ExpiresAt) > DateTimeOffset.UtcNow
            && ValidateLoginSession(binding.LoginId, binding.UserId) is not null;
    }
    public void ReconcileBrowserPush()
    {
        lock (_writeGate)
            foreach (var binding in GetBrowserPushBindings())
                if (!BrowserPushAllowed(binding.ChannelId) || GetNotificationConnection(binding.ChannelId)?.Channel is not { Enabled: true, Deleted: false })
                    RetireBrowserPush(binding.ChannelId, "Push giriş oturumu veya abonelik süresi sona erdi.");
    }
    private BrowserPushRegistration Registration(BrowserPushBinding binding)
    {
        var channel = GetNotificationConnection(binding.ChannelId)!.Channel;
        return new(binding.ChannelId, binding.UserId, binding.LoginId, binding.BindingId, channel.Name, channel.Events, channel.Revision, binding.KeyRevision, binding.ExpiresAt);
    }
    public BrowserPushRegistration? GetCurrentBrowserPush(PlaybackActor actor)
    {
        lock (_writeGate)
        {
            _ = RequirePlaybackActor(actor); ReconcileBrowserPush();
            var current = GetBrowserPushBindings().FirstOrDefault(binding => binding.DeviceId == actor.DeviceId && binding.UserId == actor.UserId && binding.LoginId == actor.LoginId);
            return current is null ? null : Registration(current);
        }
    }
    public IReadOnlyList<BrowserPushDevice> GetBrowserPushDevices(PlaybackActor actor)
    {
        lock (_writeGate)
        {
            _ = RequirePlaybackActor(actor); ReconcileBrowserPush();
            return GetBrowserPushBindings().Where(binding => binding.UserId == actor.UserId).Select(binding =>
            {
                var channel = GetNotificationConnection(binding.ChannelId)!.Channel;
                return new BrowserPushDevice(channel.Id, channel.Name, GetPlaybackDevice(binding.DeviceId)?.Name ?? "Tarayıcı", channel.Revision, channel.Events, binding.LoginId == actor.LoginId);
            }).ToArray();
        }
    }
    public BrowserPushRegistration SaveBrowserPush(PlaybackActor actor, BrowserPushInput input, string endpointHash, string keysHash, string bindingId, string protectedSecrets, string target)
    {
        lock (_writeGate)
        {
            _ = RequirePlaybackActor(actor);
            if (input.ExpectedUserId != actor.UserId || input.ExpectedSessionId != actor.LoginId) throw new DomainException("Push sırasında hesap değişti; yeniden aç.", 409);
            var config = GetPushServerSnapshot().Configuration;
            if (!config.Enabled || config.KeyRevision != input.KeyRevision || config.PublicKey != input.ApplicationServerKey) throw new DomainException("Push imza anahtarı değişti; tarayıcı aboneliğini yenile.", 409);
            using var db = Open();
            var loginExpiry = DateTimeOffset.Parse((string)Scalar(db, "SELECT expires_at FROM user_sessions WHERE id=$id", ("$id", actor.LoginId))!);
            var expires = input.ExpirationTime is { } milliseconds ? DateTimeOffset.FromUnixTimeMilliseconds(milliseconds) : loginExpiry;
            expires = expires < loginExpiry ? expires : loginExpiry;
            if (expires <= DateTimeOffset.UtcNow) throw new DomainException("Tarayıcı push aboneliğinin süresi dolmuş.", 409);
            ReconcileBrowserPush(); var all = GetBrowserPushBindings();
            var current = all.FirstOrDefault(binding => binding.DeviceId == actor.DeviceId);
            var sameEndpoint = all.FirstOrDefault(binding => binding.EndpointHash == endpointHash);
            if (sameEndpoint is not null && sameEndpoint.DeviceId != actor.DeviceId || current is not null && (current.LoginId != actor.LoginId || current.UserId != actor.UserId))
                throw new DomainException("Bu push aboneliği başka bir etkin cihaz/giriş kaydına bağlı.", 409);
            var events = input.Events.Distinct().Order(StringComparer.Ordinal).ToArray();
            var channel = current is null ? null : GetNotificationConnection(current.ChannelId)!.Channel;
            if (current is not null && current.EndpointHash == endpointHash && current.KeysHash == keysHash && current.KeyRevision == input.KeyRevision
                && channel!.Name == input.Name.Trim() && channel.Events.SequenceEqual(events) && DateTimeOffset.Parse(current.ExpiresAt) == expires) return Registration(current);
            if (current is not null && channel!.Revision != input.ExpectedRevision || current is null && input.ExpectedRevision is not null)
                throw new DomainException("Push tercihi değişti; yeniden aç.", 409);
            if (current is null && (GetNotificationChannels(actor.UserId).Count >= 8 || GetNotificationChannels().Count >= 64)) throw new DomainException("Bildirim kanal sınırına ulaşıldı.", 429);
            if (current is null && Convert.ToInt32(Scalar(db, "SELECT COUNT(*) FROM notification_channels WHERE kind='push' AND user_id=$user AND created_at>$hour", ("$user", actor.UserId), ("$hour", DateTimeOffset.UtcNow.AddHours(-1).ToString("O")))) >= 30)
                throw new DomainException("Bu hesabın saatlik push kayıt sınırına ulaşıldı; biraz sonra yeniden dene.", 429);
            var id = current?.ChannelId ?? Guid.NewGuid().ToString("N"); using var tx = db.BeginTransaction(); using var command = Command(db, """
                INSERT INTO notification_channels(id,user_id,name,kind,enabled,events_json,maximum_hourly_attempts,revision,target,protected_secrets,created_at)
                  VALUES($id,$user,$name,'push',1,$events,60,1,$target,$secret,$now)
                  ON CONFLICT(id) DO UPDATE SET name=$name,enabled=1,events_json=$events,revision=revision+1,target=$target,protected_secrets=$secret;
                UPDATE notification_deliveries SET status='cancelled',revision=revision+1,lease=NULL,finished_at=$now,result='Cihazın push tercihi yenilendi.' WHERE channel_id=$id AND status IN ('queued','sending');
                UPDATE notification_delivery_attempts SET finished_at=$now,result='Cihazın push tercihi yenilendi.' WHERE channel_id=$id AND finished_at IS NULL;
                INSERT INTO browser_push_subscriptions(channel_id,user_id,login_id,device_id,endpoint_hash,keys_hash,binding_id,key_revision,expires_at)
                  VALUES($id,$user,$login,$device,$endpoint,$keys,$binding,$keyRevision,$expires)
                  ON CONFLICT(channel_id) DO UPDATE SET endpoint_hash=$endpoint,keys_hash=$keys,binding_id=$binding,key_revision=$keyRevision,expires_at=$expires;
                """, ("$id", id), ("$user", actor.UserId), ("$login", actor.LoginId), ("$device", actor.DeviceId), ("$name", input.Name.Trim()), ("$events", JsonSerializer.Serialize(events)),
                ("$target", target), ("$secret", protectedSecrets), ("$endpoint", endpointHash), ("$keys", keysHash), ("$binding", bindingId), ("$keyRevision", input.KeyRevision), ("$expires", expires.ToString("O")), ("$now", Now()));
            command.Transaction = tx; command.ExecuteNonQuery(); tx.Commit(); return Registration(GetBrowserPushBinding(id)!);
        }
    }
    public void RemoveBrowserPush(PlaybackActor actor, string id, int revision)
    {
        lock (_writeGate)
        {
            var viewer = RequirePlaybackActor(actor); var channel = GetNotificationConnection(id)?.Channel;
            if (channel is null || channel.Kind != "push" || channel.UserId != actor.UserId && !viewer.IsAdmin) throw new DomainException("Push cihazı bulunamadı.", 404);
            DeleteNotificationChannel(id, revision);
        }
    }
    public void RetireBrowserPush(string id, string reason, int? expectedRevision = null)
    {
        lock (_writeGate)
        {
            var channel = GetNotificationConnection(id)?.Channel;
            if (channel is null || channel.Kind != "push" || channel.Deleted || expectedRevision is not null && channel.Revision != expectedRevision) return;
            using var db = Open(); using var tx = db.BeginTransaction(); using var command = Command(db, """
                UPDATE notification_deliveries SET status='cancelled',revision=revision+1,lease=NULL,finished_at=$now,result=$reason WHERE channel_id=$id AND status IN ('queued','sending');
                UPDATE notification_delivery_attempts SET finished_at=$now,result=$reason WHERE channel_id=$id AND finished_at IS NULL;
                UPDATE notification_channels SET enabled=0,deleted=1,protected_secrets='',revision=revision+1 WHERE id=$id;
                """, ("$id", id), ("$reason", reason), ("$now", Now()));
            command.Transaction = tx; command.ExecuteNonQuery(); tx.Commit();
        }
    }
}
