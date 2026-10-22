using System.Text.Json;
using HomeServer.Core;

namespace HomeServer.Infrastructure;

public sealed partial class MediaStore
{
    public PlaybackLimits GetPlaybackLimits() => GetSetting("playback.limits") is { } json ? JsonSerializer.Deserialize<PlaybackLimits>(json, WebJson)! : new();
    public PlaybackLimits SavePlaybackLimits(PlaybackLimits input)
    {
        if (input.MaximumStreams is < 1 or > 64 || input.MaximumStreamsPerDevice is < 1 or > 16) throw new DomainException("Sunucu sınırı 1–64, cihaz sınırı 1–16 oynatma olmalı.");
        lock (_writeGate)
        {
            var current = GetPlaybackLimits();
            if (current.Revision != input.Revision) throw new DomainException("Oynatma sınırları değişti; yeniden aç.", 409);
            var value = input with { Revision = current.Revision + 1 }; SetSetting("playback.limits", JsonSerializer.Serialize(value, WebJson)); return value;
        }
    }
    public UserPlaybackLimits GetUserPlaybackLimits(string userId)
    {
        using var db = Open(); return Query(db, "SELECT maximum_streams,maximum_devices,revision FROM user_playback_limits WHERE user_id=$id", r => new UserPlaybackLimits(r.GetInt32(0), r.GetInt32(1), r.GetInt32(2)), ("$id", userId)).FirstOrDefault() ?? new();
    }
    public UserPlaybackLimits SaveUserPlaybackLimits(string userId, UserPlaybackLimits input)
    {
        if (input.MaximumStreams is < 0 or > 16 || input.MaximumDevices is < 1 or > 16) throw new DomainException("Hesap sınırı 0–16 oynatma, 1–16 etkin cihaz olmalı.");
        lock (_writeGate)
        {
            _ = GetAccount(userId) ?? throw new DomainException("Hesap bulunamadı.", 404);
            var current = GetUserPlaybackLimits(userId);
            if (current.Revision != input.Revision) throw new DomainException("Hesap oynatma sınırı değişti; yeniden aç.", 409);
            using var db = Open(); Execute(db, """
                INSERT INTO user_playback_limits(user_id,maximum_streams,maximum_devices,revision) VALUES($id,$streams,$devices,$revision)
                ON CONFLICT(user_id) DO UPDATE SET maximum_streams=$streams,maximum_devices=$devices,revision=$revision
                """, ("$id", userId), ("$streams", input.MaximumStreams), ("$devices", input.MaximumDevices), ("$revision", current.Revision + 1));
            return input with { Revision = current.Revision + 1 };
        }
    }
    public PlaybackDevice? GetPlaybackDevice(string id)
    {
        using var db = Open(); return Query(db, "SELECT id,name,maximum_streams,revision,created_at,last_seen_at FROM playback_devices WHERE id=$id",
            r => new PlaybackDevice(r.GetString(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetInt32(2), r.GetInt32(3), r.GetString(4), r.GetString(5)), ("$id", id)).FirstOrDefault();
    }
    public PlaybackDevice CreatePlaybackDevice(string name)
    {
        lock (_writeGate)
        {
            using var db = Open();
            Execute(db, """
                DELETE FROM playback_devices WHERE last_seen_at<$before
                  AND NOT EXISTS(SELECT 1 FROM user_sessions s WHERE s.playback_device_id=playback_devices.id)
                  AND NOT EXISTS(SELECT 1 FROM playback_leases p WHERE p.device_id=playback_devices.id)
                """, ("$before", DateTimeOffset.UtcNow.AddDays(-30).ToString("O")));
            if (Convert.ToInt32(Scalar(db, "SELECT COUNT(*) FROM playback_devices")) >= 10000) throw new DomainException("Kayıtlı tarayıcı cihazı sınırına ulaşıldı.", 429);
            var id = Guid.NewGuid().ToString("N"); var label = new string(name.Where(c => !char.IsControl(c)).Take(160).ToArray()).Trim();
            Execute(db, "INSERT INTO playback_devices(id,name,created_at,last_seen_at) VALUES($id,$name,$now,$now)", ("$id", id), ("$name", label.Length == 0 ? "Tarayıcı" : label), ("$now", Now()));
            return GetPlaybackDevice(id)!;
        }
    }
    public IReadOnlyList<PlaybackDevice> GetPlaybackDevices(string? userId, string currentDevice)
    {
        using var db = Open(); return Query(db, """
            SELECT d.id,d.name,d.maximum_streams,d.revision,d.created_at,d.last_seen_at FROM playback_devices d
            WHERE $user IS NULL OR EXISTS(SELECT 1 FROM user_sessions s WHERE s.user_id=$user AND s.playback_device_id=d.id)
              OR EXISTS(SELECT 1 FROM playback_leases p WHERE p.user_id=$user AND p.device_id=d.id)
            ORDER BY d.last_seen_at DESC LIMIT 200
            """, r => new PlaybackDevice(r.GetString(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetInt32(2), r.GetInt32(3), r.GetString(4), r.GetString(5), r.GetString(0) == currentDevice), ("$user", userId));
    }
    public PlaybackDevice SavePlaybackDevice(string id, PlaybackDeviceInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Length > 160 || input.Name.Any(char.IsControl) || input.MaximumStreams is < 0 or > 16) throw new DomainException("Cihaz adı veya oynatma sınırı geçersiz.");
        lock (_writeGate)
        {
            var device = GetPlaybackDevice(id) ?? throw new DomainException("Cihaz bulunamadı.", 404);
            if (device.Revision != input.Revision) throw new DomainException("Cihaz sınırı değişti; yeniden aç.", 409);
            using var db = Open(); Execute(db, "UPDATE playback_devices SET name=$name,maximum_streams=$streams,revision=revision+1 WHERE id=$id", ("$id", id), ("$name", input.Name.Trim()), ("$streams", input.MaximumStreams));
            return GetPlaybackDevice(id)!;
        }
    }
    public PlaybackActor GetPlaybackActor(string userId, string? loginId)
    {
        if (loginId is null || ValidateLoginSession(loginId, userId) is null) throw new DomainException("Oturum sona erdi.", 401);
        using var db = Open(); var device = Scalar(db, "SELECT playback_device_id FROM user_sessions WHERE id=$id AND user_id=$user", ("$id", loginId), ("$user", userId)) as string;
        return device is null ? throw new DomainException("Cihaz oturumu yenilenmeli; yeniden giriş yap.", 401) : new(userId, loginId, device);
    }
}
