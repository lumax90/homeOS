using System.Security.Cryptography;
using System.Text;
using HomeServer.Core;
using HomeServer.Gaming;
using Microsoft.Data.Sqlite;

namespace HomeServer.Infrastructure;

public sealed partial class MediaStore
{
    private const string RomSelect = "SELECT id,user_id,system,file_name,title,sha256,size_bytes,imported_at FROM game_roms";
    private static GameRom ReadRom(SqliteDataReader row) => new(row.GetString(0), row.GetString(1), row.GetString(2), row.GetString(3), row.GetString(4), row.GetString(5), row.GetInt64(6), row.GetString(7));
    public IReadOnlyList<GameRom> GetGameRoms(Account actor)
    {
        var current = GetAccount(actor.Id);
        if (current is null || !GameCatalog.Allowed(current)) return [];
        using var db = Open();
        // Ownership does not expand just because another household profile is an admin.
        return Query(db, RomSelect + " WHERE user_id=$user ORDER BY imported_at DESC LIMIT 1000", ReadRom, ("$user", actor.Id));
    }
    public GameRom? GetGameRom(string id, Account actor) => GetGameRoms(actor).FirstOrDefault(rom => rom.Id == id);
    public IReadOnlyList<GameRom> GameRomsForMetadata(Account actor,string after,int limit)
    {
        var current=GetAccount(actor.Id);if(current is null||!GameCatalog.Allowed(current))return [];using var db=Open();
        return Query(db,RomSelect+" WHERE user_id=$user AND id>$after AND EXISTS (SELECT 1 FROM game_user_state s WHERE s.user_id=game_roms.user_id AND s.game_id=game_roms.id AND s.in_library=1) ORDER BY id LIMIT $limit",ReadRom,("$user",actor.Id),("$after",after),("$limit",Math.Clamp(limit,1,64)));
    }
    public Account RequireRomImportActor(Account actor, string loginId)
    {
        var current = ValidateLoginSession(loginId, actor.Id) ?? throw new DomainException("Oturum gerekli.", 401);
        GameInstaller.RequireInstallAccount(current);
        return current;
    }
    public GameRom RegisterGameRom(string fileName,string hash,long size,Account actor,string loginId,string system="gba")
    {
        NativeSystems.ValidateName(system,fileName);
        if(hash.Length!=64||hash.Any(c=>!char.IsAsciiHexDigit(c))||size<(system=="gba"?192:16)||size>NativeSystems.Require(system).MaximumBytes)
            throw new DomainException("ROM dosyası sınırları geçersiz.");
        hash = hash.ToLowerInvariant();
        lock (_writeGate)
        {
            RequireRomImportActor(actor, loginId);
            using var db = Open();
            var existing=Query(db,RomSelect+" WHERE user_id=$user AND system=$system AND sha256=$hash",ReadRom,("$user",actor.Id),("$system",system),("$hash",hash)).FirstOrDefault();
            if (existing is not null) return existing;
            if (Convert.ToInt32(Scalar(db, "SELECT COUNT(*) FROM game_roms WHERE user_id=$user", ("$user", actor.Id))) >= 1000)
                throw new DomainException("Bu profilde en fazla 1000 ROM tutulabilir.", 409);
            var id="rom-"+Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(actor.Id+"\0"+system+"\0"+hash)))[..48];
            var rom=new GameRom(id,actor.Id,system,fileName,Path.GetFileNameWithoutExtension(fileName),hash,size,Now());
            using var transaction = db.BeginTransaction();
            using var command = Command(db, """
                INSERT INTO game_roms(id,user_id,system,file_name,title,sha256,size_bytes,imported_at)
                VALUES($id,$user,$system,$name,$title,$hash,$size,$now);
                INSERT INTO game_user_state(user_id,game_id,in_library) VALUES($user,$id,1)
                  ON CONFLICT(user_id,game_id) DO UPDATE SET in_library=1;
                """,("$id",rom.Id),("$user",actor.Id),("$system",system),("$name",rom.FileName),("$title",rom.Title),("$hash",hash),("$size",size),("$now",rom.ImportedAt));
            command.Transaction = transaction; command.ExecuteNonQuery(); transaction.Commit();
            return rom;
        }
    }
}
