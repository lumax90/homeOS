using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using HomeServer.Core;
using HomeServer.Infrastructure;
using HomeServer.Operations;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;

namespace HomeServer.Gaming;
public sealed record NativeFirmwareFile(string Name,long Bytes,string Sha256);
/// <summary>Private, immutable user firmware uploads. No BIOS provider/downloader.</summary>
public sealed class NativeFirmwareLibrary(HomePaths paths):IDisposable
{
    public const long MaximumBytes=64*1024*1024;
    private readonly SemaphoreSlim _gate=new(1,1);
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    private string Root(string user,string core)=>Path.Combine(paths.State,"games","firmware",user,core);
    public NativeFirmwareFile[] Files(string user,string core)
    {
        var root=Root(user,core);var path=Path.Combine(root,"files.json");if(!File.Exists(path))return [];
        path=MediaPaths.ResolveFile(root,"files.json");if(new FileInfo(path).Length>1024*1024)throw new DomainException("BIOS kayıt dosyası geçersiz.",409);
        var files=JsonSerializer.Deserialize<NativeFirmwareFile[]>(File.ReadAllText(path),Json)??throw new DomainException("BIOS kayıt dosyası geçersiz.",409);
        if(files.Length>2048||files.Select(file=>file.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=files.Length||files.Any(file=>!SafePath(file.Name)||file.Bytes<1||file.Bytes>Limit(file.Name)||file.Sha256 is not{Length:64}||file.Sha256.Any(c=>!char.IsAsciiHexDigit(c))))throw new DomainException("BIOS kayıt dosyası geçersiz.",409);return files;
    }
    public static bool SafePath(string name)
    {
        if(name.Length is <1 or >400||name.Contains('\\')||name.Split('/').Length>20)return false;
        if(new[]{".exe",".dll",".sys",".bat",".cmd",".ps1",".sh",".lnk"}.Contains(Path.GetExtension(name),StringComparer.OrdinalIgnoreCase))return false;
        return name.Split('/').All(NativeSystems.SafeSegment);
    }
    public static CoreFirmware[] Requirements(RetroArchCore core)=>core.Id=="cemu"?[..core.Firmware,new("Cemu/keys.txt","Disc keys for WUD/WUX images",true)]:core.Id=="pcee2"?core.Firmware.Select(file=>file.Path=="pcsx2/resources"?file with{Optional=true}:file).ToArray():core.Firmware;
    public static string[] UploadRoots(RetroArchCore core)=>ResourceRoots(core.Id).Concat(Requirements(core).Where(file=>!Path.HasExtension(file.Path)).Select(file=>file.Path)).Distinct().ToArray();
    public static bool IsDeclared(RetroArchCore core,string name)=>SafePath(name)&&(Requirements(core).Any(file=>file.Path==name&&Path.HasExtension(file.Path)||!Path.HasExtension(file.Path)&&name.StartsWith(file.Path+"/",StringComparison.Ordinal))||ResourceRoots(core.Id).Any(root=>name.StartsWith(root+"/",StringComparison.Ordinal)));
    public static string[] ResourceRoots(string core)=>core switch{"dolphin" or "ishiiruka"=>["dolphin-emu/Sys"],"pcsx2" or "pcee2"=>["pcsx2/bios","pcsx2/resources"],"ppsspp"=>["PPSSPP"],"citra" or "citra2018" or "citra_canary"=>["Citra/sysdata","Citra/nand"],"azahar"=>["Azahar/sysdata","Azahar/nand"],"cemu"=>["Cemu/graphicPacks","Cemu/resources/sharedFonts"],_=>[]};
    private static long Limit(string name)=>name.EndsWith(".qcow2",StringComparison.OrdinalIgnoreCase)?64L*1024*1024*1024:name.Contains("nand",StringComparison.OrdinalIgnoreCase)||name=="dsi_sd_card.bin"?16L*1024*1024*1024:MaximumBytes;
    public string[] Missing(RetroArchCore core,GameRom rom)
    {
        var files=Files(rom.UserId,core.Id);var required=Requirements(core).Where(file=>!file.Optional).Select(file=>file.Path).ToList();
        if(core.Id=="cemu"&&NativeSystems.Extension(rom.FileName) is "wud" or "wux")required.Add("Cemu/keys.txt");
        if(core.Id=="fceumm"&&NativeSystems.Extension(rom.FileName)=="fds")required.Add("disksys.rom");
        if(rom.System=="segacd"&&!files.Any(file=>new[]{"bios_CD_E.bin","bios_CD_U.bin","bios_CD_J.bin"}.Contains(file.Name)))required.Add("bios_CD_E.bin / bios_CD_U.bin / bios_CD_J.bin");
        if(rom.System=="pcenginecd")required.Add("syscard3.pce");
        if(core.Id is "swanstation" or "mednafen_psx" or "mednafen_psx_hw" or "duckstation")
        {
            string[] alternatives=["scph5500.bin","scph5501.bin","scph5502.bin","psxonpsp660.bin","ps1_rom.bin"];
            required.RemoveAll(alternatives.Contains);if(!files.Any(file=>alternatives.Contains(file.Name)&&File.Exists(Path.Combine(Root(rom.UserId,core.Id),file.Sha256,"payload.bin"))))required.Add("scph5500.bin / scph5501.bin / scph5502.bin");
        }
        if(core.Id=="kronos")required.Remove(NativeSystems.Extension(rom.FileName)=="zip"?"kronos/saturn_bios.bin":"kronos/stvbios.zip");
        if(core.Id=="pcee2")required.Remove("pcsx2/resources"); // Current upstream embeds these optional resources.
        return required.Distinct().Where(name=>!files.Any(file=>(file.Name==name||!Path.HasExtension(name)&&file.Name.StartsWith(name+"/",StringComparison.Ordinal))&&File.Exists(Path.Combine(Root(rom.UserId,core.Id),file.Sha256,"payload.bin")))).ToArray();
    }
    public async Task<NativeFirmwareFile> ImportAsync(RetroArchCore core,string name,Stream input,long? length,Account actor,MediaStore store,string login,CancellationToken token)
    {
        store.RequireRomImportActor(actor,login);if(!actor.IsAdmin)throw new DomainException("BIOS eklemek sahip hesabını gerektirir.",403);
        if(!IsDeclared(core,name))throw new DomainException("BIOS adı çekirdeğin resmi dosya listesinde yok.",400);
        if(length<1||length>Limit(name))throw new DomainException("BIOS dosyası boyut sınırını aşıyor.",413);
        if(!await _gate.WaitAsync(0,token))throw new DomainException("Başka bir BIOS ekleniyor.",409);
        string? stage=null;
        try
        {
            var id=Guid.NewGuid().ToString("N");stage=AppFiles.DirectoryIn(paths.State,"games","firmware-staging",id);var path=Path.Combine(stage,"payload.bin");
            using var digest=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);using var md5=IncrementalHash.CreateHash(HashAlgorithmName.MD5);
            long total=0;await using(var output=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,FileOptions.Asynchronous))
            {var buffer=new byte[65536];int read;while((read=await input.ReadAsync(buffer,token))>0){total+=read;if(total>Limit(name))throw new DomainException("BIOS dosyası boyut sınırını aşıyor.",413);digest.AppendData(buffer.AsSpan(0,read));md5.AppendData(buffer.AsSpan(0,read));await output.WriteAsync(buffer.AsMemory(0,read),token);}if(total<1||length is{} expected&&total!=expected)throw new DomainException("BIOS aktarımı tamamlanmadı.",422);await output.FlushAsync(token);output.Flush(true);}
            var expectedHash=Regex.Match(core.Notes,Regex.Escape(name)+@" \(md5\):\s*([a-fA-F0-9]{32})");if(expectedHash.Success&&!expectedHash.Groups[1].Value.Equals(Convert.ToHexStringLower(md5.GetHashAndReset()),StringComparison.OrdinalIgnoreCase))throw new DomainException("BIOS dosyası resmi bütünlük bilgisiyle eşleşmiyor.",422);
            var hash=Convert.ToHexStringLower(digest.GetHashAndReset());var files=Files(actor.Id,core.Id);var old=files.FirstOrDefault(file=>file.Name==name);if(old is not null){if(old.Sha256!=hash)throw new DomainException("Bu BIOS adı için farklı dosya mevcut; mevcut dosya korunur.",409);return old;}
            if(files.Length>=2048)throw new DomainException("BIOS dosyası sınırına ulaşıldı.",409);store.RequireRomImportActor(actor,login);token.ThrowIfCancellationRequested();
            var root=AppFiles.DirectoryIn(paths.State,"games","firmware",actor.Id,core.Id);var destination=AppFiles.DirectoryIn(root,hash);await VerifiedFilePublication.StageAsync(path,destination,"payload.bin",id,total,hash,token);VerifiedFilePublication.Promote(destination,"payload.bin",id,total,hash);
            store.RequireRomImportActor(actor,login);var value=new NativeFirmwareFile(name,total,hash);PackageTrust.AtomicJson(Path.Combine(root,"files.json"),files.Append(value).ToArray());return value;
        }
        finally{if(stage is not null&&Directory.Exists(stage))AppFiles.DeleteOwnedDirectory(stage);_gate.Release();}
    }
    public async Task StageAsync(RetroArchCore core,GameRom rom,string destination,CancellationToken token)
    {
        foreach(var file in Files(rom.UserId,core.Id).Where(file=>IsDeclared(core,file.Name)))
        {
            var source=MediaPaths.ResolveFile(Root(rom.UserId,core.Id),$"{file.Sha256}/payload.bin");VerifiedFilePublication.Require(source,file.Bytes,file.Sha256);
            var directory=destination;var parts=file.Name.Split('/');if(parts[0] is "Citra" or "Azahar")directory=Path.Combine(Path.GetDirectoryName(destination)!,"saves");foreach(var part in parts[..^1]){directory=Path.Combine(directory,part);Directory.CreateDirectory(directory);AppFiles.RejectLink(directory);}
            var target=Path.Combine(directory,parts[^1]);if(File.Exists(target)&&(parts[0] is "Citra" or "Azahar"||file.Name is "xbox_hdd.qcow2" or "xbox_eeprom.bin" or "dsi_nand.bin" or "dsi_sd_card.bin")){AppFiles.RejectLink(target);continue;}
            var id=Guid.NewGuid().ToString("N");await VerifiedFilePublication.StageAsync(source,directory,parts[^1],id,file.Bytes,file.Sha256,token);VerifiedFilePublication.Promote(directory,parts[^1],id,file.Bytes,file.Sha256);
        }
    }
    public async Task<int> ImportPackAsync(RetroArchCore core,Stream body,string boundary,string prefix,Account actor,MediaStore store,string login,CancellationToken token)
    {
        if(boundary.Length is <1 or >200||prefix.Length>400||prefix.Length>0&&!SafePath(prefix))throw new DomainException("Sistem paketi dosya listesi geçersiz.");
        var reader=new MultipartReader(boundary,body){HeadersCountLimit=8,HeadersLengthLimit=4096,BodyLengthLimit=64L*1024*1024*1024};var count=0;long total=0;MultipartSection? section;
        while((section=await reader.ReadNextSectionAsync(token)) is not null)
        {
            if(++count>2048||!ContentDispositionHeaderValue.TryParse(section.ContentDisposition,out var disposition)||disposition.DispositionType!="form-data"||!disposition.FileName.HasValue)throw new DomainException("Sistem paketi dosya listesi geçersiz.");
            var name=HeaderUtilities.RemoveQuotes(disposition.FileNameStar.HasValue?disposition.FileNameStar:disposition.FileName).Value??"";name=prefix.Length>0?prefix.TrimEnd('/')+"/"+name:name;
            var file=await ImportAsync(core,name,section.Body,null,actor,store,login,token);total+=file.Bytes;if(total>64L*1024*1024*1024)throw new DomainException("BIOS dosyası boyut sınırını aşıyor.",413);
        }
        if(count==0)throw new DomainException("Sistem paketi boş.");return count;
    }
    public void Dispose()=>_gate.Dispose();
}
