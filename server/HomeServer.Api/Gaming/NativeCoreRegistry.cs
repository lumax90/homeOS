using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HomeServer.Core;
using HomeServer.Infrastructure;
using HomeServer.Operations;

namespace HomeServer.Gaming;
public sealed record InstalledNativeCore(int Format,string Id,string CatalogCommit,string Channel,string InstalledAt,string SourceUrl,string ArchiveSha256,NativeRuntimeFile Binary,NativeRuntimeFile Info);
public sealed record NativeCoreSelection(string CoreId,int Revision=0);
public sealed record NativeCoreResolution(NativeRuntimeProfile Host,RetroArchCore Definition,NativeRuntimeFile Binary,NativeRuntimeFile Info,string Version,NativeGpuProfile? Gpu=null)
{
    public bool ModernHost=>Host.LoopbackOnly||Gpu is not null;
    public bool GraphicsReady=>Gpu is not null||Host.HeadlessGraphics;
    public GameManifest Game(GameRom rom)=>new(rom.Id,rom.Title,"","","",Definition.Id=="mgba"?Host.CoreCommit:Binary.Sha256,Version,"RetroArch / "+Definition.Name,"user-supplied","native","16/9",[],["native-save.json"],[],"native");
}
public sealed class NativeCoreRegistry(HomePaths paths,RetroArchCoreCatalog catalog,NativeFirmwareLibrary firmware,NativeGpuRuntime? gpu=null)
{
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    public NativeCoreSelection Selection(GameRom rom)
    {
        var path=Path.Combine(paths.State,"games","core-selections",rom.UserId,rom.Id,"selection.json");if(!File.Exists(path))return new(NativeSystems.Require(rom.System).Core);
        path=MediaPaths.ResolveFile(paths.State,$"games/core-selections/{rom.UserId}/{rom.Id}/selection.json");if(new FileInfo(path).Length>4096)throw new DomainException("Çekirdek seçimi geçersiz.",409);
        var selection=JsonSerializer.Deserialize<NativeCoreSelection>(File.ReadAllText(path),Json)??throw new DomainException("Çekirdek seçimi geçersiz.",409);if(selection.Revision<0)throw new DomainException("Çekirdek seçimi geçersiz.",409);catalog.Require(selection.CoreId);return selection;
    }
    public NativeCoreSelection Select(GameRom rom,NativeCoreSelection input)
    {
        var core=catalog.Require(input.CoreId);if(!Compatible(rom.System,core)||!core.Extensions.Contains(NativeSystems.Extension(rom.FileName)))throw new DomainException("Çekirdek bu oyun sistemiyle eşleşmiyor.",400);
        if(Selection(rom).Revision!=input.Revision)throw new DomainException("Çekirdek seçimi değişti; yeniden yükle.",409);
        var result=input with{Revision=input.Revision+1};var root=AppFiles.DirectoryIn(paths.State,"games","core-selections",rom.UserId,rom.Id);PackageTrust.AtomicJson(Path.Combine(root,"selection.json"),result);return result;
    }
    public bool Compatible(string system,RetroArchCore core)
    {
        var definition=NativeSystems.Require(system);return definition.Core==core.Id||NativeSystems.CanonicalId(core.System)==system||definition.ArtworkDatabase is{} database&&core.Databases.Contains(database)||system=="arcade"&&core.Databases.Any(name=>name.Contains("Arcade",StringComparison.OrdinalIgnoreCase));
    }
    public string Name(string id)=>catalog.Require(id).Name;
    public InstalledNativeCore? Installed(string id)
    {
        var core=catalog.Require(id);
        if(NativeRuntimeBundle.Read(paths)?.Cores.FirstOrDefault(core=>core.Id==id) is{} bundled)return new(1,id,RetroArchCoreCatalog.Commit,"bundled","",bundled.SourceUrl,bundled.Binary.Sha256,NativeRuntimeBundle.File(paths,bundled.Binary),NativeRuntimeBundle.File(paths,bundled.Info));
        var linux=paths.NativeRuntimePath is not null;var root=Path.Combine(paths.State,"games",linux?"native-cores-linux-x64":"native-cores",core.Id);var path=Path.Combine(root,"installed.json");if(!File.Exists(path))return null;
        path=MediaPaths.ResolveFile(root,"installed.json");if(new FileInfo(path).Length>8192)throw new DomainException("Çekirdek kurulum kaydı geçersiz.",409);
        var value=JsonSerializer.Deserialize<InstalledNativeCore>(File.ReadAllText(path),Json)??throw new DomainException("Çekirdek kurulum kaydı geçersiz.",409);
        if(value.Format!=1||value.Id!=id||value.CatalogCommit!=RetroArchCoreCatalog.Commit||value.Channel!="upstream-nightly"||value.Binary.Bytes is <1 or >256*1024*1024||value.Binary.Sha256 is not{Length:64}||value.Binary.Sha256.Any(c=>!char.IsAsciiHexDigit(c))||value.Info.Sha256!=core.InfoSha256)throw new DomainException("Çekirdek kurulum kaydı geçersiz.",409);
        var binary=MediaPaths.ResolveFile(root,$"{value.Binary.Sha256}/{id}_libretro{(linux?".so":".dll")}");var info=MediaPaths.ResolveFile(root,$"{value.Binary.Sha256}/{id}_libretro.info");
        return value with{Binary=value.Binary with{Path=binary},Info=value.Info with{Path=info}};
    }
    public string? BlockReason(GameRom rom,NativeRuntimeProfile? host)
    {
        if(host?.Enabled!=true)return "Oynamak için sunucu emülasyonunu etkinleştir.";if(!host.InputReady)return NativeRuntimeProfile.InputBlockReason;
        try
        {
            var graphics=host.HeadlessGraphics||gpu?.Prepared==true;var core=catalog.Require(Selection(rom).CoreId);var adapter=RetroArchCoreCatalog.AdapterBlock(core,graphics,host.Bundled);if(adapter is not null)return adapter;
            if(!Compatible(rom.System,core))return "Çekirdek bu oyun sistemiyle eşleşmiyor.";
            if(!NativeSystems.Require(rom.System).Digital&&!graphics)return "Bu sistemin analog, dokunmatik veya özel giriş entegrasyonu henüz hazır değil.";
            if(core.Id!="mgba"&&Installed(core.Id) is null)return "Bu oyun için emülasyon çekirdeğini kur.";
            if(!core.Extensions.Contains(NativeSystems.Extension(rom.FileName)))return "Çekirdek bu oyun biçimini desteklemiyor.";
            if(firmware.Missing(core,rom).Length>0)return "Bu oyun için BIOS dosyaları eksik. Emülasyon ayarlarına ekle.";
            return null;
        }
        catch(Exception error)when(error is DomainException or IOException or JsonException){return "Çekirdek dosyaları kullanılamıyor; ROM ve kayıtlar korundu.";}
    }
    public NativeCoreResolution Resolve(GameRom rom)
    {
        var host=NativeRuntimeProfile.Read(paths)??throw new DomainException("Yerel oyun bileşenleri henüz etkinleştirilmedi.",409);var reason=BlockReason(rom,host);if(reason is not null)throw new DomainException(reason,409);host.Verify();
        var core=catalog.Require(Selection(rom).CoreId);
        if(core.Id=="mgba")return new(host,core,host.Core,host.CoreInfo,host.Version,!host.Bundled&&gpu?.Prepared==true?gpu.Verify():null);
        var installed=Installed(core.Id)??throw new DomainException("Bu oyun için emülasyon çekirdeğini kur.",409);VerifiedFilePublication.Require(installed.Binary.Path,installed.Binary.Bytes,installed.Binary.Sha256);VerifiedFilePublication.Require(installed.Info.Path,installed.Info.Bytes,installed.Info.Sha256);
        var graphics=!host.Bundled&&gpu?.Prepared==true?gpu.Verify():null;
        return new(host,core,installed.Binary,installed.Info,Version(host,core.Id,installed.Binary.Sha256),graphics);
    }
    public static string Version(NativeRuntimeProfile host,string id,string hash,string? gpuHash=null)=>"native-core-"+Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(id+hash+(gpuHash??host.RetroArch.Sha256)+host.MediaMtx.Sha256)))[..40];
    public string CurrentVersion(NativeRuntimeProfile host,string id,string hash)=>Version(host,id,hash);
}
