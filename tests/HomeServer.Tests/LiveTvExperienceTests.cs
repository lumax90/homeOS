using System.Net;
using System.Text;
using System.Text.Json;
using HomeServer.Core;
using HomeServer.Infrastructure;
using HomeServer.LiveTv;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Caching.Memory;

namespace HomeServer.Tests;

public sealed class LiveTvExperienceTests
{
    internal static (Account User,PlaybackActor Actor) Owner(TestWorkspace home){home.Store.CreateOwner(new("Owner","isolated"));var login=home.Store.CreateLoginSession("1",1,"live-fixture");return(home.Store.GetAccount("1")!,home.Store.GetPlaybackActor("1",login.Id));}
    internal sealed class Http(Func<string,HttpResponseMessage> response):ILiveHttp
    {
        public List<(string Url,string? Range,IReadOnlyDictionary<string,string>? Headers)> Calls=[];
        public Task<HttpResponseMessage> GetAsync(string url,bool allowLan,CancellationToken token,string? range=null,IReadOnlyDictionary<string,string>? headers=null){token.ThrowIfCancellationRequested();Calls.Add((url,range,headers));return Task.FromResult(response(url));}
    }
    internal static HttpResponseMessage Text(string text,string? final=null)=>new(HttpStatusCode.OK){Content=new StringContent(text,Encoding.UTF8,"application/vnd.apple.mpegurl"),RequestMessage=final is null?null:new(HttpMethod.Get,final)};
    internal static LiveSources Service(TestWorkspace home,ILiveHttp http)=>new(home.Store,http,DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(home.Paths.State,"keys"))),new EventHub());
    internal static async Task Sync(TestWorkspace home,LiveSources service,LiveSource source,Account user){service.Queue(source.Id,user);var work=home.Store.ClaimProductJob(["live-source-sync"])!;await service.RunAsync(work,CancellationToken.None);}
    [Fact]
    public void PlaylistKeepsCommasQuotedAttributesRelativeUrlsAndPerChannelHeaders()
    {
        var channels=PlaylistGuide.Playlist("""
            #EXTM3U
            #EXTINF:-1 tvg-id="one" group-title="News, world" tvg-logo="../logo.png",A channel, HD
            #EXTVLCOPT:http-user-agent=Fixture TV
            #EXTVLCOPT:http-referrer=https://broadcaster.example/
            live/one.m3u8
            #EXTINF:-1 tvg-id='two' group-title='Music' group-title="Radio" radio="true",Two
            https://example.test/radio|User-Agent=Another%20TV
            """,new Uri("https://example.test/lists/index.m3u"));
        Assert.Equal(2,channels.Length);Assert.Equal("A channel, HD",channels[0].Name);Assert.Equal("News, world",channels[0].Group);Assert.Equal("https://example.test/lists/live/one.m3u8",channels[0].Url);Assert.Equal("https://example.test/logo.png",channels[0].Logo);Assert.Equal("Fixture TV",channels[0].Headers!["User-Agent"]);Assert.Equal("https://broadcaster.example/",channels[0].Headers!["Referer"]);
        Assert.True(channels[1].Radio);Assert.Equal("Radio",channels[1].Group);Assert.Equal("Another TV",channels[1].Headers!["User-Agent"]);Assert.False(channels[1].Headers!.ContainsKey("Referer"));
        Assert.Equal(PlaylistGuide.Id("one|A channel, HD|News, world"),channels[0].Key);
    }
    [Theory]
    [InlineData("#EXTM3U\n#EXT-X-TARGETDURATION:6\n#EXTINF:6,\nsegment.ts")]
    [InlineData("#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=1000000\nmain.m3u8")]
    [InlineData("#EXTM3U\n#EXTINF:-1,Bad\nfile:///secret")]
    [InlineData("#EXTM3U\n")]
    public void MalformedOrMediaPlaylistsAreNotImportedAsChannelLibraries(string text)
    {Assert.Throws<DomainException>(()=>PlaylistGuide.Playlist(text));}
    [Fact]
    public void InvalidOptionalReferrerIsNotForwardedAndDoesNotDiscardAValidChannel()
    {
        var channel=Assert.Single(PlaylistGuide.Playlist("#EXTM3U\n#EXTINF:-1,Existing channel\n#EXTVLCOPT:http-referrer=No response\nhttps://example.test/stream.m3u8"));Assert.Null(channel.Headers);Assert.Equal("Existing channel",channel.Name);
    }
    [Fact]
    public void MixedPublicFeedsKeepHttpChannelsAndIgnoreUnsupportedNetworkTransports()
    {
        var channels=PlaylistGuide.Playlist("#EXTM3U\n#EXTINF:-1,HTTP\nhttps://example.test/live.m3u8#cell=region\n#EXTINF:-1,RTMP\nrtmp://example.test/live\n#EXTINF:-1,SRT\nsrt://example.test:4000\n#EXTINF:-1,TS\nhttps://example.test/live.ts");Assert.Equal(new[]{"HTTP","TS"},channels.Select(c=>c.Name));Assert.Equal("https://example.test/live.m3u8",channels[0].Url);
    }
    [Fact]
    public async Task DirectLinksRequireNoMediaFetchAndSecretsStayEncryptedInPublicViews()
    {
        using var home=new TestWorkspace();var(user,_)=Owner(home);var http=new Http(_=>throw new InvalidOperationException("Adding a direct stream must not open it."));var service=Service(home,http);
        var source=service.Save(null,new("Private TV",StreamUrl:"https://provider.example/live?username=one&password=keep-secret",Group:"Home"),user);await Sync(home,service,source,user);var channel=Assert.Single(home.Store.LiveChannels(user));
        Assert.Empty(http.Calls);Assert.Contains("keep-secret",service.ChannelUrl(channel.Id));Assert.DoesNotContain("keep-secret",home.Store.LiveChannelSecret(channel.Id));Assert.DoesNotContain("keep-secret",home.Store.LiveSourceSecret(source.Id));Assert.DoesNotContain("keep-secret",JsonSerializer.Serialize(new{settings=service.PublicSettings(source.Id),library=home.Store.BrowseLiveChannels(user)}));
        var updated=service.Save(source.Id,new("Renamed",ExpectedRevision:source.Revision,Group:"Home"),user);await Sync(home,service,updated,user);Assert.Equal(channel.Id,Assert.Single(home.Store.LiveChannels(user)).Id);Assert.Equal("Renamed",home.Store.GetLiveChannel(channel.Id).Name);Assert.Empty(home.Store.GetDownloads());
    }
    [Fact]
    public async Task PublicListInstallIsIdempotentAndFailedRefreshKeepsFavoritesOverridesAndLastCatalog()
    {
        using var home=new TestWorkspace();var(user,_)=Owner(home);var content="#EXTM3U\n#EXTINF:-1 tvg-id=\"one\" tvg-logo=\"https://example.test/logo?token=logo-secret\" group-title=\"News\",İstanbul TV\n#EXTVLCOPT:http-referrer=https://example.test/?token=ref-secret\nhttps://example.test/live?token=stream-secret";
        var http=new Http(_=>Text(content));var service=Service(home,http);using var cache=new MemoryCache(new MemoryCacheOptions{SizeLimit=100});var catalog=new LivePublicCatalog(http,cache,service,home.Store);
        var first=catalog.Add("iptv-tr",user);var again=catalog.Add("iptv-tr",user);Assert.Equal(first.Id,again.Id);Assert.Empty(http.Calls);Assert.Single(home.Store.LiveSources());await Sync(home,service,first,user);var channel=Assert.Single(home.Store.LiveChannels(user));
        Assert.Equal("https://example.test/logo?token=logo-secret",service.ChannelLogo(channel.Id));Assert.DoesNotContain("logo-secret",home.Store.LiveChannelLogoSecret(channel.Id));Assert.DoesNotContain("ref-secret",home.Store.LiveChannelHeadersSecret(channel.Id));Assert.Equal("https://example.test/?token=ref-secret",service.ChannelHeaders(channel.Id)!["Referer"]);
        home.Store.SetLiveFavorite(channel.Id,user,true);home.Store.SetLiveChannel(channel.Id,new(true,12,7,"manual-epg"));var before=home.Store.GetLiveChannel(channel.Id);content="#EXTM3U\n";await Assert.ThrowsAsync<DomainException>(()=>Sync(home,service,first,user));Assert.Equal(before,home.Store.GetLiveChannel(channel.Id));Assert.Equal(1,home.Store.BrowseLiveChannels(user,view:"favorites").Total);Assert.NotNull(home.Store.GetLiveSource(first.Id).LastError);
        Assert.Equal(channel.Id,Assert.Single(home.Store.BrowseLiveChannels(user,query:"istanbul").Items).Channel.Id);Assert.StartsWith("/api/live/channels/",home.Store.BrowseLiveChannels(user).Items[0].Channel.Logo);
    }
    [Fact]
    public void FavoritePagingHistoryAndCurrentProgrammeAgeRespectEachProfile()
    {
        using var home=new TestWorkspace();var(user,actor)=Owner(home);var source=home.Store.SaveLiveSource(null,new("Fixture",StreamUrl:"https://example.test/live"),"protected",user);var channels=home.Store.SyncLiveChannels(source,Enumerable.Range(0,125).Select(i=>new ParsedChannel("channel-"+i,"Channel "+i.ToString("D3"),i%2==0?"News":"Music",null,null,"https://example.test/"+i,false,i)).ToArray(),value=>value);
        var one=channels.First(c=>c.Name=="Channel 000");home.Store.SetLiveFavorite(one.Id,user,true);var child=home.Store.SaveAccount(null,new AccountInput("Child",MaximumAge:12,AllowUnrated:false),"fixture");home.Store.SetLiveChannel(one.Id,new(true,0,0));
        var now=DateTimeOffset.UtcNow;var programme=new LiveProgramme(Guid.NewGuid().ToString("N"),one.Id,now.AddMinutes(-10).ToString("O"),now.AddMinutes(10).ToString("O"),"Rated programme","Description","fixture",false,18);home.Store.SaveLiveGuide(source,[programme]);
        Assert.Equal(125,home.Store.BrowseLiveChannels(user).Total);Assert.Equal(60,home.Store.BrowseLiveChannels(user).Items.Length);Assert.Equal(5,home.Store.BrowseLiveChannels(user,120).Items.Length);Assert.Equal(0,home.Store.BrowseLiveChannels(child).Total);Assert.Empty(home.Store.BrowseLiveChannels(child,view:"favorites").Items);
        var card=home.Store.LiveChannelDetails(one.Id,user);Assert.Equal(programme.Id,card.Now!.Id);Assert.True(card.Favorite);Assert.Throws<DomainException>(()=>home.Store.LiveChannelDetails(one.Id,child));
        var lease=home.Store.AdmitLiveSession(one.Id,actor);home.Store.MarkLiveWatched(lease.Id,actor);Assert.Equal(one.Id,Assert.Single(home.Store.BrowseLiveChannels(user,view:"recent").Items).Channel.Id);Assert.Empty(home.Store.BrowseLiveChannels(child,view:"recent").Items);
        home.Store.SetLiveSession(lease.Id,"closed");home.Store.SaveLiveSource(source.Id,new("Fixture",Enabled:false,ExpectedRevision:source.Revision),"protected",user);var favorite=Assert.Single(home.Store.BrowseLiveChannels(user,view:"favorites").Items);Assert.False(favorite.Playable);Assert.Equal(one.Id,favorite.Channel.Id);Assert.Empty(home.Store.BrowseLiveChannels(user).Items);
    }
    [Fact]
    public void ClientRevisionReplacesOneTunerAndSharesSlotsWithMusicWithoutRevivingOlderCommands()
    {
        using var home=new TestWorkspace();var(user,actor)=Owner(home);var source=home.Store.SaveLiveSource(null,new("Fixture",Tuners:1,StreamUrl:"https://example.test/live"),"protected",user);var channels=home.Store.SyncLiveChannels(source,[new("one","One","",null,null,"https://example.test/one",false,1),new("two","Two","",null,null,"https://example.test/two",false,2)],value=>value);home.Store.SaveUserPlaybackLimits(user.Id,new(1,1));var client=Guid.NewGuid().ToString("N");
        var one=home.Store.AdmitLiveSession(channels[0].Id,actor,client,1);var two=home.Store.AdmitLiveSession(channels[1].Id,actor,client,2);Assert.Equal("closed",home.Store.GetLiveSession(one.Id)!.Status);Assert.Single(home.Store.ActiveLiveSessions());Assert.Equal(1,home.Store.SourceTunerUse(source.Id));Assert.Equal(409,Assert.Throws<DomainException>(()=>home.Store.AdmitLiveSession(channels[0].Id,actor,client,1)).StatusCode);
        Assert.Equal(429,Assert.Throws<DomainException>(()=>home.Store.AdmitLiveSession(channels[0].Id,actor,Guid.NewGuid().ToString("N"),1)).StatusCode);home.Store.CloseLiveClient(actor,client,1);Assert.Equal("starting",home.Store.GetLiveSession(two.Id)!.Status);home.Store.CloseLiveClient(actor,client,2);Assert.Empty(home.Store.ActiveLiveSessions());Assert.Throws<DomainException>(()=>home.Store.AdmitLiveSession(channels[0].Id,actor,client,2));
        home.Store.AdmitLiveSession(channels[0].Id,actor,client,3);var album=MusicExperienceTests.Album(home,user);var track=home.Store.GetMusicTracks(album.Id,user).Items[0];home.Store.AdmitPlayback(actor,client,4,track.MediaId,"pcm","fixture",DateTimeOffset.UtcNow);Assert.Empty(home.Store.ActiveLiveSessions());Assert.Single(home.Store.ActivePlaybackLeases());
    }
    [Fact]
    public void MigrationFrom42KeepsSourcesChannelOverridesDvrRowsAndCredentials()
    {
        using var home=new TestWorkspace();var directory=Path.Combine(home.Root,"schema42");Directory.CreateDirectory(directory);var database=Path.Combine(directory,"home.db");
        using(var db=new SqliteConnection($"Data Source={database}")){db.Open();using var command=db.CreateCommand();command.CommandText="PRAGMA foreign_keys=OFF";command.ExecuteNonQuery();foreach(var name in typeof(MediaStore).Assembly.GetManifestResourceNames().Where(name=>name.Contains(".Migrations.")&&int.TryParse(name.Split(".Migrations.")[1][..3],out var number)&&number<=42).Order()){using var stream=typeof(MediaStore).Assembly.GetManifestResourceStream(name)!;using var reader=new StreamReader(stream);command.CommandText=reader.ReadToEnd();command.ExecuteNonQuery();if(name.EndsWith("001_initial.sql",StringComparison.Ordinal)){command.CommandText="INSERT INTO owner VALUES(1,'Owner','preserved')";command.ExecuteNonQuery();}}
            command.CommandText="""
                INSERT INTO live_sources(id,name,protected_input,enabled,tuners,created_by,next_sync) VALUES('source','Existing','secret-input',1,2,'1','2026-10-22');
                INSERT INTO live_channels(id,source_id,source_key,name,group_name,epg_override,protected_url,minimum_age,ordinal) VALUES('channel','source','key','Existing channel','News','manual-id','secret-url',12,7);
                INSERT INTO dvr_rules(id,user_id,user_version,channel_id,title,title_key,enabled,only_new,padding_before,padding_after,priority) VALUES('rule','1',1,'channel','Existing show','existing show',1,1,60,120,25);
                PRAGMA user_version=42;
                """;command.ExecuteNonQuery();}
        var store=new MediaStore(new(directory,Path.Combine(directory,"thumbnails"),null));store.Initialize();store.Initialize();Assert.Equal("secret-input",store.LiveSourceSecret("source"));Assert.Equal("secret-url",store.LiveChannelSecret("channel"));Assert.Equal("manual-id",store.GetLiveChannel("channel").EpgId);Assert.Equal(7,store.GetLiveChannel("channel").Ordinal);Assert.Single(store.DvrRules(store.GetAccount("1")!));
        using var verified=new SqliteConnection($"Data Source={database}");verified.Open();using var check=verified.CreateCommand();check.CommandText="PRAGMA foreign_key_check";using var rows=check.ExecuteReader();Assert.False(rows.Read());
    }
}
