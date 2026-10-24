using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HomeServer.Core;
using HomeServer.Infrastructure;

namespace HomeServer.LiveTv;

public sealed class LivePlayback(MediaStore store,LiveSources sources,LiveInputRelay relay,HomePaths paths,ILiveProducer producer,EventHub events,IConfiguration? configuration=null):BackgroundService
{
    private sealed class Running(string directory,CancellationTokenSource cancel,(string Token,string Url) link)
    {
        public string Directory {get;}=directory;
        public CancellationTokenSource Cancel {get;}=cancel;
        public CancellationToken Lifetime {get;}=cancel.Token;
        public CancellationTokenSource ProducerCancel {get;}=CancellationTokenSource.CreateLinkedTokenSource(cancel.Token);
        public (string Token,string Url) Link {get;}=link;
        public SemaphoreSlim Gate {get;}=new(1,1);
        public Task? Task;
        public ProbeResult? Probe;
        public MediaTracks? Tracks;
        public LiveStreamPlan? Plan;
        public string? ConfigurationKey;
        public string State="preparing";
        public string? Error;
        public bool Recoverable=true;
        public long Bytes;
    }
    private readonly ConcurrentDictionary<string,Running> _running=new();
    private readonly CancellationTokenSource _shutdown=new();
    private readonly SemaphoreSlim _admission=new(1,1);
    private readonly long _budget=(long)(Math.Clamp(configuration?.GetValue("Home:LiveTv:CacheGiBPerSession",8d)??8,.125,16)*1024*1024*1024);
    public override async Task StartAsync(CancellationToken token)
    {
        store.RecoverLiveSessions();var root=Path.Combine(paths.Data,"live-cache");
        if(Directory.Exists(root))foreach(var directory in Directory.EnumerateDirectories(root))
        {
            var marker=Path.Combine(directory,".home-live");
            if(!Guid.TryParseExact(Path.GetFileName(directory),"N",out _)||!File.Exists(marker))continue;
            AppFiles.RejectLink(directory);AppFiles.RejectLink(marker);
            if(File.ReadAllText(marker)!="Home.Live.v1")continue;
            await OwnedNativeProcess.RecoverAsync(directory,token);AppFiles.DeleteOwnedDirectory(directory);
        }
        await base.StartAsync(token);
    }

    public async Task<LiveLaunch> StartAsync(string channelId,PlaybackActor actor,CancellationToken token,LivePlayInput? command=null)
    {
        if(!producer.Available)throw new DomainException("Canlı yayın için FFmpeg gerekli.",409);
        await _admission.WaitAsync(token);LiveSession? session=null;Running? running=null;LiveChannel? channel=null;string? directory=null;
        try
        {
            _shutdown.Token.ThrowIfCancellationRequested();token.ThrowIfCancellationRequested();
            var old=command?.ClientId is null?[]:_running.Keys.Select(store.GetLiveSession).OfType<LiveSession>().Where(s=>s.LoginId==actor.LoginId&&s.ClientId==command.ClientId).ToArray();
            session=store.AdmitLiveSession(channelId,actor,command?.ClientId,command?.ClientRevision??1);
            foreach(var previous in old)await CloseAsync(previous.Id,actor);
            channel=store.GetLiveChannel(channelId);var input=sources.Input(channel.SourceId);
            directory=AppFiles.DirectoryIn(paths.Data,"live-cache",session.Id);
            await File.WriteAllTextAsync(Path.Combine(directory,".home-live"),"Home.Live.v1",token);
            var link=relay.Register(sources.ChannelUrl(channelId),input.AllowLan,()=>store.RequireLiveSession(session.Id,actor),sources.ChannelHeaders(channelId));
            running=new(directory,CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token),link);_running[session.Id]=running;
        }
        catch{if(session is not null)await CloseAsync(session.Id,actor);if(running is null&&directory is not null&&Directory.Exists(directory))AppFiles.DeleteOwnedDirectory(directory);throw;}
        finally{_admission.Release();}
        try
        {
            if(command?.PrepareOnly==true)
            {
                using var probe=CancellationTokenSource.CreateLinkedTokenSource(token,running.Lifetime);probe.CancelAfter(TimeSpan.FromSeconds(30));
                running.Probe=await producer.ProbeAsync(running.Link.Url,probe.Token);probe.Token.ThrowIfCancellationRequested();RequireCurrent(session.Id,actor,running);
                var streams=(running.Probe.Streams??[]).Where(stream=>stream.Kind=="audio"||!channel.Radio&&stream.Kind=="video").ToArray();
                running.Tracks=new(channel.Id,"live-"+session.Id,null,running.Probe.Container,running.Probe.BitRate,streams,[],[]);
                return View(session.Id,channel,running,true);
            }
            running.State="starting";running.Task=ProduceAsync(running,channel.Radio);
            return await ReadyAsync(session.Id,actor,channel,running,token);
        }
        catch{await CloseAsync(session.Id,actor);throw;}
    }
    public async Task<LiveLaunch> ConfigureAsync(string id,PlaybackActor actor,LiveConfigureInput input,CancellationToken token)
    {
        var running=RequireCurrent(id,actor);using var lifetime=CancellationTokenSource.CreateLinkedTokenSource(token,running.Lifetime);
        await running.Gate.WaitAsync(lifetime.Token);
        var starting=running.Task is null;
        try
        {
            RequireCurrent(id,actor,running);
            var tracks=running.Tracks??throw new DomainException("Önce canlı yayın izlerini hazırla.",409);
            var plan=LiveStreamPlanner.Decide(tracks,input.Client,input.ForceCompatible,_budget);
            var key=Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(plan)));
            if(running.Task is not null&&running.ConfigurationKey!=key)throw new DomainException("Canlı oturum farklı bir oynatma profiliyle başlatıldı.",409);
            if(running.Task is null)
            {
                running.Plan=plan;running.ConfigurationKey=key;running.State="starting";
                running.Task=ProduceAsync(running,plan.VideoStream is null);
            }
            return await ReadyAsync(id,actor,store.GetLiveChannel(store.GetLiveSession(id)!.ChannelId),running,lifetime.Token);
        }
        catch{if(starting)await CloseAsync(id,actor);throw;}
        finally{running.Gate.Release();}
    }
    private async Task<LiveLaunch> ReadyAsync(string id,PlaybackActor actor,LiveChannel channel,Running running,CancellationToken token)
    {
        using var ready=CancellationTokenSource.CreateLinkedTokenSource(token,running.Lifetime);ready.CancelAfter(TimeSpan.FromSeconds(45));
        while(!HasReadyPlaylist(running))
        {
            if(running.Task?.IsCompleted==true)throw new DomainException(running.Error??"Canlı kaynak başlamadan sona erdi.",running.Recoverable?502:409);
            await Task.Delay(100,ready.Token);
        }
        ready.Token.ThrowIfCancellationRequested();RequireCurrent(id,actor,running);
        if(running.State=="failed")throw new DomainException(running.Error??"Canlı yayın kaynağı kesildi.",running.Recoverable?502:409);
        store.ActivateLiveSession(id,actor);Interlocked.CompareExchange(ref running.State,"active","starting");store.MarkLiveWatched(id,actor);events.Publish("live.changed");return View(id,channel,running,false);
    }
    private static bool HasReadyPlaylist(Running running)
    {
        var file=Path.Combine(running.Directory,"index.m3u8");if(!File.Exists(file))return false;if(running.Plan is null)return true;
        try
        {
            using var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);using var reader=new StreamReader(stream);
            var text=reader.ReadToEnd();if(text.Length>256*1024)throw new DomainException("Canlı oynatma listesi çok büyük.",422);
            var duration=text.Split('\n').Where(line=>line.StartsWith("#EXTINF:",StringComparison.Ordinal)).Sum(line=>double.TryParse(line[8..].Split(',')[0],CultureInfo.InvariantCulture,out var value)?value:0);
            return duration>=8||duration>0&&text.Contains("#EXT-X-ENDLIST",StringComparison.Ordinal);
        }
        catch(IOException){return false;}
    }
    private async Task ProduceAsync(Running running,bool radio)
    {
        try
        {
            if(running.Plan is{}plan)await producer.RunAsync(running.Directory,running.Link.Url,plan,running.ProducerCancel.Token);
            else await producer.RunAsync(running.Directory,running.Link.Url,radio,running.ProducerCancel.Token);
            if(!running.ProducerCancel.IsCancellationRequested)running.State=running.Probe?.Duration is>0?"completed":"interrupted";
        }
        catch(OperationCanceledException)when(running.ProducerCancel.IsCancellationRequested){}
        catch(Exception)
        {
            if(!running.ProducerCancel.IsCancellationRequested){running.Error="Canlı yayın kaynağı kesildi.";running.State="failed";}
        }
        finally{relay.Remove(running.Link.Token);events.Publish("live.changed");}
    }
    private static LiveLaunch View(string id,LiveChannel channel,Running running,bool prepared)=>new(id,$"/api/live/sessions/{id}/index.m3u8",MediaStore.PublicLiveChannel(channel),
        running.Plan is{}plan?plan.Segments*4/60:30,running.Tracks,prepared,running.Plan);
    private Running RequireCurrent(string id,PlaybackActor actor,Running? expected=null)
    {
        _=store.RequireLiveSession(id,actor,true);
        if(!_running.TryGetValue(id,out var running)||expected is not null&&!ReferenceEquals(running,expected)||running.Lifetime.IsCancellationRequested)
            throw new DomainException("Canlı oturum yeniden açılmalı.",410);
        return running;
    }
    public LivePlaybackStatus Touch(string id,PlaybackActor actor)
    {
        var running=RequireCurrent(id,actor);
        return new(id,running.State=="starting"&&HasReadyPlaylist(running)?"active":running.State,running.Recoverable,running.Error,running.Plan,Interlocked.Read(ref running.Bytes));
    }
    public async Task CloseClientAsync(string clientId,long revision,PlaybackActor actor)
    {
        store.CloseLiveClient(actor,clientId,revision);
        foreach(var id in _running.Keys)if(store.GetLiveSession(id) is{}row&&row.LoginId==actor.LoginId&&row.ClientId==clientId&&row.ClientRevision<=revision)await CloseAsync(id,actor);
    }
    public async Task CloseAsync(string id,PlaybackActor actor)
    {
        var session=store.GetLiveSession(id);if(session is null)return;
        if(session.UserId!=actor.UserId||session.LoginId!=actor.LoginId)throw new DomainException("Canlı oturum bulunamadı.",404);
        store.SetLiveSession(id,"closed");
        if(_running.TryRemove(id,out var running))
        {
            running.Cancel.Cancel();relay.Remove(running.Link.Token);if(running.Task is not null)await running.Task;
            running.ProducerCancel.Dispose();running.Cancel.Dispose();AppFiles.DeleteOwnedDirectory(running.Directory);
        }
        events.Publish("live.changed");
    }
    public IResult Artifact(string id,string file,PlaybackActor actor)
    {
        var running=RequireCurrent(id,actor);
        var segment=file.Length is 19 or 20&&file.StartsWith("segment",StringComparison.Ordinal)&&file[7..16].All(char.IsAsciiDigit)&&(file.EndsWith(".ts",StringComparison.Ordinal)||file.EndsWith(".m4s",StringComparison.Ordinal));
        if(file!="index.m3u8"&&file!="init.mp4"&&!segment)throw new DomainException("Canlı yayın parçası bulunamadı.",404);
        var path=MediaPaths.ResolveFile(running.Directory,file);
        return new LiveResult(path,file.EndsWith(".m3u8",StringComparison.Ordinal)?"application/vnd.apple.mpegurl":file.EndsWith(".ts",StringComparison.Ordinal)?"video/mp2t":"video/mp4",running.Lifetime);
    }
    private sealed class LiveResult(string path,string mime,CancellationToken lifetime):IResult
    {
        public async Task ExecuteAsync(HttpContext context)
        {
            using var abort=lifetime.Register(context.Abort);lifetime.ThrowIfCancellationRequested();context.Response.Headers.CacheControl="no-store";
            await Results.Stream(new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete,65536,FileOptions.Asynchronous),mime,enableRangeProcessing:true).ExecuteAsync(context);
        }
    }
    protected override async Task ExecuteAsync(CancellationToken token)
    {
        while(!token.IsCancellationRequested)
        {
            foreach(var pair in _running.ToArray())
            {
                var session=store.GetLiveSession(pair.Key);if(session is null)continue;
                try
                {
                    var actor=new PlaybackActor(session.UserId,session.LoginId,session.DeviceId);_=store.RequireLiveSession(session.Id,actor);store.CheckTransientLimits(actor);
                    long bytes=0;try{foreach(var file in Directory.EnumerateFiles(pair.Value.Directory))try{bytes+=new FileInfo(file).Length;}catch(FileNotFoundException){}}catch(DirectoryNotFoundException){}
                    Interlocked.Exchange(ref pair.Value.Bytes,bytes);
                    if(bytes>_budget){pair.Value.Recoverable=false;pair.Value.Error="Canlı yayın geçici alan sınırına ulaştı.";pair.Value.State="failed";pair.Value.ProducerCancel.Cancel();}
                }
                catch(ObjectDisposedException)when(pair.Value.Lifetime.IsCancellationRequested){}
                catch(DomainException){await CloseAsync(session.Id,new(session.UserId,session.LoginId,session.DeviceId));}
            }
            try{await Task.Delay(2000,token);}catch(OperationCanceledException){break;}
        }
    }
    public override async Task StopAsync(CancellationToken token)
    {_shutdown.Cancel();foreach(var id in _running.Keys)if(store.GetLiveSession(id) is{}session)await CloseAsync(id,new(session.UserId,session.LoginId,session.DeviceId));await base.StopAsync(token);}
}
