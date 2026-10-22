using System.Security.Cryptography;
using System.Text.Json;
using HomeServer.Core;
using HomeServer.Watching;
using Microsoft.Data.Sqlite;

namespace HomeServer.Infrastructure;

public sealed partial class MediaStore
{
    public const int WatchPresenceMilliseconds = 15000;
    private sealed record WatchIntent(long Revision, bool Closed, string? RoomId, string? MemberId, string Hash);
    private static string WatchHash<T>(T input) => Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(input, WebJson)));
    private static int WatchSql(SqliteConnection db, SqliteTransaction? tx, string sql, params (string, object?)[] args)
    { using var command = Command(db, sql, args); command.Transaction = tx; return command.ExecuteNonQuery(); }
    private static object? WatchScalar(SqliteConnection db, SqliteTransaction? tx, string sql, params (string, object?)[] args)
    { using var command = Command(db, sql, args); command.Transaction = tx; return command.ExecuteScalar(); }
    private static WatchRoomDocument? ReadWatchRoom(SqliteConnection db, SqliteTransaction? tx, string id)
    {
        var json = WatchScalar(db, tx, "SELECT document_json FROM watch_rooms WHERE id=$id", ("$id", id)) as string;
        return json is null ? null : JsonSerializer.Deserialize<WatchRoomDocument>(json, WebJson);
    }
    private static void WriteWatchRoom(SqliteConnection db, SqliteTransaction tx, WatchRoomDocument room, DateTimeOffset now)
    {
        room.Members = room.Members.Where(member => member.Active || member.Id == room.HostMemberId)
            .Concat(room.Members.Where(member => !member.Active && member.Id != room.HostMemberId).OrderByDescending(member => member.SeenUnixMs).Take(48)).ToList();
        WatchSql(db, tx, """
            INSERT INTO watch_rooms(id,owner_id,owner_login_id,mode,status,revision,created_at,updated_at,document_json)
              VALUES($id,$owner,$login,$mode,$status,$revision,$created,$now,$json)
            ON CONFLICT(id) DO UPDATE SET status=$status,revision=$revision,updated_at=$now,document_json=$json
            """, ("$id", room.Id), ("$owner", room.OwnerId), ("$login", room.OwnerLoginId), ("$mode", room.Mode), ("$status", room.State),
            ("$revision", room.Revision), ("$created", DateTimeOffset.FromUnixTimeMilliseconds(room.CreatedUnixMs).ToString("O")), ("$now", now.ToString("O")), ("$json", JsonSerializer.Serialize(room, WebJson)));
    }
    private static WatchIntent? ReadWatchIntent(SqliteConnection db, SqliteTransaction? tx, PlaybackActor actor, string clientId)
    {
        using var command = Command(db, "SELECT revision,closed,room_id,member_id,request_hash FROM watch_room_clients WHERE login_id=$login AND client_id=$client", ("$login", actor.LoginId), ("$client", clientId)); command.Transaction = tx;
        using var reader = command.ExecuteReader(); return reader.Read() ? new(reader.GetInt64(0), reader.GetBoolean(1), Text(reader, 2), Text(reader, 3), reader.GetString(4)) : null;
    }
    private static void CheckWatchRevision(long revision) { if (revision is < 1 or > 9_007_199_254_740_991) throw new DomainException("Oda istemci revizyonu geçersiz."); }
    private bool WatchAllowed(WatchRoomDocument room, string userId)
        => userId == room.OwnerId || room.Mode == "together" && room.Invitations.Any(invite => invite.UserId == userId && !invite.Removed);
    private void RequireWatchAccess(WatchRoomDocument room, PlaybackActor actor, bool closed = false)
    {
        var viewer = RequirePlaybackActor(actor);
        if (!WatchAllowed(room, actor.UserId) || GetMedia(room.MediaId, viewer) is null) throw new DomainException("Oda bulunamadı veya erişimin yok.", 404);
        if (!closed && room.State == "closed") throw new DomainException("Oda kapatıldı.", 410);
    }
    private static bool WatchConnected(WatchMember member, long now) => member.Active && now - member.SeenUnixMs < WatchPresenceMilliseconds;
    private static double WatchPosition(WatchRoomDocument room, long now) => Math.Clamp(room.PositionSeconds + (room.State == "playing" ? Math.Max(0, now - room.AnchorUnixMs) / 1000d * room.Rate : 0), 0, room.DurationSeconds);
    private static void WatchPause(WatchRoomDocument room, string state, string message, long now, double? position = null)
    {
        room.PositionSeconds = position ?? WatchPosition(room, now); room.AnchorUnixMs = now; room.State = state;
        room.Message = message; room.Revision++; room.SyncRevision++;
    }
    private static void EndWatchPlayback(SqliteConnection db, SqliteTransaction tx, WatchMember member, DateTimeOffset now, string? preserve = null, bool all = true)
    {
        WatchSql(db, tx, """
            UPDATE playback_leases SET status='closed',ended_at=$now,end_reason='watch-room-ended'
              WHERE (id=$id OR watch_member_id=$member AND $all=1) AND ($preserve IS NULL OR id<>$preserve) AND login_id=$login AND client_id=$client AND status<>'closed';
            UPDATE playback_intents SET closed=1,updated_at=$now WHERE login_id=$login AND client_id=$client AND EXISTS
              (SELECT 1 FROM playback_leases p WHERE (p.id=$id OR p.watch_member_id=$member AND $all=1) AND p.status='closed' AND p.client_revision=playback_intents.revision);
            """, ("$id", member.PlaybackId), ("$member", member.Id), ("$preserve", preserve), ("$all", all), ("$login", member.LoginId), ("$client", member.ClientId), ("$now", now.ToString("O")));
        member.PlaybackId = null; member.Ready = false; member.Playing = false;
    }
    private static void EndWatchMember(SqliteConnection db, SqliteTransaction tx, WatchMember member, DateTimeOffset now, string? preserve = null)
    {
        EndWatchPlayback(db, tx, member, now, preserve); member.Active = false; member.State = "left";
        WatchSql(db, tx, "UPDATE watch_room_clients SET closed=1,updated_at=$now WHERE login_id=$login AND client_id=$client AND revision=$revision AND member_id=$member",
            ("$now", now.ToString("O")), ("$login", member.LoginId), ("$client", member.ClientId), ("$revision", member.ClientRevision), ("$member", member.Id));
    }
    private static void EndWatchRoom(SqliteConnection db, SqliteTransaction tx, WatchRoomDocument room, string message, DateTimeOffset now, string? preserve = null)
    {
        if (room.State == "closed") return;
        WatchPause(room, "closed", message, now.ToUnixTimeMilliseconds());
        foreach (var member in room.Members.Where(member => member.Active)) EndWatchMember(db, tx, member, now, preserve);
    }
    private void EvaluateWatchRoom(SqliteConnection db, SqliteTransaction tx, WatchRoomDocument room, DateTimeOffset now)
    {
        if (room.State == "closed") return;
        var time = now.ToUnixTimeMilliseconds(); var owner = ValidateLoginSession(room.OwnerLoginId, room.OwnerId);
        if (owner is null) { EndWatchRoom(db, tx, room, "Ev sahibinin giriş oturumu kapandı.", now); return; }
        if (time - room.CreatedUnixMs >= 86400000 || time - room.ActiveUnixMs >= 1800000)
        { EndWatchRoom(db, tx, room, "Odanın oturum süresi doldu.", now); return; }
        try
        {
            var source = PlaybackSource(room.MediaId, owner);
            if (source.Fingerprint != room.Fingerprint || source.Media.MatchRevision != room.MatchRevision) throw new DomainException("Kaynak değişti.");
        }
        catch (Exception ex) when (ex is DomainException or IOException or UnauthorizedAccessException)
        { EndWatchRoom(db, tx, room, "Odanın medya dosyası veya erişimi değişti.", now); return; }
        foreach (var member in room.Members.Where(member => member.Active))
        {
            var viewer = ValidateLoginSession(member.LoginId, member.UserId);
            if (viewer is null || !WatchAllowed(room, member.UserId) || GetMedia(room.MediaId, viewer) is null)
            { EndWatchMember(db, tx, member, now); room.Revision++; continue; }
            if (member.PlaybackId is { } id)
            {
                var lease = GetPlaybackLease(id);
                if (lease is null || lease.Status == "closed" || InvalidPlayback(lease, now) is not null)
                { EndWatchPlayback(db, tx, member, now, all: false); member.State = "preparing"; }
            }
        }
        var host = room.Members.FirstOrDefault(member => member.Id == room.HostMemberId);
        if (room.Mode == "remote" && host is not { Active: true }) { EndWatchRoom(db, tx, room, "Uzaktaki oynatıcı kapandı.", now); return; }
        var players = room.Members.Where(member => member.Role == "player" && WatchConnected(member, time)).ToArray();
        var ownerPresent = room.Mode == "remote" ? host is not null && WatchConnected(host, time) : room.Members.Any(member => member.UserId == room.OwnerId && WatchConnected(member, time));
        if ((!ownerPresent || players.Length == 0) && room.State is "playing" or "waiting")
            WatchPause(room, "paused", "Ev sahibi veya oynatıcı bağlantısı bekleniyor. Bağlanınca oynat ile devam et.", time);
        if (room.State == "playing" && WatchPosition(room, time) >= room.DurationSeconds)
            WatchPause(room, "paused", "Paylaşılan dosya sona erdi.", time, room.DurationSeconds);
        if (room.State == "playing" && players.Any(member => !member.Ready || member.SyncRevision != room.SyncRevision))
            WatchPause(room, "waiting", "Oynatıcılar aynı konuma hazırlanıyor.", time);
        if (room.State == "waiting" && ownerPresent && players.Length > 0 && players.All(member => member.Ready && member.SyncRevision == room.SyncRevision && member.PlaybackId is not null))
        {
            room.State = "playing"; room.AnchorUnixMs = time + 2000; room.Revision++;
            room.Message = "Birlikte oynatılıyor.";
        }
    }
    private WatchRoomDocument RefreshWatchRoom(string id, DateTimeOffset now)
    {
        _ = AppFiles.Id(id); using var db = Open(); var room = ReadWatchRoom(db, null, id) ?? throw new DomainException("Oda bulunamadı.", 404);
        using var tx = db.BeginTransaction(); var before = JsonSerializer.Serialize(room, WebJson); EvaluateWatchRoom(db, tx, room, now);
        if (before != JsonSerializer.Serialize(room, WebJson)) WriteWatchRoom(db, tx, room, now);
        tx.Commit(); return room;
    }
    private WatchMember RequireWatchMember(SqliteConnection db, SqliteTransaction? tx, WatchRoomDocument room, string id, long revision, PlaybackActor actor)
    {
        var member = room.Members.FirstOrDefault(member => member.Id == id && member.UserId == actor.UserId && member.LoginId == actor.LoginId && member.DeviceId == actor.DeviceId);
        if (member is null) throw new DomainException("Oda katılımı bu girişe ait değil.", 404);
        var intent = ReadWatchIntent(db, tx, actor, member.ClientId);
        if (!member.Active || member.ClientRevision != revision || intent is null || intent.Closed || intent.MemberId != member.Id || intent.Revision != revision)
            throw new DomainException("Bu oda katılımı sona erdi; odayı yeniden aç.", 410);
        return member;
    }
    private void RequireWatchPlayback(WatchPlaybackBinding binding, PlaybackActor actor, string clientId, string mediaId, DateTimeOffset now)
    {
        var room = RefreshWatchRoom(binding.RoomId, now); RequireWatchAccess(room, actor); using var db = Open();
        var member = RequireWatchMember(db, null, room, binding.MemberId, binding.ClientRevision, actor);
        if (member.Role != "player" || member.ClientId != clientId || room.MediaId != mediaId || room.Epoch != binding.Epoch)
            throw new DomainException("Oda dosyası veya oynatıcı katılımı değişti.", 409);
    }
    private bool WatchPlaybackCurrent(PlaybackLease lease)
    {
        using var db = Open(); var room = ReadWatchRoom(db, null, lease.WatchRoomId!);
        return room is not null && room.State != "closed" && room.Epoch == lease.WatchEpoch && room.MediaId == lease.MediaId
            && room.Members.Any(member => member.Id == lease.WatchMemberId && member.Active && member.Role == "player" && member.LoginId == lease.LoginId && member.ClientId == lease.ClientId && member.UserId == lease.UserId);
    }
    private WatchRoomSnapshot WatchView(WatchRoomDocument room, PlaybackActor actor, WatchMember? member, DateTimeOffset now)
    {
        var owner = actor.UserId == room.OwnerId; var time = now.ToUnixTimeMilliseconds();
        var control = member is { Active: true } && room.State != "closed" && (owner || room.Mode == "together" && room.GuestControl);
        return new(room.Id, room.Mode, room.OwnerId, GetAccount(room.OwnerId)?.DisplayName ?? "Ev sahibi", room.MediaId, room.Title, room.Kind, room.Fingerprint, room.MatchRevision,
            room.State, room.PositionSeconds, room.DurationSeconds, room.AnchorUnixMs, time, room.Rate, room.Volume, room.Muted, room.Revision, room.SyncRevision, room.Epoch,
            room.GuestControl, control, member is { Active: true } && owner && room.State != "closed", room.Message,
            room.Members.Where(member => member.Active).Select(member => new WatchRoomParticipant(member.Id, member.UserId, GetAccount(member.UserId)?.DisplayName ?? "Katılımcı",
                GetPlaybackDevice(member.DeviceId)?.Name ?? "Cihaz", member.Role, WatchConnected(member, time), member.Ready && member.SyncRevision == room.SyncRevision,
                member.State, member.PositionSeconds, member.Playing, member.UserId == room.OwnerId)).ToArray(),
            owner ? room.Invitations.Select(invite => new WatchRoomInvite(invite.UserId, GetAccount(invite.UserId)?.DisplayName ?? "Hesap", invite.Removed)).ToArray() : []);
    }
    public WatchRoomSnapshot GetWatchRoom(string id, PlaybackActor actor, DateTimeOffset now, string? memberId = null)
    {
        lock (_writeGate)
        {
            var room = RefreshWatchRoom(id, now); RequireWatchAccess(room, actor, true);
            var member = room.Members.FirstOrDefault(member => member.Id == memberId && member.LoginId == actor.LoginId && member.UserId == actor.UserId && member.Active);
            return WatchView(room, actor, member, now);
        }
    }
    public IReadOnlyList<WatchRoomSummary> GetWatchRooms(PlaybackActor actor, DateTimeOffset now)
    {
        lock (_writeGate)
        {
            var viewer = RequirePlaybackActor(actor); using var db = Open(); var documents = Query(db, "SELECT document_json FROM watch_rooms WHERE status<>'closed' ORDER BY created_at DESC", r => JsonSerializer.Deserialize<WatchRoomDocument>(r.GetString(0), WebJson)!);
            var rooms = new List<WatchRoomSummary>();
            foreach (var document in documents.Where(room => WatchAllowed(room, actor.UserId)))
            {
                var room = RefreshWatchRoom(document.Id, now); if (room.State == "closed" || GetMedia(room.MediaId, viewer) is null) continue;
                var host = room.Members.FirstOrDefault(member => member.Id == room.HostMemberId);
                rooms.Add(new(room.Id, room.Mode, room.MediaId, room.Title, room.Kind, GetAccount(room.OwnerId)?.DisplayName ?? "Ev sahibi", host is null ? "" : GetPlaybackDevice(host.DeviceId)?.Name ?? "",
                    room.State, room.Members.Count(member => member.Role == "player" && WatchConnected(member, now.ToUnixTimeMilliseconds())), room.OwnerId == actor.UserId, room.Members.Any(member => member.Active && member.UserId == actor.UserId)));
            }
            return rooms;
        }
    }
    private WatchRoomConnection? ExistingWatchCommand(PlaybackActor actor, string clientId, long revision, string hash, DateTimeOffset now)
    {
        using var db = Open(); var intent = ReadWatchIntent(db, null, actor, clientId);
        if (intent is null || intent.Revision < revision) return null;
        if (intent.Revision > revision || intent.Closed || intent.Hash != hash || intent.RoomId is null) throw new DomainException("Daha yeni oda komutu zaten uygulandı.", 409);
        var room = RefreshWatchRoom(intent.RoomId, now); RequireWatchAccess(room, actor); var member = RequireWatchMember(db, null, room, intent.MemberId!, revision, actor);
        return new(WatchView(room, actor, member, now), member.Id, member.ClientId, revision, member.Role);
    }
    private void ReplaceWatchClient(SqliteConnection db, SqliteTransaction tx, PlaybackActor actor, string clientId, DateTimeOffset now, string? preserve = null)
    {
        var intent = ReadWatchIntent(db, tx, actor, clientId);
        if (intent is { Closed: false, RoomId: { } id } && ReadWatchRoom(db, tx, id) is { } room)
        {
            var member = room.Members.FirstOrDefault(member => member.Id == intent.MemberId && member.Active);
            if (member is not null)
            {
                if (member.Id == room.HostMemberId) EndWatchRoom(db, tx, room, "Ev sahibi bu oynatıcıdaki odayı kapattı.", now, preserve);
                else { EndWatchMember(db, tx, member, now, preserve); room.Revision++; }
                WriteWatchRoom(db, tx, room, now);
            }
        }
        if (intent is not null) return;
        WatchSql(db, tx, "DELETE FROM watch_room_clients WHERE NOT EXISTS(SELECT 1 FROM user_sessions s WHERE s.id=watch_room_clients.login_id AND s.revoked_at IS NULL AND s.expires_at>$now)", ("$now", now.ToString("O")));
        if (Convert.ToInt32(WatchScalar(db, tx, "SELECT COUNT(*) FROM watch_room_clients WHERE login_id=$login", ("$login", actor.LoginId))) >= 1024 || Convert.ToInt32(WatchScalar(db, tx, "SELECT COUNT(*) FROM watch_room_clients")) >= 20000)
            throw new DomainException("Oda istemci kaydı sınırı dolu; eski girişleri kapat.", 429);
    }
    private static WatchMember AddWatchMember(WatchRoomDocument room, PlaybackActor actor, string clientId, long revision, string role, DateTimeOffset now)
    {
        var member = new WatchMember { Id = Guid.NewGuid().ToString("N"), UserId = actor.UserId, LoginId = actor.LoginId, DeviceId = actor.DeviceId, ClientId = clientId, ClientRevision = revision, Role = role, SeenUnixMs = now.ToUnixTimeMilliseconds() };
        room.Members.Add(member); room.ActiveUnixMs = now.ToUnixTimeMilliseconds(); return member;
    }
    private static void WriteWatchIntent(SqliteConnection db, SqliteTransaction tx, PlaybackActor actor, string clientId, long revision, string hash, string? roomId, string? memberId, DateTimeOffset now)
        => WatchSql(db, tx, """
            INSERT INTO watch_room_clients(login_id,client_id,revision,closed,room_id,member_id,request_hash,updated_at) VALUES($login,$client,$revision,$closed,$room,$member,$hash,$now)
              ON CONFLICT(login_id,client_id) DO UPDATE SET revision=$revision,closed=$closed,room_id=$room,member_id=$member,request_hash=$hash,updated_at=$now
            """, ("$login", actor.LoginId), ("$client", clientId), ("$revision", revision), ("$closed", memberId is null), ("$room", roomId), ("$member", memberId), ("$hash", hash), ("$now", now.ToString("O")));
    public WatchRoomConnection CreateWatchRoom(PlaybackActor actor, WatchRoomCreate input, DateTimeOffset now)
    {
        _ = AppFiles.Id(input.ClientId); CheckWatchRevision(input.ClientRevision);
        if (input.Mode is not ("together" or "remote") || !double.IsFinite(input.PositionSeconds) || !double.IsFinite(input.Rate) || input.Rate is < .5 or > 2 || !double.IsFinite(input.Volume) || input.Volume is < 0 or > 1) throw new DomainException("Oda tercihi geçersiz.");
        lock (_writeGate)
        {
            _ = RequirePlaybackActor(actor); var hash = WatchHash(input);
            if (ExistingWatchCommand(actor, input.ClientId, input.ClientRevision, hash, now) is { } existing) return existing;
            var lease = RequirePlaybackLease(input.PlaybackId, actor, now);
            if (lease.ClientId != input.ClientId || lease.Status != "active" || lease.Mode == "pcm" || lease.Session?.DurationSeconds is not > 0 or > 86400) throw new DomainException("Oda için bu cihazın hazır video/özgün ses oynatıcısını aç.", 409);
            if (input.PositionSeconds < 0 || input.PositionSeconds > lease.Session.DurationSeconds) throw new DomainException("Oda konumu dosya sınırları dışında.");
            var media = GetMedia(lease.MediaId, GetAccount(actor.UserId))!;
            var room = new WatchRoomDocument { Id = Guid.NewGuid().ToString("N"), OwnerId = actor.UserId, OwnerLoginId = actor.LoginId, Mode = input.Mode, MediaId = media.Id,
                Title = media.Title, Kind = media.Kind, Fingerprint = lease.Fingerprint, MatchRevision = lease.MatchRevision, DurationSeconds = lease.Session.DurationSeconds,
                PositionSeconds = input.PositionSeconds, Rate = input.Rate, Volume = input.Volume, Muted = input.Muted, AnchorUnixMs = now.ToUnixTimeMilliseconds(), CreatedUnixMs = now.ToUnixTimeMilliseconds(), ActiveUnixMs = now.ToUnixTimeMilliseconds() };
            using var db = Open(); using var tx = db.BeginTransaction(); ReplaceWatchClient(db, tx, actor, input.ClientId, now, lease.Id);
            if (Convert.ToInt32(WatchScalar(db, tx, "SELECT COUNT(*) FROM watch_rooms WHERE status<>'closed' AND owner_id=$user", ("$user", actor.UserId))) >= 4 || Convert.ToInt32(WatchScalar(db, tx, "SELECT COUNT(*) FROM watch_rooms WHERE status<>'closed'")) >= 32)
                throw new DomainException("Etkin oda sınırına ulaşıldı: hesap başına 4, sunucuda 32.", 429);
            var member = AddWatchMember(room, actor, input.ClientId, input.ClientRevision, "player", now); room.HostMemberId = member.Id;
            member.PlaybackId = lease.Id; member.PositionSeconds = input.PositionSeconds;
            WatchSql(db, tx, "UPDATE playback_leases SET watch_room_id=$room,watch_member_id=$member,watch_epoch=$epoch WHERE id=$id", ("$room", room.Id), ("$member", member.Id), ("$epoch", room.Epoch), ("$id", lease.Id));
            if (input.Mode == "remote" && input.Playing) room.State = "waiting";
            room.Message = input.Mode == "remote" ? "Bu oynatıcı kendi hesabının diğer cihazlarından yönetilebilir." : "Hane hesabını davet et; hazır olunca birlikte başla.";
            WriteWatchRoom(db, tx, room, now); WriteWatchIntent(db, tx, actor, input.ClientId, input.ClientRevision, hash, room.Id, member.Id, now); tx.Commit();
            return new(WatchView(room, actor, member, now), member.Id, input.ClientId, input.ClientRevision, member.Role);
        }
    }
    public WatchRoomConnection JoinWatchRoom(string id, PlaybackActor actor, WatchRoomJoin input, DateTimeOffset now)
    {
        _ = AppFiles.Id(input.ClientId); CheckWatchRevision(input.ClientRevision);
        if (input.Role is not ("player" or "controller")) throw new DomainException("Oda katılım biçimi geçersiz.");
        lock (_writeGate)
        {
            var room = RefreshWatchRoom(id, now); RequireWatchAccess(room, actor); var hash = WatchHash(new { id, input });
            if (ExistingWatchCommand(actor, input.ClientId, input.ClientRevision, hash, now) is { } existing) return existing;
            if (room.Mode == "remote" && input.Role != "controller") throw new DomainException("Bu oda uzaktaki tek oynatıcıyı yönetir.", 409);
            using var db = Open(); using var tx = db.BeginTransaction(); ReplaceWatchClient(db, tx, actor, input.ClientId, now);
            room = ReadWatchRoom(db, tx, id)!; if (room.State == "closed") throw new DomainException("Oda kapatıldı.", 410);
            if (room.Members.Count(member => member.Active) >= 16 || input.Role == "player" && room.Members.Count(member => member.Active && member.Role == "player") >= 12) throw new DomainException("Oda katılım sınırına ulaşıldı.", 429);
            var member = AddWatchMember(room, actor, input.ClientId, input.ClientRevision, input.Role, now); room.Revision++;
            if (input.Role == "player" && room.State == "playing") WatchPause(room, "waiting", "Yeni katılımcı hazırlanıyor.", now.ToUnixTimeMilliseconds());
            WriteWatchRoom(db, tx, room, now); WriteWatchIntent(db, tx, actor, input.ClientId, input.ClientRevision, hash, room.Id, member.Id, now); tx.Commit();
            return new(WatchView(room, actor, member, now), member.Id, input.ClientId, input.ClientRevision, input.Role);
        }
    }
    public void LeaveWatchClient(PlaybackActor actor, string clientId, long revision, DateTimeOffset now)
    {
        _ = AppFiles.Id(clientId); CheckWatchRevision(revision);
        lock (_writeGate)
        {
            _ = RequirePlaybackActor(actor); using var db = Open(); using var tx = db.BeginTransaction(); var current = ReadWatchIntent(db, tx, actor, clientId);
            if (current is not null && current.Revision > revision) return;
            ReplaceWatchClient(db, tx, actor, clientId, now); WriteWatchIntent(db, tx, actor, clientId, revision, "", null, null, now); tx.Commit();
        }
    }
    public WatchRoomSnapshot PulseWatchRoom(string id, string memberId, PlaybackActor actor, WatchRoomHeartbeat input, DateTimeOffset now)
    {
        CheckWatchRevision(input.Sequence);
        if (!double.IsFinite(input.PositionSeconds) || input.PositionSeconds < 0 || input.State is not ("preparing" or "buffering" or "gesture" or "ready" or "failed")) throw new DomainException("Oda oynatıcı durumu geçersiz.");
        lock (_writeGate)
        {
            var room = RefreshWatchRoom(id, now); RequireWatchAccess(room, actor);
            using var db = Open(); var member = RequireWatchMember(db, null, room, memberId, input.ClientRevision, actor);
            if (input.Sequence <= member.Sequence) return WatchView(room, actor, member, now);
            if (input.PositionSeconds > room.DurationSeconds + 5 && input.Epoch == room.Epoch) throw new DomainException("Oda konumu dosya sınırları dışında.");
            PlaybackLease? lease = null;
            if (member.Role == "player" && input.Epoch == room.Epoch && input.PlaybackId is { } playbackId)
            {
                lease = RequirePlaybackLease(playbackId, actor, now, mediaId: room.MediaId);
                if (lease.Status != "active" || lease.Mode == "pcm" || lease.ClientId != member.ClientId || lease.MediaId != room.MediaId || lease.Fingerprint != room.Fingerprint || lease.MatchRevision != room.MatchRevision || lease.WatchMemberId is not null && lease.WatchMemberId != member.Id)
                    throw new DomainException("Bu oynatma odanın dosyasına ait değil.", 409);
            }
            var reconnected = !WatchConnected(member, now.ToUnixTimeMilliseconds());
            member.Sequence = input.Sequence; member.SeenUnixMs = now.ToUnixTimeMilliseconds(); room.ActiveUnixMs = member.SeenUnixMs;
            member.PositionSeconds = input.Epoch == room.Epoch ? Math.Min(input.PositionSeconds, room.DurationSeconds) : 0;
            member.Playing = input.Playing; member.State = input.State; member.PlaybackId = lease?.Id;
            member.SyncRevision = input.SyncRevision;
            member.Ready = lease is not null && input.Epoch == room.Epoch && input.SyncRevision == room.SyncRevision && input.Ready && !reconnected;
            using var tx = db.BeginTransaction();
            if (lease is not null) WatchSql(db, tx, "UPDATE playback_leases SET watch_room_id=$room,watch_member_id=$member,watch_epoch=$epoch WHERE id=$id", ("$room", room.Id), ("$member", member.Id), ("$epoch", room.Epoch), ("$id", lease.Id));
            EvaluateWatchRoom(db, tx, room, now); WriteWatchRoom(db, tx, room, now); tx.Commit();
            return WatchView(room, actor, member, now);
        }
    }
    public WatchRoomCommandResult CommandWatchRoom(string id, PlaybackActor actor, WatchRoomCommand input, DateTimeOffset now, AnalyzedMedia? selected = null)
    {
        _ = AppFiles.Id(input.CommandId); CheckWatchRevision(input.ClientRevision);
        if (input.Action is not ("play" or "pause" or "seek" or "rate" or "volume" or "media" or "invite" or "remove" or "control" or "close")) throw new DomainException("Oda komutu geçersiz.");
        lock (_writeGate)
        {
            var room = RefreshWatchRoom(id, now); RequireWatchAccess(room, actor, true); var hash = WatchHash(input);
            var candidate = room.Members.FirstOrDefault(member => member.Id == input.MemberId && member.LoginId == actor.LoginId && member.UserId == actor.UserId && member.ClientRevision == input.ClientRevision);
            if (candidate is null) throw new DomainException("Oda katılımı bu girişe ait değil.", 404);
            using var db = Open();
            var prior = Query(db, "SELECT request_hash,login_id,client_id,revision FROM watch_room_commands WHERE room_id=$room AND command_id=$id", r => (Hash: r.GetString(0), Login: r.GetString(1), Client: r.GetString(2), Revision: r.GetInt32(3)), ("$room", id), ("$id", input.CommandId)).FirstOrDefault();
            if (prior.Hash is not null) return prior.Hash == hash && prior.Login == actor.LoginId && prior.Client == candidate.ClientId ? new(prior.Revision, true, WatchView(room, actor, candidate, now)) : throw new DomainException("Oda komut kimliği farklı içerikle tekrar kullanıldı.", 409);
            var member = RequireWatchMember(db, null, room, input.MemberId, input.ClientRevision, actor);
            if (room.State == "closed") throw new DomainException("Oda kapatıldı.", 410);
            var owner = actor.UserId == room.OwnerId;
            if (!owner && (!room.GuestControl || input.Action is not ("play" or "pause" or "seek" or "rate"))) throw new DomainException("Bu odanın kumandası ev sahibinde.", 403);
            if (input.ExpectedRevision != room.Revision) throw new DomainException("Oda arada değişti; güncel konumu görüp yeniden dene.", 409);
            var time = now.ToUnixTimeMilliseconds();
            if (input.Action == "media")
            {
                if (selected is null || selected.Media.Id != input.MediaId || selected.Probe.Duration is not > 0 or > 86400) throw new DomainException("Paylaşılacak dosyanın süresi okunamadı.", 409);
                var current = PlaybackSource(selected.Media.Id, RequirePlaybackActor(actor));
                if (current.Fingerprint != selected.Fingerprint || current.Media.MatchRevision != selected.Media.MatchRevision) throw new DomainException("Paylaşılacak dosya değişti.", 409);
                foreach (var userId in room.Invitations.Where(invite => !invite.Removed).Select(invite => invite.UserId).Concat(room.Members.Where(value => value.Active).Select(value => value.UserId)).Distinct())
                    if (GetAccount(userId) is not { Enabled: true } user || GetMedia(selected.Media.Id, user) is null) throw new DomainException("Yeni dosya odadaki tüm hesaplara açık olmalı.", 403);
            }
            Account? invitee = null;
            if (input.Action == "invite")
            {
                if (room.Mode != "together" || input.Name is not { Length: > 0 and <= 60 }) throw new DomainException("Davet için bir hane hesabı adı yaz.");
                invitee = FindAccount(input.Name)?.Account;
                if (invitee is not { Enabled: true } || invitee.Id == room.OwnerId || GetMedia(room.MediaId, invitee) is null) throw new DomainException("Hesap bulunamadı veya bu dosyaya erişemiyor.", 404);
                if (room.Invitations.All(invite => invite.UserId != invitee.Id) && room.Invitations.Count >= 32) throw new DomainException("Odanın davet sınırına ulaşıldı.", 429);
            }
            using var tx = db.BeginTransaction();
            switch (input.Action)
            {
                case "play":
                    if (!room.Members.Any(value => value.Role == "player" && WatchConnected(value, time))) throw new DomainException("Önce oynatıcı cihazını odaya bağla.", 409);
                    WatchPause(room, "waiting", "Katılımcıların hazır olması bekleniyor.", time, WatchPosition(room, time) >= room.DurationSeconds ? 0 : null); break;
                case "pause": WatchPause(room, "paused", "Oynatma duraklatıldı.", time); break;
                case "seek":
                    if (input.PositionSeconds is not { } position || !double.IsFinite(position) || position < 0 || position > room.DurationSeconds) throw new DomainException("Ortak konum dosya sınırları dışında.");
                    WatchPause(room, room.State is "playing" or "waiting" ? "waiting" : "paused", "Ortak konum değişti.", time, position); break;
                case "rate":
                    if (input.Rate is not { } rate || !double.IsFinite(rate) || rate is < .5 or > 2) throw new DomainException("Ortak hız 0,5–2 arasında olmalı.");
                    WatchPause(room, room.State is "playing" or "waiting" ? "waiting" : "paused", "Ortak oynatma hızı değişti.", time); room.Rate = rate; break;
                case "volume":
                    if (room.Mode != "remote" || input.Volume is not { } volume || !double.IsFinite(volume) || volume is < 0 or > 1 || input.Muted is null) throw new DomainException("Uzak ses seviyesi geçersiz.");
                    room.Volume = volume; room.Muted = input.Muted.Value; room.Revision++; break;
                case "media":
                    foreach (var player in room.Members.Where(value => value.Active && value.Role == "player")) { EndWatchPlayback(db, tx, player, now); player.State = "preparing"; }
                    room.MediaId = selected!.Media.Id; room.Title = selected.Media.Title; room.Kind = selected.Media.Kind; room.Fingerprint = selected.Fingerprint; room.MatchRevision = selected.Media.MatchRevision;
                    room.DurationSeconds = selected.Probe.Duration!.Value; room.Epoch++;
                    WatchPause(room, room.State is "playing" or "waiting" ? "waiting" : "paused", "Yeni dosya oynatıcılarda hazırlanıyor.", time, 0); break;
                case "invite":
                    var invitation = room.Invitations.FirstOrDefault(value => value.UserId == invitee!.Id);
                    if (invitation is null) room.Invitations.Add(new() { UserId = invitee!.Id }); else invitation.Removed = false;
                    room.Revision++; break;
                case "remove":
                    if (input.UserId == room.OwnerId || input.UserId is null) throw new DomainException("Ev sahibi odadan çıkarılamaz.");
                    var removed = room.Invitations.FirstOrDefault(value => value.UserId == input.UserId) ?? throw new DomainException("Davet bulunamadı.", 404); removed.Removed = true;
                    foreach (var participant in room.Members.Where(value => value.Active && value.UserId == removed.UserId)) EndWatchMember(db, tx, participant, now);
                    room.Revision++; break;
                case "control":
                    if (room.Mode != "together" || input.Enabled is null) throw new DomainException("Katılımcı kumanda tercihi geçersiz.");
                    room.GuestControl = input.Enabled.Value; room.Revision++; break;
                case "close": EndWatchRoom(db, tx, room, "Ev sahibi odayı kapattı.", now); break;
            }
            room.ActiveUnixMs = time; member.SeenUnixMs = time; WriteWatchRoom(db, tx, room, now);
            WatchSql(db, tx, """
                INSERT INTO watch_room_commands(room_id,command_id,user_id,login_id,client_id,request_hash,action,revision,created_at) VALUES($room,$id,$user,$login,$client,$hash,$action,$revision,$now);
                DELETE FROM watch_room_commands WHERE room_id=$room AND command_id IN (SELECT command_id FROM watch_room_commands WHERE room_id=$room ORDER BY revision DESC LIMIT -1 OFFSET 256);
                """, ("$room", id), ("$id", input.CommandId), ("$user", actor.UserId), ("$login", actor.LoginId), ("$client", member.ClientId), ("$hash", hash), ("$action", input.Action), ("$revision", room.Revision), ("$now", now.ToString("O")));
            tx.Commit(); return new(room.Revision, false, WatchView(room, actor, member, now));
        }
    }
    public IReadOnlyList<WatchRoomEvent> GetWatchRoomEvents(string id, PlaybackActor actor, DateTimeOffset now)
    {
        lock (_writeGate)
        {
            var room = RefreshWatchRoom(id, now); RequireWatchAccess(room, actor, true); using var db = Open();
            return Query(db, "SELECT c.action,u.display_name,c.revision,c.created_at FROM watch_room_commands c JOIN users u ON u.id=c.user_id WHERE room_id=$id ORDER BY c.revision DESC LIMIT 32", r => new WatchRoomEvent(r.GetString(0), r.GetString(1), r.GetInt32(2), r.GetString(3)), ("$id", id));
        }
    }
    public bool ReconcileWatchRooms(DateTimeOffset now, bool restart = false)
    {
        lock (_writeGate)
        {
            using var db = Open(); var ids = Query(db, "SELECT id FROM watch_rooms WHERE status<>'closed'", r => r.GetString(0)); var changed = false;
            foreach (var id in ids)
            {
                var before = ReadWatchRoom(db, null, id)!;
                if (restart)
                {
                    using var tx = db.BeginTransaction();
                    // Never count server downtime as watched time. Freeze at the last accepted presence report.
                    var lastSeen = before.Members.Select(member => member.SeenUnixMs).DefaultIfEmpty(before.AnchorUnixMs).Max();
                    var position = WatchPosition(before, Math.Min(now.ToUnixTimeMilliseconds(), lastSeen));
                    WatchPause(before, "paused", "Sunucu yeniden başladı. Hazır olunca oynat ile devam et.", now.ToUnixTimeMilliseconds(), position); before.Epoch++;
                    foreach (var member in before.Members.Where(member => member.Active)) { EndWatchPlayback(db, tx, member, now); member.State = "preparing"; member.SyncRevision = 0; }
                    WriteWatchRoom(db, tx, before, now); tx.Commit(); changed = true;
                }
                var after = RefreshWatchRoom(id, now); changed |= before.Revision != after.Revision;
            }
            Execute(db, """
                DELETE FROM watch_rooms WHERE status='closed' AND (updated_at<$before OR id IN (SELECT id FROM watch_rooms WHERE status='closed' ORDER BY updated_at DESC LIMIT -1 OFFSET 500));
                DELETE FROM watch_room_clients WHERE NOT EXISTS(SELECT 1 FROM user_sessions s WHERE s.id=watch_room_clients.login_id AND s.revoked_at IS NULL AND s.expires_at>$now)
                """, ("$before", now.AddDays(-7).ToString("O")), ("$now", now.ToString("O")));
            return changed;
        }
    }
}
