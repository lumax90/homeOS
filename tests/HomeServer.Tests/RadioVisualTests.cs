using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HomeServer.Core;
using HomeServer.Infrastructure;
using HomeServer.LiveTv;
using HomeServer.Music;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace HomeServer.Tests;

public sealed class RadioVisualTests
{
    private const string StationId="11111111-2222-4333-8444-555555555555";
    private sealed class SceneSource(byte[] bytes):IMusicSceneSource
    {
        public int Calls;
        public Task<HttpResponseMessage> GetAsync(string url,CancellationToken token){Calls++;return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(bytes)});}
    }
    private sealed class RadioSource:IRadioStreamSource
    {public Task<HttpResponseMessage> GetAsync(string url,CancellationToken token,string? range=null)=>throw new InvalidOperationException("Visual output uses the private relay.");}
    private static async Task<string> Fixture(TestWorkspace home,MediaTools tools)
    {
        var path=Path.Combine(home.Root,"isolated-scene-with-audio.mp4");
        await MediaProcess.RunAsync(tools.FfmpegExecutable,["-hide_banner","-loglevel","error","-nostdin","-f","lavfi","-i","testsrc2=size=160x90:rate=24","-f","lavfi","-i","sine=frequency=1200:sample_rate=48000","-t","4","-c:v","libx264","-threads","1","-preset","ultrafast","-c:a","aac",path],TimeSpan.FromSeconds(30),CancellationToken.None);
        return path;
    }
    [Fact]
    public void SceneCatalogAllowsOnlyTheReviewedSelectionAndKeepsAttribution()
    {
        var scene=MusicSceneCatalog.Require("fireplace-original");Assert.Equal("CC BY 3.0",scene.License);Assert.Contains("77473809",scene.Source);
        Assert.Contains("33649027",MusicSceneCatalog.Require("rain-window-07").Source);
        Assert.Throws<DomainException>(()=>MusicSceneCatalog.Require("https://arbitrary.invalid/video"));Assert.Throws<DomainException>(()=>MusicSceneCatalog.Require("../other"));
    }
    [MediaFact]
    public async Task ScenePreparationStripsAudioCachesOnceAndRejectsModifiedOutput()
    {
        using var home=new TestWorkspace();var tools=SubtitleBurnTests.Tools(home);var input=await Fixture(home,tools);var original=StateFolders.Hash(input);
        var source=new SceneSource(await File.ReadAllBytesAsync(input));var cache=new MusicSceneCache(home.Paths,tools,source);
        await cache.StartAsync(CancellationToken.None);
        var first=await cache.PrepareAsync("fireplace-original",CancellationToken.None);var next=await cache.PrepareAsync("fireplace-original",CancellationToken.None);
        Assert.Equal(first,next);Assert.Equal(1,source.Calls);Assert.True(File.Exists(first.Poster));
        var probe=(await tools.ProbeAsync(first.Path,CancellationToken.None))!;Assert.Equal("h264",probe.VideoCodec);Assert.Null(probe.AudioCodec);Assert.Single(probe.Streams!);Assert.Equal(24,probe.Streams![0].FrameRate);
        Assert.Equal(original,StateFolders.Hash(input));await File.AppendAllTextAsync(first.Path,"modified fixture");
        await Assert.ThrowsAsync<DomainException>(()=>cache.OpenAsync(first.Id,CancellationToken.None));Assert.Equal(1,source.Calls);
        await cache.StopAsync(CancellationToken.None);
    }
    [MediaFact]
    public async Task OversizedSceneResponseDoesNotPublishAPartialAssetOrLeaveWork()
    {
        using var home=new TestWorkspace();var tools=SubtitleBurnTests.Tools(home);var cache=new MusicSceneCache(home.Paths,tools,new LargeSource());
        // Rejection occurs before any decoder is started.
        Assert.Equal(413,(await Assert.ThrowsAsync<DomainException>(()=>cache.PrepareAsync("fireplace-original",CancellationToken.None))).StatusCode);
        Assert.Empty(Directory.EnumerateDirectories(Path.Combine(home.Paths.Data,"downloads","staging","music-scenes")));
        Assert.Null(await cache.OpenAsync("fireplace-original",CancellationToken.None));
    }
    private sealed class LargeSource:IMusicSceneSource
    {public Task<HttpResponseMessage> GetAsync(string url,CancellationToken token){var value=new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent([])};value.Content.Headers.ContentLength=129L*1024*1024;return Task.FromResult(value);}}
    [MediaFact]
    public async Task HttpScenePreparationNeedsAnActiveRadioLoginCsrfAndExplicitOnlineScenes()
    {
        using var home=new TestWorkspace();var tools=SubtitleBurnTests.Tools(home);var source=new SceneSource(await File.ReadAllBytesAsync(await Fixture(home,tools)));
        await using var factory=new Factory(home,source);using var client=factory.CreateClient();
        async Task Session(HttpClient browser)
        {
            var session=await browser.GetFromJsonAsync<JsonElement>("/api/session");foreach(var key in new[]{"X-CSRF-TOKEN","X-Home-User","X-Home-Session"})browser.DefaultRequestHeaders.Remove(key);
            browser.DefaultRequestHeaders.Add("X-CSRF-TOKEN",session.GetProperty("csrfToken").GetString());
            if(session.GetProperty("authenticated").GetBoolean()){browser.DefaultRequestHeaders.Add("X-Home-User",session.GetProperty("userId").GetString());browser.DefaultRequestHeaders.Add("X-Home-Session",session.GetProperty("sessionId").GetString());}
        }
        await Session(client);(await client.PostAsJsonAsync("/api/setup",new Credentials("Radio fixture owner","isolated-radio-visual-password"))).EnsureSuccessStatusCode();await Session(client);
        var response=await client.PostAsJsonAsync("/api/music/radio/play",new RadioPlayRequest(StationId,Guid.NewGuid().ToString("N")));response.EnsureSuccessStatusCode();var radio=(await response.Content.ReadFromJsonAsync<RadioLaunch>())!;
        var path=$"/api/music/radio/sessions/{radio.Id}/scenes/fireplace-original/prepare";
        Assert.Equal(HttpStatusCode.Conflict,(await client.PostAsync(path,null)).StatusCode);Assert.Equal(0,source.Calls);
        var preferences=(await client.GetFromJsonAsync<MusicExperienceOptions>("/api/music/experience"))!;(await client.PutAsJsonAsync("/api/music/experience",preferences with{OnlineScenes=true})).EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsync(path,null)).StatusCode);await Session(client);
        using var other=factory.CreateClient();await Session(other);(await other.PostAsJsonAsync("/api/auth/login",new Credentials("Radio fixture owner","isolated-radio-visual-password"))).EnsureSuccessStatusCode();await Session(other);
        Assert.Equal(HttpStatusCode.Gone,(await other.PostAsync(path,null)).StatusCode);Assert.Equal(0,source.Calls);
        (await client.PostAsync(path,null)).EnsureSuccessStatusCode();(await client.PostAsync(path,null)).EnsureSuccessStatusCode();Assert.Equal(1,source.Calls);
        using var anonymous=factory.CreateClient();Assert.Equal(HttpStatusCode.Unauthorized,(await anonymous.GetAsync("/api/music/scenes/fireplace-original/poster")).StatusCode);
        var poster=await client.GetAsync("/api/music/scenes/fireplace-original/poster");poster.EnsureSuccessStatusCode();Assert.Equal("image/jpeg",poster.Content.Headers.ContentType!.MediaType);
        (await client.DeleteAsync($"/api/music/radio/sessions/{radio.Id}")).EnsureSuccessStatusCode();Assert.Equal(HttpStatusCode.Gone,(await client.PostAsync(path,null)).StatusCode);Assert.Equal(1,source.Calls);
    }
    private sealed class Factory(TestWorkspace home,IMusicSceneSource sceneSource):WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");builder.ConfigureAppConfiguration((_,config)=>config.AddInMemoryCollection(new Dictionary<string,string?>
            {["Home:DataPath"]=home.Paths.Data,["Home:FfmpegPath"]=Environment.GetEnvironmentVariable("HOME_TEST_FFMPEG"),["Home:FfprobePath"]=Environment.GetEnvironmentVariable("HOME_TEST_FFPROBE"),["Home:Downloads:LoopbackOnly"]="true"}));
            builder.ConfigureServices(services=>
            {
                services.RemoveAll<IMusicSceneSource>();services.AddSingleton(sceneSource);services.RemoveAll<RadioDirectory>();
                services.AddSingleton(provider=>new RadioDirectory(home.Paths,provider.GetRequiredService<MusicExperiencePreferences>(),new MusicExperienceTests.Handler(request=>MusicExperienceTests.Json(request.RequestUri!.AbsolutePath.Contains("/url/")?new{ok=true}:(object)new[]{new{stationuuid=StationId,name="Isolated radio",url_resolved="https://fixture.invalid/radio.mp3",codec="MP3",bitrate=128,lastcheckok=1}}))));
            });
        }
    }
    [MediaFact]
    public async Task RadioAndSceneShareOneLeaseAndOneAvOutputAndLateSelectionsCannotRestartIt()
    {
        using var home=new TestWorkspace();var(actor,login)=MusicExperienceTests.Owner(home);var tools=SubtitleBurnTests.Tools(home);
        var video=await Fixture(home,tools);var audio=Path.Combine(home.Root,"isolated-radio.mp3");
        await MediaProcess.RunAsync(tools.FfmpegExecutable,["-hide_banner","-loglevel","error","-nostdin","-f","lavfi","-i","sine=frequency=440:sample_rate=48000","-t","30","-c:a","libmp3lame",audio],TimeSpan.FromSeconds(20),CancellationToken.None);
        var prefs=new MusicExperiencePreferences(home.Paths,home.Store,new EventHub());prefs.Save(actor,login,new(OnlineScenes:true));
        var sceneSource=new SceneSource(await File.ReadAllBytesAsync(video));var cache=new MusicSceneCache(home.Paths,tools,sceneSource);await cache.StartAsync(CancellationToken.None);var scene=await cache.PrepareAsync("fireplace-original",CancellationToken.None);
        const string station="11111111-2222-4333-8444-555555555555";
        using var handler=new MusicExperienceTests.Handler(request=>MusicExperienceTests.Json(request.RequestUri!.AbsolutePath.Contains("/url/")?new{ok=true}:(object)new[]{new{stationuuid=station,name="Isolated radio",url_resolved="https://fixture.invalid/radio.mp3",codec="MP3",bitrate=128,lastcheckok=1}}));
        using var directory=new RadioDirectory(home.Paths,prefs,handler);using var radio=new RadioPlayback(home.Store,directory,new RadioSource(),new EventHub());
        home.Store.SaveUserPlaybackLimits(actor.Id,new(1,1));var launch=await radio.StartAsync(actor,login,new(station,Guid.NewGuid().ToString("N")),CancellationToken.None);
        var http=new LiveTvExperienceTests.Http(_=>{var result=new HttpResponseMessage(HttpStatusCode.OK){Content=new StreamContent(File.OpenRead(audio))};result.Content.Headers.ContentType=new("audio/mpeg");return result;});
        var builder=WebApplication.CreateBuilder();builder.Logging.ClearProviders();builder.WebHost.UseUrls("http://127.0.0.1:0");await using var app=builder.Build();LiveInputRelay? relay=null;
        app.MapGet("/internal/live/{key}/{node}",(string key,string node,HttpContext context)=>relay!.ServeAsync(key,node,context));await app.StartAsync();
        relay=new(http,app.Services.GetRequiredService<IServer>(),new ConfigurationBuilder().Build());using var visuals=new RadioVisualPlayback(home.Paths,tools,radio,cache,prefs,relay);await visuals.StartAsync(CancellationToken.None);
        try
        {
            var first=await visuals.ConfigureAsync(launch.Id,actor,login,new(scene.Id,1),CancellationToken.None);Assert.True(first.Hls);Assert.Equal(scene.Id,first.SceneId);Assert.Single(home.Store.ActiveMusicRadioSessions());Assert.Empty(home.Store.ActivePlaybackLeases());
            var folder=Path.Combine(home.Paths.Data,"radio-visual",first.Generation!);var sample=Path.Combine(home.Root,"isolated-single-player.mp4");
            await using(var output=File.Create(sample))foreach(var file in new[]{"init.mp4","segment000000.m4s"}){await using var input=File.OpenRead(Path.Combine(folder,file));await input.CopyToAsync(output);}
            var probe=(await tools.ProbeAsync(sample,CancellationToken.None))!;Assert.Equal("h264",probe.VideoCodec);Assert.Equal("aac",probe.AudioCodec);Assert.Equal(2,probe.Streams!.Count(stream=>stream.Kind is "video" or "audio"));
            var otherLogin=home.Store.CreateLoginSession(actor.Id,1,"different fixture").Id;
            Assert.Throws<DomainException>(()=>visuals.Artifact(launch.Id,first.Generation!,"init.mp4",actor,otherLogin));
            var reverted=await visuals.ConfigureAsync(launch.Id,actor,login,new(null,2),CancellationToken.None);Assert.Equal(launch.Url,reverted.Url);Assert.False(Directory.Exists(folder));Assert.Single(home.Store.ActiveMusicRadioSessions());
            await Assert.ThrowsAsync<DomainException>(()=>visuals.ConfigureAsync(launch.Id,actor,login,new(scene.Id,1),CancellationToken.None));
            var second=await visuals.ConfigureAsync(launch.Id,actor,login,new(scene.Id,3),CancellationToken.None);radio.End(launch.Id,actor,login);
            Assert.Throws<DomainException>(()=>visuals.Artifact(launch.Id,second.Generation!,"init.mp4",actor,login));Assert.Empty(home.Store.ActiveMusicRadioSessions());Assert.Equal(1,sceneSource.Calls);
        }
        finally{radio.End(launch.Id,actor,login);await visuals.StopAsync(CancellationToken.None);await cache.StopAsync(CancellationToken.None);await app.StopAsync();}
    }
}
