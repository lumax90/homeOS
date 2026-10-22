using System.Text.Json;
using HomeServer.Core;
using HomeServer.Infrastructure;
using HomeServer.Operations;

namespace HomeServer.Gaming;

public sealed record GameLibraryOptions(int Revision=0,bool AutomaticMetadata=true,bool MatchByName=true,string CoverRegion="auto",int RefreshGeneration=0);
public sealed record GameLibraryOptionsChange(int Revision,bool AutomaticMetadata,bool MatchByName,string CoverRegion);
public sealed record GameLibraryRefresh(int Revision);

public sealed class GameLibraryPreferences(HomePaths paths,MediaStore store,EventHub events)
{
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);private readonly object _gate=new();
    public GameLibraryOptions Read(string user)
    {
        if(!NativeSystems.SafeSegment(user))throw new DomainException("Oyun ayarı kapsamı geçersiz.",400);var relative=$"games/library-settings/{user}.json";
        if(!File.Exists(Path.Combine(paths.State,relative)))return new();var file=MediaPaths.ResolveFile(paths.State,relative);if(new FileInfo(file).Length>8192)throw new DomainException("Oyun kütüphanesi ayarı geçersiz.",409);
        try{var value=JsonSerializer.Deserialize<GameLibraryOptions>(File.ReadAllText(file),Json)??throw new JsonException();Validate(value);return value;}catch(JsonException){throw new DomainException("Oyun kütüphanesi ayarı geçersiz.",409);}
    }
    private static void Validate(GameLibraryOptions value){if(value.Revision<0||value.RefreshGeneration<0||value.CoverRegion is not("auto" or "us" or "eu" or "jp" or "world"))throw new DomainException("Oyun kütüphanesi ayarı geçersiz.",400);}
    public GameLibraryOptions Save(Account actor,string login,GameLibraryOptionsChange input)
    {
        lock(_gate){store.RequireRomImportActor(actor,login);var current=Read(actor.Id);if(input.Revision!=current.Revision)throw new DomainException("Oyun ayarları değişti; yeniden yükle.",409);
            var value=new GameLibraryOptions(current.Revision+1,input.AutomaticMetadata,input.MatchByName,input.CoverRegion,current.RefreshGeneration);Validate(value);var directory=AppFiles.DirectoryIn(paths.State,"games","library-settings");PackageTrust.AtomicJson(Path.Combine(directory,actor.Id+".json"),value);
            if(!value.AutomaticMetadata)foreach(var job in store.GetProductJobs(actor,"rom-metadata").Where(j=>j.Status is "queued" or "running"&&RomMetadata.IsAutomatic(j)))store.CancelProductJob(job.Id,job.Revision,actor);
            events.Publish("games.changed");return value;}
    }
    public GameLibraryOptions Refresh(Account actor,string login,int revision)
    {
        lock(_gate){store.RequireRomImportActor(actor,login);var current=Read(actor.Id);if(current.Revision!=revision)throw new DomainException("Oyun ayarları değişti; yeniden yükle.",409);if(!current.AutomaticMetadata)throw new DomainException("Önce otomatik oyun bilgilerini etkinleştir.",409);var next=current with{Revision=current.Revision+1,RefreshGeneration=current.RefreshGeneration+1};var root=AppFiles.DirectoryIn(paths.State,"games","library-settings");PackageTrust.AtomicJson(Path.Combine(root,actor.Id+".json"),next);events.Publish("games.changed");return next;}
    }
}
