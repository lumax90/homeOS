using System.Security.Cryptography;
using System.Text.Json;
using HomeServer.Core;

namespace HomeServer.Infrastructure;

public sealed partial class MediaStore
{
    public AccountSecret SetAccountPin(string id, string? hash, int expectedVersion)
    {
        lock (_writeGate)
        {
            var user = GetAccountSecret(id) ?? throw new DomainException("Hesap bulunamadı.", 404);
            if (user.Version != expectedVersion) throw new DomainException("Hesap değişti; yeniden giriş yap.", 409);
            using var db = Open(); using var tx = db.BeginTransaction();
            using var command = Command(db, "UPDATE users SET pin_hash=$hash,version=version+1 WHERE id=$id; UPDATE user_sessions SET revoked_at=$now WHERE user_id=$id AND revoked_at IS NULL;",
                ("$id", id), ("$hash", hash), ("$now", Now())); command.Transaction = tx; command.ExecuteNonQuery(); tx.Commit();
            return GetAccountSecret(id)!;
        }
    }
    public (HouseholdDevice Device, string Token) CreateHouseholdDevice(Account owner, string[] users)
    {
        if (!owner.IsAdmin || users is null || users.Length is < 1 or > 100 || users.Distinct().Count() != users.Length) throw new DomainException("Güvenilen cihaz profilleri geçersiz.");
        lock (_writeGate)
        {
            var actor = GetAccountSecret(owner.Id)!;
            if (!actor.Account.Enabled || !actor.Account.IsAdmin) throw new DomainException("Cihaz yönetim yetkisi değişti.", 403);
            var grants = new Dictionary<string, int>();
            foreach (var id in users)
            {
                var user = GetAccountSecret(id);
                if (user is null || !user.Account.Enabled || user.PinHash is null) throw new DomainException("Seçilen tüm etkin hesaplarda önce PIN oluştur.");
                grants[id] = user.Version;
            }
            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            var result = new HouseholdDevice(Guid.NewGuid().ToString("N"), owner.Id, actor.Version, grants, DateTimeOffset.UtcNow.AddDays(30).ToString("O"), 0, null);
            using var db = Open();
            Execute(db, "DELETE FROM household_devices WHERE expires_at<$now OR NOT EXISTS(SELECT 1 FROM users u WHERE u.id=household_devices.owner_id AND u.version=household_devices.owner_version AND u.enabled=1 AND u.role='admin')", ("$now", Now()));
            if (Convert.ToInt32(Scalar(db, "SELECT COUNT(*) FROM household_devices WHERE owner_id=$user", ("$user", owner.Id))) >= 20) throw new DomainException("Güvenilen cihaz sınırı 20.", 409);
            Execute(db, "INSERT INTO household_devices(id,token_hash,owner_id,owner_version,users_json,expires_at) VALUES($id,$hash,$owner,$version,$users,$expires)",
                ("$id", result.Id), ("$hash", TokenHash(token)), ("$owner", owner.Id), ("$version", actor.Version), ("$users", JsonSerializer.Serialize(grants)), ("$expires", result.ExpiresAt));
            return (result, token);
        }
    }
    public HouseholdDevice? GetHouseholdDevice(string? token)
    {
        if (token is null || token.Length != 64 || token.Any(c => !char.IsAsciiHexDigit(c))) return null;
        using var db = Open();
        return Query(db, """
            SELECT d.id,d.owner_id,d.owner_version,d.users_json,d.expires_at,d.failures,d.retry_at FROM household_devices d JOIN users u ON u.id=d.owner_id
            WHERE d.token_hash=$hash AND d.expires_at>$now AND u.enabled=1 AND u.role='admin' AND u.version=d.owner_version
            """, r => new HouseholdDevice(r.GetString(0), r.GetString(1), r.GetInt32(2), JsonSerializer.Deserialize<Dictionary<string, int>>(r.GetString(3))!, r.GetString(4), r.GetInt32(5), Text(r, 6)), ("$hash", TokenHash(token)), ("$now", Now())).FirstOrDefault();
    }
    public void RecordPinAttempt(string deviceId, bool success)
    {
        lock (_writeGate)
        {
            using var db = Open();
            if (Scalar(db, "SELECT id FROM household_devices WHERE id=$id AND retry_at>$now", ("$id", deviceId), ("$now", Now())) is string) throw new DomainException("PIN denemelerine kısa süre ara ver. 15 dakika sonra yeniden deneyebilirsin.", 429);
            Execute(db, """
                UPDATE household_devices SET failures=CASE WHEN $success=1 THEN 0 WHEN retry_at IS NOT NULL THEN 1 ELSE failures+1 END,
                  retry_at=CASE WHEN $success=1 THEN NULL WHEN retry_at IS NULL AND failures>=4 THEN $retry ELSE NULL END WHERE id=$id
                """, ("$id", deviceId), ("$success", success), ("$retry", DateTimeOffset.UtcNow.AddMinutes(15).ToString("O")));
        }
    }
    public void DeleteHouseholdDevice(string id) { lock (_writeGate) { using var db = Open(); Execute(db, "DELETE FROM household_devices WHERE id=$id", ("$id", id)); } }
}
