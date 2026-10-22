using System.Text.Json;
using HomeServer.Core;
using HomeServer.Infrastructure;

namespace HomeServer.Gaming;
public sealed record NativeGpuProfile(int Format,bool Enabled,NativeRuntimeFile Frontend,NativeRuntimeFile[] Dependencies,NativeRuntimeFile Proof,bool LoopbackOnly=false,NativeRuntimeFile[]? BackendProofs=null,NativeRuntimeFile? GbaProof=null);
/// <summary>Separate proven graphics frontend; never replaces the released GBA frontend/save partition.</summary>
public sealed class NativeGpuRuntime(HomePaths paths)
{
    public static string? Renderer(RetroArchCore core)
    {
        if(!core.Hardware)return null;
        if(string.IsNullOrWhiteSpace(core.HardwareApi))return "glcore"; // Upstream can select a supported context at core load.
        if(core.HardwareApi.Contains("OpenGL Core",StringComparison.OrdinalIgnoreCase))return "glcore";
        if(core.HardwareApi.Split('|').Any(api=>api.TrimStart().StartsWith("OpenGL ",StringComparison.OrdinalIgnoreCase)&&!api.TrimStart().StartsWith("OpenGL ES",StringComparison.OrdinalIgnoreCase)))return "gl";
        if(core.HardwareApi.Contains("Direct3D",StringComparison.OrdinalIgnoreCase))return "d3d11";
        if(core.HardwareApi.Contains("Vulkan",StringComparison.OrdinalIgnoreCase))return "vulkan";
        return null;
    }
    public NativeGpuProfile? Read()
    {
        const string relative="games/native-gpu-profile.json";if(!File.Exists(Path.Combine(paths.State,relative)))return null;
        var file=MediaPaths.ResolveFile(paths.State,relative);if(new FileInfo(file).Length>128*1024)throw new DomainException("GPU çalışma profili geçersiz.",409);
        var profile=JsonSerializer.Deserialize<NativeGpuProfile>(File.ReadAllText(file),new JsonSerializerOptions(JsonSerializerDefaults.Web))??throw new DomainException("GPU çalışma profili geçersiz.",409);
        if(profile.Format!=1||profile.Dependencies is null||profile.Dependencies.Length>128||profile.Frontend is null||profile.Proof is null||profile.Frontend.Sha256==NativeRuntimeProfile.RejectedRemotePadSha256)throw new DomainException("GPU çalışma profili geçersiz.",409);
        foreach(var asset in profile.Dependencies.Append(profile.Frontend).Append(profile.Proof))if(!Path.IsPathFullyQualified(asset.Path)||asset.Bytes is <1 or >256*1024*1024||asset.Sha256 is not{Length:64}||asset.Sha256.Any(c=>!char.IsAsciiHexDigit(c)))throw new DomainException("GPU çalışma profili geçersiz.",409);
        if(profile.Proof.Bytes>128*1024)throw new DomainException("GPU çalışma profili geçersiz.",409);VerifiedFilePublication.Require(profile.Proof.Path,profile.Proof.Bytes,profile.Proof.Sha256);RequireProof(profile,File.ReadAllText(profile.Proof.Path));
        if(profile.BackendProofs is not{Length:3})throw new DomainException("GPU arka uç kanıtları eksik.",409);var backends=new HashSet<string>();foreach(var proof in profile.BackendProofs.Append(profile.Proof)){if(proof.Bytes is <1 or >128*1024||proof.Sha256 is not{Length:64})throw new DomainException("GPU arka uç kanıtları eksik.",409);VerifiedFilePublication.Require(proof.Path,proof.Bytes,proof.Sha256);var content=File.ReadAllText(proof.Path);RequireProof(profile,content);using var report=JsonDocument.Parse(content);backends.Add(report.RootElement.GetProperty("recording").GetProperty("renderingApi").GetString()??"");}if(!backends.SetEquals(["gl","glcore","d3d11","vulkan"]))throw new DomainException("GPU arka uç kanıtları eksik.",409);
        if(profile.GbaProof is not{} gba||gba.Bytes is <1 or >128*1024)throw new DomainException("Emülasyonun native giriş kanıtı eksik.",409);VerifiedFilePublication.Require(gba.Path,gba.Bytes,gba.Sha256);var original=NativeRuntimeProfile.Read(paths)??throw new DomainException("GPU çalışma profili geçersiz.",409);NativeRuntimeCandidate.RequireProof(original with{RetroArch=profile.Frontend},File.ReadAllText(gba.Path));return profile;
    }
    public static void RequireProof(NativeGpuProfile profile,string text)
    {
        try
        {
            using var doc=JsonDocument.Parse(text);var root=doc.RootElement;
            var stream=root.GetProperty("streaming");if(root.GetProperty("frontendSha256").GetString()!=profile.Frontend.Sha256||root.GetProperty("error").ValueKind!=JsonValueKind.Null||root.GetProperty("privateWindows").GetInt32()<1||root.GetProperty("defaultWindows").GetInt32()!=0||root.GetProperty("defaultDesktopCapture").GetBoolean()||root.GetProperty("switchedDesktop").GetBoolean()||!root.GetProperty("recording").GetProperty("actualGpuFramebuffer").GetBoolean()||stream.GetProperty("whepStatus").GetInt32()!=201||!stream.GetProperty("inputStateRestore").GetBoolean()||!stream.GetProperty("peripherals").GetBoolean()||!stream.GetProperty("coreOptions").GetBoolean()||!stream.GetProperty("diskControl").GetBoolean()||stream.GetProperty("controllerPorts").GetInt32()!=4||profile.LoopbackOnly&&!stream.GetProperty("loopbackListeners").GetBoolean())throw new JsonException();
        }
        catch(Exception error)when(error is JsonException or InvalidOperationException or KeyNotFoundException){throw new DomainException("GPU yayın ve kontrol kanıtı eksik.",409);}
    }
    public bool Prepared{get{try{return Read() is{Enabled:true};}catch(Exception error)when(error is DomainException or IOException or JsonException){return false;}}}
    public NativeGpuProfile Verify()
    {
        var profile=Read();if(profile is not{Enabled:true})throw new DomainException("GPU yayın çalıştırıcısı henüz hazırlanmadı.",409);
        foreach(var file in profile.Dependencies.Append(profile.Frontend))VerifiedFilePublication.Require(file.Path,file.Bytes,file.Sha256);
        if(!profile.LoopbackOnly&&!NativeLocalInputGuard.InspectPorts(profile.Frontend.Path,[62401,62406]))throw new DomainException("Yerel oyun giriş ve komut bağlantıları dış erişime kapatılmalı.",409);return profile;
    }
}
