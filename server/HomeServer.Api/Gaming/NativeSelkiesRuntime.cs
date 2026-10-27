using System.Text.Json;
using HomeServer.Core;
using HomeServer.Infrastructure;

namespace HomeServer.Gaming;

public sealed record NativeStreamingBackend(string Id,bool Available,bool Experimental,string? Reason=null);
public sealed record NativeSelkiesCore(string Id,string Sha256);
public sealed record NativeSelkiesManifest(int Format,string Version,string BaseFrontendSha256,NativeBundleAsset Frontend,
    string Python,string PythonTarget,string WebRoot,NativeSelkiesCore[] Cores,NativeRuntimeFile[] Dependencies);
public sealed record NativeSelkiesInstallation(NativeSelkiesManifest Manifest,string Frontend);

/// <summary>The optional package is inert until a game explicitly selects it.</summary>
public sealed class NativeSelkiesRuntime(IConfiguration configuration)
{
    private string? Root=>configuration["Home:Games:SelkiesRuntimePath"];
    public NativeStreamingBackend Status()
    {
        if(!OperatingSystem.IsLinux()||System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture!=System.Runtime.InteropServices.Architecture.X64)
            return new("selkies",false,true,"Selkies şu anda Linux x64 sunucuda kullanılabilir.");
        if(string.IsNullOrWhiteSpace(Root)||!File.Exists(Path.Combine(Root,"manifest.json")))return new("selkies",false,true,"Selkies paketi bu sunucuda kurulu değil.");
        try{_=Read();return new("selkies",true,true);}
        catch(Exception error)when(error is DomainException or IOException or UnauthorizedAccessException){return new("selkies",false,true,"Selkies paketi doğrulanamadı.");}
    }
    private NativeSelkiesManifest Read()
    {
        if(string.IsNullOrWhiteSpace(Root))throw new DomainException("Selkies paketi bu sunucuda kurulu değil.",409);
        var path=MediaPaths.ResolveFile(Root,"manifest.json");if(new FileInfo(path).Length>1024*1024)throw new DomainException("Selkies paketi doğrulanamadı.",409);
        try
        {
            var value=JsonSerializer.Deserialize<NativeSelkiesManifest>(File.ReadAllText(path),new JsonSerializerOptions(JsonSerializerDefaults.Web))??throw new JsonException();
            if(value.Format!=1||value.Version!="2.1.0"||value.Frontend is not{Path:"retroarch",Bytes:>0 and <128*1024*1024}
                ||!Hash(value.Frontend.Sha256)||!Hash(value.BaseFrontendSha256)||value.Cores is not{Length:>0 and <=32}
                ||value.Cores.Any(core=>string.IsNullOrWhiteSpace(core.Id)||!Hash(core.Sha256))||value.Dependencies is not{Length:>0 and <=4096}
                ||value.Dependencies.Any(file=>!Path.IsPathFullyQualified(file.Path)||file.Bytes is <1 or >256*1024*1024||!Hash(file.Sha256))
                ||!Path.IsPathFullyQualified(value.Python)||!Path.IsPathFullyQualified(value.PythonTarget)||!Path.IsPathFullyQualified(value.WebRoot)
                ||!value.Dependencies.Any(file=>file.Path==value.PythonTarget))throw new JsonException();
            return value;
        }
        catch(Exception error)when(error is JsonException or ArgumentException){throw new DomainException("Selkies paketi doğrulanamadı.",409);}
    }
    private static bool Hash(string? value)=>value is{Length:64}&&value.All(char.IsAsciiHexDigit);
    public static void RequireCompatibility(NativeSelkiesManifest manifest,NativeRuntimeProfile profile,string coreId,string coreHash)
    {
        if(!profile.Bundled||profile.Platform!="linux-x64"||profile.RetroArch.Sha256!=manifest.BaseFrontendSha256)
            throw new DomainException("Selkies ile mevcut emülasyon paketi eşleşmiyor.",409);
        if(!manifest.Cores.Any(core=>core.Id==coreId&&core.Sha256==coreHash))
            throw new DomainException("Bu çekirdek Selkies ile henüz doğrulanmadı. Standart yayın yöntemini seç.",409);
    }
    public bool Supports(NativeRuntimeProfile profile,string coreId,string coreHash)
    {
        try{RequireCompatibility(Read(),profile,coreId,coreHash);return true;}
        catch(Exception error)when(error is DomainException or IOException or UnauthorizedAccessException){return false;}
    }
    public async Task<NativeSelkiesInstallation> RequireAsync(NativeRuntimeProfile profile,string coreId,string coreHash,CancellationToken token)
    {
        var status=Status();if(!status.Available)throw new DomainException(status.Reason!,409);var manifest=Read();RequireCompatibility(manifest,profile,coreId,coreHash);
        var frontend=MediaPaths.ResolveFile(Root!,manifest.Frontend.Path);
        foreach(var file in manifest.Dependencies.Append(new(frontend,manifest.Frontend.Bytes,manifest.Frontend.Sha256)))
        {
            token.ThrowIfCancellationRequested();
            if(!File.Exists(file.Path)||new FileInfo(file.Path).Length!=file.Bytes||await VerifiedFilePublication.HashAsync(file.Path,token)!=file.Sha256)
                throw new DomainException("Selkies paketi doğrulanamadı.",409);
        }
        var python=new FileInfo(manifest.Python);var target=python.ResolveLinkTarget(true)?.FullName??python.FullName;
        if(target!=manifest.PythonTarget)throw new DomainException("Selkies paketi doğrulanamadı.",409);
        return new(manifest,frontend);
    }
    public static string Backend(string? value)=>value switch{null or "standard"=>"standard","selkies"=>"selkies",_=>throw new DomainException("Oyun yayın yöntemi desteklenmiyor.")};
}
