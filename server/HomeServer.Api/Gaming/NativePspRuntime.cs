using System.Text.Json;
using HomeServer.Core;
using HomeServer.Infrastructure;

namespace HomeServer.Gaming;

public sealed record NativePspManifest(int Format,string Platform,string Version,string SourceCommit,string Profile,
    NativeBundleAsset Binary,NativeBundleAsset Info,NativeBundleAsset[] Assets,NativeBundleAsset? Proof=null,string? MemoryAdapter=null);

/// <summary>Separate pinned PSP software core; never changes existing frontend/core/save identities.</summary>
public sealed class NativePspRuntime(IConfiguration configuration,RetroArchCoreCatalog catalog)
{
    public const string Commit="fa50bb1976065c4f8b1b47af227d367fe9771555";
    private string? Root=>configuration["Home:Games:PspRuntimePath"];
    public NativePspManifest? Read()
    {
        if(string.IsNullOrWhiteSpace(Root)||!File.Exists(Path.Combine(Root,"manifest.json")))return null;
        var path=MediaPaths.ResolveFile(Root,"manifest.json");if(new FileInfo(path).Length>512*1024)throw new DomainException("PSP paketi geçersiz.",409);
        var value=JsonSerializer.Deserialize<NativePspManifest>(File.ReadAllText(path),new JsonSerializerOptions(JsonSerializerDefaults.Web))??throw new DomainException("PSP paketi geçersiz.",409);
        if(value.Format!=1||value.Platform!="linux-x64"||value.Version!="1.20.4"||value.SourceCommit!=Commit||value.Profile!="libretro-software-native-1x"||value.MemoryAdapter!="linux-anonymous-memfd-v1"
            ||value.Binary.Path!="ppsspp_libretro.so"||value.Info.Path!="ppsspp_libretro.info"||value.Info.Sha256!=catalog.Require("ppsspp").InfoSha256
            ||value.Binary.Bytes<1||value.Info.Bytes<1||value.Assets is not{Length:>0 and <=2048}||value.Assets.Any(asset=>asset.Bytes is <0 or >128*1024*1024||!asset.Path.StartsWith("assets/",StringComparison.Ordinal))||value.Assets.Sum(asset=>asset.Bytes)>128*1024*1024||value.Assets.Select(asset=>asset.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=value.Assets.Length
            ||!value.Assets.Any(asset=>asset.Path=="assets/ppge_atlas.zim"))throw new DomainException("PSP paketi sürümü veya yardımcı dosyaları geçersiz.",409);
        foreach(var asset in value.Assets.Append(value.Binary).Append(value.Info))_ = RuntimeFile(asset);
        return value;
    }
    private NativeRuntimeFile RuntimeFile(NativeBundleAsset asset)
    {
        if(asset.Bytes is <0 or >256*1024*1024||asset.Sha256 is not{Length:64}||!asset.Sha256.All(char.IsAsciiHexDigit)
            ||asset.Path.Contains('\\')||asset.Path.Split('/').Any(part=>!NativeSystems.SafeSegment(part)))throw new DomainException("PSP paket dosyası geçersiz.",409);
        return new(MediaPaths.ResolveFile(Root!,asset.Path),asset.Bytes,asset.Sha256);
    }
    public InstalledNativeCore? Installed()
    {
        var value=Read();return value is null?null:new(1,"ppsspp",RetroArchCoreCatalog.Commit,"bundled-psp","","https://github.com/hrydgard/ppsspp/tree/"+Commit,
            value.Binary.Sha256,RuntimeFile(value.Binary),RuntimeFile(value.Info));
    }
    public string? BlockReason(NativeRuntimeProfile? host)
    {
        try
        {
            var value=Read();if(value is null)return "PSP paketi bu sunucuda kurulu değil. PSP içeren Home sürümünü kur.";
            if(host is not{Bundled:true,Platform:"linux-x64"})return "Bu PSP paketi Linux x64 sunucu içindir.";
            if(value.Proof is null)return "PSP yazılımsal yayın, giriş ve kayıt doğrulaması henüz tamamlanmadı.";
            if(value.Proof.Bytes is <1 or >64*1024)throw new DomainException("PSP doğrulama kaydı geçersiz.",409);
            var proof=RuntimeFile(value.Proof);VerifiedFilePublication.Require(proof.Path,proof.Bytes,proof.Sha256);
            using var json=JsonDocument.Parse(System.IO.File.ReadAllText(proof.Path));var root=json.RootElement;
            if(root.GetProperty("coreSha256").GetString()!=value.Binary.Sha256||root.GetProperty("frontendSha256").GetString()!=host.RetroArch.Sha256
                ||!root.GetProperty("softwareFrames").GetBoolean()||!root.GetProperty("inputVerified").GetBoolean()||!root.GetProperty("saveRestore").GetBoolean()
                ||!root.GetProperty("h264Opus").GetBoolean()||!root.GetProperty("audioSignal").GetBoolean()||root.GetProperty("error").ValueKind!=JsonValueKind.Null)
                return "PSP yayın/giriş/kayıt kanıtı bu paketle eşleşmedi.";
            return null;
        }
        catch(Exception error)when(error is IOException or JsonException or DomainException or KeyNotFoundException or InvalidOperationException){return "PSP paketi doğrulanamadı; mevcut ROM ve kayıtlar korundu.";}
    }
    public void StageAssets(string destination)
    {
        var value=Read()??throw new DomainException("PSP paketi kurulu değil.",409);
        var root=AppFiles.DirectoryIn(destination,"PPSSPP");
        foreach(var asset in value.Assets)
        {
            if(!asset.Path.StartsWith("assets/",StringComparison.Ordinal))throw new DomainException("PSP yardımcı dosyası sınırı geçersiz.",409);
            var source=RuntimeFile(asset);VerifiedFilePublication.Require(source.Path,source.Bytes,source.Sha256);
            var relative=asset.Path[7..];var parent=Path.GetDirectoryName(relative);var target=root;
            if(parent is not null)foreach(var part in parent.Replace('\\','/').Split('/')){target=Path.Combine(target,part);Directory.CreateDirectory(target);AppFiles.RejectLink(target);}
            var output=Path.Combine(target,Path.GetFileName(relative));
            if(System.IO.File.Exists(output)){AppFiles.RejectLink(output);continue;} // Already verified user-supplied font/resource override.
            System.IO.File.Copy(source.Path,output,overwrite:false);
        }
    }
}
