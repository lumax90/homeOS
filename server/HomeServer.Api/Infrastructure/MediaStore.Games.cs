using System.Globalization;
using System.Text;
using System.Text.Json;
using HomeServer.Core;
using HomeServer.Gaming;
using Microsoft.Data.Sqlite;

namespace HomeServer.Infrastructure;

public sealed partial class MediaStore
{
    private const string GameStateSelect = "SELECT game_id,in_library,favorite,last_played_at,played_seconds,save_version,save_revision,save_json<>'{}' FROM game_user_state";
    private const string GameSessionSelect = "SELECT g.id,g.user_id,g.login_id,g.game_id,g.game_version,g.last_seen_at,g.expires_at,g.running,g.closed_at FROM game_sessions g";
    private const string GameSessionValid = "g.closed_at IS NULL AND g.expires_at>$now AND u.enabled=1 AND u.version=g.user_version AND s.revoked_at IS NULL AND s.expires_at>$now AND s.user_version=u.version";
    private static GameUserState ReadGameState(SqliteDataReader r) => new(r.GetString(0), r.GetBoolean(1), r.GetBoolean(2), Text(r, 3), r.GetInt64(4), Text(r, 5), r.GetInt32(6), r.GetBoolean(7));
    private static GameSession ReadGameSession(SqliteDataReader r) => new(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5), r.GetString(6), r.GetBoolean(7), Text(r, 8));
    public IReadOnlyList<GameUserState> GetGameStates(string userId)
    {using var db=Open();return Query(db,GameStateSelect+" WHERE user_id=$user ORDER BY last_played_at DESC LIMIT 2048",ReadGameState,("$user",userId));}
    public GameUserState SetGamePreference(string gameId, GamePreference input, Account actor)
    {
        lock (_writeGate)
        {
            RequireGameAccount(actor);
            if(GameRemovalPending(gameId))throw new DomainException("Oyun kaldırma işlemi sürüyor.",409);
            if(input.InLibrary&&gameId.StartsWith("rom-",StringComparison.Ordinal)&&GetGameRom(gameId,actor) is{} rom)
            {RequireRomPublication(rom.System,rom.Sha256);if(!RomPayloadPresent(rom))throw new DomainException("Oyun kopyası kaldırılmış. ROM'u yeniden ekle; kayıtların korunuyor.",409);}
            using var db = Open();
            Execute(db, """
                INSERT INTO game_user_state(user_id,game_id,in_library,favorite) VALUES($user,$game,$library,$favorite)
                ON CONFLICT(user_id,game_id) DO UPDATE SET in_library=$library,favorite=$favorite;
                """, ("$user", actor.Id), ("$game", gameId), ("$library", input.InLibrary), ("$favorite", input.Favorite));
            return GetGameStates(actor.Id).Single(state => state.GameId == gameId);
        }
    }
    public GameCheckpoint GetGameCheckpoint(string gameId, string version, string userId)
    {
        using var db = Open();
        return Query(db, "SELECT revision,files_json,game_version FROM game_saves WHERE user_id=$user AND game_id=$game AND game_version=$build",
            r => new GameCheckpoint(r.GetInt32(0), JsonSerializer.Deserialize<Dictionary<string, string>>(r.GetString(1))!, r.GetString(2)), ("$user", userId), ("$game", gameId), ("$build", version)).FirstOrDefault() ?? new(0, [], null);
    }
    public GameSession StartGameSession(GameManifest game, GameStart input, Account actor, string loginId)
    {
        AppFiles.Id(input.ClientId);
        if (input.Ticket is not { Length: 64 } || input.Ticket.Any(c => !char.IsAsciiHexDigit(c))) throw new DomainException("Oyun oturumu bileti geçersiz.");
        lock (_writeGate)
        {
            RequireContainerWorkAllowed();
            if(GameRemovalPending(game.Id))throw new DomainException("Oyun kaldırma işlemi sürüyor.",409);
            var account = RequireGameAccount(actor);
            if (ValidateLoginSession(loginId, actor.Id) is null) throw new DomainException("Oturum gerekli.", 401);
            using var db = Open();
            if (Scalar(db, "SELECT id FROM game_sessions WHERE id=$id", ("$id", input.ClientId)) is string)
            {
                var existing = GetGameRuntimeSession(input.ClientId, input.Ticket);
                if (existing is not null && existing.UserId == actor.Id && existing.LoginId == loginId && existing.GameId == game.Id && existing.GameVersion == game.Version) return existing;
                throw new DomainException("Oyun oturumu değişti; yeniden başlat.", 409);
            }
            var now = Now(); var expires = DateTimeOffset.UtcNow.AddMinutes(3).ToString("O");
            using var tx = db.BeginTransaction();
            using var command = Command(db, """
                DELETE FROM game_sessions WHERE expires_at<$old OR closed_at IS NOT NULL;
                UPDATE game_sessions SET closed_at=$now,running=0 WHERE user_id=$user AND closed_at IS NULL;
                INSERT INTO game_user_state(user_id,game_id,in_library,last_played_at) VALUES($user,$game,1,$now)
                  ON CONFLICT(user_id,game_id) DO UPDATE SET in_library=1,last_played_at=$now;
                INSERT OR IGNORE INTO game_saves(user_id,game_id,game_version) VALUES($user,$game,$build);
                INSERT INTO game_sessions(id,ticket_hash,user_id,login_id,user_version,game_id,game_version,created_at,last_seen_at,expires_at)
                  VALUES($id,$hash,$user,$login,$version,$game,$build,$now,$now,$expires);
                """, ("$old", DateTimeOffset.UtcNow.AddDays(-1).ToString("O")), ("$now", now), ("$user", actor.Id), ("$game", game.Id), ("$id", input.ClientId),
                ("$hash", TokenHash(input.Ticket)), ("$login", loginId), ("$version", account.Version), ("$build", game.Version), ("$expires", expires));
            command.Transaction = tx; command.ExecuteNonQuery(); tx.Commit();
            return GetGameRuntimeSession(input.ClientId, input.Ticket)!;
        }
    }
    public GameSession? GetGameRuntimeSession(string id, string ticket)
    {
        if (!Guid.TryParseExact(id, "N", out _) || ticket.Length != 64 || ticket.Any(c => !char.IsAsciiHexDigit(c))) return null;
        using var db = Open();
        return Query(db, GameSessionSelect + " JOIN users u ON u.id=g.user_id JOIN user_sessions s ON s.id=g.login_id WHERE g.id=$id AND g.ticket_hash=$hash AND " + GameSessionValid,
            ReadGameSession, ("$id", id), ("$hash", TokenHash(ticket)), ("$now", Now())).FirstOrDefault();
    }
    public GameSession RequireGameSession(string id, Account actor, string loginId)
    {
        using var db = Open();
        return Query(db, GameSessionSelect + " JOIN users u ON u.id=g.user_id JOIN user_sessions s ON s.id=g.login_id WHERE g.id=$id AND g.user_id=$user AND g.login_id=$login AND " + GameSessionValid,
            ReadGameSession, ("$id", id), ("$user", actor.Id), ("$login", loginId), ("$now", Now())).FirstOrDefault()
            ?? throw new DomainException("Oyun oturumu sona erdi veya başka bir cihazda açıldı.", 409);
    }
    public IReadOnlyList<GameSession> GetActiveGameSessions(Account? actor = null)
    {
        using var db = Open();
        return Query(db, GameSessionSelect + " JOIN users u ON u.id=g.user_id JOIN user_sessions s ON s.id=g.login_id WHERE " + GameSessionValid + " AND ($all=1 OR g.user_id=$user)", ReadGameSession,
            ("$all", actor is null || actor.IsAdmin), ("$user", actor?.Id ?? ""), ("$now", Now()));
    }
    public GameCheckpoint PulseGameSession(string id, GameManifest game, GamePulse input, Account actor, string loginId)
    {
        string? payload = null;
        if (input.Files is not null)
        {
            if (input.SaveRevision is null || input.SaveRevision < 0 || input.Files.Count > game.SavePaths.Length || input.Files.Keys.Any(path => !game.SavePaths.Contains(path, StringComparer.Ordinal)))
                throw new DomainException("Oyun kayıt dosyaları geçersiz.");
            foreach (var content in input.Files.Values)
            {
                if (content is null || Encoding.UTF8.GetByteCount(content) > 32768) throw new DomainException("Oyun kayıt boyutu sınırı aşıldı.", 413);
                try { using var document = JsonDocument.Parse(content, new JsonDocumentOptions { MaxDepth = 24 }); if (document.RootElement.ValueKind != JsonValueKind.Object) throw new DomainException("Oyun kayıt biçimi geçersiz."); }
                catch (JsonException) { throw new DomainException("Oyun kayıt biçimi geçersiz."); }
            }
            payload = JsonSerializer.Serialize(input.Files);
            if (Encoding.UTF8.GetByteCount(payload) > 48 * 1024) throw new DomainException("Oyun kayıt boyutu sınırı aşıldı.", 413);
        }
        lock (_writeGate)
        {
            RequireGameAccount(actor);
            var session = RequireGameSession(id, actor, loginId);
            if (session.GameId != game.Id || session.GameVersion != game.Version) throw new DomainException("Oyun paketi değişti; yeniden aç.", 409);
            using var db = Open(); using var tx = db.BeginTransaction();
            var seconds = input.Running && session.Running ? Math.Clamp((long)(DateTimeOffset.UtcNow - DateTimeOffset.Parse(session.LastSeenAt, CultureInfo.InvariantCulture)).TotalSeconds, 0, 35) : 0;
            if (payload is not null)
            {
                var saved = Query(db, "SELECT revision,files_json FROM game_saves WHERE user_id=$user AND game_id=$game AND game_version=$build", r => (Revision: r.GetInt32(0), Json: r.GetString(1)), ("$user", actor.Id), ("$game", game.Id), ("$build", game.Version)).Single();
                if (saved.Json != payload)
                {
                    if (saved.Revision != input.SaveRevision) throw new DomainException("Oyun kaydı değişti; eski kayıt üzerine yazılmadı.", 409);
                    using var save = Command(db, "UPDATE game_saves SET files_json=$data,revision=revision+1 WHERE user_id=$user AND game_id=$game AND game_version=$build; UPDATE game_user_state SET save_json=$data,save_version=$build,save_revision=$revision WHERE user_id=$user AND game_id=$game", ("$data", payload), ("$build", game.Version), ("$user", actor.Id), ("$game", game.Id), ("$revision", saved.Revision + 1));
                    save.Transaction = tx; save.ExecuteNonQuery();
                }
            }
            using var pulse = Command(db, "UPDATE game_sessions SET last_seen_at=$now,expires_at=$expires,running=$running WHERE id=$id; UPDATE game_user_state SET played_seconds=played_seconds+$seconds WHERE user_id=$user AND game_id=$game;",
                ("$now", Now()), ("$expires", DateTimeOffset.UtcNow.AddMinutes(3).ToString("O")), ("$running", input.Running), ("$id", id), ("$seconds", seconds), ("$user", actor.Id), ("$game", game.Id));
            pulse.Transaction = tx; pulse.ExecuteNonQuery(); tx.Commit();
            return GetGameCheckpoint(game.Id, game.Version, actor.Id);
        }
    }
    public void CloseGameSession(string id, Account actor, string loginId)
    {
        lock (_writeGate)
        {
            using var db = Open();
            Execute(db, "UPDATE game_sessions SET closed_at=$now,running=0 WHERE id=$id AND user_id=$user AND login_id=$login", ("$id", id), ("$user", actor.Id), ("$login", loginId), ("$now", Now()));
        }
    }
    public GameSession? GetClosedNativeGameSession(string id,Account actor,string loginId)
    {
        using var db=Open();return Query(db,GameSessionSelect+" WHERE g.id=$id AND g.user_id=$user AND g.login_id=$login AND g.closed_at IS NOT NULL AND g.game_version LIKE 'native-%'",ReadGameSession,("$id",id),("$user",actor.Id),("$login",loginId)).FirstOrDefault();
    }
    public bool RecoverNativeCheckpoint(string user,string game,string version,int revision,string manifest)
    {
        if(version.Length>100||!version.StartsWith("native-",StringComparison.Ordinal)||revision<0||Encoding.UTF8.GetByteCount(manifest)>32768)throw new DomainException("Yerel kayıt kurtarma tanımı geçersiz.",409);
        using var data=JsonDocument.Parse(manifest);if(data.RootElement.GetProperty("gameId").GetString()!=game||data.RootElement.GetProperty("build").GetString()!=version)throw new DomainException("Yerel kayıt kurtarma tanımı geçersiz.",409);
        lock(_writeGate)
        {
            var actor=GetAccount(user);if(actor is null||!actor.Enabled||GetGameRom(game,actor) is null)return false;using var db=Open();using var tx=db.BeginTransaction();
            var current=Query(db,"SELECT revision,files_json FROM game_saves WHERE user_id=$user AND game_id=$game AND game_version=$version",r=>(Revision:r.GetInt32(0),Files:r.GetString(1)),("$user",user),("$game",game),("$version",version)).FirstOrDefault();
            var payload=JsonSerializer.Serialize(new Dictionary<string,string>{{"native-save.json",manifest}});if(current.Files==payload)return true;if(current.Revision!=revision)return false;
            using var command=Command(db,"INSERT INTO game_saves(user_id,game_id,game_version,revision,files_json) VALUES($user,$game,$version,$next,$files) ON CONFLICT(user_id,game_id,game_version) DO UPDATE SET revision=$next,files_json=$files; UPDATE game_user_state SET save_version=$version,save_revision=$next,save_json=$files WHERE user_id=$user AND game_id=$game;",("$user",user),("$game",game),("$version",version),("$next",revision+1),("$files",payload));command.Transaction=tx;command.ExecuteNonQuery();tx.Commit();return true;
        }
    }
    public GameCheckpoint EndGameSession(string id, GameManifest game, GamePulse input, Account actor, string loginId)
    {
        lock (_writeGate)
        {
            RequireGameSession(id, actor, loginId);
            // Keep a confirmed save on CAS failure, but still release this authenticated session on page exit.
            try { return PulseGameSession(id, game, input with { Running = false }, actor, loginId); }
            finally { CloseGameSession(id, actor, loginId); }
        }
    }
    private AccountSecret RequireGameAccount(Account actor)
    {
        var current = GetAccountSecret(actor.Id);
        if (current is null || !GameCatalog.Allowed(current.Account)) throw new DomainException("Oyun bu profil için kullanılamıyor.", 403);
        return current;
    }
}
