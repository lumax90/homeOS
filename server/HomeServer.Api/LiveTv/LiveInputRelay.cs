using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using HomeServer.Core;
using HomeServer.Infrastructure;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace HomeServer.LiveTv;
public sealed class LiveInputRelay(ILiveHttp http,IServer server,IConfiguration configuration)
{
    private sealed class Source(bool lan,Action validate,IReadOnlyDictionary<string,string>? headers)
    {public bool Lan=lan;public Action Validate=validate;public IReadOnlyDictionary<string,string>? Headers=headers;public readonly CancellationTokenSource Stop=new();public readonly ConcurrentDictionary<string,(string Url,DateTimeOffset Seen)> Nodes=new();public int Readers;}
    private readonly ConcurrentDictionary<string,Source> _sources=new();
    private string Base
    {
        get{var addresses=server.Features.Get<IServerAddressesFeature>()?.Addresses;var address=addresses?.FirstOrDefault(value=>value.StartsWith("http://",StringComparison.OrdinalIgnoreCase))??addresses?.FirstOrDefault()??throw new DomainException("İç medya bağlantısı için sunucu adresi bulunamadı.");var uri=new Uri(address.Replace("*","127.0.0.1").Replace("+","127.0.0.1"));return $"{uri.Scheme}://127.0.0.1:{uri.Port}"+(configuration["Home:Network:PathBase"]??"");}
    }
    public (string Token,string Url) Register(string url,bool lan,Action validate,IReadOnlyDictionary<string,string>? headers=null)
    {
        if(_sources.Count>=64)throw new DomainException("İç medya kaynağı sınırı dolu.",429);_=RemoteMediaHttp.Validate(url);var token=Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));var source=new Source(lan,validate,headers);var node=Node(token,source,url);_sources[token]=source;return(token,node);
    }
    private string Node(string token,Source source,string url)
    {
        _=RemoteMediaHttp.Validate(url);if(source.Nodes.Count>4096){foreach(var entry in source.Nodes.Where(entry=>entry.Value.Seen<DateTimeOffset.UtcNow.AddMinutes(-20)).ToArray())source.Nodes.TryRemove(entry.Key,out _);if(source.Nodes.Count>4096)throw new DomainException("Canlı kaynak manifest sınırı dolu.",429);}
        var extension=Path.GetExtension(new Uri(url).AbsolutePath).ToLowerInvariant();if(extension is not(".ts" or ".m3u8" or ".m3u" or ".mp3" or ".aac" or ".m4s" or ".mp4" or ".key"))extension=".ts";var key=PlaylistGuide.Id(url)+extension;source.Nodes[key]=(url,DateTimeOffset.UtcNow);return Base+"/internal/live/"+token+"/"+key;
    }
    public void Remove(string token){if(_sources.TryRemove(token,out var source))source.Stop.Cancel();}
    public async Task ServeAsync(string token,string key,HttpContext context)
    {
        if(context.Connection.RemoteIpAddress is not{} address||!IPAddress.IsLoopback(address)||!_sources.TryGetValue(token,out var source)||!source.Nodes.TryGetValue(key,out var node))throw new DomainException("İç medya izni bulunamadı.",404);
        if(Interlocked.Increment(ref source.Readers)>8){Interlocked.Decrement(ref source.Readers);throw new DomainException("Yayın bağlantı sınırı dolu.",429);}
        using var lifetime=CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted,source.Stop.Token);var cancel=lifetime.Token;
        try
        {
            source.Validate();using var first=CancellationTokenSource.CreateLinkedTokenSource(cancel);first.CancelAfter(TimeSpan.FromSeconds(20));var range=context.Request.Headers.Range.ToString();if(range.Length>100||range.Length>0&&!Regex.IsMatch(range,@"^bytes=(?:\d+-\d*|-\d+)$",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(50)))throw new DomainException("Yayın byte aralığı geçersiz.",416);
            using var response=await http.GetAsync(node.Url,source.Lan,first.Token,range.Length==0?null:range,source.Headers);if(!response.IsSuccessStatusCode)throw new DomainException($"Yayın kaynağı HTTP {(int)response.StatusCode} döndürdü.",502);
            var uri=response.RequestMessage?.RequestUri??RemoteMediaHttp.Validate(node.Url);var mime=response.Content.Headers.ContentType?.MediaType??"application/octet-stream";
            if(mime.Contains("dash",StringComparison.OrdinalIgnoreCase)||uri.AbsolutePath.EndsWith(".mpd",StringComparison.OrdinalIgnoreCase))throw new DomainException("Bu kaynak için M3U/HLS veya doğrudan kanal akışı gerekli.",422);
            await using var body=await response.Content.ReadAsStreamAsync(cancel);var head=new byte[512];var count=await body.ReadAtLeastAsync(head,16,false,first.Token);var playlist=mime.Contains("mpegurl",StringComparison.OrdinalIgnoreCase)||Encoding.UTF8.GetString(head,0,count).TrimStart('\ufeff','\r','\n',' ').StartsWith("#EXTM3U",StringComparison.Ordinal);
            context.Response.Headers.CacheControl="no-store";
            if(playlist){var remaining=await Metadata.MetadataProvider.ReadLimitedAsync(body,2*1024*1024-count,first.Token);var text=Encoding.UTF8.GetString([..head.AsSpan(0,count),..remaining]);context.Response.ContentType="application/vnd.apple.mpegurl";await context.Response.WriteAsync(Rewrite(text,uri,next=>Node(token,source,next)),cancel);return;}
            if(mime.StartsWith("text/",StringComparison.OrdinalIgnoreCase)||mime.Contains("json",StringComparison.OrdinalIgnoreCase)||mime.Contains("xml",StringComparison.OrdinalIgnoreCase))throw new DomainException("Yayın kaynağı geçerli medya döndürmedi.",502);
            context.Response.StatusCode=(int)response.StatusCode;context.Response.ContentType=mime;if(response.Content.Headers.ContentLength is{} length)context.Response.ContentLength=length;if(response.Content.Headers.ContentRange is{} contentRange)context.Response.Headers.ContentRange=contentRange.ToString();
            await context.Response.Body.WriteAsync(head.AsMemory(0,count),cancel);var buffer=new byte[65536];var nextCheck=DateTimeOffset.UtcNow;
            while(true){using var idle=CancellationTokenSource.CreateLinkedTokenSource(cancel);idle.CancelAfter(TimeSpan.FromSeconds(30));count=await body.ReadAsync(buffer,idle.Token);if(count==0)break;if(DateTimeOffset.UtcNow>=nextCheck){source.Validate();nextCheck=DateTimeOffset.UtcNow.AddSeconds(1);}await context.Response.Body.WriteAsync(buffer.AsMemory(0,count),cancel);}
        }
        catch(OperationCanceledException)when(cancel.IsCancellationRequested){context.Abort();}
        catch(Exception error)when(error is DomainException or IOException or HttpRequestException or OperationCanceledException){if(context.Response.HasStarted)context.Abort();else if(error is DomainException)throw;else throw new DomainException("Yayın kaynağına ulaşılamadı.",502);}
        finally{Interlocked.Decrement(ref source.Readers);}
    }
    public static string Rewrite(string text,Uri root,Func<string,string> node)
    {
        if(text.Length>2*1024*1024||text.Contains("#EXT-X-DEFINE",StringComparison.OrdinalIgnoreCase)||text.Contains("#EXT-X-CONTENT-STEERING",StringComparison.OrdinalIgnoreCase)||text.Contains("#EXT-X-SESSION-DATA",StringComparison.OrdinalIgnoreCase))throw new DomainException("Yayın manifesti desteklenmiyor.",422);
        string Map(string path){if(!Uri.TryCreate(root,path,out var uri))throw new DomainException("Yayın manifest adresi geçersiz.",422);return node(RemoteMediaHttp.Validate(uri.AbsoluteUri).AbsoluteUri);}
        return string.Join('\n',text.Split('\n').Select(raw=>{var line=raw.Trim();if(line.Length==0)return "";if(!line.StartsWith('#'))return Map(line);return Regex.Replace(line,"\\bURI\\s*=\\s*(?:\"([^\"]*)\"|([^,]*))",match=>match.Groups[1].Success?"URI=\""+Map(match.Groups[1].Value)+"\"":throw new DomainException("Yayın manifest adresi geçersiz.",422),RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));}));
    }
}
