using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HomeServer.Core;

namespace HomeServer.Gaming;
public sealed record CoreFirmware(string Path,string Description,bool Optional);
public sealed record RetroArchCore(string Id,string Name,string DisplayName,string System,string SystemName,string[] Databases,string[] Extensions,string License,string[] Categories,bool Hardware,string HardwareApi,bool SaveState,bool DiskControl,bool Experimental,string Version,CoreFirmware[] Firmware,string Notes,string Description,bool WindowsBuild,string Info,string InfoSha256);
public sealed record RetroArchCatalog(int Format,string Commit,string ReviewedAt,string Source,string BuildSource,RetroArchCore[] Entries);
public sealed class RetroArchCoreCatalog
{
    public const string Commit="5a74858ab2f7a50cebb5a6330895bc38899531c0";
    public RetroArchCatalog Data {get;}
    public RetroArchCoreCatalog(IWebHostEnvironment environment)
    {
        var path=Path.Combine(environment.ContentRootPath,"NativeCoreCatalog","catalog.json");
        if(!File.Exists(path))path=Path.Combine(AppContext.BaseDirectory,"NativeCoreCatalog","catalog.json");
        if(new FileInfo(path).Length>8*1024*1024)throw new DomainException("Çekirdek kataloğu geçersiz.",409);
        Data=JsonSerializer.Deserialize<RetroArchCatalog>(File.ReadAllText(path),new JsonSerializerOptions(JsonSerializerDefaults.Web))??throw new DomainException("Çekirdek kataloğu geçersiz.",409);
        if(Data.Format!=1||Data.Commit!=Commit||Data.Entries.Length is <1 or >512||Data.Entries.Select(core=>core.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=Data.Entries.Length)throw new DomainException("Çekirdek kataloğu geçersiz.",409);
        foreach(var core in Data.Entries)
            if(core.Id.Length is <1 or >80||core.Id.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c is not('-' or '_'))||core.Info.Length>64*1024||Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(core.Info)))!=core.InfoSha256)throw new DomainException("Çekirdek kataloğu geçersiz.",409);
        NativeSystems.RegisterOfficial(Data.Entries);
    }
    public RetroArchCore Require(string id)=>Data.Entries.FirstOrDefault(core=>core.Id==id)??throw new DomainException("Emülasyon çekirdeği bulunamadı.",404);
    // A .info capability is not enough to authorize a core that writes firmware/disks or needs special input.
    public static readonly string[] SoftwareAdapters=["mgba","fceumm","nestopia","quicknes","mesen","snes9x","snes9x2010","snes9x2005","snes9x2002","bsnes","bsnes2014_accuracy","bsnes2014_balanced","bsnes2014_performance","genesis_plus_gx","picodrive","pcsx_rearmed","mednafen_psx","mednafen_saturn","mednafen_pce_fast","mednafen_pce","mednafen_supergrafx","stella","stella2014","prosystem","handy","mednafen_lynx","virtualjaguar","mednafen_ngp","mednafen_wswan","mednafen_vb","gearcoleco","pokemini","gambatte","sameboy","gearboy","gearsystem"];
    public static string? AdapterBlock(RetroArchCore core,bool graphicsReady=false,bool linux=false)
    {
        if(!core.Categories.Contains("Emulator"))return "Bu çekirdek bir emülasyon çekirdeği değil.";
        if(linux&&!NativeBasicSelection.CoreSystems(core.Id).Any()&&core.Id!="citra")return "Bu çekirdeğin Linux sunucu desteği henüz hazır değil.";
        if(!linux&&!core.WindowsBuild&&core.Id!="mgba")return "Bu çekirdeğin Windows paketi resmi katalogda yok.";
        if(linux&&core.Hardware&&NativeGpuRuntime.Renderer(core)=="d3d11")return "Bu çekirdek Windows grafik API’sini gerektirir.";
        if(core.Hardware&&(!graphicsReady||NativeGpuRuntime.Renderer(core) is null))return "Bu çekirdek için GPU görüntü yayını entegrasyonu henüz hazır değil.";
        if(!core.SaveState&&!graphicsReady)return "Bu çekirdeğin kalıcı kayıt yolu henüz Home’a bağlanmadı.";
        if(!core.Hardware&&!SoftwareAdapters.Contains(core.Id)&&!graphicsReady)return "Bu çekirdeğin özel giriş veya dosya gereksinimleri henüz Home’a bağlanmadı.";
        return null;
    }
}
