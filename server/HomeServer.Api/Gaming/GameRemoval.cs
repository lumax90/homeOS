using System.Text.Json;
using HomeServer.Core;
using HomeServer.Infrastructure;

namespace HomeServer.Gaming;

public sealed record GameUninstallCommand(bool Confirmed=false,bool KeepSaves=true);
public sealed record GameRemovalFile(string Name,long Bytes,string Sha256);
public sealed record GameRemovalPlan(string Kind,string? System,string? Sha256,string? RelativeRoot,GameRemovalFile[] Files);

/// <summary>Durable, confirmed removal of managed payloads only. Game saves/metadata and source libraries are untouched.</summary>
public sealed class GameRemoval(HomePaths paths,MediaStore store,GameCatalog catalog,NativeRomLibrary roms,NativeGameManager native,EventHub events):IProductWorkHandler
{
    public string[] Kinds=>["game-uninstall"];
    public ProductJob Queue(string id,GameUninstallCommand command,Account actor)
    {
        GameInstaller.RequireInstallAccount(actor);
        if(!command.Confirmed)throw new DomainException("Oyunu kaldırmak için açık onay gerekli.",400);
        if(!command.KeepSaves)throw new DomainException("Bu kaldırma işlemi kayıtları korur. Kayıt silmek ayrı bir onaylı işlem olmalıdır.",400);
        _ = catalog.RequireEntry(id,store,actor);GameRemovalPlan plan;
        if(store.GetGameRom(id,actor) is{} rom)
        {
            var root=$"games/roms/{rom.System}/{rom.Sha256}";
            var files=roms.RemovalFiles(rom);
            plan=new("native",rom.System,rom.Sha256,root,files);
        }
        else plan=catalog.RemovalPlan(id);
        return store.QueueGameRemoval(id,plan,actor);
    }
    public async Task RunAsync(ProductJob job,CancellationToken token)
    {
        var actor=store.RequireProductJob(job);GameInstaller.RequireInstallAccount(actor);
        var plan=job.Input.Deserialize<GameRemovalPlan>(new JsonSerializerOptions(JsonSerializerDefaults.Web))??throw new DomainException("Oyun kaldırma tanımı geçersiz.",409);
        if(plan.Kind is not("native" or "web")||plan.Files.Length>2049)throw new DomainException("Oyun kaldırma tanımı geçersiz.",409);
        GameRom? rom=plan.Kind=="native"?store.GetGameRom(job.ScopeId??"",actor)??throw new DomainException("ROM bulunamadı.",404):null;
        if(rom is not null&&(rom.System!=plan.System||rom.Sha256!=plan.Sha256||plan.RelativeRoot!=$"games/roms/{rom.System}/{rom.Sha256}"))throw new DomainException("Kaldırılacak ROM kimliği değişti.",409);
        using var budget=CancellationTokenSource.CreateLinkedTokenSource(token);budget.CancelAfter(TimeSpan.FromMinutes(5));token=budget.Token;
        await roms.WithContentLockAsync(async cancellation=>
        {
            await native.ConfigureIdleRuntimeAsync(async _=>
            {
                var files=new List<string>();
                var trash=Path.Combine(paths.State,"games","removal-trash",job.Id);
                if(plan.RelativeRoot is{} relative)
                {
                    if(relative.Contains('\\')||relative.Split('/').Any(part=>!NativeSystems.SafeSegment(part)))throw new DomainException("Oyun kaldırma dizin sınırı geçersiz.",409);
                    if(plan.Kind=="web"&&!relative.StartsWith("games/library/"+job.ScopeId+"/",StringComparison.Ordinal))throw new DomainException("Oyun paketi kaldırma sınırı geçersiz.",409);
                    foreach(var item in plan.Files)
                    {
                        if(item.Bytes<0||item.Sha256.Length!=64||!item.Sha256.All(char.IsAsciiHexDigit)||item.Name.Contains('\\')||item.Name.Split('/').Any(part=>!NativeSystems.SafeSegment(part)))throw new DomainException("Oyun kaldırma dosya sınırı geçersiz.",409);
                        var useTrash=plan.Kind=="web"&&Directory.Exists(trash);
                        var basePath=useTrash?$"games/removal-trash/{job.Id}":relative;
                        var path=Path.Combine(paths.State,basePath.Replace('/',Path.DirectorySeparatorChar),item.Name.Replace('/',Path.DirectorySeparatorChar));
                        if(!File.Exists(path))continue; // Durable retry after a partial removal.
                        path=MediaPaths.ResolveFile(paths.State,basePath+"/"+item.Name);
                        if(new FileInfo(path).Length!=item.Bytes||!(await VerifiedFilePublication.HashAsync(path,cancellation)).Equals(item.Sha256,StringComparison.OrdinalIgnoreCase))throw new DomainException("Oyun kopyası değişmiş; farklı dosyalar silinmedi.",409);
                        files.Add(path);
                    }
                }
                store.RequireProductJob(job);
                var removed=store.ApplyGameRemoval(job,rom,()=>
                {
                    if(plan.Kind=="web"&&plan.RelativeRoot is{} relative)
                    {
                        var original=Path.Combine(paths.State,relative.Replace('/',Path.DirectorySeparatorChar));
                        if(Directory.Exists(original))
                        {
                            RequireInventory(original,plan.Files.Select(file=>file.Name).ToHashSet(StringComparer.Ordinal));
                            AppFiles.DirectoryIn(paths.State,"games","removal-trash");if(Directory.Exists(trash))throw new DomainException("Kaldırma hedefi farklı veri içeriyor.",409);
                            Directory.Move(original,trash); // Original manifest is never left behind with partially deleted assets.
                        }
                        files=plan.Files.Select(item=>Path.Combine(trash,item.Name.Replace('/',Path.DirectorySeparatorChar))).Where(File.Exists).ToList();
                    }
                    foreach(var path in files){MediaPaths.ResolveFile(paths.State,Path.GetRelativePath(paths.State,path));AppFiles.RejectLink(path);File.SetAttributes(path,File.GetAttributes(path)&~FileAttributes.ReadOnly);File.Delete(path);}
                    if(plan.Kind=="web"&&plan.RelativeRoot is not null)catalog.Forget(job.ScopeId!);
                });
                store.FinishProductJob(job,new{removedPayload=removed&&plan.RelativeRoot is not null,sharedPayloadRetained=!removed,immutablePackageRetained=plan.RelativeRoot is null,savesPreserved=true,sourceFilesDeleted=false},message:"Oyun kaldırıldı; kayıtlar ve bilgiler korundu.");events.Publish("games.changed");
            },cancellation);
        },token);
    }
    private static void RequireInventory(string root,HashSet<string> declared)
    {
        AppFiles.RejectLink(root);
        var seen=new HashSet<string>(StringComparer.Ordinal);var count=0;
        void Walk(string directory,int depth=0)
        {
            if(depth>20)throw new DomainException("Oyun kaldırma dosya sınırı geçersiz.",409);
            foreach(var path in Directory.EnumerateFileSystemEntries(directory))
            {
                if(++count>4096)throw new DomainException("Oyun kaldırma dosya sınırı geçersiz.",409);
                AppFiles.RejectLink(path);if(Directory.Exists(path))Walk(path,depth+1);else seen.Add(Path.GetRelativePath(root,path).Replace('\\','/'));
            }
        }
        Walk(root);if(!seen.SetEquals(declared))throw new DomainException("Oyun klasöründe tanınmayan dosyalar var; mevcut içerik silinmedi.",409);
    }
}
