using System.Text;
using System.Text.RegularExpressions;
using HomeServer.Core;

namespace HomeServer.Gaming;

public sealed record NativeRomProbeSample(long Offset,string Base64);
public sealed record NativeRomProbeRead(string Name,long Offset,int Length);
public sealed record NativeRomProbeFile(string Name,long Bytes,string? PrefixBase64=null,NativeRomProbeSample[]? Samples=null);
public sealed record NativeRomProbeRequest(NativeRomProbeFile[] Files,string Entry);
public sealed record NativeRomProbeResult(string? SystemId,string[] Candidates,string Entry,string[] MissingFiles,string Reason,NativeRomProbeRead[]? Reads=null);

/// <summary>Bounded read-only import hints. Ambiguous disc formats never silently become PS1.</summary>
public static class NativeRomDetection
{
    public const int PrefixBytes=65536,MaximumSampleBytes=8*PrefixBytes,MaximumRequestBytes=1024*1024;
    public static NativeRomProbeResult Detect(NativeRomProbeRequest input)
    {
        if(input.Files is null||input.Files.Length is <1 or >128||input.Files.Any(file=>file is null))throw new DomainException("ROM algılama dosya listesi geçersiz.",400);
        var files=new Dictionary<string,(NativeRomProbeFile File,byte[] Prefix,List<(long Offset,byte[] Data)> Samples)>(StringComparer.OrdinalIgnoreCase);var sampled=0;
        foreach(var file in input.Files)
        {
            if(string.IsNullOrWhiteSpace(file.Name))throw new DomainException("ROM algılama dosya listesi geçersiz.",400);
            NativeSystems.ValidateFileName(file.Name);
            if(file.Bytes is <1 or >64L*1024*1024*1024||file.PrefixBase64?.Length>4*((PrefixBytes+2)/3))throw new DomainException("ROM algılama örneği çok büyük veya geçersiz.",413);
            byte[] prefix;try{prefix=file.PrefixBase64 is null?[]:Convert.FromBase64String(file.PrefixBase64);}catch(FormatException){throw new DomainException("ROM algılama örneği geçersiz.",400);}
            var samples=new List<(long Offset,byte[] Data)>();if(file.Samples?.Length>8)throw new DomainException("ROM algılama örneği çok büyük veya geçersiz.",400);
            foreach(var sample in file.Samples??[])
            {
                if(sample is null||sample.Offset<0||sample.Offset>=file.Bytes||sample.Base64 is null||sample.Base64.Length>4*((PrefixBytes+2)/3))throw new DomainException("ROM algılama örneği çok büyük veya geçersiz.",400);
                byte[] data;try{data=Convert.FromBase64String(sample.Base64);}catch(FormatException){throw new DomainException("ROM algılama örneği geçersiz.",400);}if(data.Length is <1 or >PrefixBytes||data.Length>file.Bytes-sample.Offset)throw new DomainException("ROM algılama örneği çok büyük veya geçersiz.",400);sampled+=data.Length;samples.Add((sample.Offset,data));
            }
            sampled+=prefix.Length;if(prefix.Length>PrefixBytes||prefix.Length>file.Bytes||sampled>MaximumSampleBytes||!files.TryAdd(file.Name,(file,prefix,samples)))throw new DomainException("ROM algılama örneği çok büyük veya geçersiz.",400);
        }
        if(string.IsNullOrEmpty(input.Entry))return new(null,[],"",[],"choose-entry");
        NativeSystems.ValidateFileName(input.Entry);if(!files.TryGetValue(input.Entry,out var entry))throw new DomainException("Disk başlangıç dosyası pakette yok.",422);
        var extension=NativeSystems.Extension(entry.File.Name);var candidates=NativeBasicSelection.Order(NativeSystems.All.Where(system=>system.Extensions.Contains(extension))).Select(system=>system.Id).ToArray();
        var missing=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var item in files.Values.Where(item=>NativeSystems.Extension(item.File.Name)=="cue"))
        {
            if(item.File.Bytes>PrefixBytes)throw new DomainException("Disk listesi çok büyük.",413);
            if(item.Prefix.Length!=item.File.Bytes)continue;
            using var content=new MemoryStream(item.Prefix);using var reader=new StreamReader(content,Encoding.UTF8,true);
            foreach(var reference in NativeRomLibrary.CueReferences(reader.ReadToEnd().TrimStart('\ufeff')))if(!files.ContainsKey(reference))missing.Add(reference);
        }
        string? identified=null;
        // Generic upstream adapters may advertise a cartridge extension for several systems.
        // Conventional explicit file types remain useful hints; upload header checks are independent.
        var conventional=extension switch{"gba"=>"gba","gb" or "dmg" or "sgb"=>"gb","gbc" or "cgb"=>"gbc","nes" or "unf" or "unif"=>"nes","smc" or "sfc" or "swc" or "fig"=>"snes","nds"=>"nds","3ds" or "cci" or "cxi" or "3dsx"=>"3ds",_=>null};
        if(conventional is not null&&candidates.Contains(conventional))identified=conventional;
        if(entry.Prefix.Length>=0x110)
        {
            var console=Encoding.ASCII.GetString(entry.Prefix,0x100,16);
            var sega=console.StartsWith("SEGA 32X",StringComparison.Ordinal)||console.StartsWith("SEGA MARS",StringComparison.Ordinal)?"sega32x":console.StartsWith("SEGA MEGA DRIVE",StringComparison.Ordinal)||console.StartsWith("SEGA GENESIS",StringComparison.Ordinal)?"megadrive":null;
            if(sega is not null&&candidates.Contains(sega))identified=sega;
        }
        // Only structured, console-specific content markers. A filename/title, CUE+BIN, MODE2 or
        // PLAYSTATION volume label alone is not enough: PlayStation 2 CD media can share those.
        var reads=new List<NativeRomProbeRead>();
        foreach(var item in files.Values.Where(item=>NativeSystems.Extension(item.File.Name) is "bin" or "iso" or "img"))
        {
            var text=Encoding.ASCII.GetString(item.Prefix);
            string? signature=Regex.IsMatch(text,@"(?im)^\s*BOOT2\s*=\s*cdrom0?:",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100))?"ps2":
                Regex.IsMatch(text,@"(?im)^\s*BOOT\s*=\s*cdrom0?:",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100))?"ps1":
                text.StartsWith("SEGA SEGASATURN",StringComparison.Ordinal)||text.Length>=32&&text.AsSpan(16).StartsWith("SEGA SEGASATURN",StringComparison.Ordinal)?"saturn":null;
            signature??=NativeDiscDetection.Identify(item.File,item.Prefix,item.Samples,reads);
            if(signature is not null&&candidates.Contains(signature)){if(identified is not null&&identified!=signature)return new(null,candidates,entry.File.Name,missing.ToArray(),"ambiguous");identified=signature;}
        }
        if(identified is not null)return new(identified,candidates,entry.File.Name,missing.ToArray(),"header");
        if(missing.Count==0&&reads.Count>0&&reads.Sum(read=>read.Length)+sampled<=MaximumSampleBytes)return new(null,candidates,entry.File.Name,[],"reading-disc",reads.Distinct().Take(4).ToArray());
        if(candidates.Length==1)return new(candidates[0],candidates,entry.File.Name,missing.ToArray(),"extension");
        return new(null,candidates,entry.File.Name,missing.ToArray(),candidates.Length==0?"unsupported":"ambiguous");
    }
}
