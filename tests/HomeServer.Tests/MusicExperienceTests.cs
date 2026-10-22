using System.Net;
using System.Text;
using System.Text.Json;
using HomeServer.Core;
using HomeServer.Infrastructure;
using HomeServer.Music;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace HomeServer.Tests;

public sealed class MusicExperienceTests
{
    private const string Group="aaaaaaaa-1111-4111-8111-111111111111";
    internal static (Account Actor,string Login) Owner(TestWorkspace home)
    {home.Store.CreateOwner(new("Owner","isolated"));return(home.Store.GetAccount("1")!,home.Store.CreateLoginSession("1",1,"music-experience-fixture").Id);}
    internal static MusicAlbum Album(TestWorkspace home,Account actor,string title="An Album",string artist="An Artist",int index=0,string? group=null)
    {
        var library=home.Store.GetLibraries().FirstOrDefault(l=>l.Kind=="music")??home.Store.AddLibrary(new AddLibrary("Music",home.Source,"music"));
        var path=home.Write("original-"+index+".flac");var item=home.Store.UpsertFile(library.Id,MediaPaths.Inspect(home.Source,path)!,"fixture");
        home.Store.SaveMusicTags(item.Id,new("Track "+index,artist,artist,title,"2001",1,index+1,null,group,null,null,null,null,16));
        return home.Store.GetMusicAlbum(home.Store.MusicAlbumForMedia(item.Id)!,actor)!;
    }
    internal sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> respond):HttpMessageHandler
    {
        public int Calls;public List<Uri> Uris=[];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){token.ThrowIfCancellationRequested();Calls++;Uris.Add(request.RequestUri!);return Task.FromResult(respond(request));}
    }
    internal sealed class Factory(HttpMessageHandler handler):IHttpClientFactory
    {public HttpClient CreateClient(string name)=>new(handler,false){BaseAddress=new Uri("https://musicbrainz.org/ws/2/"),Timeout=TimeSpan.FromSeconds(3)};}
    internal static HttpResponseMessage Json(object value)=>new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(value),Encoding.UTF8,"application/json")};
    private static object Candidate(string id=Group,string title="An Album")=>new Dictionary<string,object>{{"id",id},{"title",title},{"first-release-date","2001-03-04"},{"primary-type","Album"},{"artist-credit",new[]{new{ name="An Artist",joinphrase="",artist=new{id="bbbbbbbb-1111-4111-8111-111111111111",name="An Artist"}}}}};
    private static object Search(int count=1,params object[]? entries)=>new Dictionary<string,object>{{"count",count},{"release-groups",entries is{Length:>0}?entries:new[]{Candidate()}}};

    [Fact]
    public void PreferencesArePrivateRevisionedAndRejectStaleAccountsAndMalformedFields()
    {
        using var home=new TestWorkspace();var(actor,login)=Owner(home);var prefs=new MusicExperiencePreferences(home.Paths,home.Store,new EventHub());
        Assert.False(prefs.Read(actor.Id).OnlineMetadata);Assert.False(prefs.Read(actor.Id).OnlineCovers);Assert.False(prefs.Read(actor.Id).OnlineScenes);
        var saved=prefs.Save(actor,login,new(OnlineMetadata:true,Scene:"ocean",RadioCountry:"tr"));Assert.Equal("TR",saved.RadioCountry);Assert.Equal(1,saved.Revision);
        var other=home.Store.SaveAccount(null,new AccountInput("Other"),"isolated");Assert.Equal("fireplace",prefs.Read(other.Id).Scene);
        Assert.Equal(409,Assert.Throws<DomainException>(()=>prefs.Save(actor,login,new(Scene:"rain"))).StatusCode);
        Assert.Throws<DomainException>(()=>MusicExperiencePreferences.Validate(saved with{RadioCountry=null!}));Assert.Throws<DomainException>(()=>MusicExperiencePreferences.Validate(saved with{Scene="../../file"}));
        var rescanned=prefs.Refresh(actor,login,1);Assert.Equal(1,rescanned.MetadataGeneration);Assert.Equal("ocean",rescanned.Scene);
        home.Store.RevokeLoginSession(login,actor.Id);Assert.Equal(401,Assert.Throws<DomainException>(()=>prefs.Save(actor,login,rescanned)).StatusCode);
    }
    [Fact]
    public void ExistingVisualPreferencesAcceptANewClipAndVinylWithoutResettingOtherChoices()
    {
        using var home=new TestWorkspace();var(actor,login)=Owner(home);var root=AppFiles.DirectoryIn(home.Paths.State,"music","preferences");File.WriteAllText(Path.Combine(root,actor.Id+".json"),"""{"revision":4,"scene":"rain","onlineScenes":true,"showNowPlaying":false,"radioCountry":"TR"}""");var prefs=new MusicExperiencePreferences(home.Paths,home.Store,new EventHub());var old=prefs.Read(actor.Id);Assert.Equal("",old.SceneVideo);
        var updated=prefs.Save(actor,login,old with{SceneVideo="rain-night"});Assert.Equal("rain-night",prefs.Read(actor.Id).SceneVideo);Assert.True(updated.OnlineScenes);Assert.False(updated.ShowNowPlaying);Assert.Equal("TR",updated.RadioCountry);
        Assert.Equal("vinyl",prefs.Save(actor,login,updated with{Scene="vinyl",SceneVideo=""}).Scene);Assert.Throws<DomainException>(()=>MusicExperiencePreferences.Validate(updated with{SceneVideo="https://arbitrary.example/video"}));
    }
    [Fact]
    public async Task MetadataIsOptInCachesExactIdentityAndNeverWritesTagsQueueFavoritesOrSourceBytes()
    {
        using var home=new TestWorkspace();var(actor,login)=Owner(home);var album=Album(home,actor);var track=Assert.Single(home.Store.GetMusicTracks(album.Id,actor).Items);var sourceHash=StateFolders.Hash(Path.Combine(home.Source,track.Media.RelativePath));
        home.Store.SaveProgress(track.MediaId,new PlaybackProgress(42,100),actor);home.Store.SetMusicFavorite(track.MediaId,actor,true);home.Store.SaveQueue(new([track.MediaId]),actor);
        using var handler=new Handler(_=>Json(Search()));using var memory=new MemoryCache(new MemoryCacheOptions{SizeLimit=100});var prefs=new MusicExperiencePreferences(home.Paths,home.Store,new EventHub());var metadata=new MusicOnlineMetadata(home.Paths,prefs,new(new Factory(handler),memory),home.Store);
        Assert.Null(await metadata.ResolveAsync(album,actor,CancellationToken.None));Assert.Equal(0,handler.Calls);
        var options=prefs.Save(actor,login,new(OnlineMetadata:true));var info=await metadata.ResolveAsync(album,actor,CancellationToken.None);Assert.Equal(Group,info!.GroupId);Assert.Equal("matched",info.Status);
        Assert.Equal(info.Fingerprint,(await metadata.ResolveAsync(album,actor,CancellationToken.None))!.Fingerprint);Assert.Equal(1,handler.Calls);
        prefs.Save(actor,login,options with{OnlineMetadata=false});Assert.Equal(Group,(await metadata.ResolveAsync(album,actor,CancellationToken.None))!.GroupId);Assert.Equal(1,handler.Calls);
        Assert.Equal("tags",home.Store.GetMusicAlbum(album.Id,actor)!.Source);Assert.Equal("Track 0",Assert.Single(home.Store.GetMusicTracks(album.Id,actor).Items).Title);Assert.Equal(42,home.Store.GetMedia(track.MediaId,actor)!.PositionSeconds);Assert.True(home.Store.IsMusicFavorite(track.MediaId,actor.Id));Assert.Equal(track.MediaId,Assert.Single(home.Store.GetQueue(actor).Items).Id);Assert.Equal(sourceHash,StateFolders.Hash(Path.Combine(home.Source,track.Media.RelativePath)));Assert.Empty(home.Store.GetDownloads());
    }
    [Theory]
    [InlineData(2,false)]
    [InlineData(30,true)]
    public async Task AmbiguousOrTruncatedSearchNeverChoosesAnAlbumAndPersistsARetryReceipt(int count,bool truncated)
    {
        using var home=new TestWorkspace();var(actor,login)=Owner(home);var album=Album(home,actor);
        using var handler=new Handler(_=>Json(truncated?Search(count):Search(count,Candidate(),Candidate("cccccccc-1111-4111-8111-111111111111"))));using var memory=new MemoryCache(new MemoryCacheOptions{SizeLimit=100});var prefs=new MusicExperiencePreferences(home.Paths,home.Store,new EventHub());prefs.Save(actor,login,new(OnlineMetadata:true));
        var metadata=new MusicOnlineMetadata(home.Paths,prefs,new(new Factory(handler),memory),home.Store);var result=await metadata.ResolveAsync(album,actor,CancellationToken.None);Assert.Null(result!.GroupId);Assert.Equal("unmatched",result.Status);Assert.True(DateTimeOffset.Parse(result.RetryAt!)>DateTimeOffset.UtcNow.AddDays(6));
        var restarted=new MusicOnlineMetadata(home.Paths,prefs,new(new Factory(handler),memory),home.Store);Assert.Equal(result,await restarted.ResolveAsync(album,actor,CancellationToken.None));Assert.Equal(1,handler.Calls);
        Assert.False(MusicOnlineMetadata.Same("An Album (Live)","An Album"));Assert.False(MusicOnlineMetadata.Same("An Artist","An Artist feat. Guest"));
    }
    [Fact]
    public async Task ProviderFailureGetsBackoffAndOptionalCoverMatchingWorksWithoutEnablingMetadataDisplay()
    {
        using var home=new TestWorkspace();var(actor,login)=Owner(home);var album=Album(home,actor);
        using var handler=new Handler(_=>new(HttpStatusCode.ServiceUnavailable));using var memory=new MemoryCache(new MemoryCacheOptions{SizeLimit=100});var prefs=new MusicExperiencePreferences(home.Paths,home.Store,new EventHub());prefs.Save(actor,login,new(OnlineCovers:true));
        var metadata=new MusicOnlineMetadata(home.Paths,prefs,new(new Factory(handler),memory),home.Store);var result=await metadata.ResolveAsync(album,actor,CancellationToken.None);Assert.Equal("retry",result!.Status);Assert.True(DateTimeOffset.Parse(result.RetryAt!)>DateTimeOffset.UtcNow.AddMinutes(20));await metadata.ResolveAsync(album,actor,CancellationToken.None);Assert.Equal(1,handler.Calls);Assert.False(prefs.Read(actor.Id).OnlineMetadata);
    }
    [Fact]
    public async Task BackgroundSweepIsBoundedRestartableAndCanBeDisabledWithoutTouchingPlayback()
    {
        using var home=new TestWorkspace();var(actor,login)=Owner(home);for(var i=0;i<9;i++)Album(home,actor,"An Album "+i,index:i,group:Group);
        using var handler=new Handler(_=>Json(Candidate()));using var memory=new MemoryCache(new MemoryCacheOptions{SizeLimit=100});var prefs=new MusicExperiencePreferences(home.Paths,home.Store,new EventHub());var options=prefs.Save(actor,login,new(OnlineMetadata:true));var provider=new MusicBrainzProvider(new Factory(handler),memory);var metadata=new MusicOnlineMetadata(home.Paths,prefs,provider,home.Store);var covers=new CoverArtCache(home.Paths,new Factory(new Handler(_=>throw new InvalidOperationException("Covers are disabled."))),memory);
        using var first=new MusicEnrichmentWorker(home.Paths,home.Store,prefs,metadata,covers,new EventHub(),NullLogger<MusicEnrichmentWorker>.Instance);Assert.Equal(4,await first.FillAsync(CancellationToken.None));Assert.Equal(4,first.Status(actor.Id).Offset);
        using var restarted=new MusicEnrichmentWorker(home.Paths,home.Store,prefs,metadata,covers,new EventHub(),NullLogger<MusicEnrichmentWorker>.Instance);Assert.Equal(4,await restarted.FillAsync(CancellationToken.None));Assert.Equal(8,restarted.Status(actor.Id).Checked);
        options=prefs.Save(actor,login,options with{OnlineMetadata=false});Assert.Equal(0,await restarted.FillAsync(CancellationToken.None));Assert.Equal(8,restarted.Status(actor.Id).Offset);
        prefs.Save(actor,login,options with{OnlineMetadata=true});Assert.Equal(1,await restarted.FillAsync(CancellationToken.None));Assert.Equal(9,restarted.Status(actor.Id).Checked);Assert.Equal(0,await restarted.FillAsync(CancellationToken.None));Assert.Equal(1,handler.Calls);Assert.Equal(9,home.Store.GetMusicAlbums(actor,0).Total);Assert.Empty(home.Store.ActivePlaybackLeases());Assert.Empty(home.Store.GetDownloads());
    }
    [Fact]
    public async Task OptingOutDuringAProviderResponsePreventsLateMetadataPublication()
    {
        using var home=new TestWorkspace();var(actor,login)=Owner(home);var album=Album(home,actor);var prefs=new MusicExperiencePreferences(home.Paths,home.Store,new EventHub());var enabled=prefs.Save(actor,login,new(OnlineMetadata:true));
        using var handler=new Handler(_=>{prefs.Save(actor,login,enabled with{OnlineMetadata=false});return Json(Search());});using var memory=new MemoryCache(new MemoryCacheOptions{SizeLimit=100});var metadata=new MusicOnlineMetadata(home.Paths,prefs,new(new Factory(handler),memory),home.Store);
        Assert.Null(await metadata.ResolveAsync(album,actor,CancellationToken.None));Assert.Null(metadata.Cached(album,actor.Id));Assert.Equal(1,handler.Calls);Assert.False(prefs.Read(actor.Id).OnlineMetadata);
    }
    [Fact]
    public void LibrarySongPagesKeepPersonalHistoryAndFavoritesPrivateAndRespectLibraryRevocation()
    {
        using var home=new TestWorkspace();var(actor,_)=Owner(home);var album=Album(home,actor,"Private Album");var song=Assert.Single(home.Store.GetMusicTracks(album.Id,actor).Items);home.Store.SetMusicFavorite(song.MediaId,actor,true);home.Store.SaveProgress(song.MediaId,new PlaybackProgress(12,100),actor);
        Assert.Equal(song.MediaId,Assert.Single(home.Store.GetMusicLibraryTracks(actor,view:"liked").Items).MediaId);Assert.Equal(song.MediaId,Assert.Single(home.Store.GetMusicLibraryTracks(actor,view:"recent").Items).MediaId);
        var member=home.Store.SaveAccount(null,new AccountInput("Member"),"isolated");Assert.Single(home.Store.GetMusicLibraryTracks(member).Items);Assert.Empty(home.Store.GetMusicLibraryTracks(member,view:"liked").Items);Assert.Empty(home.Store.GetMusicLibraryTracks(member,view:"recent").Items);
        var restricted=home.Store.SaveAccount(member.Id,new AccountInput("Member",AllLibraries:false));Assert.Empty(home.Store.GetMusicLibraryTracks(restricted).Items);Assert.Empty(home.Store.GetMusicLibraryTracks(actor,query:"absent").Items);Assert.Equal("Private Album",Assert.Single(home.Store.GetMusicLibraryTracks(actor).Items).Album);
        Assert.Throws<DomainException>(()=>home.Store.GetMusicLibraryTracks(actor,-1));Assert.Throws<DomainException>(()=>home.Store.GetMusicLibraryTracks(actor,view:"all;drop table"));
    }
}
