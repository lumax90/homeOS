using System.Security.Cryptography;
using System.Text.Json;
using HomeServer.Core;
using HomeServer.Infrastructure;

namespace HomeServer.Gaming;

public sealed partial class NativeRomLibrary
{
    public async Task WithContentLockAsync(Func<CancellationToken,Task> change,CancellationToken token)
    {await _upload.WaitAsync(token);try{await change(token);}finally{_upload.Release();}}
    public GameRemovalFile[] RemovalFiles(GameRom rom)
    {
        if(NativeSystems.Extension(rom.FileName) is not("cue" or "m3u" or "gdi"))return[new(NativeSystems.ContentName(rom),rom.SizeBytes,rom.Sha256)];
        var path=MediaPaths.ResolveFile(paths.State,$"games/roms/{rom.System}/{rom.Sha256}/disc.json");
        if(new FileInfo(path).Length>64*1024)throw new DomainException("Disk kaldırma manifesti geçersiz.",409);
        var data=File.ReadAllBytes(path);if(Convert.ToHexStringLower(SHA256.HashData(data))!=rom.Sha256)throw new DomainException("Disk kopyası değişmiş; mevcut dosyalar korundu.",409);
        var manifest=JsonSerializer.Deserialize<NativeDiscManifest>(data,DiscJson)??throw new DomainException("Disk kaldırma manifesti geçersiz.",409);
        if(manifest.Format!=1||manifest.System!=rom.System||manifest.Entry!=rom.FileName||manifest.Files.Length is <1 or >128||manifest.Files.Sum(file=>file.Bytes)!=rom.SizeBytes)throw new DomainException("Disk kaldırma manifesti geçersiz.",409);
        return manifest.Files.Select(file=>new GameRemovalFile(file.Name,file.Bytes,file.Sha256)).Append(new("disc.json",data.Length,rom.Sha256)).ToArray();
    }
}
