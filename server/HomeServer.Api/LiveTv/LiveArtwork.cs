using HomeServer.Core;
using HomeServer.Infrastructure;
using HomeServer.Metadata;
using Microsoft.Extensions.Caching.Memory;

namespace HomeServer.LiveTv;

public sealed class LiveArtwork(HomePaths paths,MediaStore store,LiveSources sources,ILiveHttp http,IMemoryCache cache)
{
    private readonly SemaphoreSlim _gate=new(2,2);
    public async Task<(string Path,string Mime)?> GetAsync(string id,Account actor,CancellationToken token)
    {
        var channel=store.RequireLiveChannel(id,actor);var logo=sources.ChannelLogo(id);if(string.IsNullOrWhiteSpace(logo))return null;var key=PlaylistGuide.Id(id+"|"+logo);var root=AppFiles.DirectoryIn(paths.Data,"live-logos");var file=Path.Combine(root,key+".image");
        (string,string)? Cached(){if(!File.Exists(file))return null;AppFiles.RejectLink(file);return LocalMetadataReader.ImageMime(file) is{} mime?(file,mime):null;}
        if(Cached() is{} image)return image;if(cache.TryGetValue<bool>("live-logo-missing:"+key,out _))return null;
        await _gate.WaitAsync(token);try
        {
            if(Cached() is{} existing)return existing;var response=await LiveHttpRead.ReadAsync(http,logo,sources.Input(channel.SourceId).AllowLan,2*1024*1024,token);var temporary=file+"."+Guid.NewGuid().ToString("N")+".tmp";
            try{await File.WriteAllBytesAsync(temporary,response.Bytes,token);var mime=LocalMetadataReader.ImageMime(temporary)??throw new DomainException("Kanal logosu geçerli bir resim değil.",422);store.RequireLiveChannel(id,store.GetAccount(actor.Id)??throw new DomainException("Oturum gerekli.",401));if(File.Exists(file))AppFiles.RejectLink(file);File.Move(temporary,file,true);Prune(root);return(file,mime);}finally{if(File.Exists(temporary))File.Delete(temporary);}
        }
        catch(Exception error)when(error is DomainException or HttpRequestException or IOException||error is OperationCanceledException&&!token.IsCancellationRequested){cache.Set("live-logo-missing:"+key,true,new MemoryCacheEntryOptions{Size=1,AbsoluteExpirationRelativeToNow=TimeSpan.FromMinutes(15)});return null;}
        finally{_gate.Release();}
    }
    private static void Prune(string root){var files=Directory.EnumerateFiles(root,"*.image").Where(path=>Guid.TryParseExact(Path.GetFileNameWithoutExtension(path),"N",out _)).Select(path=>new FileInfo(path)).OrderBy(file=>file.LastWriteTimeUtc).ToArray();var bytes=files.Sum(file=>file.Length);var count=files.Length;foreach(var file in files){if(count<=1500&&bytes<=128L*1024*1024)break;AppFiles.RejectLink(file.FullName);try{File.Delete(file.FullName);count--;bytes-=file.Length;}catch(IOException){}}}
}
