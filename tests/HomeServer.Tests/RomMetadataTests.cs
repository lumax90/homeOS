using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HomeServer.Core;
using HomeServer.Gaming;
using HomeServer.Infrastructure;

namespace HomeServer.Tests;

// Original isolated headers/API fixtures only. No user ROM execution, provider request or internet image download.
public sealed class RomMetadataTests
{
    private static (Account Actor,string Login,GameRom Rom) Seed(TestWorkspace home)
    {home.Store.CreateOwner(new("Original owner","isolated-secret"));var actor=home.Store.GetAccount("1")!;var login=home.Store.CreateLoginSession("1",home.Store.GetAccountSecret("1")!.Version,"metadata-fixture").Id;var bytes=new byte[512];"MComprHD"u8.CopyTo(bytes);var sha=Convert.ToHexStringLower(SHA256.HashData(bytes));var dir=AppFiles.DirectoryIn(home.Paths.State,"games","roms","ps1",sha);File.WriteAllBytes(Path.Combine(dir,"game.chd"),bytes);return(actor,login,home.Store.RegisterGameRom("Original fixture.chd",sha,bytes.Length,actor,login,"ps1"));}
    private static byte[] Image(byte color=1){var data=new byte[32];new byte[]{137,80,78,71,13,10,26,10}.CopyTo(data,0);System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(16),1);System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(20),1);data[31]=color;return data;}
    private sealed class RpcHandler(bool match=true,string platform="Sony PlayStation",bool badSize=false,bool brokenDetails=false,bool emptyHashes=false):HttpMessageHandler
    {
        public readonly List<string> Requests=[];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            Assert.Equal("https://hasheous.org/api/v1/Mcp",request.RequestUri!.AbsoluteUri);Assert.Null(request.Headers.Authorization);Assert.False(request.Headers.Contains("X-Client-API-Key"));var body=await request.Content!.ReadAsStringAsync(token);Requests.Add(body);Assert.DoesNotContain("prefixBase64",body);using var json=JsonDocument.Parse(body);var root=json.RootElement;var args=root.GetProperty("params").GetProperty("arguments");var method=root.GetProperty("params").GetProperty("name").GetString();object payload;
            if(method=="hasheous_get_game_details"&&brokenDetails)return new(HttpStatusCode.OK){Content=JsonContent.Create(new{jsonrpc="2.0",id=root.GetProperty("id").GetInt64(),error=new{code=-32000,message="The node already has a parent."}})};
            if(method=="hasheous_lookup_hashes"&&emptyHashes)payload=new{games=Array.Empty<object>()};
            else if(method=="hasheous_lookup_hashes")payload=new{games=new[]{new{id=42,name="Original fixture (World)",platform,publisher="Original publisher",year="1995",countries=new[]{"World"},languages=new[]{"English"},matchedRoms=new[]{new{size=badSize?123:512,md5=match?args.GetProperty("md5").GetString():new string('e',32),sha1=match?args.GetProperty("sha1").GetString():new string('e',40),sha256=match?args.GetProperty("sha256").GetString():new string('e',64)}}}}};
            else if(method=="hasheous_search_games")payload=new{games=new[]{new{id=42,name="Original fixture (World)",platform=new{name=platform},publisher=new{name="Original publisher"},year="1995",country="World",language="English"}}};
            else payload=new{game=new{id=42,name="Original fixture (World)",platform=new{name=platform},publisher=new{name="Original publisher"},description="Original isolated fixture description.",year="1995",country="World",language="English"}};
            return new(HttpStatusCode.OK){Content=JsonContent.Create(new{jsonrpc="2.0",id=root.GetProperty("id").GetInt64(),result=new{structuredContent=payload}})};
        }
    }
    private sealed class ArtHandler:HttpMessageHandler
    {
        public readonly List<Uri> Requests=[];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {Requests.Add(request.RequestUri!);Assert.Equal("thumbnails.libretro.com",request.RequestUri!.Host);Assert.DoesNotContain(".index",request.RequestUri.AbsoluteUri);return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=request.RequestUri.AbsolutePath.EndsWith('/')?new StringContent("<a href=\"Original%20fixture%20(World).png\">Original</a>"):new ByteArrayContent(Image())});}
    }
    private sealed class TimeoutHandler:HttpMessageHandler { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>throw new TaskCanceledException("Original isolated HTTP timeout."); }
    [Fact]
    public async Task PublicHashMatchAndDirectoryCoverUseCorrectIdsWithoutChangingRomPreferencesOrSources()
    {
        using var home=new TestWorkspace();var(actor,login,rom)=Seed(home);home.Store.SetGamePreference(rom.Id,new(true,true),actor);var state=home.Store.GetGameStates(actor.Id).Single(s=>s.GameId==rom.Id);var rpc=new RpcHandler();var artHttp=new ArtHandler();using var client=new HasheousMetadataClient(rpc);using var art=new RomArtwork(home.Paths,home.Store,new EventHub(),artHttp);var metadata=new RomMetadata(home.Paths,home.Store,new EventHub(),art,client);
        metadata.Queue(rom,actor);var job=home.Store.ClaimProductJob(["rom-metadata"])!;await metadata.RunAsync(job,CancellationToken.None);var saved=RomMetadata.Read(home.Paths,rom)!;Assert.Equal("hash",saved.MatchMethod);Assert.Equal(42,saved.Game.Id);Assert.Equal("Original isolated fixture description.",saved.Game.Description);Assert.True(art.HasCover(rom));Assert.Equal(2,rpc.Requests.Count);Assert.Equal(2,artHttp.Requests.Count);Assert.Equal(state,home.Store.GetGameStates(actor.Id).Single(s=>s.GameId==rom.Id));using var original=new NativeRomLibrary(home.Paths,home.Store).OpenVerified(rom);Assert.Equal(512,original.Length);Assert.Empty(home.Store.GetActiveGameSessions());Assert.Equal("completed",home.Store.GetProductJob(job.Id)!.Status);
    }
    [Theory]
    [InlineData(false,"Sony PlayStation",false)]
    [InlineData(true,"Sony PlayStation 2",false)]
    [InlineData(true,"Sony PlayStation",true)]
    public async Task MissingHashWrongPlatformOrWrongSizeCannotAutoMatch(bool match,string platform,bool badSize)
    {
        using var home=new TestWorkspace();var(actor,_,rom)=Seed(home);using var client=new HasheousMetadataClient(new RpcHandler(match,platform,badSize));using var art=new RomArtwork(home.Paths,home.Store,new EventHub(),new ArtHandler());var metadata=new RomMetadata(home.Paths,home.Store,new EventHub(),art,client);metadata.Queue(rom,actor);await metadata.RunAsync(home.Store.ClaimProductJob(["rom-metadata"])!,CancellationToken.None);Assert.Null(RomMetadata.Read(home.Paths,rom));Assert.False(art.HasCover(rom));Assert.Single(home.Store.GetGameRoms(actor));Assert.False(HasheousMetadataClient.PlatformMatches("ps1","Sony PlayStation 2"));
    }
    [Fact]
    public async Task ManualMatchRevisionAndManualTitleAndCoverArePreserved()
    {
        using var home=new TestWorkspace();var(actor,login,rom)=Seed(home);using var client=new HasheousMetadataClient(new RpcHandler());using var art=new RomArtwork(home.Paths,home.Store,new EventHub(),new ArtHandler());var metadata=new RomMetadata(home.Paths,home.Store,new EventHub(),art,client);art.SetPresentation(rom,new("My manual title","My category"));await art.SaveImageAsync(rom,Image(9),null,"Original private fixture",CancellationToken.None);var cover=art.Presentation(rom).Sha256;
        await metadata.SearchAsync(rom,"Original fixture",actor,login,CancellationToken.None);var result=await metadata.SelectAsync(rom,new(42,0),actor,login,CancellationToken.None);Assert.Equal("manual",result.MatchMethod);Assert.Equal("My manual title",art.Presentation(rom).Title);Assert.Equal(cover,art.Presentation(rom).Sha256);Assert.Equal(409,(await Assert.ThrowsAsync<DomainException>(()=>metadata.SelectAsync(rom,new(42,0),actor,login,CancellationToken.None))).StatusCode);Assert.Equal(1,RomMetadata.Read(home.Paths,rom)!.Revision);
    }
    [Fact]
    public async Task DiscFingerprintUsesTheDataTrackNotTheManifestOrLargerAudioTrack()
    {
        using var home=new TestWorkspace();var(actor,login,_)=Seed(home);using var roms=new NativeRomLibrary(home.Paths,home.Store);var cue=Encoding.UTF8.GetBytes("FILE \"audio.bin\" BINARY\n TRACK 01 AUDIO\n INDEX 01 00:00:00\nFILE \"data.bin\" BINARY\n TRACK 02 MODE2/2352\n INDEX 01 00:00:00\n");var data=Enumerable.Repeat((byte)7,512).ToArray();using var body=new MultipartFormDataContent("original");body.Add(new ByteArrayContent(cue),"files","entry.cue");body.Add(new ByteArrayContent(new byte[2048]),"files","audio.bin");body.Add(new ByteArrayContent(data),"files","data.bin");using var input=await body.ReadAsStreamAsync();var rom=await roms.ImportDiscAsync(input,"original","ps1","entry.cue",actor,login,CancellationToken.None);using var client=new HasheousMetadataClient(new RpcHandler());using var art=new RomArtwork(home.Paths,home.Store,new EventHub(),new ArtHandler());var metadata=new RomMetadata(home.Paths,home.Store,new EventHub(),art,client);var fingerprint=await metadata.FingerprintAsync(rom,CancellationToken.None);Assert.Equal("data.bin",fingerprint.Name);Assert.Equal(Convert.ToHexStringLower(SHA1.HashData(data)),fingerprint.Sha1);Assert.NotEqual(rom.Sha256,fingerprint.Sha256);Assert.Empty(home.Store.GetActiveGameSessions());
    }
    [Fact]
    public async Task CorruptedContentFailsBeforeNetworkAndPendingRefreshIsCoalesced()
    {
        using var home=new TestWorkspace();var(actor,_,rom)=Seed(home);var rpc=new RpcHandler();using var client=new HasheousMetadataClient(rpc);using var art=new RomArtwork(home.Paths,home.Store,new EventHub(),new ArtHandler());var metadata=new RomMetadata(home.Paths,home.Store,new EventHub(),art,client);Assert.Equal(metadata.Queue(rom,actor,true).Id,metadata.Queue(rom,actor,true).Id);File.WriteAllBytes(Path.Combine(home.Paths.State,"games","roms","ps1",rom.Sha256,"game.chd"),new byte[512]);await Assert.ThrowsAsync<DomainException>(()=>metadata.FingerprintAsync(rom,CancellationToken.None));Assert.Empty(rpc.Requests);Assert.Null(RomMetadata.Read(home.Paths,rom));Assert.Single(home.Store.GetGameRoms(actor));
    }
    [Fact]
    public async Task DirectoryParsingRejectsEscapesAndCancelledAuthorityCannotPublishCover()
    {
        var entries=RomArtwork.ParseDirectory("<a href=\"Original%20%26%20fixture.png\">good</a><a href='../bad.png'>bad</a><a href='https://evil.invalid/a.png'>bad</a><a href='folder%2Fbad.png'>bad</a>");Assert.Equal(new[]{"Original & fixture.png"},entries);
        var punctuation=RomArtwork.ParseDirectory("<a href=\"Original Hero's Adventure (USA).png\">one</a><a href='Original%20Hero%27s%20Adventure%20(Europe).png'>two</a><a href=\"Original Hero&#39;s Adventure (World).png\">three</a>");
        Assert.Equal(new[]{"Original Hero's Adventure (USA).png","Original Hero's Adventure (Europe).png","Original Hero's Adventure (World).png"},punctuation);
        using var home=new TestWorkspace();var(_,_,rom)=Seed(home);using var art=new RomArtwork(home.Paths,home.Store,new EventHub(),new ArtHandler());await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>art.SetAutomaticCoverAsync(rom,"Original fixture (World).png",CancellationToken.None,()=>throw new OperationCanceledException()));Assert.False(art.HasCover(rom));
    }
    [Fact]
    public async Task RealHostedDetailsErrorFallsBackToTheSameSearchIdentityAndCompletesManualSelection()
    {
        using var home=new TestWorkspace();var(actor,login,rom)=Seed(home);var rpc=new RpcHandler(brokenDetails:true);using var client=new HasheousMetadataClient(rpc);using var art=new RomArtwork(home.Paths,home.Store,new EventHub(),new ArtHandler());var metadata=new RomMetadata(home.Paths,home.Store,new EventHub(),art,client);
        await metadata.SearchAsync(rom,"Original fixture",actor,login,CancellationToken.None);var result=await metadata.SelectAsync(rom,new(42,0),actor,login,CancellationToken.None);Assert.Equal(42,result.Game.Id);Assert.Equal("Original publisher",result.Game.Publisher);Assert.True(result.Game.DetailsPartial);Assert.Equal("manual",result.MatchMethod);Assert.True(art.HasCover(rom));Assert.Equal(3,rpc.Requests.Count);Assert.Empty(home.Store.GetActiveGameSessions());
    }
    [Fact]
    public async Task ExactNameAndPlatformCanAutomaticallyMatchWhenHashIsUnknown()
    {
        using var home=new TestWorkspace();var(actor,_,rom)=Seed(home);using var client=new HasheousMetadataClient(new RpcHandler(emptyHashes:true,brokenDetails:true));using var art=new RomArtwork(home.Paths,home.Store,new EventHub(),new ArtHandler());var metadata=new RomMetadata(home.Paths,home.Store,new EventHub(),art,client);metadata.Queue(rom,actor,automatic:true);await metadata.RunAsync(home.Store.ClaimProductJob(["rom-metadata"])!,CancellationToken.None);Assert.Equal("name",RomMetadata.Read(home.Paths,rom)!.MatchMethod);Assert.True(art.HasCover(rom));Assert.False(metadata.ShouldEnrich(rom,actor,new(),DateTimeOffset.UtcNow));
    }
    [Fact]
    public void DuplicateHashRecordsMergeButSequelsAndSpecialEditionsDoNotNameMatch()
    {
        var a=new RomMetadataCandidate(1,"Original fixture","Sony PlayStation","",null,null,[],[],true);var b=a with{Id=2,Publisher="Original publisher",Platform="PlayStation"};Assert.Equal(2,RomMatching.Automatic([a,b],"Original fixture (USA)",false)!.Id);
        Assert.Null(RomMatching.Automatic([a with{HashMatch=false,Name="Original fixture 2"}],"Original fixture",true));Assert.Null(RomMatching.Automatic([a with{HashMatch=false,Edition="Demos"}],"Original fixture",true));
        Assert.Null(RomMatching.Automatic([a,b with{Name="Other title"}],"Original fixture",false));Assert.Null(RomMatching.Automatic([a with{HashMatch=false}],"Original fixture",false));
        Assert.NotEqual(RomMatching.TitleKey("Original fixture (Special Edition)"),RomMatching.TitleKey("Original fixture"));
    }
    [Fact]
    public void RegionalCoverSelectionIsAutomaticAndStillRestrictedToAnExactTitle()
    {
        var covers=new[]{new RomCover("Original fixture (USA).png","Original fixture (USA)",""),new RomCover("Original fixture (Europe).png","Original fixture (Europe)",""),new RomCover("Original fixture 2 (Europe).png","Original fixture 2 (Europe)","")};
        Assert.Equal(covers[1],RomMatching.Cover(covers,"Original fixture","Original fixture (Europe).cue","auto"));Assert.Equal(covers[0],RomMatching.Cover(covers,"Original fixture","Original fixture (Europe).cue","us"));Assert.Null(RomMatching.Cover(covers,"Unrelated","Unrelated.iso","auto"));
    }
    [Fact]
    public async Task NetworkTimeoutHasDurableBackoffInsteadOfImmediateRequeue()
    {
        using var home=new TestWorkspace();var(actor,_,rom)=Seed(home);using var client=new HasheousMetadataClient(new TimeoutHandler());using var art=new RomArtwork(home.Paths,home.Store,new EventHub(),new ArtHandler());var metadata=new RomMetadata(home.Paths,home.Store,new EventHub(),art,client);metadata.Queue(rom,actor,automatic:true);var job=home.Store.ClaimProductJob(["rom-metadata"])!;
        Assert.Equal(502,(await Assert.ThrowsAsync<DomainException>(()=>metadata.RunAsync(job,CancellationToken.None))).StatusCode);home.Store.FinishProductJob(job,null,"failed");Assert.False(metadata.ShouldEnrich(rom,actor,new(),DateTimeOffset.UtcNow));Assert.True(metadata.ShouldEnrich(rom,actor,new(),DateTimeOffset.UtcNow.AddHours(1)));Assert.Null(RomMetadata.Read(home.Paths,rom));
    }
}
