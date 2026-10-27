using HomeServer.Core;
using HomeServer.Gaming;

namespace HomeServer.Infrastructure;

public sealed partial class MediaStore
{
    public bool GameRemovalPending(string id)
    {
        using var db=Open();return Convert.ToInt32(Scalar(db,"SELECT COUNT(*) FROM product_jobs WHERE kind='game-uninstall' AND scope_id=$game AND status IN ('queued','running','applying')",("$game",id)))>0;
    }
    public void RequireRomPublication(string system,string hash)
    {
        using var db=Open();
        if(Convert.ToInt32(Scalar(db,"SELECT COUNT(*) FROM product_jobs WHERE kind='game-uninstall' AND status IN ('queued','running','applying') AND json_extract(input_json,'$.system')=$system AND json_extract(input_json,'$.sha256')=$hash",("$system",system),("$hash",hash)))>0)
            throw new DomainException("Bu oyun kopyası kaldırılıyor. İşlem bitince yeniden ekle.",409);
    }
    public ProductJob QueueGameRemoval(string id,object input,Account actor)
    {
        lock(_writeGate)
        {
            RequireGameAccount(actor);
            var previous=GetProductJobs(actor,"game-uninstall",id).FirstOrDefault();
            if(previous?.Status is "queued" or "running" or "applying")return previous;
            if(previous is{Status:"completed",Result:{} result}&&(!result.TryGetProperty("reimported",out var reimported)||reimported.ValueKind!=System.Text.Json.JsonValueKind.True)
                &&GetGameStates(actor.Id).FirstOrDefault(state=>state.GameId==id) is{InLibrary:false})return previous;
            if(GetActiveGameSessions().Any(session=>session.GameId==id))throw new DomainException("Oyunu kaldırmadan önce aktif oturumdan çık.",409);
            using(var db=Open())if(Convert.ToInt32(Scalar(db,"SELECT COUNT(*) FROM product_jobs WHERE kind='game-install' AND scope_id=$game AND status IN ('queued','running','applying')",("$game",id)))>0)
                throw new DomainException("Oyun kurulumu sürerken kaldırma başlatılamaz.",409);
            return QueueProductJob("game-uninstall",id,input,actor);
        }
    }
    public bool ApplyGameRemoval(ProductJob job,GameRom? rom,Action removePayload)
    {
        lock(_writeGate)
        {
            var actor=RequireProductJob(job);GameInstaller.RequireInstallAccount(actor);
            if(rom is not null&&(rom.UserId!=actor.Id||GetGameRom(rom.Id,actor) is not{} current||current.System!=rom.System||current.Sha256!=rom.Sha256))
                throw new DomainException("Kaldırılacak oyun kimliği değişti.",409);
            var ids=rom is null?[job.ScopeId!]:AllRomReferenceIds(rom.System,rom.Sha256);
            if(GetActiveGameSessions().Any(session=>ids.Contains(session.GameId)))throw new DomainException("Oyunu kaldırmadan önce aktif oturumdan çık.",409);
            using var db=Open();using var transaction=db.BeginTransaction();
            using(var command=Command(db,"INSERT INTO game_user_state(user_id,game_id,in_library) VALUES($user,$game,0) ON CONFLICT(user_id,game_id) DO UPDATE SET in_library=0; UPDATE product_jobs SET status='applying' WHERE id=$job AND lease=$lease;",("$user",actor.Id),("$game",job.ScopeId),("$job",job.Id),("$lease",job.Lease)))
            {command.Transaction=transaction;command.ExecuteNonQuery();}
            using var references=Command(db,rom is null
                ?"SELECT COUNT(*) FROM game_user_state WHERE game_id=$game AND in_library=1"
                :"SELECT COUNT(*) FROM game_roms r LEFT JOIN game_user_state s ON s.game_id=r.id AND s.user_id=r.user_id WHERE r.system=$system AND r.sha256=$hash AND COALESCE(s.in_library,1)=1",
                ("$game",job.ScopeId),("$system",rom?.System),("$hash",rom?.Sha256));references.Transaction=transaction;
            var shared=Convert.ToInt32(references.ExecuteScalar())>0;
            if(!shared)removePayload();
            transaction.Commit();return !shared;
        }
    }
    private string[] AllRomReferenceIds(string system,string hash)
    {using var db=Open();return Query(db,"SELECT id FROM game_roms WHERE system=$system AND sha256=$hash",reader=>reader.GetString(0),("$system",system),("$hash",hash)).ToArray();}
    private bool RomPayloadPresent(GameRom rom)
    {
        var root=Path.Combine(_paths.State,"games","roms",rom.System,rom.Sha256);
        return File.Exists(Path.Combine(root,NativeSystems.Extension(rom.FileName) is "cue" or "m3u" or "gdi"?"disc.json":NativeSystems.ContentName(rom)));
    }
    public GameRom CorrectRomSystem(GameRom rom,string system,Account actor,string login)
    {
        lock(_writeGate)
        {
            RequireRomImportActor(actor,login);var current=GetGameRom(rom.Id,actor)??throw new DomainException("ROM bu profile ait değil.",404);
            if(current.System!=rom.System||current.Sha256!=rom.Sha256||GameRemovalPending(rom.Id)||GetActiveGameSessions().Any(session=>session.GameId==rom.Id))throw new DomainException("Oyun sistemi şu anda değiştirilemez.",409);
            RequireRomPublication(rom.System,rom.Sha256);RequireRomPublication(system,rom.Sha256);using var db=Open();
            if(Scalar(db,"SELECT id FROM game_roms WHERE user_id=$user AND system=$system AND sha256=$hash AND id<>$id",("$user",actor.Id),("$system",system),("$hash",rom.Sha256),("$id",rom.Id)) is string)
                throw new DomainException("Bu PSP oyunu zaten aynı profilde var; kayıtlar otomatik birleştirilmedi.",409);
            Execute(db,"UPDATE game_roms SET system=$system WHERE id=$id AND user_id=$user",("$system",system),("$id",rom.Id),("$user",actor.Id));return current with{System=system};
        }
    }
}
