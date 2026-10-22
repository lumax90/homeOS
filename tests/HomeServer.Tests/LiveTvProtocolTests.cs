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
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HomeServer.Tests;

public sealed class LiveTvProtocolTests
{
    private sealed class FixtureProducer:ILiveProducer
    {
        public bool Available=>true;public int Started;public int Stopped;
        public async Task RunAsync(string directory,string input,bool radio,CancellationToken token){Interlocked.Increment(ref Started);try{await File.WriteAllTextAsync(Path.Combine(directory,"index.m3u8"),"#EXTM3U\n#EXT-X-TARGETDURATION:4\n#EXTINF:4,\nsegment000000000.ts\n",token);await File.WriteAllBytesAsync(Path.Combine(directory,"segment000000000.ts"),[1,2,3,4,5,6,7,8],token);await Task.Delay(Timeout.Infinite,token);}finally{Interlocked.Increment(ref Stopped);}}
    }
    private sealed class Factory(TestWorkspace home,ILiveHttp http,ILiveProducer producer):WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder){builder.UseEnvironment("Testing");builder.ConfigureAppConfiguration((_,config)=>config.AddInMemoryCollection(new Dictionary<string,string?>{["Home:DataPath"]=home.Paths.Data,["Home:FfmpegPath"]="absent",["Home:FfprobePath"]="absent",["Home:Downloads:LoopbackOnly"]="true"}));builder.ConfigureServices(services=>{services.RemoveAll<ILiveHttp>();services.AddSingleton(http);services.RemoveAll<ILiveProducer>();services.AddSingleton(producer);foreach(var descriptor in services.Where(item=>item.ServiceType==typeof(IHostedService)&&(item.ImplementationType==typeof(ProductWorker)||item.ImplementationType==typeof(LiveSourceScheduler))).ToArray())services.Remove(descriptor);});}
    }
    private static async Task Session(HttpClient client)
    {var session=await client.GetFromJsonAsync<JsonElement>("/api/session");foreach(var key in new[]{"X-CSRF-TOKEN","X-Home-User","X-Home-Session"})client.DefaultRequestHeaders.Remove(key);client.DefaultRequestHeaders.Add("X-CSRF-TOKEN",session.GetProperty("csrfToken").GetString());if(session.GetProperty("authenticated").GetBoolean()){client.DefaultRequestHeaders.Add("X-Home-User",session.GetProperty("userId").GetString());client.DefaultRequestHeaders.Add("X-Home-Session",session.GetProperty("sessionId").GetString());}}
    [Fact]
    public async Task HttpSourceCommandsAreActorBoundAndOnlyPlayCreatesALeasedOutput()
    {
        using var home=new TestWorkspace();var http=new LiveTvExperienceTests.Http(_=>throw new InvalidOperationException("Direct registration must not fetch media."));var producer=new FixtureProducer();await using var app=new Factory(home,http,producer);using var owner=app.CreateClient();await Session(owner);(await owner.PostAsJsonAsync("/api/setup",new Credentials("Owner","live-fixture-password"))).EnsureSuccessStatusCode();await Session(owner);
        var addresses=app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();if(addresses is null){addresses=new ServerAddressesFeature();app.Services.GetRequiredService<IServer>().Features.Set(addresses);}addresses.Addresses.Add("http://127.0.0.1:50123");
        var input=new LiveSourceInput("My IPTV",StreamUrl:"https://example.test/live?password=kept-private");owner.DefaultRequestHeaders.Remove("X-Home-User");Assert.Equal(HttpStatusCode.Conflict,(await owner.PostAsJsonAsync("/api/live/sources",input)).StatusCode);await Session(owner);owner.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");Assert.Equal(HttpStatusCode.BadRequest,(await owner.PostAsJsonAsync("/api/live/sources",input)).StatusCode);await Session(owner);
        using var created=await owner.PostAsJsonAsync("/api/live/sources",input);created.EnsureSuccessStatusCode();var sourceJson=await created.Content.ReadFromJsonAsync<JsonElement>();var sourceId=sourceJson.GetProperty("source").GetProperty("id").GetString()!;var store=app.Services.GetRequiredService<MediaStore>();var service=app.Services.GetRequiredService<LiveSources>();await service.RunAsync(store.ClaimProductJob(["live-source-sync"])!,CancellationToken.None);
        var page=(await owner.GetFromJsonAsync<LiveLibraryPage>("/api/live/library"))!;var channel=Assert.Single(page.Items).Channel;Assert.Empty(http.Calls);Assert.Equal(0,producer.Started);Assert.DoesNotContain("kept-private",await owner.GetStringAsync("/api/live/sources/"+sourceId+"/settings"));
        (await owner.PutAsJsonAsync($"/api/live/channels/{channel.Id}/favorite",new LiveChannelPreference(true))).EnsureSuccessStatusCode();Assert.True(Assert.Single((await owner.GetFromJsonAsync<LiveLibraryPage>("/api/live/library?view=favorites"))!.Items).Favorite);
        var client=Guid.NewGuid().ToString("N");using var playing=await owner.PostAsJsonAsync($"/api/live/channels/{channel.Id}/play",new LivePlayInput(client,1));playing.EnsureSuccessStatusCode();var launch=(await playing.Content.ReadFromJsonAsync<LiveLaunch>())!;Assert.Contains("#EXTM3U",await owner.GetStringAsync(launch.Url));Assert.Equal(1,producer.Started);
        using var second=app.CreateClient();await Session(second);(await second.PostAsJsonAsync("/api/auth/login",new Credentials("Owner","live-fixture-password"))).EnsureSuccessStatusCode();await Session(second);Assert.Equal(HttpStatusCode.Gone,(await second.GetAsync(launch.Url)).StatusCode);
        (await owner.PostAsJsonAsync($"/api/live/clients/{client}/close",new PlaybackClientClose(1))).EnsureSuccessStatusCode();Assert.Equal(1,producer.Stopped);Assert.Equal(HttpStatusCode.Gone,(await owner.GetAsync(launch.Url)).StatusCode);Assert.Empty(store.ActiveLiveSessions());Assert.Empty(store.GetDownloads());
    }
    [Theory]
    [InlineData("#EXTM3U\nfile:///private")]
    [InlineData("#EXTM3U\n#EXT-X-KEY:URI=javascript:bad")]
    [InlineData("#EXTM3U\n#EXT-X-DEFINE:NAME=\"x\",VALUE=\"url\"")]
    public void ManifestRewriteRefusesUnsupportedUriEscapePaths(string text)
    {Assert.Throws<DomainException>(()=>LiveInputRelay.Rewrite(text,new Uri("https://example.test/live/index.m3u8"),value=>value));}
    [Fact]
    public void ManifestRewriteUsesTheFinalRedirectRootForSegmentsMapsAndKeys()
    {
        var urls=new List<string>();var output=LiveInputRelay.Rewrite("#EXTM3U\n#EXT-X-KEY:METHOD=AES-128,URI=\"../key\"\n#EXT-X-MAP:URI=\"init.mp4\"\n#EXTINF:4,\nchunk.m4s\n",new Uri("https://cdn.example.test/final/list.m3u8"),url=>{urls.Add(url);return "http://127.0.0.1/owned/"+urls.Count;});
        Assert.Equal(new[]{"https://cdn.example.test/key","https://cdn.example.test/final/init.mp4","https://cdn.example.test/final/chunk.m4s"},urls);Assert.DoesNotContain("cdn.example",output);Assert.Contains("URI=\"http://127.0.0.1/owned/1\"",output);
    }
    [MediaFact]
    public async Task NativeLiveProducerConsumesOnlyAnIsolatedRelayAndProducesPlayableHlsThenCancelsCleanly()
    {
        using var home=new TestWorkspace();var(user,actor)=LiveTvExperienceTests.Owner(home);var tools=SubtitleBurnTests.Tools(home);var input=Path.Combine(home.Source,"original-live-fixture.ts");
        await MediaProcess.RunAsync(tools.FfmpegExecutable,["-hide_banner","-loglevel","error","-nostdin","-f","lavfi","-i","color=c=navy:s=320x180:r=24","-f","lavfi","-i","sine=frequency=440:sample_rate=48000","-t","12","-c:v","libx264","-threads","1","-g","48","-c:a","aac","-f","mpegts",input],TimeSpan.FromSeconds(20),CancellationToken.None);var hash=StateFolders.Hash(input);
        var http=new LiveTvExperienceTests.Http(_=>{var response=new HttpResponseMessage(HttpStatusCode.OK){Content=new StreamContent(File.OpenRead(input))};response.Content.Headers.ContentType=new("video/mp2t");return response;});var sources=LiveTvExperienceTests.Service(home,http);var source=sources.Save(null,new("Isolated fixture",StreamUrl:"https://fixture.invalid/live.ts"),user);await LiveTvExperienceTests.Sync(home,sources,source,user);var channel=Assert.Single(home.Store.LiveChannels(user));
        var builder=WebApplication.CreateBuilder();builder.Logging.ClearProviders();builder.WebHost.UseUrls("http://127.0.0.1:0");await using var app=builder.Build();LiveInputRelay? relay=null;app.MapGet("/internal/live/{key}/{node}",(string key,string node,HttpContext context)=>relay!.ServeAsync(key,node,context));await app.StartAsync();
        relay=new(http,app.Services.GetRequiredService<IServer>(),new ConfigurationBuilder().Build());using var playback=new LivePlayback(home.Store,sources,relay,home.Paths,new LiveProducer(tools),new EventHub());await playback.StartAsync(CancellationToken.None);
        try{using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));var launch=await playback.StartAsync(channel.Id,actor,timeout.Token,new(Guid.NewGuid().ToString("N"),1));var directory=Path.Combine(home.Paths.Data,"live-cache",launch.Id);var segment=Directory.EnumerateFiles(directory,"segment*.ts").First();var probe=await tools.ProbeAsync(segment,timeout.Token);Assert.Equal("h264",probe!.VideoCodec);Assert.Equal("aac",probe.AudioCodec);Assert.True(probe.Duration>0);Assert.NotEmpty(http.Calls);await playback.CloseAsync(launch.Id,actor);Assert.Empty(home.Store.ActiveLiveSessions());Assert.False(Directory.Exists(directory));Assert.Equal(hash,StateFolders.Hash(input));Assert.Empty(home.Store.GetDownloads());}
        finally{await playback.StopAsync(CancellationToken.None);await app.StopAsync();}
    }
}
