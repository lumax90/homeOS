using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using HomeServer.Core;
using HomeServer.Infrastructure;
using HomeServer.LiveTv;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HomeServer.Tests;

public sealed class LiveStabilityTests
{
    private static ProbeResult Probe(string video="h264",string audio="aac")=>new(null,1920,1080,video,audio,Streams:
    [new(0,"video",video,"und","",true,false,false,1920,1080,null,null,null,4_000_000,"High","yuv420p",null,null),
     new(1,"audio",audio,"en","",true,false,false,null,null,2,"stereo",48000,128000,null,null,null,null)],Container:"mpegts",BitRate:4_128_000);
    private static PlaybackClient Client(MediaTracks tracks,bool original=true)=>new([],[],[],true,Capabilities:new(tracks.Fingerprint,0,1,[new("mp4","hls.js",original,true,true,true)]));
    [Fact]
    public void LivePlanPreservesSupportedStreamsAndOnlyConvertsWhatTheBrowserNeeds()
    {
        var probe=Probe("hevc","ac3");var tracks=new MediaTracks("channel","lease",null,"mpegts",probe.BitRate,probe.Streams!,[],[]);
        var copy=LiveStreamPlanner.Decide(tracks,Client(tracks),false,8L*1024*1024*1024);
        Assert.Equal("remux",copy.Mode);Assert.True(copy.CopyVideo);Assert.True(copy.CopyAudio);Assert.Equal("hevc",copy.VideoCodec);
        var audio=LiveStreamPlanner.Decide(tracks,Client(tracks,false),false,8L*1024*1024*1024);Assert.Equal("audio-transcode",audio.Mode);Assert.Equal("aac",audio.AudioCodec);
        var compatible=LiveStreamPlanner.Decide(tracks,Client(tracks),true,8L*1024*1024*1024);Assert.Equal("video-transcode",compatible.Mode);Assert.False(compatible.CopyVideo);
        Assert.Equal(409,Assert.Throws<DomainException>(()=>LiveStreamPlanner.Decide(tracks with{Fingerprint="other"},Client(tracks),false,100_000_000)).StatusCode);
    }
    [Fact]
    public async Task TemporaryUpstreamFailuresRetryBeforePublishingBytesAndKeepRangeAndHeaders()
    {
        var count=0;var checkedAccess=0;var waits=new List<double>();var headers=new Dictionary<string,string>{{"User-Agent","Isolated fixture"}};
        var http=new LiveTvExperienceTests.Http(_=>new(++count<3?HttpStatusCode.ServiceUnavailable:HttpStatusCode.PartialContent));
        using var response=await LiveHttpRetry.OpenAsync(http,"https://fixture.invalid/segment.ts",false,()=>checkedAccess++,CancellationToken.None,"bytes=2-9",headers,
            (delay,_)=>{waits.Add(delay.TotalMilliseconds);return Task.CompletedTask;});
        Assert.Equal(HttpStatusCode.PartialContent,response.StatusCode);Assert.Equal(3,count);Assert.Equal(3,checkedAccess);Assert.Equal(new[]{250d,750d},waits);
        Assert.All(http.Calls,call=>{Assert.Equal("bytes=2-9",call.Range);Assert.Same(headers,call.Headers);});
    }
    [Fact]
    public async Task UpstreamAccessDenialDoesNotRetryAndRevocationStopsAPendingRetry()
    {
        var denied=new LiveTvExperienceTests.Http(_=>new(HttpStatusCode.Forbidden));
        using var response=await LiveHttpRetry.OpenAsync(denied,"https://fixture.invalid/segment.ts",false,()=>{},CancellationToken.None);
        Assert.Single(denied.Calls);Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
        var attempts=0;var source=new LiveTvExperienceTests.Http(_=>new(HttpStatusCode.ServiceUnavailable));
        Assert.Equal(403,(await Assert.ThrowsAsync<DomainException>(()=>LiveHttpRetry.OpenAsync(source,"https://fixture.invalid/segment.ts",false,
            ()=>{if(++attempts>1)throw new DomainException("revoked",403);},CancellationToken.None,delay:(_,_)=>Task.CompletedTask))).StatusCode);
        Assert.Single(source.Calls);
    }
    private sealed class Producer:ILiveProducer
    {
        public bool Available=>true;public int Started;public TaskCompletionSource? Entered;public TaskCompletionSource<ProbeResult>? Delayed;
        public Task<ProbeResult> ProbeAsync(string input,CancellationToken token){Entered?.TrySetResult();return Delayed?.Task??Task.FromResult(Probe());}
        public async Task RunAsync(string directory,string input,bool radio,CancellationToken token)
        {
            Interlocked.Increment(ref Started);await File.WriteAllBytesAsync(Path.Combine(directory,"init.mp4"),new byte[32],token);
            for(var index=0;index<2;index++)await File.WriteAllBytesAsync(Path.Combine(directory,$"segment{index:D9}.m4s"),new byte[32],token);
            await File.WriteAllTextAsync(Path.Combine(directory,"index.m3u8"),"#EXTM3U\n#EXT-X-TARGETDURATION:4\n#EXT-X-MAP:URI=\"init.mp4\"\n#EXTINF:4,\nsegment000000000.m4s\n#EXTINF:4,\nsegment000000001.m4s\n",token);
            await Task.Delay(Timeout.Infinite,token);
        }
    }
    [Fact]
    public async Task PrepareIsActorBoundAndConfigurationIsIdempotentWithoutResurrectingClosedLeases()
    {
        using var home=new TestWorkspace();var(user,actor)=LiveTvExperienceTests.Owner(home);var http=new LiveTvExperienceTests.Http(_=>throw new InvalidOperationException("No media acquisition"));
        var sources=LiveTvExperienceTests.Service(home,http);var source=sources.Save(null,new("Isolated live fixture",StreamUrl:"https://fixture.invalid/live?token=private",Tuners:1),user);
        await LiveTvExperienceTests.Sync(home,sources,source,user);var channel=Assert.Single(home.Store.LiveChannels(user));
        var builder=WebApplication.CreateBuilder();builder.Logging.ClearProviders();builder.WebHost.UseUrls("http://127.0.0.1:0");await using var app=builder.Build();await app.StartAsync();
        var producer=new Producer();var relay=new LiveInputRelay(http,app.Services.GetRequiredService<IServer>(),new ConfigurationBuilder().Build());using var player=new LivePlayback(home.Store,sources,relay,home.Paths,producer,new EventHub());
        await player.StartAsync(CancellationToken.None);
        try
        {
            var client=Guid.NewGuid().ToString("N");var prepared=await player.StartAsync(channel.Id,actor,CancellationToken.None,new(client,1,true));
            Assert.True(prepared.Prepared);Assert.Equal(0,producer.Started);Assert.Equal(1,home.Store.SourceTunerUse(source.Id));Assert.DoesNotContain("private",JsonSerializer.Serialize(prepared));
            var other=home.Store.CreateLoginSession(user.Id,1,"second fixture");var otherActor=home.Store.GetPlaybackActor(user.Id,other.Id);
            await Assert.ThrowsAsync<DomainException>(()=>player.ConfigureAsync(prepared.Id,otherActor,new(Client(prepared.Tracks!)),CancellationToken.None));Assert.Equal(0,producer.Started);
            var config=new LiveConfigureInput(Client(prepared.Tracks!));var running=await player.ConfigureAsync(prepared.Id,actor,config,CancellationToken.None);
            await player.ConfigureAsync(prepared.Id,actor,config,CancellationToken.None);Assert.Equal(1,producer.Started);Assert.Equal("remux",running.Plan!.Mode);
            await player.CloseClientAsync(client,1,actor);Assert.Empty(home.Store.ActiveLiveSessions());Assert.Equal(0,home.Store.SourceTunerUse(source.Id));
            await Assert.ThrowsAsync<DomainException>(()=>player.ConfigureAsync(prepared.Id,actor,config,CancellationToken.None));Assert.Equal(1,producer.Started);
        }
        finally{await player.StopAsync(CancellationToken.None);await app.StopAsync();}
    }
    [Fact]
    public async Task ClosingDuringAnUncooperativeProbeCannotReturnOrReactivateTheOldSession()
    {
        using var home=new TestWorkspace();var(user,actor)=LiveTvExperienceTests.Owner(home);var http=new LiveTvExperienceTests.Http(_=>throw new InvalidOperationException());var sources=LiveTvExperienceTests.Service(home,http);
        var source=sources.Save(null,new("Isolated delayed probe",StreamUrl:"https://fixture.invalid/live"),user);await LiveTvExperienceTests.Sync(home,sources,source,user);var channel=Assert.Single(home.Store.LiveChannels(user));
        var builder=WebApplication.CreateBuilder();builder.Logging.ClearProviders();builder.WebHost.UseUrls("http://127.0.0.1:0");await using var app=builder.Build();await app.StartAsync();
        var producer=new Producer{Entered=new(TaskCreationOptions.RunContinuationsAsynchronously),Delayed=new(TaskCreationOptions.RunContinuationsAsynchronously)};
        var relay=new LiveInputRelay(http,app.Services.GetRequiredService<IServer>(),new ConfigurationBuilder().Build());using var player=new LivePlayback(home.Store,sources,relay,home.Paths,producer,new EventHub());
        await player.StartAsync(CancellationToken.None);
        try
        {
            var client=Guid.NewGuid().ToString("N");var preparing=player.StartAsync(channel.Id,actor,CancellationToken.None,new(client,1,true));
            await producer.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));await player.CloseClientAsync(client,1,actor);producer.Delayed.SetResult(Probe());
            await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>preparing);Assert.Empty(home.Store.ActiveLiveSessions());Assert.Equal(0,producer.Started);
        }
        finally{producer.Delayed.TrySetResult(Probe());await player.StopAsync(CancellationToken.None);await app.StopAsync();}
    }
    [MediaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RealLiveRelayKeepsVideoAndRecoversAnUpstream503BeforeProducingFmp4(bool convertAudio)
    {
        using var home=new TestWorkspace();var(user,actor)=LiveTvExperienceTests.Owner(home);var tools=SubtitleBurnTests.Tools(home);var input=Path.Combine(home.Source,"isolated-live.ts");
        await MediaProcess.RunAsync(tools.FfmpegExecutable,["-hide_banner","-loglevel","error","-nostdin","-f","lavfi","-i","testsrc2=size=320x180:rate=24","-f","lavfi","-i","sine=sample_rate=48000","-t","12","-c:v","libx264","-threads","1","-g","48","-c:a",convertAudio?"ac3":"aac","-f","mpegts",input],TimeSpan.FromSeconds(30),CancellationToken.None);
        var hash=StateFolders.Hash(input);var calls=0;var http=new LiveTvExperienceTests.Http(_=>{if(Interlocked.Increment(ref calls)==1)return new(HttpStatusCode.ServiceUnavailable);var result=new HttpResponseMessage(HttpStatusCode.OK){Content=new StreamContent(File.OpenRead(input))};result.Content.Headers.ContentType=new("video/mp2t");return result;});
        var sources=LiveTvExperienceTests.Service(home,http);var source=sources.Save(null,new("Synthetic broadcast",StreamUrl:"https://fixture.invalid/live.ts"),user);await LiveTvExperienceTests.Sync(home,sources,source,user);var channel=Assert.Single(home.Store.LiveChannels(user));
        var builder=WebApplication.CreateBuilder();builder.Logging.ClearProviders();builder.WebHost.UseUrls("http://127.0.0.1:0");await using var app=builder.Build();LiveInputRelay? relay=null;
        app.MapGet("/internal/live/{key}/{node}",(string key,string node,HttpContext context)=>relay!.ServeAsync(key,node,context));await app.StartAsync();
        relay=new(http,app.Services.GetRequiredService<IServer>(),new ConfigurationBuilder().Build());using var player=new LivePlayback(home.Store,sources,relay,home.Paths,new LiveProducer(tools),new EventHub());await player.StartAsync(CancellationToken.None);
        try
        {
            var prepared=await player.StartAsync(channel.Id,actor,CancellationToken.None,new(Guid.NewGuid().ToString("N"),1,true));Assert.NotNull(prepared.Tracks);Assert.True(calls>=2);
            var result=await player.ConfigureAsync(prepared.Id,actor,new(Client(prepared.Tracks,!convertAudio)),CancellationToken.None);Assert.True(result.Plan!.CopyVideo);Assert.Equal(convertAudio?"audio-transcode":"remux",result.Plan.Mode);
            var root=Path.Combine(home.Paths.Data,"live-cache",result.Id);var sample=Path.Combine(home.Root,"isolated-output.mp4");
            await using(var file=File.Create(sample))foreach(var name in new[]{"init.mp4","segment000000000.m4s"}){await using var part=File.OpenRead(Path.Combine(root,name));await part.CopyToAsync(file);}
            var probe=(await tools.ProbeAsync(sample,CancellationToken.None))!;Assert.Equal("h264",probe.VideoCodec);Assert.Equal(320,probe.Width);Assert.Equal("aac",probe.AudioCodec);
            async Task<string[]> Pixels(string path)
            {
                var text=await MediaProcess.RunAsync(tools.FfmpegExecutable,["-hide_banner","-loglevel","error","-nostdin","-i",path,"-map","0:v:0","-an","-frames:v","48","-f","framemd5","-"],TimeSpan.FromSeconds(20),CancellationToken.None);
                return text.Split('\n').Where(line=>line.Length>0&&!line.StartsWith('#')).Select(line=>line.Split(',').Last().Trim()).ToArray();
            }
            Assert.Equal(await Pixels(input),await Pixels(sample));
            await player.CloseAsync(result.Id,actor);Assert.False(Directory.Exists(root));Assert.Equal(hash,StateFolders.Hash(input));Assert.Empty(home.Store.GetDownloads());
        }
        finally{await player.StopAsync(CancellationToken.None);await app.StopAsync();}
    }
}
