using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using HomeServer.Core;
using HomeServer.Infrastructure;

namespace HomeServer.Music;

public interface IRadioStreamSource { Task<HttpResponseMessage> GetAsync(string url,CancellationToken token,string? range=null); }
public sealed class RadioStreamSource(RemoteMediaHttp http):IRadioStreamSource { public Task<HttpResponseMessage> GetAsync(string url,CancellationToken token,string? range=null)=>http.GetAsync(url,false,token,range); }
public sealed record RadioPlayRequest(string StationId,string ClientId,long Revision=1);
public sealed record RadioLaunch(string Id,string Url,RadioStation Station);
public sealed class RadioPlayback(MediaStore store,RadioDirectory directory,IRadioStreamSource source,EventHub events):BackgroundService
{
    private sealed class Lease(MusicRadioSession session,RadioStation station)
    {
        public MusicRadioSession Session=session;public RadioStation Station=station;public PlaybackActor Actor=new(session.UserId,session.LoginId,session.DeviceId);
        public readonly CancellationTokenSource Stop=new();public readonly Dictionary<string,(string Url,DateTimeOffset Seen)> Nodes=new();public int Readers;
    }
    private readonly ConcurrentDictionary<string,Lease> _leases=new();private readonly SemaphoreSlim _admission=new(1,1);
    public async Task<RadioLaunch> StartAsync(Account actor,string login,RadioPlayRequest input,CancellationToken token)
    {
        var identity=store.GetPlaybackActor(actor.Id,login);if(!MusicAccess.Online(actor))throw new DomainException("Bu profil radyo yayınlarına erişemiyor.",403);
        _=AppFiles.Id(input.ClientId);var station=await directory.StationAsync(actor.Id,input.StationId,token);
        await _admission.WaitAsync(token);
        try
        {
            token.ThrowIfCancellationRequested();var session=store.AdmitMusicRadio(station,identity,input.ClientId,input.Revision);
            foreach(var pair in _leases.Where(p=>p.Value.Session.LoginId==login&&p.Value.Session.ClientId==input.ClientId))Close(pair.Key,pair.Value);
            var lease=new Lease(session,station);_leases[session.Id]=lease;var launch=new RadioLaunch(session.Id,Node(session.Id,lease,station.Url),station);
            try{await directory.ReportPlayAsync(actor.Id,station.Id,token);token.ThrowIfCancellationRequested();return launch;}catch{Close(session.Id,lease);throw;}
        }
        finally{_admission.Release();events.Publish("playback.changed");}
    }
    private Lease Get(string id,Account actor,string login,bool touch=false)
    {
        var identity=store.GetPlaybackActor(actor.Id,login);_=store.RequireMusicRadio(id,identity,touch);
        return _leases.TryGetValue(id,out var lease)?lease:throw new DomainException("Radyo oturumu sona erdi.",410);
    }
    public void Touch(string id,Account actor,string login)=>_=Get(id,actor,login,true);
    public void End(string id,Account actor,string login,bool manage=false)
    {if(_leases.TryGetValue(id,out var lease)&&(lease.Session.UserId==actor.Id&&(manage||lease.Session.LoginId==login)||manage&&actor.IsAdmin))Close(id,lease);}
    private void Close(string id,Lease lease)
    {if(_leases.TryRemove(id,out _)){store.CloseMusicRadio(id);lease.Stop.Cancel();events.Publish("playback.changed");}}
    private static string Node(string id,Lease lease,string url)
    {
        if(!RadioDirectory.ValidPublicUrl(url))throw new DomainException("Radyo kaynağının adresine izin verilmiyor.",502);
        var key=Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(url)))[..32];
        lock(lease.Nodes)
        {
            if(!lease.Nodes.ContainsKey(key)&&lease.Nodes.Count>=512)
            {foreach(var old in lease.Nodes.Where(p=>DateTimeOffset.UtcNow-p.Value.Seen>TimeSpan.FromMinutes(3)).Select(p=>p.Key).ToArray())lease.Nodes.Remove(old);}
            if(!lease.Nodes.ContainsKey(key)&&lease.Nodes.Count>=1024)throw new DomainException("Radyo yayın listesi sınırı aşıldı.",502);
            lease.Nodes[key]=(url,DateTimeOffset.UtcNow);
        }
        return $"/api/music/radio/sessions/{id}/stream/{key}";
    }
    public async Task ServeAsync(string id,string key,Account actor,string login,HttpContext context)
    {
        var lease=Get(id,actor,login);string url;
        lock(lease.Nodes){if(!lease.Nodes.TryGetValue(key,out var node))throw new DomainException("Radyo parçası bulunamadı.",404);url=node.Url;lease.Nodes[key]=(url,DateTimeOffset.UtcNow);}
        if(Interlocked.Increment(ref lease.Readers)>8){Interlocked.Decrement(ref lease.Readers);throw new DomainException("Radyo bağlantı sınırı aşıldı.",429);}
        using var cancel=CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted,lease.Stop.Token);var token=cancel.Token;
        try
        {
            using var first=CancellationTokenSource.CreateLinkedTokenSource(token);first.CancelAfter(TimeSpan.FromSeconds(20));var range=context.Request.Headers.Range.ToString();
            if(range.Length>100||range.Length>0&&!Regex.IsMatch(range,@"^bytes=(?:\d+-\d*|-\d+)$",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(50)))throw new DomainException("Radyo aralığı geçersiz.",416);
            using var response=await source.GetAsync(url,first.Token,range.Length>0?range:null);
            if(response.StatusCode==System.Net.HttpStatusCode.RequestedRangeNotSatisfiable){context.Response.StatusCode=416;return;}
            if(!response.IsSuccessStatusCode)throw new DomainException("Radyo yayınına bağlanılamadı.",502);
            await using var stream=await response.Content.ReadAsStreamAsync(token);var head=new byte[512];var count=await stream.ReadAtLeastAsync(head,16,false,first.Token);var mime=response.Content.Headers.ContentType?.MediaType??"application/octet-stream";
            context.Response.Headers.CacheControl="no-store";context.Response.Headers["X-Content-Type-Options"]="nosniff";
            var playlist=Encoding.UTF8.GetString(head,0,count).TrimStart('\ufeff','\r','\n',' ').StartsWith("#EXTM3U",StringComparison.Ordinal);
            if(playlist)
            {
                var rest=await Metadata.MetadataProvider.ReadLimitedAsync(stream,512*1024-count,first.Token);var text=Encoding.UTF8.GetString([..head.AsSpan(0,count),..rest]);var root=response.RequestMessage?.RequestUri??new Uri(url);
                var rewritten=RewritePlaylist(text,root,next=>context.Request.PathBase+Node(id,lease,next));context.Response.ContentType="application/vnd.apple.mpegurl";await context.Response.WriteAsync(rewritten,token);return;
            }
            if(mime.StartsWith("text/",StringComparison.OrdinalIgnoreCase)||mime.Contains("json",StringComparison.OrdinalIgnoreCase)||mime.Contains("xml",StringComparison.OrdinalIgnoreCase)||mime.Contains("scpls",StringComparison.OrdinalIgnoreCase))throw new DomainException("Radyo kaynağı desteklenen bir ses yayını döndürmedi.",502);
            context.Response.ContentType=mime;context.Response.StatusCode=(int)response.StatusCode;if(response.Content.Headers.ContentLength is{} length)context.Response.ContentLength=length;if(response.Content.Headers.ContentRange is{} contentRange)context.Response.Headers.ContentRange=contentRange.ToString();
            await context.Response.Body.WriteAsync(head.AsMemory(0,count),token);var checkedAt=DateTimeOffset.UtcNow;var buffer=new byte[32768];
            while(true)
            {
                using var idle=CancellationTokenSource.CreateLinkedTokenSource(token);idle.CancelAfter(TimeSpan.FromSeconds(30));count=await stream.ReadAsync(buffer,idle.Token);if(count==0)break;
                if(DateTimeOffset.UtcNow-checkedAt>TimeSpan.FromSeconds(5)){_=store.RequireMusicRadio(id,lease.Actor);checkedAt=DateTimeOffset.UtcNow;}
                await context.Response.Body.WriteAsync(buffer.AsMemory(0,count),token);
            }
        }
        catch(OperationCanceledException)when(token.IsCancellationRequested){context.Abort();}
        catch(Exception error)when(error is HttpRequestException or OperationCanceledException or IOException or DomainException)
        {if(context.Response.HasStarted)context.Abort();else if(error is DomainException)throw;else throw new DomainException("Radyo bağlantısı kesildi. Kanalı yeniden açabilirsin.",502);}
        finally{Interlocked.Decrement(ref lease.Readers);}
    }
    public static string RewritePlaylist(string text,Uri root,Func<string,string> map)
    {
        if(text.Length>512*1024||text.Contains("#EXT-X-SESSION-DATA",StringComparison.OrdinalIgnoreCase)||text.Contains("#EXT-X-CONTENT-STEERING",StringComparison.OrdinalIgnoreCase)||text.Contains("#EXT-X-DEFINE",StringComparison.OrdinalIgnoreCase))throw new DomainException("Radyo yayın listesi geçersiz.",502);
        string Rewrite(string value){if(!Uri.TryCreate(root,value,out var uri)||!RadioDirectory.ValidPublicUrl(uri.AbsoluteUri))throw new DomainException("Radyo kaynağının adresine izin verilmiyor.",502);return map(uri.AbsoluteUri);}
        return string.Join('\n',text.Split('\n').Select(raw=>
        {
            var line=raw.Trim();if(line.Length==0)return "";
            if(!line.StartsWith('#'))return Rewrite(line);
            return Regex.Replace(line,"\\bURI\\s*=\\s*(?:\"([^\"]*)\"|([^,]*))",match=>match.Groups[1].Success?"URI=\""+Rewrite(match.Groups[1].Value)+"\"":throw new DomainException("Radyo yayın listesi geçersiz.",502),RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
        }));
    }
    protected override async Task ExecuteAsync(CancellationToken token)
    {using var timer=new PeriodicTimer(TimeSpan.FromSeconds(15));while(await timer.WaitForNextTickAsync(token))foreach(var pair in _leases)try{_=store.RequireMusicRadio(pair.Key,pair.Value.Actor);}catch(DomainException){Close(pair.Key,pair.Value);}}
    public override async Task StopAsync(CancellationToken token){foreach(var pair in _leases)Close(pair.Key,pair.Value);await base.StopAsync(token);}
}
