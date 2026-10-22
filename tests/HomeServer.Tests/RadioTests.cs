using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using HomeServer.Core;
using HomeServer.Infrastructure;
using HomeServer.Music;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HomeServer.Tests;

public sealed class RadioTests
{
    private const string StationId="11111111-2222-4333-8444-555555555555";
    private static RadioStation Station(bool hls=false)=>new(StationId,"Isolated Jazz","https://stream.example.test/"+(hls?"index.m3u8":"live.mp3"),"TR","turkish",["jazz","ambient"],"MP3",128,hls);
    private static object StationJson(RadioStation? station=null)
    {var s=station??Station();return new{stationuuid=s.Id,name=s.Name,url_resolved=s.Url,countrycode=s.Country,language=s.Language,tags=string.Join(',',s.Tags),codec=s.Codec,bitrate=s.Bitrate,hls=s.Hls?1:0,lastcheckok=1,homepage="https://example.test"};}
    private static MusicExperienceTests.Handler DirectoryHandler(RadioStation? station=null)=>new(request=>MusicExperienceTests.Json(request.RequestUri!.AbsolutePath.Contains("/url/")?new{ok=true}:new[]{StationJson(station)}));
    private sealed class Streams(Func<string,string?,HttpResponseMessage> response):IRadioStreamSource
    {
        public int Calls;public List<(string Url,string? Range)> Requests=[];
        public Task<HttpResponseMessage> GetAsync(string url,CancellationToken token,string? range=null){token.ThrowIfCancellationRequested();Calls++;Requests.Add((url,range));return Task.FromResult(response(url,range));}
    }
    private static HttpResponseMessage Bytes(byte[] bytes,string mime="audio/mpeg",HttpStatusCode status=HttpStatusCode.OK)
    {var response=new HttpResponseMessage(status){Content=new ByteArrayContent(bytes)};response.Content.Headers.ContentType=new MediaTypeHeaderValue(mime);return response;}
    private static DefaultHttpContext Context(){var value=new DefaultHttpContext();value.Response.Body=new MemoryStream();return value;}
    private static string Text(DefaultHttpContext context)=>Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray());

    [Fact]
    public async Task DirectoryMetadataNeverStartsAudioAndFavoritesSurviveRestartWithPrivateCategories()
    {
        using var home=new TestWorkspace();var(actor,login)=MusicExperienceTests.Owner(home);var prefs=new MusicExperiencePreferences(home.Paths,home.Store,new EventHub());using var handler=DirectoryHandler();using var directory=new RadioDirectory(home.Paths,prefs,handler);
        var page=await directory.SearchAsync(actor.Id,"jazz","jazz","TR",0,CancellationToken.None);var station=Assert.Single(page.Items);Assert.False(page.HasMore);Assert.Equal("TR",station.Country);Assert.Contains("countrycode=TR",handler.Uris[0].Query);Assert.Single(handler.Uris);
        directory.Favorite(actor.Id,station,new(true,"Evening room"));var other=home.Store.SaveAccount(null,new AccountInput("Other"),"isolated");Assert.Empty(directory.Favorites(other.Id));
        using var restarted=new RadioDirectory(home.Paths,prefs,new MusicExperienceTests.Handler(_=>throw new InvalidOperationException("Directory was disabled.")));prefs.Save(actor,login,new(RadioDirectory:false));Assert.Equal("Evening room",Assert.Single(restarted.Favorites(actor.Id)).Category);Assert.Equal(station.Id,(await restarted.StationAsync(actor.Id,station.Id,CancellationToken.None)).Id);
        Assert.Equal(409,(await Assert.ThrowsAsync<DomainException>(()=>restarted.SearchAsync(actor.Id,null,null,null,0,CancellationToken.None))).StatusCode);Assert.Empty(home.Store.ActiveMusicRadioSessions());Assert.Empty(home.Store.GetDownloads());
    }
    [Fact]
    public void DirectoryRejectsBrokenMalformedAndPrivateEntriesWithoutLosingPublicOnes()
    {
        var json=JsonSerializer.SerializeToUtf8Bytes(new object?[]{null,4,"unexpected",StationJson(),StationJson(Station() with{Id=Guid.NewGuid().ToString(),Url="http://127.0.0.1:5080/api/session"}),new{stationuuid=Guid.NewGuid().ToString(),name="broken",url="https://example.test/live",lastcheckok=0}});
        Assert.Equal(StationId,Assert.Single(RadioDirectory.Parse(json)).Id);
        foreach(var url in new[]{"file:///secret","http://[::1]/","http://192.168.1.2/","http://10.0.0.1/","http://localhost./","http://user:pass@example.test/","https://example.test/#fragment"})Assert.False(RadioDirectory.ValidPublicUrl(url),url);
        Assert.True(RadioDirectory.ValidPublicUrl("https://stream.example.test:8443/live?station=1"));
    }
    [Fact]
    public async Task DirectoryPaginationUsesRawProviderCountWhenSomeStationsAreFiltered()
    {
        using var home=new TestWorkspace();var(actor,_)=MusicExperienceTests.Owner(home);var prefs=new MusicExperiencePreferences(home.Paths,home.Store,new EventHub());var stations=Enumerable.Range(0,48).Select(i=>StationJson(Station() with{Id=Guid.NewGuid().ToString(),Url=i==0?"http://127.0.0.1/":"https://example.test/"+i})).ToArray();
        using var handler=new MusicExperienceTests.Handler(_=>MusicExperienceTests.Json(stations));using var directory=new RadioDirectory(home.Paths,prefs,handler);var page=await directory.SearchAsync(actor.Id,null,null,null,0,CancellationToken.None);Assert.Equal(47,page.Items.Length);Assert.True(page.HasMore);Assert.DoesNotContain("countrycode",Assert.Single(handler.Uris).Query);
    }
    [Fact]
    public async Task OpaqueRelayForwardsExactBytesRangesAndDoesNotAllowAnotherLoginOrAnEndedSession()
    {
        using var home=new TestWorkspace();var(actor,login)=MusicExperienceTests.Owner(home);var prefs=new MusicExperiencePreferences(home.Paths,home.Store,new EventHub());using var directory=new RadioDirectory(home.Paths,prefs,DirectoryHandler());var payload=Enumerable.Range(0,256).Select(i=>(byte)i).ToArray();
        var source=new Streams((_,range)=>{var result=Bytes(range is null?payload:payload[20..40],status:range is null?HttpStatusCode.OK:HttpStatusCode.PartialContent);if(range is not null)result.Content.Headers.ContentRange=new(20,39,256);return result;});using var playback=new RadioPlayback(home.Store,directory,source,new EventHub());
        var launch=await playback.StartAsync(actor,login,new(StationId,Guid.NewGuid().ToString("N")),CancellationToken.None);Assert.Equal(0,source.Calls);var key=launch.Url.Split('/').Last();var context=Context();context.Request.Headers.Range="bytes=20-39";await playback.ServeAsync(launch.Id,key,actor,login,context);Assert.Equal(206,context.Response.StatusCode);Assert.Equal(payload[20..40],((MemoryStream)context.Response.Body).ToArray());Assert.Equal("bytes=20-39",Assert.Single(source.Requests).Range);
        var otherLogin=home.Store.CreateLoginSession(actor.Id,1,"other browser").Id;Assert.Equal(410,(await Assert.ThrowsAsync<DomainException>(()=>playback.ServeAsync(launch.Id,key,actor,otherLogin,Context()))).StatusCode);
        var bad=Context();bad.Request.Headers.Range="bytes=1-2,8-9";Assert.Equal(416,(await Assert.ThrowsAsync<DomainException>(()=>playback.ServeAsync(launch.Id,key,actor,login,bad))).StatusCode);
        playback.End(launch.Id,actor,login);Assert.Empty(home.Store.ActiveMusicRadioSessions());Assert.Equal(410,(await Assert.ThrowsAsync<DomainException>(()=>playback.ServeAsync(launch.Id,key,actor,login,Context()))).StatusCode);Assert.Equal(1,source.Calls);
    }
    [Fact]
    public async Task HlsRelayRewritesRedirectRelativeSegmentsKeysAndBasePathIntoOwnedOpaqueNodes()
    {
        using var home=new TestWorkspace();var(actor,login)=MusicExperienceTests.Owner(home);using var directory=new RadioDirectory(home.Paths,new(home.Paths,home.Store,new EventHub()),DirectoryHandler(Station(true)));
        var source=new Streams((_,_)=>{var response=Bytes(Encoding.UTF8.GetBytes("#EXTM3U\n#EXT-X-KEY:METHOD=AES-128,URI=\"../keys/key.bin\"\n#EXT-X-MAP:URI=\"init.mp4\"\n#EXTINF:6,\nsegment-1.m4s\n"),"application/vnd.apple.mpegurl");response.RequestMessage=new(HttpMethod.Get,"https://cdn.example.test/final/live/index.m3u8");return response;});using var playback=new RadioPlayback(home.Store,directory,source,new EventHub());var launch=await playback.StartAsync(actor,login,new(StationId,Guid.NewGuid().ToString("N")),CancellationToken.None);
        var context=Context();context.Request.PathBase="/home";await playback.ServeAsync(launch.Id,launch.Url.Split('/').Last(),actor,login,context);var text=Text(context);Assert.DoesNotContain("https://",text);Assert.DoesNotContain("key.bin",text);Assert.Contains("/home/api/music/radio/sessions/"+launch.Id+"/stream/",text);Assert.Contains("URI=\"/home/",text);Assert.Equal("application/vnd.apple.mpegurl",context.Response.ContentType);
        var mapped=new List<string>();RadioPlayback.RewritePlaylist("#EXTM3U\n#EXT-X-KEY:URI=\"../key\"\npart.ts",new Uri("https://cdn.example.test/final/live/index.m3u8"),url=>{mapped.Add(url);return "/owned";});Assert.Equal(new[]{"https://cdn.example.test/final/key","https://cdn.example.test/final/live/part.ts"},mapped);playback.End(launch.Id,actor,login);
    }
    [Theory]
    [InlineData("#EXTM3U\nhttp://127.0.0.1/private")]
    [InlineData("#EXTM3U\n#EXT-X-KEY:URI=\"file:///secret\"")]
    [InlineData("#EXTM3U\n#EXT-X-KEY:URI=https://example.test/key")]
    [InlineData("#EXTM3U\n#EXT-X-CONTENT-STEERING:SERVER-URI=\"https://example.test/steer\"")]
    public void HlsCannotEscapeThePublicRelayBoundary(string text)
    {Assert.Equal(502,Assert.Throws<DomainException>(()=>RadioPlayback.RewritePlaylist(text,new Uri("https://example.test/live"),url=>url)).StatusCode);}
    [Fact]
    public void RadioAndLocalPlaybackShareQuotasAndMonotonicClientIntentAcrossSourceSwitches()
    {
        using var home=new TestWorkspace();var(actor,login)=MusicExperienceTests.Owner(home);var album=MusicExperienceTests.Album(home,actor);var song=Assert.Single(home.Store.GetMusicTracks(album.Id,actor).Items);var identity=home.Store.GetPlaybackActor(actor.Id,login);var client=Guid.NewGuid().ToString("N");home.Store.SaveUserPlaybackLimits(actor.Id,new(1,1));
        var local=home.Store.AdmitPlayback(identity,client,1,song.MediaId,"pcm","fixture",DateTimeOffset.UtcNow);var radio=home.Store.AdmitMusicRadio(Station(),identity,client,2);Assert.Equal("closed",home.Store.GetPlaybackLease(local.Lease.Id)!.Status);Assert.Single(home.Store.ActiveMusicRadioSessions());
        Assert.Equal(429,Assert.Throws<DomainException>(()=>home.Store.AdmitPlayback(identity,Guid.NewGuid().ToString("N"),1,song.MediaId,"pcm","other",DateTimeOffset.UtcNow)).StatusCode);
        Assert.Equal(409,Assert.Throws<DomainException>(()=>home.Store.AdmitPlayback(identity,client,1,song.MediaId,"pcm","old",DateTimeOffset.UtcNow)).StatusCode);
        var resumed=home.Store.AdmitPlayback(identity,client,3,song.MediaId,"pcm","resumed",DateTimeOffset.UtcNow);Assert.Empty(home.Store.ActiveMusicRadioSessions());Assert.Equal(409,Assert.Throws<DomainException>(()=>home.Store.AdmitMusicRadio(Station(),identity,client,2)).StatusCode);
        home.Store.EndPlaybackLease(resumed.Lease.Id,"fixture-end",DateTimeOffset.UtcNow);radio=home.Store.AdmitMusicRadio(Station(),identity,client,4);home.Store.SaveUserPlaybackLimits(actor.Id,new(0,1,1));Assert.Equal(410,Assert.Throws<DomainException>(()=>home.Store.RequireMusicRadio(radio.Id,identity)).StatusCode);
        var restarted=new MediaStore(home.Paths);restarted.Initialize();Assert.Empty(restarted.ActiveMusicRadioSessions());Assert.Equal(409,Assert.Throws<DomainException>(()=>restarted.AdmitMusicRadio(Station(),identity,client,4)).StatusCode);
    }
    private sealed class BlockingStream:Stream
    {
        public readonly TaskCompletionSource Waiting=new(TaskCreationOptions.RunContinuationsAsynchronously);public bool Disposed;private bool _sent;
        public override bool CanRead=>true;public override bool CanSeek=>false;public override bool CanWrite=>false;public override long Length=>throw new NotSupportedException();public override long Position{get=>0;set=>throw new NotSupportedException();}
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken token=default){if(!_sent){_sent=true;buffer.Span[..32].Fill(9);return 32;}Waiting.TrySetResult();await Task.Delay(Timeout.Infinite,token);return 0;}
        protected override void Dispose(bool disposing){Disposed=true;base.Dispose(disposing);}public override int Read(byte[] buffer,int offset,int count)=>throw new NotSupportedException();public override void Flush(){}public override long Seek(long offset,SeekOrigin origin)=>throw new NotSupportedException();public override void SetLength(long length)=>throw new NotSupportedException();public override void Write(byte[] buffer,int offset,int count)=>throw new NotSupportedException();
    }
    private sealed class DelayedDirectory:HttpMessageHandler
    {
        public readonly TaskCompletionSource Started=new(TaskCreationOptions.RunContinuationsAsynchronously);public readonly TaskCompletionSource<HttpResponseMessage> Response=new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){Started.TrySetResult();return await Response.Task.WaitAsync(token);}
    }
    [Fact]
    public async Task LogoutDuringDirectoryLookupCannotPublishALateRadioLease()
    {
        using var home=new TestWorkspace();var(actor,login)=MusicExperienceTests.Owner(home);using var handler=new DelayedDirectory();using var directory=new RadioDirectory(home.Paths,new(home.Paths,home.Store,new EventHub()),handler);var source=new Streams((_,_)=>throw new InvalidOperationException("No source request is authorized."));using var playback=new RadioPlayback(home.Store,directory,source,new EventHub());
        var pending=playback.StartAsync(actor,login,new(StationId,Guid.NewGuid().ToString("N")),CancellationToken.None);await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));home.Store.RevokeLoginSession(login,actor.Id);handler.Response.SetResult(MusicExperienceTests.Json(new[]{StationJson()}));Assert.Equal(401,(await Assert.ThrowsAsync<DomainException>(()=>pending)).StatusCode);Assert.Empty(home.Store.ActiveMusicRadioSessions());Assert.Equal(0,source.Calls);
    }
    [Fact]
    public async Task ClosingRadioCancelsAStalledUpstreamAndReleasesItsStream()
    {
        using var home=new TestWorkspace();var(actor,login)=MusicExperienceTests.Owner(home);using var directory=new RadioDirectory(home.Paths,new(home.Paths,home.Store,new EventHub()),DirectoryHandler());var blocking=new BlockingStream();var source=new Streams((_,_)=>new(HttpStatusCode.OK){Content=new StreamContent(blocking)});using var playback=new RadioPlayback(home.Store,directory,source,new EventHub());var launch=await playback.StartAsync(actor,login,new(StationId,Guid.NewGuid().ToString("N")),CancellationToken.None);
        var pending=playback.ServeAsync(launch.Id,launch.Url.Split('/').Last(),actor,login,Context());await blocking.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(3));playback.End(launch.Id,actor,login);await pending.WaitAsync(TimeSpan.FromSeconds(3));Assert.True(blocking.Disposed);Assert.Empty(home.Store.ActiveMusicRadioSessions());
    }
    [Fact]
    public async Task HttpCommandsRequireCsrfExpectedAccountAndUnratedDiscoveryAndDoNotStartDownloads()
    {
        using var home=new TestWorkspace();var source=new Streams((_,_)=>Bytes([1,2,3,4]));await using var app=new ApiFactory(home,source);using var admin=app.CreateClient();await Session(admin);(await admin.PostAsJsonAsync("/api/setup",new Credentials("Admin","music-fixture-password"))).EnsureSuccessStatusCode();await Session(admin);
        var prefs=(await admin.GetFromJsonAsync<MusicExperienceOptions>("/api/music/experience"))!;admin.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");Assert.Equal(HttpStatusCode.BadRequest,(await admin.PutAsJsonAsync("/api/music/experience",prefs)).StatusCode);await Session(admin);admin.DefaultRequestHeaders.Remove("X-Home-User");Assert.Equal(HttpStatusCode.Conflict,(await admin.PutAsJsonAsync("/api/music/experience",prefs)).StatusCode);await Session(admin);
        (await admin.GetAsync("/api/music/radio/stations?tag=jazz&country=TR")).EnsureSuccessStatusCode();Assert.Equal(0,source.Calls);
        var station=Station();(await admin.PutAsJsonAsync("/api/music/radio/favorites/"+station.Id,new RadioFavoriteCommand(true,"Desk"))).EnsureSuccessStatusCode();Assert.Single((await admin.GetFromJsonAsync<RadioFavorite[]>("/api/music/radio/favorites"))!);
        (await admin.PostAsJsonAsync("/api/users",new CreateAccount(new AccountInput("Child",MaximumAge:12,AllowUnrated:false),"music-fixture-password"))).EnsureSuccessStatusCode();using var child=app.CreateClient();await Session(child);(await child.PostAsJsonAsync("/api/auth/login",new Credentials("Child","music-fixture-password"))).EnsureSuccessStatusCode();await Session(child);
        Assert.Equal(HttpStatusCode.Forbidden,(await child.GetAsync("/api/music/radio/stations")).StatusCode);Assert.Empty((await child.GetFromJsonAsync<RadioFavorite[]>("/api/music/radio/favorites"))!);Assert.Equal(HttpStatusCode.Forbidden,(await child.PostAsJsonAsync("/api/music/radio/play",new RadioPlayRequest(station.Id,Guid.NewGuid().ToString("N")))).StatusCode);
        Assert.Equal(0,source.Calls);var started=await admin.PostAsJsonAsync("/api/music/radio/play",new RadioPlayRequest(station.Id,Guid.NewGuid().ToString("N")));started.EnsureSuccessStatusCode();var launch=(await started.Content.ReadFromJsonAsync<RadioLaunch>())!;
        Assert.Equal(new byte[]{1,2,3,4},await admin.GetByteArrayAsync(launch.Url));Assert.Equal(1,source.Calls);var usage=(await admin.GetFromJsonAsync<PlaybackUsage>("/api/account/playback"))!;Assert.Contains(usage.Active,item=>item.Mode=="radio"&&item.Id=="radio-"+launch.Id);
        (await admin.DeleteAsync("/api/music/radio/sessions/"+launch.Id)).EnsureSuccessStatusCode();Assert.Equal(HttpStatusCode.Gone,(await admin.GetAsync(launch.Url)).StatusCode);Assert.Empty(app.Services.GetRequiredService<MediaStore>().GetDownloads());
    }
    private static async Task Session(HttpClient client)
    {var session=await client.GetFromJsonAsync<JsonElement>("/api/session");foreach(var key in new[]{"X-CSRF-TOKEN","X-Home-User","X-Home-Session"})client.DefaultRequestHeaders.Remove(key);client.DefaultRequestHeaders.Add("X-CSRF-TOKEN",session.GetProperty("csrfToken").GetString());if(session.GetProperty("authenticated").GetBoolean()){client.DefaultRequestHeaders.Add("X-Home-User",session.GetProperty("userId").GetString());client.DefaultRequestHeaders.Add("X-Home-Session",session.GetProperty("sessionId").GetString());}}
    private sealed class ApiFactory(TestWorkspace home,IRadioStreamSource source):WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {builder.UseEnvironment("Testing");builder.ConfigureAppConfiguration((_,config)=>config.AddInMemoryCollection(new Dictionary<string,string?>{["Home:DataPath"]=home.Paths.Data,["Home:FfmpegPath"]="absent",["Home:FfprobePath"]="absent",["Home:Downloads:LoopbackOnly"]="true"}));builder.ConfigureServices(services=>{services.RemoveAll<RadioDirectory>();services.AddSingleton(provider=>new RadioDirectory(home.Paths,provider.GetRequiredService<MusicExperiencePreferences>(),DirectoryHandler()));services.RemoveAll<IRadioStreamSource>();services.AddSingleton(source);});}
    }
}
