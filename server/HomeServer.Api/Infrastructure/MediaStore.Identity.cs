using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HomeServer.Core;
using Microsoft.Data.Sqlite;

namespace HomeServer.Infrastructure;

public sealed partial class MediaStore
{
    public void ReconcileNetworkSecurity(NetworkPreferences preferences)
    {
        var marker = preferences.PathBase + ":" + preferences.RequireHttps;
        lock (_writeGate)
        {
            var previous = GetSetting("network.session-boundary");
            using var db = Open(); using var tx = db.BeginTransaction();
            using var command = Command(db, """
                UPDATE user_sessions SET revoked_at=$now WHERE revoked_at IS NULL AND $revoke=1;
                DELETE FROM household_devices WHERE $revoke=1;
                INSERT INTO settings(key,value) VALUES('network.session-boundary',$value) ON CONFLICT(key) DO UPDATE SET value=$value;
                """, ("$now", Now()), ("$value", marker), ("$revoke", previous is not null ? previous != marker : preferences.RequireHttps || preferences.PathBase.Length > 0));
            command.Transaction = tx; command.ExecuteNonQuery(); tx.Commit();
        }
    }
    private const string UserSelect = "SELECT id,display_name,role,enabled,all_libraries,can_discover,can_acquire,auto_approve,monthly_request_limit,created_at,password_hash,version,maximum_age,allow_unrated,pin_hash FROM users";
    public AccountSecret? GetAccountSecret(string id)
    {
        using var db = Open();
        var row = Query(db, UserSelect + " WHERE id=$id", ReadAccount, ("$id", id)).FirstOrDefault();
        return row is null ? null : WithLibraries(row);
    }
    public Account? GetAccount(string id) => GetAccountSecret(id)?.Account;
    public AccountSecret? FindAccount(string name)
    {
        using var db = Open();
        var row = Query(db, UserSelect + " WHERE name_key=$key", ReadAccount, ("$key", MediaPaths.SearchKey(name.Trim()))).FirstOrDefault();
        return row is null ? null : WithLibraries(row);
    }
    public IReadOnlyList<Account> GetAccounts()
    {
        using var db = Open();
        return Query(db, UserSelect + " ORDER BY created_at,id", ReadAccount).Select(WithLibraries).Select(row => row.Account).ToArray();
    }
    private AccountSecret WithLibraries(AccountSecret user)
    {
        using var db = Open();
        var ids = Query(db, "SELECT library_id FROM user_libraries WHERE user_id=$id", r => r.GetString(0), ("$id", user.Account.Id));
        return user with { Account = user.Account with { LibraryIds = ids.ToArray() } };
    }
    public Account SaveAccount(string? id, AccountInput input, string? passwordHash = null)
    {
        ValidateAccount(input);
        lock (_writeGate)
        {
            if (id is null && GetAccounts().Count >= 100) throw new DomainException("Hane hesabı sınırı 100.", 409);
            if (id == "1" && (input.Role != "admin" || !input.Enabled)) throw new DomainException("Sunucu sahibi etkin yönetici olarak kalmalı.");
            if (id is not null && GetAccount(id) is null) throw new DomainException("Hesap bulunamadı.", 404);
            if (id is null && passwordHash is null) throw new DomainException("Yeni hesap için parola gerekli.");
            id ??= Guid.NewGuid().ToString("N");
            using var db = Open(); using var tx = db.BeginTransaction();
            try
            {
                using var command = Command(db, """
                    INSERT INTO users(id,display_name,name_key,password_hash,role,enabled,all_libraries,can_discover,can_acquire,auto_approve,monthly_request_limit,created_at,maximum_age,allow_unrated)
                    VALUES($id,$name,$key,$hash,$role,$enabled,$all,$discover,$acquire,$approve,$quota,$now,$age,$unrated)
                    ON CONFLICT(id) DO UPDATE SET display_name=$name,name_key=$key,role=$role,enabled=$enabled,all_libraries=$all,
                      can_discover=$discover,can_acquire=$acquire,auto_approve=$approve,monthly_request_limit=$quota,maximum_age=$age,allow_unrated=$unrated,version=version+1;
                    DELETE FROM user_libraries WHERE user_id=$id;
                    UPDATE user_sessions SET revoked_at=$now WHERE user_id=$id AND revoked_at IS NULL;
                    UPDATE owner SET display_name=$name WHERE id=1 AND $id='1';
                    """, ("$id", id), ("$name", input.DisplayName.Trim()), ("$key", MediaPaths.SearchKey(input.DisplayName.Trim())), ("$hash", passwordHash ?? ""),
                    ("$role", input.Role), ("$enabled", input.Enabled), ("$all", input.AllLibraries), ("$discover", input.CanDiscover),
                    ("$acquire", input.CanAcquire), ("$approve", input.AutoApprove), ("$quota", input.MonthlyRequestLimit), ("$now", Now()), ("$age", input.MaximumAge), ("$unrated", input.AllowUnrated));
                command.Transaction = tx; command.ExecuteNonQuery();
                foreach (var library in input.LibraryIds ?? [])
                {
                    using var grant = Command(db, "INSERT INTO user_libraries(user_id,library_id) VALUES($user,$library)", ("$user", id), ("$library", library));
                    grant.Transaction = tx; grant.ExecuteNonQuery();
                }
                tx.Commit();
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 19) { throw new DomainException("Bu hesap adı zaten kullanılıyor veya kütüphane seçimi geçersiz.", 409); }
            return GetAccount(id)!;
        }
    }
    public void ValidateAccount(AccountInput input)
    {
        if (string.IsNullOrWhiteSpace(input.DisplayName) || input.DisplayName.Trim().Length > 60 || input.DisplayName.Any(char.IsControl)
            || input.Role is not ("admin" or "member" or "viewer") || input.MonthlyRequestLimit is < 0 or > 10000 || input.MaximumAge is < 0 or > 21)
            throw new DomainException("Hesap adı, rol veya kota geçersiz.");
        if (input.Role == "viewer" && (input.CanAcquire || input.AutoApprove)) throw new DomainException("İzleyiciye edinme veya otomatik istek onayı verilemez.");
        if (input.Role != "admin" && input.MaximumAge is not null && input.CanAcquire) throw new DomainException("Yaş kısıtlı profil transfer yönetim yetkisi alamaz.");
        var ids = input.LibraryIds ?? [];
        if (ids.Length > 100 || ids.Distinct().Count() != ids.Length || ids.Any(id => !GetLibraries(true).Any(library => library.Id == id)))
            throw new DomainException("Kütüphane izinleri geçersiz.");
    }
    public AccountSecret ChangePassword(string id, string newHash, int? expectedVersion = null)
    {
        lock (_writeGate)
        {
            var user = GetAccountSecret(id) ?? throw new DomainException("Hesap bulunamadı.", 404);
            if (expectedVersion is not null && expectedVersion != user.Version) throw new DomainException("Hesap arada değişti; yeniden giriş yap.", 409);
            using var db = Open(); using var tx = db.BeginTransaction();
            using var command = Command(db, """
                UPDATE users SET password_hash=$hash,version=version+1 WHERE id=$id;
                UPDATE owner SET password_hash=$hash WHERE id=1 AND $id='1';
                UPDATE user_sessions SET revoked_at=$now WHERE user_id=$id AND revoked_at IS NULL;
                """, ("$id", id), ("$hash", newHash), ("$now", Now()));
            command.Transaction = tx; command.ExecuteNonQuery(); tx.Commit();
            return GetAccountSecret(id)!;
        }
    }
    public LoginSession CreateLoginSession(string userId, int version, string device, string? householdDeviceId = null, string? playbackDeviceId = null)
    {
        lock (_writeGate)
        {
            var user = GetAccountSecret(userId);
            if (user is null || !user.Account.Enabled || user.Version != version) throw new DomainException("Hesap değişti; yeniden giriş yap.", 401);
            using var db = Open();
            if (householdDeviceId is not null && Scalar(db, """
                SELECT d.id FROM household_devices d JOIN users owner ON owner.id=d.owner_id
                WHERE d.id=$id AND d.expires_at>$now AND (d.retry_at IS NULL OR d.retry_at<=$now) AND owner.enabled=1 AND owner.role='admin' AND owner.version=d.owner_version
                  AND EXISTS(SELECT 1 FROM json_each(d.users_json) j WHERE j.key=$user AND j.value=$version)
                """, ("$id", householdDeviceId), ("$now", Now()), ("$user", userId), ("$version", version)) is not string) throw new DomainException("Cihaz profil yetkisi değişti.", 403);
            playbackDeviceId ??= CreatePlaybackDevice(device).Id;
            if (GetPlaybackDevice(playbackDeviceId) is null) throw new DomainException("Oynatma cihazı bulunamadı.", 409);
            var session = new LoginSession(Guid.NewGuid().ToString("N"), userId, device[..Math.Min(device.Length, 160)], Now(), DateTimeOffset.UtcNow.AddDays(14).ToString("O"));
            Execute(db, """
                DELETE FROM user_sessions WHERE expires_at<$now OR revoked_at IS NOT NULL;
                UPDATE user_sessions SET revoked_at=$now WHERE id IN (SELECT id FROM user_sessions WHERE user_id=$user ORDER BY created_at DESC LIMIT -1 OFFSET 19);
                INSERT INTO user_sessions(id,user_id,user_version,device,created_at,expires_at,household_device_id,playback_device_id) VALUES($id,$user,$version,$device,$now,$expires,$household,$player);
                UPDATE playback_devices SET last_seen_at=$now WHERE id=$player;
                """, ("$id", session.Id), ("$user", userId), ("$version", version), ("$device", session.Device), ("$now", session.CreatedAt), ("$expires", session.ExpiresAt), ("$household", householdDeviceId), ("$player", playbackDeviceId));
            return session;
        }
    }
    public Account? ValidateLoginSession(string sessionId, string userId)
    {
        using var db = Open();
        var valid = Scalar(db, """
            SELECT u.id FROM user_sessions s JOIN users u ON u.id=s.user_id
            WHERE s.id=$session AND s.user_id=$user AND s.revoked_at IS NULL AND s.expires_at>$now AND u.enabled=1 AND s.user_version=u.version
              AND (s.household_device_id IS NULL OR EXISTS(SELECT 1 FROM household_devices d JOIN users owner ON owner.id=d.owner_id
                WHERE d.id=s.household_device_id AND d.expires_at>$now AND owner.version=d.owner_version AND owner.enabled=1 AND owner.role='admin'
                AND EXISTS(SELECT 1 FROM json_each(d.users_json) j WHERE j.key=u.id AND j.value=u.version)))
            """, ("$session", sessionId), ("$user", userId), ("$now", Now())) as string;
        return valid is null ? null : GetAccount(valid);
    }
    public IReadOnlyList<LoginSession> GetLoginSessions(string userId, string? current)
    {
        using var db = Open();
        return Query(db, "SELECT id,user_id,device,created_at,expires_at FROM user_sessions WHERE user_id=$user AND revoked_at IS NULL AND expires_at>$now ORDER BY created_at DESC",
            r => new LoginSession(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(0) == current), ("$user", userId), ("$now", Now()));
    }
    public void RevokeLoginSession(string id, string userId)
    {
        lock (_writeGate) { using var db = Open(); Execute(db, "UPDATE user_sessions SET revoked_at=$now WHERE id=$id AND user_id=$user", ("$id", id), ("$user", userId), ("$now", Now())); }
    }
    public Invitation CreateInvitation(AccountInput input)
    {
        ValidateAccount(input with { DisplayName = "Davet" });
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var id = Guid.NewGuid().ToString("N"); var expires = DateTimeOffset.UtcNow.AddDays(2).ToString("O");
        lock (_writeGate)
        {
            using var db = Open();
            Execute(db, "DELETE FROM user_invitations WHERE expires_at<$now OR used_at IS NOT NULL", ("$now", Now()));
            if (Convert.ToInt32(Scalar(db, "SELECT COUNT(*) FROM user_invitations")) >= 100) throw new DomainException("Etkin davet sınırına ulaşıldı.", 409);
            Execute(db, "INSERT INTO user_invitations(id,token_hash,settings_json,created_at,expires_at) VALUES($id,$hash,$settings,$now,$expires)",
                ("$id", id), ("$hash", TokenHash(token)), ("$settings", JsonSerializer.Serialize(input, WebJson)), ("$now", Now()), ("$expires", expires));
        }
        return new Invitation(id, token, expires, input.Role);
    }
    public Account AcceptInvitation(string token, string name, string hash)
    {
        if (token.Length != 64) throw new DomainException("Davet geçersiz veya süresi dolmuş.", 404);
        lock (_writeGate)
        {
            using var db = Open();
            var json = Scalar(db, "SELECT settings_json FROM user_invitations WHERE token_hash=$hash AND used_at IS NULL AND expires_at>$now", ("$hash", TokenHash(token)), ("$now", Now())) as string;
            if (json is null) throw new DomainException("Davet geçersiz veya süresi dolmuş.", 404);
            var input = JsonSerializer.Deserialize<AccountInput>(json, WebJson)! with { DisplayName = name };
            ValidateAccount(input);
            if (GetAccounts().Count >= 100) throw new DomainException("Hane hesabı sınırı 100.", 409);
            var id = Guid.NewGuid().ToString("N");
            using var tx = db.BeginTransaction();
            using var consume = Command(db, "UPDATE user_invitations SET used_at=$now WHERE token_hash=$hash AND used_at IS NULL AND expires_at>$now", ("$hash", TokenHash(token)), ("$now", Now()));
            consume.Transaction = tx;
            if (consume.ExecuteNonQuery() != 1) throw new DomainException("Davet kullanılmış veya süresi dolmuş.", 409);
            try
            {
                using var insert = Command(db, """
                    INSERT INTO users(id,display_name,name_key,password_hash,role,enabled,all_libraries,can_discover,can_acquire,auto_approve,monthly_request_limit,created_at,maximum_age,allow_unrated)
                    VALUES($id,$name,$key,$password,$role,1,$all,$discover,$acquire,$approve,$quota,$now,$age,$unrated)
                    """, ("$id", id), ("$name", name.Trim()), ("$key", MediaPaths.SearchKey(name.Trim())), ("$password", hash), ("$role", input.Role),
                    ("$all", input.AllLibraries), ("$discover", input.CanDiscover), ("$acquire", input.CanAcquire), ("$approve", input.AutoApprove), ("$quota", input.MonthlyRequestLimit), ("$now", Now()), ("$age", input.MaximumAge), ("$unrated", input.AllowUnrated));
                insert.Transaction = tx; insert.ExecuteNonQuery();
                foreach (var library in input.LibraryIds ?? [])
                {
                    using var grant = Command(db, "INSERT INTO user_libraries(user_id,library_id) VALUES($user,$library)", ("$user", id), ("$library", library));
                    grant.Transaction = tx; grant.ExecuteNonQuery();
                }
                tx.Commit();
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 19) { throw new DomainException("Bu hesap adı kullanılıyor.", 409); }
            return GetAccount(id)!;
        }
    }
    private static string TokenHash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    private static AccountSecret ReadAccount(SqliteDataReader r) => new(new Account(r.GetString(0), r.GetString(1), r.GetString(2), r.GetBoolean(3),
        r.GetBoolean(4), [], r.GetBoolean(5), r.GetBoolean(6), r.GetBoolean(7), r.GetInt32(8), r.GetString(9), r.IsDBNull(12) ? null : r.GetInt32(12), r.GetBoolean(13), !r.IsDBNull(14)), r.GetString(10), r.GetInt32(11), Text(r, 14));
}
