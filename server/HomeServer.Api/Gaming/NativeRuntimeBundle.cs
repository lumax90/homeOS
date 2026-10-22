using System.Text.Json;
using HomeServer.Core;
using HomeServer.Infrastructure;

namespace HomeServer.Gaming;

public sealed record NativeBundleAsset(string Path,long Bytes,string Sha256);
public sealed record NativeBundleCore(string Id,string Version,string Commit,string License,NativeBundleAsset Binary,NativeBundleAsset Info,string SourceUrl);
public sealed record NativeBundleManifest(int Format,string Platform,string FrontendVersion,string FrontendCommit,string InputFix,string Adapter,string MediaMtxVersion,
    NativeBundleAsset Frontend,NativeBundleAsset MediaMtx,NativeBundleCore[] Cores,NativeBundleAsset Proof,bool HeadlessGraphics=false);
public sealed record NativeBundleSettings(int Format=1,bool Enabled=true);

/// <summary>Read-only, hash-bound software shipped in the image; private enable/preferences live in /data.</summary>
public static class NativeRuntimeBundle
{
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    public static NativeBundleManifest? Read(HomePaths paths)
    {
        if(paths.NativeRuntimePath is not{} root)return null;
        var file=MediaPaths.ResolveFile(root,"manifest.json");if(new FileInfo(file).Length>128*1024)throw new DomainException("Sunucu emülasyon paketi geçersiz.",409);
        NativeBundleManifest value;try{value=JsonSerializer.Deserialize<NativeBundleManifest>(System.IO.File.ReadAllText(file),Json)??throw new JsonException();}catch(JsonException){throw new DomainException("Sunucu emülasyon paketi geçersiz.",409);}
        if(value.Format!=1||value.Platform!="linux-x64"||value.FrontendVersion!="1.22.2"||value.FrontendCommit!="69a4f0ea1e8aaf442ae4858f2e7f2b31a1776576"||value.InputFix!="cfb1204314dd6b061a7b1cda77221735939bf5ba"||value.Adapter is not("home-host-1" or "home-linux-host-1")||value.MediaMtxVersion!="1.21.2"||value.Cores is not{Length:>0 and <=32}||value.Cores.Select(c=>c.Id).Distinct().Count()!=value.Cores.Length)
            throw new DomainException("Sunucu emülasyon paketi sürümü desteklenmiyor.",409);
        foreach(var asset in value.Cores.SelectMany(core=>new[]{core.Binary,core.Info}).Concat([value.Frontend,value.MediaMtx,value.Proof]))_ = File(paths,asset);
        if(value.Cores.FirstOrDefault(core=>core.Id=="mgba") is not{Version:"0.10.5",Commit:"26b7884bc25a5933960f3cdcd98bac1ae14d42e2"})throw new DomainException("Sunucu emülasyon paketi geçersiz.",409);
        return value;
    }
    public static NativeRuntimeFile File(HomePaths paths,NativeBundleAsset asset)
    {
        if(asset is null||asset.Path is null||asset.Bytes is <1 or >256*1024*1024||asset.Sha256 is not{Length:64}||asset.Sha256.Any(c=>!char.IsAsciiHexDigit(c))||!GameCatalog.IsSafeAssetPath(asset.Path)&&asset.Path is not("retroarch" or "mediamtx")&&!SafeCore(asset.Path))throw new DomainException("Sunucu emülasyon dosyası geçersiz.",409);
        return new(MediaPaths.ResolveFile(paths.NativeRuntimePath!,asset.Path),asset.Bytes,asset.Sha256);
    }
    private static bool SafeCore(string path)=>path.StartsWith("cores/",StringComparison.Ordinal)&&path.Count(c=>c=='/')==1&&path[6..].All(c=>char.IsAsciiLetterOrDigit(c)||c is '_' or '.')&&(path.EndsWith("_libretro.so",StringComparison.Ordinal)||path.EndsWith("_libretro.info",StringComparison.Ordinal));
    public static NativeRuntimeProfile? Profile(HomePaths paths)
    {
        var bundle=Read(paths);if(bundle is null)return null;var core=bundle.Cores.Single(c=>c.Id=="mgba");
        var result=new NativeRuntimeProfile(2,Settings(paths).Enabled,core.Version,core.Commit,File(paths,bundle.Frontend),File(paths,core.Binary),File(paths,core.Info),File(paths,bundle.MediaMtx),[],true,bundle.Platform,true,File(paths,bundle.Proof),bundle.HeadlessGraphics);
        VerifiedFilePublication.Require(result.Proof!.Path,result.Proof.Bytes,result.Proof.Sha256);RequireProof(result,System.IO.File.ReadAllText(result.Proof.Path));return result;
    }
    public static NativeBundleSettings Settings(HomePaths paths)
    {
        const string name="games/server-emulation.json";if(!System.IO.File.Exists(Path.Combine(paths.State,name)))return new();var path=MediaPaths.ResolveFile(paths.State,name);
        if(new FileInfo(path).Length>4096)throw new DomainException("Sunucu emülasyon ayarı geçersiz.",409);
        try{var value=JsonSerializer.Deserialize<NativeBundleSettings>(System.IO.File.ReadAllText(path),Json)??throw new JsonException();if(value.Format!=1)throw new JsonException();return value;}catch(JsonException){throw new DomainException("Sunucu emülasyon ayarı geçersiz.",409);}
    }
    public static void RequireProof(NativeRuntimeProfile profile,string text)
    {
        try
        {
            using var document=JsonDocument.Parse(text);var root=document.RootElement;
            if(root.GetProperty("format").GetInt32()!=1||root.GetProperty("platform").GetString()!=profile.Platform||root.GetProperty("frontendSha256").GetString()!=profile.RetroArch.Sha256||root.GetProperty("coreSha256").GetString()!=profile.Core.Sha256||root.GetProperty("mediamtxSha256").GetString()!=profile.MediaMtx.Sha256||root.GetProperty("error").ValueKind!=JsonValueKind.Null||root.GetProperty("heldFrames").GetInt32()<10||!root.GetProperty("saveRestore").GetBoolean()||!root.GetProperty("h264Opus").GetBoolean()||!root.GetProperty("loopbackControl").GetBoolean()||!root.GetProperty("headless").GetBoolean()||profile.HeadlessGraphics&&!root.GetProperty("headlessGraphics").GetBoolean())throw new JsonException();
        }
        catch(Exception error)when(error is JsonException or KeyNotFoundException or InvalidOperationException){throw new DomainException("Sunucu emülasyonunun native doğrulaması eksik.",409);}
    }
}
