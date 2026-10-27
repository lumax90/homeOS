using System.Text.Json;
using HomeServer.Core;
using HomeServer.Infrastructure;

namespace HomeServer.Gaming;

public sealed record NativeHandheldCore(string Id,string System,string Name,string Version,string SourceCommit,string SourceUrl,string License,string Profile,NativeBundleAsset Binary,NativeBundleAsset Info,NativeBundleAsset? Proof=null,bool Experimental=false);
public sealed record NativeHandheldManifest(int Format,string Platform,NativeHandheldCore[] Cores);

/// <summary>Separate pinned DS/3DS software. Existing installed core/save partitions are not overwritten.</summary>
public sealed class NativeHandheldRuntime(IConfiguration configuration,RetroArchCoreCatalog catalog)
{
    private string? Root=>configuration["Home:Games:HandheldRuntimePath"];
    private static readonly Dictionary<string,string> Commits=new(){{"desmume","95b4d798731caa809125b6c3c11d17cc332ff6ef"},{"citra","a0483e9abe8134ae6b290cdca7d49cb77689f05b"}};
    public NativeHandheldManifest? Read()
    {
        if(string.IsNullOrWhiteSpace(Root)||!File.Exists(Path.Combine(Root,"manifest.json")))return null;
        var path=MediaPaths.ResolveFile(Root,"manifest.json");if(new FileInfo(path).Length>128*1024)throw new DomainException("DS/3DS paketi geçersiz.",409);
        var value=JsonSerializer.Deserialize<NativeHandheldManifest>(File.ReadAllText(path),new JsonSerializerOptions(JsonSerializerDefaults.Web))??throw new DomainException("DS/3DS paketi geçersiz.",409);
        if(value.Format!=1||value.Platform!="linux-x64"||value.Cores is not{Length:2}||value.Cores.Select(core=>core.Id).Distinct().Count()!=2)throw new DomainException("DS/3DS paketi kimliği geçersiz.",409);
        foreach(var core in value.Cores)
        {
            if(!Commits.TryGetValue(core.Id,out var commit)||core.SourceCommit!=commit||core.System!=(core.Id=="desmume"?"nds":"3ds")
                ||core.Profile!=(core.Id=="desmume"?"software-native-dual-screen":"opengl-native-dual-screen")
                ||core.Binary.Path!=$"cores/{core.Id}_libretro.so"||core.Info.Path!=$"cores/{core.Id}_libretro.info"||core.Info.Sha256!=catalog.Require(core.Id).InfoSha256)throw new DomainException("DS/3DS paketi kimliği geçersiz.",409);
            _=RuntimeFile(core.Binary);_=RuntimeFile(core.Info);
        }
        return value;
    }
    private NativeRuntimeFile RuntimeFile(NativeBundleAsset asset)
    {
        if(asset.Bytes is <1 or >256*1024*1024||asset.Sha256 is not{Length:64}||!asset.Sha256.All(char.IsAsciiHexDigit)||asset.Path.Contains('\\')||asset.Path.Split('/').Any(part=>!NativeSystems.SafeSegment(part)))throw new DomainException("DS/3DS paket dosyası geçersiz.",409);
        return new(MediaPaths.ResolveFile(Root!,asset.Path),asset.Bytes,asset.Sha256);
    }
    public InstalledNativeCore? Installed(string id)
    {
        var core=Read()?.Cores.FirstOrDefault(core=>core.Id==id);return core is null?null:new(1,id,RetroArchCoreCatalog.Commit,"bundled-handheld","",core.SourceUrl,core.Binary.Sha256,RuntimeFile(core.Binary),RuntimeFile(core.Info));
    }
    public string? BlockReason(string id,NativeRuntimeProfile? host)
    {
        try
        {
            var core=Read()?.Cores.FirstOrDefault(core=>core.Id==id);if(core is null)return "DS/3DS paketi bu sunucuda kurulu değil.";
            if(host is not{Bundled:true,Platform:"linux-x64"})return "Bu DS/3DS paketi Linux x64 sunucu içindir.";
            if(core.Proof is null)return "DS/3DS görüntü, kumanda, dokunmatik ve kayıt doğrulaması henüz tamamlanmadı.";
            if(core.Proof.Bytes>64*1024)throw new DomainException("DS/3DS doğrulama kaydı geçersiz.",409);
            var proof=RuntimeFile(core.Proof);VerifiedFilePublication.Require(proof.Path,proof.Bytes,proof.Sha256);
            using var json=JsonDocument.Parse(File.ReadAllText(proof.Path));var value=json.RootElement;
            if(value.GetProperty("coreSha256").GetString()!=core.Binary.Sha256||value.GetProperty("frontendSha256").GetString()!=host.RetroArch.Sha256
                ||!value.GetProperty("videoFrames").GetBoolean()||!value.GetProperty("inputVerified").GetBoolean()||!value.GetProperty("touchVerified").GetBoolean()
                ||!value.GetProperty("saveRestore").GetBoolean()||!value.GetProperty("h264Opus").GetBoolean()||value.GetProperty("error").ValueKind!=JsonValueKind.Null)
                return "DS/3DS paketi yayın ve kayıt kanıtıyla eşleşmedi.";
            return null;
        }
        catch(Exception error)when(error is IOException or JsonException or DomainException or KeyNotFoundException or InvalidOperationException){return "DS/3DS paketi doğrulanamadı; oyunlar ve kayıtlar korundu.";}
    }
}
