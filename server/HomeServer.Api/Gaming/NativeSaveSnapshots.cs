using System.Text.Json;
using System.Text;
using HomeServer.Core;
using HomeServer.Infrastructure;

namespace HomeServer.Gaming;

public sealed record NativeSavedFile(string Name,long Bytes,string Sha256,string? Blob=null);
public sealed record NativeSavedSnapshot(string Id, string GameId, string Build, NativeSavedFile[] Files);
public sealed record NativeSavedSnapshotReference(string Id,string GameId,string Build,long ManifestBytes,string ManifestSha256);

/// <summary>Binary native states remain managed files; the existing CAS checkpoint stores only a bounded manifest.</summary>
public sealed class NativeSaveSnapshots(HomePaths paths)
{
    private static readonly string[] Names=["game.state","game.srm","game.rtc","game.sav","game.mcr","game.1.mcr","game.2.mcr","game.bkr","game.bcr","game.smpc","game.nv","game.eep","pcsx-card2.mcd"];
    private static readonly string[] SystemRoots=["pcsx2/memcards","pcsx2/inis","dolphin-emu/User","Cemu/settings.xml","xbox_hdd.qcow2","xbox_eeprom.bin","dsi_nand.bin","dsi_sd_card.bin"];
    private static readonly string[] DisposableDirectories=["Citra/shader","Citra/shader_cache","Citra/log","Citra/logs","Citra/cache","Azahar/shader","Azahar/shader_cache","Azahar/log","Azahar/logs","Azahar/cache","PSP/SYSTEM/CACHE","dolphin-emu/User/Cache","dolphin-emu/User/Logs","system/dolphin-emu/User/Cache","system/dolphin-emu/User/Logs","pcsx2/cache","pcsx2/logs"];
    private const long MaximumFile=64L*1024*1024*1024,MaximumTotal=128L*1024*1024*1024;
    public static bool SafeName(string name)
    {
        if(string.IsNullOrEmpty(name))return false;if(Names.Contains(name))return true;var parts=name.Split('/');if(parts.Length>20||name.Length>400||parts[0]=="system"&&!SystemRoots.Any(root=>name=="system/"+root||name.StartsWith("system/"+root+"/",StringComparison.Ordinal)))return false;
        if(new[]{".exe",".dll",".sys",".ps1",".cmd",".bat",".lnk"}.Contains(Path.GetExtension(name),StringComparer.OrdinalIgnoreCase))return false;
        if(parts.Any(part=>!NativeSystems.SafeSegment(part)))return false;
        return true;
    }
    private static IEnumerable<string> SourceNames(string source)
    {
        foreach(var name in Walk(source,""))yield return name;
        foreach(var root in SystemRoots)
        {var path=Path.Combine(Path.GetDirectoryName(source)!,"system",root.Replace('/',Path.DirectorySeparatorChar));if(Directory.Exists(path))foreach(var name in Walk(path,"system/"+root))yield return name;else if(File.Exists(path))yield return "system/"+root;}
        IEnumerable<string> Walk(string directory,string prefix)
        {
            AppFiles.RejectLink(directory);foreach(var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                AppFiles.RejectLink(entry);var name=(prefix.Length>0?prefix+"/":"")+Path.GetFileName(entry);if(name.Split('/').Length>20)throw new DomainException("Yerel oyun kayıt yolu çok derin.",413);
                if(Directory.Exists(entry)&&DisposableDirectories.Contains(name,StringComparer.OrdinalIgnoreCase))continue;
                if(Directory.Exists(entry)){foreach(var nested in Walk(entry,name))yield return nested;}else if(SafeName(name))yield return name;else throw new DomainException("Yerel oyun kayıt dosyası yolu geçersiz.",409);
            }
        }
    }
    public async Task<string> PublishAsync(string user,string game,string build,string source,CancellationToken token,bool requireState=true,bool ignoreState=false)
    {
        var id = Guid.NewGuid().ToString("N"); var root = AppFiles.DirectoryIn(paths.State, "games", "native-snapshots", user, game, build, id);
        var files = new List<NativeSavedFile>(); long total = 0;
        foreach(var name in SourceNames(source))
        {
            if(ignoreState&&name=="game.state")continue;
            if(files.Count>=2048)throw new DomainException("Yerel oyun kayıt dosyası sayısı sınırını aşıyor.",413);
            var input=name.StartsWith("system/",StringComparison.Ordinal)?MediaPaths.ResolveFile(Path.Combine(Path.GetDirectoryName(source)!,"system"),name[7..]):MediaPaths.ResolveFile(source,name);
            var bytes = new FileInfo(input).Length; total += bytes;
            if(bytes<0||bytes>MaximumFile||total>MaximumTotal||name=="game.state"&&bytes==0)throw new DomainException("Yerel oyun kaydı boyut sınırını aşıyor.",413);
            var hash = await VerifiedFilePublication.HashAsync(input, token);
            if(bytes>64L*1024*1024){var destination=AppFiles.DirectoryIn(paths.State,"games","native-save-objects",user,hash);await VerifiedFilePublication.StageAsync(input,destination,"payload",id,bytes,hash,token);VerifiedFilePublication.Promote(destination,"payload",id,bytes,hash);files.Add(new(name,bytes,hash,hash));}
            else{var destination=OwnedParent(root,name);var leaf=Path.GetFileName(name);await VerifiedFilePublication.StageAsync(input,destination,leaf,id,bytes,hash,token);VerifiedFilePublication.Promote(destination,leaf,id,bytes,hash);files.Add(new(name,bytes,hash));}
        }
        if(requireState&&!files.Any(file=>file.Name=="game.state"))throw new DomainException("Yerel oyun anlık kaydı doğrulanamadı.",409);
        var text=JsonSerializer.Serialize(new NativeSavedSnapshot(id,game,build,files.ToArray()),new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var manifestSize=Encoding.UTF8.GetByteCount(text);if(manifestSize<24000)return text;if(manifestSize>1024*1024)throw new DomainException("Yerel oyun kayıt manifesti çok büyük.",413);
        var manifest=Path.Combine(root,"snapshot.json");await File.WriteAllTextAsync(manifest,text,new UTF8Encoding(false),token);var manifestHash=await VerifiedFilePublication.HashAsync(manifest,token);
        return JsonSerializer.Serialize(new NativeSavedSnapshotReference(id,game,build,new FileInfo(manifest).Length,manifestHash),new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }
    public async Task<bool> RestoreAsync(GameCheckpoint checkpoint, string user, string game, string build, string destination, CancellationToken token)
    {
        if (!checkpoint.Files.TryGetValue("native-save.json", out var text)) return false;
        var json=new JsonSerializerOptions(JsonSerializerDefaults.Web);using var document=JsonDocument.Parse(text);string? referencedId=null;
        if(document.RootElement.TryGetProperty("manifestSha256",out _))
        {
            var reference=JsonSerializer.Deserialize<NativeSavedSnapshotReference>(text,json)??throw new DomainException("Yerel oyun kayıt manifesti geçersiz.",409);AppFiles.Id(reference.Id);
            referencedId=reference.Id;
            if(reference.GameId!=game||reference.Build!=build||reference.ManifestBytes is <1 or >1024*1024||reference.ManifestSha256 is not{Length:64}||reference.ManifestSha256.Any(c=>!char.IsAsciiHexDigit(c)))throw new DomainException("Yerel oyun kayıt manifesti geçersiz.",409);
            var file=MediaPaths.ResolveFile(Path.Combine(paths.State,"games","native-snapshots",user,game,build,reference.Id),"snapshot.json");VerifiedFilePublication.Require(file,reference.ManifestBytes,reference.ManifestSha256);text=await File.ReadAllTextAsync(file,token);
        }
        var snapshot=JsonSerializer.Deserialize<NativeSavedSnapshot>(text,json)??throw new DomainException("Yerel oyun kayıt manifesti geçersiz.",409);
        AppFiles.Id(snapshot.Id);
        if(snapshot.GameId!=game||snapshot.Build!=build||referencedId is not null&&snapshot.Id!=referencedId||snapshot.Files is null||snapshot.Files.Length is <1 or >2048||snapshot.Files.Sum(file=>file.Bytes)>MaximumTotal||snapshot.Files.Select(file=>file.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=snapshot.Files.Length)
            throw new DomainException("Yerel oyun kaydı başka bir oyun veya çekirdek sürümüne ait.", 409);
        var source = Path.Combine(paths.State, "games", "native-snapshots", user, game, build, snapshot.Id);
        if(!Directory.Exists(source))throw new DomainException("Yerel oyun kaydı bu profile ait değil veya bulunamadı.",409);
        foreach(var file in snapshot.Files)
        {
            if(!SafeName(file.Name)||file.Bytes<0||file.Bytes>MaximumFile||file.Sha256 is not{Length:64}||file.Sha256.Any(c=>!char.IsAsciiHexDigit(c)))throw new DomainException("Yerel oyun kayıt dosyası geçersiz.",409);
            if(file.Blob is not null&&file.Blob!=file.Sha256)throw new DomainException("Yerel oyun kayıt dosyası geçersiz.",409);VerifiedFilePublication.Require(file.Blob is null?MediaPaths.ResolveFile(source,file.Name):MediaPaths.ResolveFile(Path.Combine(paths.State,"games","native-save-objects",user,file.Blob),"payload"),file.Bytes,file.Sha256);
        }
        foreach (var file in snapshot.Files)
        {
            var input=file.Blob is null?MediaPaths.ResolveFile(source,file.Name):MediaPaths.ResolveFile(Path.Combine(paths.State,"games","native-save-objects",user,file.Blob),"payload");VerifiedFilePublication.Require(input,file.Bytes,file.Sha256);
            var operation=Guid.NewGuid().ToString("N");var target=file.Name.StartsWith("system/",StringComparison.Ordinal)?OwnedParent(Path.GetDirectoryName(destination)!,file.Name):OwnedParent(destination,file.Name);var leaf=Path.GetFileName(file.Name);await VerifiedFilePublication.StageAsync(input,target,leaf,operation,file.Bytes,file.Sha256,token);VerifiedFilePublication.Promote(target,leaf,operation,file.Bytes,file.Sha256);
        }
        return snapshot.Files.Any(file=>file.Name=="game.state"&&file.Bytes>0);
    }
    private static string OwnedParent(string root,string name)
    {
        if(!SafeName(name))throw new DomainException("Yerel oyun kayıt dosyası geçersiz.",409);var current=root;AppFiles.RejectLink(current);foreach(var part in name.Split('/')[..^1]){current=Path.Combine(current,part);Directory.CreateDirectory(current);AppFiles.RejectLink(current);}return current;
    }
}
