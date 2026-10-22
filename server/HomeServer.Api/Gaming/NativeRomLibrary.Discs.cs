using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HomeServer.Core;
using HomeServer.Infrastructure;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;

namespace HomeServer.Gaming;
public sealed record NativeDiscFile(string Name,long Bytes,string Sha256);
public sealed record NativeDiscManifest(int Format,string System,string Entry,NativeDiscFile[] Files);
public sealed partial class NativeRomLibrary
{
    private static readonly JsonSerializerOptions DiscJson=new(JsonSerializerDefaults.Web);
    private static readonly string[] DiscExtensions=["cue","m3u","gdi","bin","img","iso","chd","pbp","wav","ogg","mp3","flac","sub","ccd","cso","zso","gcm","rvz","gcz","ciso","wia","wbfs","cdi","adf","adz","d64","d71","d81","g64","dsk","hdf","hdi","hdm","ima","hfv","vhd","vfd","st","stx","msa"];
    private static readonly string[] WritableImageExtensions=["img","adf","adz","d64","d71","d81","g64","dsk","hdf","hdi","hdm","ima","hfv","vhd","vfd","st","stx","msa"];
    public static bool UsesWritableMedia(GameRom rom)=>(rom.System.StartsWith("commodore_",StringComparison.Ordinal)||new[]{"dos","atari_st","pc_88","pc_98","pcxt","sharp_x68000","msx","cpc","zx_spectrum","mac68k","apple_ii","bbcmicro"}.Contains(rom.System))&&(WritableImageExtensions.Contains(NativeSystems.Extension(rom.FileName))||NativeSystems.Extension(rom.FileName)=="m3u");
    public async Task<GameRom> ImportDiscAsync(Stream input,string boundary,string system,string entry,Account actor,string login,CancellationToken token)
    {
        NativeSystems.ValidateName(system,entry);if(NativeSystems.Extension(entry) is not("cue" or "m3u" or "gdi"))throw new DomainException("Disk paketi için CUE, M3U veya GDI başlangıç dosyası seç.",400);
        if(boundary.Length is <1 or >200)throw new DomainException("ROM aktarım biçimi geçersiz.",400);store.RequireRomImportActor(actor,login);
        if(!await _upload.WaitAsync(0,token))throw new DomainException("Başka bir ROM ekleniyor. Biraz bekleyip yeniden dene.",409);
        using var budget=CancellationTokenSource.CreateLinkedTokenSource(token);budget.CancelAfter(TimeSpan.FromHours(2));token=budget.Token;string? stage=null;
        try
        {
            var operation=Guid.NewGuid().ToString("N");stage=AppFiles.DirectoryIn(paths.State,"games","rom-staging",operation);var files=new List<NativeDiscFile>();long total=0;var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var reader=new MultipartReader(boundary,input){HeadersCountLimit=8,HeadersLengthLimit=4096,BodyLengthLimit=NativeSystems.Require(system).MaximumBytes};MultipartSection? section;
            while((section=await reader.ReadNextSectionAsync(token)) is not null)
            {
                if(files.Count>=128||!ContentDispositionHeaderValue.TryParse(section.ContentDisposition,out var disposition)||disposition.DispositionType!="form-data"||!disposition.FileName.HasValue&&!disposition.FileNameStar.HasValue)throw new DomainException("Disk paketi dosya listesi geçersiz.",400);
                var name=HeaderUtilities.RemoveQuotes(disposition.FileNameStar.HasValue?disposition.FileNameStar:disposition.FileName).Value??"";NativeSystems.ValidateFileName(name);if(!DiscExtensions.Contains(NativeSystems.Extension(name))||!names.Add(name))throw new DomainException("Disk paketinde geçersiz veya yinelenen dosya var.",400);
                var path=Path.Combine(stage,name);long bytes=0;using var digest=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                await using(var output=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,FileOptions.Asynchronous))
                {var buffer=new byte[65536];int read;while((read=await section.Body.ReadAsync(buffer,token))>0){bytes+=read;total+=read;if(total>NativeSystems.Require(system).MaximumBytes||NativeSystems.Extension(name) is "cue" or "m3u" or "gdi"&&bytes>64*1024)throw new DomainException("Disk paketi boyut sınırını aşıyor.",413);digest.AppendData(buffer.AsSpan(0,read));await output.WriteAsync(buffer.AsMemory(0,read),token);}await output.FlushAsync(token);output.Flush(true);}
                if(bytes<1)throw new DomainException("Disk paketinde boş dosya var.",422);files.Add(new(name,bytes,Convert.ToHexStringLower(digest.GetHashAndReset())));
            }
            var manifest=new NativeDiscManifest(1,system,entry,files.OrderBy(file=>file.Name,StringComparer.Ordinal).ToArray());await ValidateDiscReferencesAsync(manifest,stage,token);
            var manifestBytes=JsonSerializer.SerializeToUtf8Bytes(manifest,DiscJson);var hash=Convert.ToHexStringLower(SHA256.HashData(manifestBytes));var root=AppFiles.DirectoryIn(paths.State,"games","roms",system,hash);
            foreach(var file in manifest.Files){await VerifiedFilePublication.StageAsync(Path.Combine(stage,file.Name),root,file.Name,operation,file.Bytes,file.Sha256,token);VerifiedFilePublication.Promote(root,file.Name,operation,file.Bytes,file.Sha256);}
            var descriptor=Path.Combine(stage,"disc.json");await File.WriteAllBytesAsync(descriptor,manifestBytes,token);await VerifiedFilePublication.StageAsync(descriptor,root,"disc.json",operation,manifestBytes.Length,hash,token);store.RequireRomImportActor(actor,login);token.ThrowIfCancellationRequested();VerifiedFilePublication.Promote(root,"disc.json",operation,manifestBytes.Length,hash);
            return store.RegisterGameRom(entry,hash,total,actor,login,system);
        }
        finally{if(stage is not null&&Directory.Exists(stage))AppFiles.DeleteOwnedDirectory(stage);_upload.Release();}
    }
    public static async Task ValidateDiscReferencesAsync(NativeDiscManifest manifest,string root,CancellationToken token)
    {
        if(manifest.Format!=1||manifest.Files.Length is <1 or >128||manifest.Files.Select(file=>file.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=manifest.Files.Length)throw new DomainException("Disk paketi manifesti geçersiz.",409);
        var files=manifest.Files.ToDictionary(file=>file.Name,StringComparer.OrdinalIgnoreCase);if(!files.ContainsKey(manifest.Entry))throw new DomainException("Disk başlangıç dosyası pakette yok.",422);
        var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var visiting=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        async Task Walk(string name)
        {
            token.ThrowIfCancellationRequested();try{NativeSystems.ValidateFileName(name);}catch(DomainException){throw new DomainException("Disk dosyası referansı paket dışında veya eksik.",422);}
            if(!files.TryGetValue(name,out var file)||!DiscExtensions.Contains(NativeSystems.Extension(name)))throw new DomainException("Disk dosyası referansı paket dışında veya eksik.",422);
            if(visiting.Contains(name))throw new DomainException("Disk listesi döngü içeriyor.",422);if(!seen.Add(name))return;
            var ext=NativeSystems.Extension(name);if(ext is not("cue" or "m3u" or "gdi"))return;
            if(file.Bytes>64*1024)throw new DomainException("Disk listesi çok büyük.",413);visiting.Add(name);
            var text=await File.ReadAllTextAsync(MediaPaths.ResolveFile(root,file.Name),Encoding.UTF8,token);var references=new List<string>();
            if(ext=="m3u")
            {
                var lines=text.Split('\n').Select(line=>line.Trim()).Where(line=>line.Length>0).ToArray();
                if(lines.Any(line=>line.StartsWith('#')&&line.Contains(':')))throw new DomainException("Disk listesinde özel komut kullanılamaz.",422);
                references.AddRange(lines.Where(line=>!line.StartsWith('#')));
            }
            else if(ext=="cue")
            {
                references.AddRange(CueReferences(text));
            }
            else
            {
                var lines=text.Split('\n',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries);if(lines.Length<2||!int.TryParse(lines[0],out var tracks)||tracks!=lines.Length-1)throw new DomainException("GDI disk listesi geçersiz.",422);
                foreach(var line in lines.Skip(1)){var match=Regex.Match(line,"^\\d+\\s+\\d+\\s+\\d+\\s+\\d+\\s+(?:\"([^\"]+)\"|([^\\s]+))\\s+\\d+$");if(!match.Success)throw new DomainException("GDI dosyası referansı geçersiz.",422);references.Add(match.Groups[1].Success?match.Groups[1].Value:match.Groups[2].Value);}
            }
            if(references.Count is <1 or >128)throw new DomainException("Disk listesi boş veya çok büyük.",422);
            foreach(var reference in references){var extension=NativeSystems.Extension(reference);if(ext=="m3u"&&!WritableImageExtensions.Contains(extension)&&extension is not("cue" or "chd" or "iso" or "pbp" or "gdi" or "cso" or "zso" or "gcm" or "rvz" or "gcz" or "ciso" or "wia" or "wbfs" or "cdi"))throw new DomainException("Disk listesi desteklenmeyen dosya içeriyor.",422);await Walk(reference);}
            visiting.Remove(name);
        }
        await Walk(manifest.Entry);if(seen.Count!=files.Count)throw new DomainException("Disk paketinde kullanılmayan dosya var.",422);
    }
    public static string[] CueReferences(string text)
    {
        if(Regex.IsMatch(text,@"^\s*CDTEXTFILE\b",RegexOptions.IgnoreCase|RegexOptions.Multiline))throw new DomainException("CUE dosyasında dış metin referansı kullanılamaz.",422);
        var references=new List<string>();
        foreach(var line in text.Split('\n').Where(line=>Regex.IsMatch(line,@"^\s*FILE(?:\s|$)",RegexOptions.IgnoreCase)))
        {
            var match=Regex.Match(line,"^\\s*FILE\\s+(?:\"([^\"]+)\"|([^\\s]+))\\s+(?:BINARY|WAVE|MP3)\\s*$",RegexOptions.IgnoreCase);if(!match.Success)throw new DomainException("CUE dosyası referansı geçersiz.",422);
            var reference=match.Groups[1].Success?match.Groups[1].Value:match.Groups[2].Value;try{NativeSystems.ValidateFileName(reference);}catch(DomainException){throw new DomainException("Disk dosyası referansı paket dışında veya eksik.",422);}references.Add(reference);
        }
        if(references.Count is <1 or >128)throw new DomainException("Disk listesi boş veya çok büyük.",422);return references.ToArray();
    }
    public async Task<string> StageContentAsync(GameRom rom,string destination,CancellationToken token,bool canonicalizeCueTracks=false)
    {
        var name=NativeSystems.ContentName(rom);
        if(NativeSystems.Extension(rom.FileName) is not("cue" or "m3u" or "gdi"))
        {await using var source=OpenVerified(rom);await using var output=new FileStream(Path.Combine(destination,name),FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,FileOptions.Asynchronous);await source.CopyToAsync(output,token);return name;}
        var root=Path.Combine(paths.State,"games","roms",rom.System,rom.Sha256);var path=MediaPaths.ResolveFile(root,"disc.json");if(new FileInfo(path).Length>64*1024)throw new DomainException("Disk paketi manifesti geçersiz.",409);var bytes=await File.ReadAllBytesAsync(path,token);
        if(Convert.ToHexStringLower(SHA256.HashData(bytes))!=rom.Sha256)throw new DomainException("Disk paketi bütünlüğü değişti.",409);
        var manifest=JsonSerializer.Deserialize<NativeDiscManifest>(bytes,DiscJson)??throw new DomainException("Disk paketi manifesti geçersiz.",409);
        if(manifest.System!=rom.System||manifest.Entry!=rom.FileName||manifest.Files.Sum(file=>file.Bytes)!=rom.SizeBytes)throw new DomainException("Disk paketi manifesti geçersiz.",409);
        foreach(var file in manifest.Files){NativeSystems.ValidateFileName(file.Name);if(file.Bytes<1||file.Sha256 is not{Length:64}||file.Sha256.Any(c=>!char.IsAsciiHexDigit(c)))throw new DomainException("Disk paketi manifesti geçersiz.",409);VerifiedFilePublication.Require(MediaPaths.ResolveFile(root,file.Name),file.Bytes,file.Sha256);}
        await ValidateDiscReferencesAsync(manifest,root,token);
        var canonical=manifest.Files.ToDictionary(file=>file.Name,file=>file.Name==rom.FileName?name:file.Name,StringComparer.OrdinalIgnoreCase);
        if(canonicalizeCueTracks&&NativeSystems.Extension(rom.FileName)=="cue")
        {
            // PCSX's first-track fast path expects the CUE and binary basenames to agree.
            // Only owned runtime copies are renamed; the imported manifest and source bytes stay intact.
            var references=CueReferences(await File.ReadAllTextAsync(MediaPaths.ResolveFile(root,rom.FileName),Encoding.UTF8,token)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            for(var index=0;index<references.Length;index++)canonical[references[index]]=(index==0?"game":"game-track-"+(index+1).ToString("D3"))+"."+NativeSystems.Extension(references[index]);
        }
        if(canonical.Values.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=canonical.Count)throw new DomainException("Disk dosyası adı başlangıç dosyasıyla çakışıyor.",409);
        foreach(var file in manifest.Files){var target=canonical[file.Name];var operation=Guid.NewGuid().ToString("N");await VerifiedFilePublication.StageAsync(MediaPaths.ResolveFile(root,file.Name),destination,target,operation,file.Bytes,file.Sha256,token);VerifiedFilePublication.Promote(destination,target,operation,file.Bytes,file.Sha256);}
        // Normalize only the owned runtime copy, never the imported manifest/source. Windows-authored
        // descriptors often differ only in case; matching exact staged filenames also works on case-sensitive hosts.
        foreach(var file in manifest.Files.Where(file=>NativeSystems.Extension(file.Name) is "cue" or "m3u" or "gdi"))
        {
            var target=MediaPaths.ResolveFile(destination,canonical[file.Name]);var text=await File.ReadAllTextAsync(target,Encoding.UTF8,token);var ext=NativeSystems.Extension(file.Name);
            var normalized=string.Join('\n',text.Split('\n').Select(line=>
            {
                if(ext=="m3u"){var reference=line.Trim();return reference.Length>0&&!reference.StartsWith('#')&&canonical.TryGetValue(reference,out var actual)?line.Replace(reference,actual,StringComparison.Ordinal):line;}
                var pattern=ext=="cue"?"^\\s*FILE\\s+(?:\"([^\"]+)\"|([^\\s]+))\\s+(?:BINARY|WAVE|MP3)\\s*$":"^\\d+\\s+\\d+\\s+\\d+\\s+\\d+\\s+(?:\"([^\"]+)\"|([^\\s]+))\\s+\\d+\\s*$";
                var match=Regex.Match(line,pattern,RegexOptions.IgnoreCase);if(!match.Success)return line;var value=match.Groups[1].Success?match.Groups[1]:match.Groups[2];return canonical.TryGetValue(value.Value,out var actualName)?line[..value.Index]+actualName+line[(value.Index+value.Length)..]:line;
            }));
            if(normalized!=text)await File.WriteAllTextAsync(target,normalized,new UTF8Encoding(false),token);
        }
        return name;
    }
    public async Task<string> PrepareWritableContentAsync(GameRom rom,string workspace,string content,CancellationToken token)
    {
        if(!UsesWritableMedia(rom))return content;
        string[] names=[content];
        if(NativeSystems.Extension(rom.FileName)=="m3u")
        {
            var path=MediaPaths.ResolveFile(paths.State,$"games/roms/{rom.System}/{rom.Sha256}/disc.json");var data=await File.ReadAllBytesAsync(path,token);
            if(data.Length>64*1024||Convert.ToHexStringLower(SHA256.HashData(data))!=rom.Sha256)throw new DomainException("Disk paketi bütünlüğü değişti.",409);
            var manifest=JsonSerializer.Deserialize<NativeDiscManifest>(data,DiscJson)??throw new DomainException("Disk paketi manifesti geçersiz.",409);
            names=manifest.Files.Select(file=>file.Name==manifest.Entry?content:file.Name).ToArray();
        }
        var directory=AppFiles.DirectoryIn(workspace,"saves","writable-media");
        foreach(var name in names)
        {
            NativeSystems.ValidateFileName(name);var destination=Path.Combine(directory,name);
            if(File.Exists(destination)){AppFiles.RejectLink(destination);continue;} // Already restored, integrity-bound to this game's checkpoint.
            var source=MediaPaths.ResolveFile(workspace,name);await using var input=File.OpenRead(source);await using var output=new FileStream(destination,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,FileOptions.Asynchronous);
            await input.CopyToAsync(output,token);await output.FlushAsync(token);output.Flush(true);
        }
        return Path.Combine("saves","writable-media",content);
    }
}
