using System.Net;
using System.Text;
using System.Text.Json;
using HomeServer.Core;
using HomeServer.Downloads;
using HomeServer.Infrastructure;
using HomeServer.Sources;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MonoTorrent;

namespace HomeServer.Tests;

public sealed class SourceTests
{
    private const string Caps="""<caps><limits max="20"/><searching><search available="yes" supportedParams="q"/><movie-search available="yes" supportedParams="q,imdbid"/></searching><categories><category id="2000" name="Movies"><subcat id="2040" name="HD"/></category></categories></caps>""";
    private const string Rss="""
        <rss xmlns:torznab="http://torznab.com/schemas/2015/feed"><channel><torznab:response offset="0" total="30"/>
        <item><title>Example.Movie.2020.1080p.WEB-DL.x264</title><guid>release-1</guid><pubDate>Mon, 12 Oct 2026 12:00:00 +0000</pubDate>
        <enclosure url="https://indexer.test/api?t=get&amp;id=1&amp;apikey=secret-key" length="900000000" type="application/x-bittorrent"/>
        <torznab:attr name="category" value="2000"/><torznab:attr name="category" value="2040"/><torznab:attr name="category" value="2000"/>
        <torznab:attr name="seeders" value="20"/><torznab:attr name="imdb" value="tt1234567"/></item></channel></rss>
        """;
    private static ProviderContent Movie=>new("tmdb",42,"movie","Örnek Film","Example Movie","","2020-01-01",null,null,"https://www.themoviedb.org/movie/42",[],"tt1234567");
    [Fact]
    public void CapabilitiesAndRepeatedAttributesArePreserved()
    {
        var caps=ZnabParser.Capabilities(Encoding.UTF8.GetBytes(Caps));Assert.Equal(20,caps.MaxPageSize);Assert.Contains("imdbid",caps.Searches["movie-search"]);
        var parsed=ZnabParser.Releases(Encoding.UTF8.GetBytes(Rss),"torznab",new Uri("https://indexer.test/api"));
        var result=Assert.Single(parsed.Items);Assert.Equal(new[]{"2000","2040"},result.Attributes["category"]);Assert.True(parsed.More);
        Assert.Equal(20,result.Seeders);Assert.Equal(900000000,result.SizeBytes);
    }
    [Theory]
    [InlineData("<error code=\"100\" description=\"secret\"/>")]
    [InlineData("<!DOCTYPE rss [<!ENTITY x SYSTEM 'file:///private'>]><rss>&x;</rss>")]
    [InlineData("<html>not RSS</html>")]
    public void ApiErrorsAndUnsafeXmlAreNotTreatedAsEmptySuccess(string xml)=>Assert.Throws<DomainException>(()=>ZnabParser.Releases(Encoding.UTF8.GetBytes(xml),"torznab",new Uri("https://indexer.test/api")));
    [Fact]
    public void QualityDecisionsRejectWrongScopeAndExplainTheReason()
    {
        using var home=new TestWorkspace();var rules=home.Store.GetQualityProfile("balanced").Rules;
        var raw=Assert.Single(ZnabParser.Releases(Encoding.UTF8.GetBytes(Rss),"torznab",new Uri("https://indexer.test/api")).Items);
        Assert.True(ReleaseEvaluator.Evaluate(raw,Movie,rules).Eligible);
        var tooLarge=ReleaseEvaluator.Evaluate(raw with{SizeBytes=100L*1024*1024*1024},Movie,rules);
        Assert.False(tooLarge.Eligible);Assert.Contains(tooLarge.Rejections,r=>r.Contains("boyutu"));
        var wrongEpisode=ReleaseEvaluator.Evaluate(raw with{Title="Example.Show.S02E04.1080p.WEB-DL",Attributes=[]},Movie with{Kind="show",Title="Example Show",OriginalTitle="Example Show"},rules,2,3);
        Assert.False(wrongEpisode.Eligible);Assert.Contains(wrongEpisode.Rejections,r=>r.Contains("Bölüm"));
        Assert.Equal(2017,ReleaseEvaluator.Parse(raw with{Title="Blade.Runner.2049.2017.1080p.WEB-DL"}).Year);
    }
    [Fact]
    public void ConflictingIdsAndSequelsAreRejectedWhileExplicitEpisodeRangesContainTheirMembers()
    {
        using var home = new TestWorkspace();
        var rules = home.Store.GetQualityProfile("balanced").Rules;
        var raw = Assert.Single(ZnabParser.Releases(Encoding.UTF8.GetBytes(Rss), "torznab", new Uri("https://indexer.test/api")).Items);
        Assert.False(ReleaseEvaluator.Evaluate(raw with { Attributes = new() { ["imdbid"] = ["tt7654321"] } }, Movie, rules).Eligible);
        Assert.False(ReleaseEvaluator.Evaluate(raw with { Title = "Example.Movie.Part.Two.2020.1080p", Attributes = [] }, Movie, rules).Eligible);
        var show = Movie with { Kind = "show", Title = "Example Show", OriginalTitle = "Example Show" };
        Assert.True(ReleaseEvaluator.Evaluate(raw with { Title = "Example.Show.S01E01E02.1080p", Attributes = [] }, show, rules, 1, 1).Eligible);
        Assert.False(ReleaseEvaluator.Evaluate(raw with { Title = "Example.Show.S01E01E02.1080p", Attributes = [] }, show, rules, 1, 3).Eligible);
        Assert.True(ReleaseEvaluator.Evaluate(raw with { Title = "Example.Show.1x01.1080p", Attributes = [] }, show, rules, 1, 1).Eligible);
        var languages = rules with { Languages = ["tr"] };
        Assert.False(ReleaseEvaluator.Evaluate(raw, Movie, languages).Eligible);
        Assert.True(ReleaseEvaluator.Evaluate(raw with { Attributes = new() { ["language"] = ["tur"] } }, Movie, languages).Eligible);
    }
    [Fact]
    public async Task SearchHonorsCapsKeepsCredentialsPrivateAndDoesNotDownload()
    {
        using var home=new TestWorkspace();var events=new EventHub();var config=new ConfigurationBuilder().Build();var protection=new EphemeralDataProtectionProvider();
        var files=new DownloadFiles(home.Paths);using var engine=new TorrentEngine(files,home.Store,config,protection);
        using var worker=new DownloadWorker(home.Store,engine,new DownloadImporter(files,home.Store,events),events,config,NullLogger<DownloadWorker>.Instance);
        var handler=new Handler();var service=new IndexerService(home.Store,new Factory(handler),protection,engine,events);
        var source=service.Save(null,new IndexerInput("Test source","https://indexer.test/api","torznab","secret-key"));
        await service.TestAsync(source.Id,CancellationToken.None);
        var target=home.Store.RequestContent(Movie,"all","balanced");
        var result=await service.SearchAsync(new ReleaseSearchInput(target.Id),CancellationToken.None);
        Assert.Single(result.Items);Assert.True(result.Items[0].Evaluation.Eligible);
        Assert.Contains(handler.Urls,u=>u.Contains("t=movie")&&u.Contains("imdbid=1234567")&&u.Contains("limit=20"));
        Assert.DoesNotContain("secret-key",JsonSerializer.Serialize(result));Assert.DoesNotContain("secret-key",home.Store.GetRelease(result.Items[0].Id).ProtectedUrl);
        Assert.Empty(home.Store.GetDownloads());
        var rss = await service.ReadRssAsync(source.Id,CancellationToken.None);
        Assert.Single(rss.Releases);
        Assert.Contains(handler.Urls, url => url.Contains("t=search") && !url.Contains("q="));
        handler.Throttled=true;
        var throttled=await service.SearchAsync(new ReleaseSearchInput(target.Id),CancellationToken.None);
        Assert.NotNull(throttled.Sources[0].Error);Assert.NotNull(home.Store.GetIndexer(source.Id).Info.RetryAfter);
    }
    [Fact]
    public async Task SelectingAStoredReleaseIsIdempotentAndDoesNotResumeALaterPause()
    {
        using var home = new TestWorkspace();
        var payload = home.Write("Example.Movie.2020.1080p.mkv");
        var torrentBytes = (await new TorrentCreator().CreateAsync(new TorrentFileSource(payload))).Encode();
        var handler = new Handler { TorrentBytes = torrentBytes };
        using var engine = new TorrentEngine(new DownloadFiles(home.Paths), home.Store, new ConfigurationBuilder().Build(), new EphemeralDataProtectionProvider());
        var service = new IndexerService(home.Store, new Factory(handler), new EphemeralDataProtectionProvider(), engine, new EventHub());
        service.Save(null, new IndexerInput("Fixture", "https://indexer.test/api", "torznab", "secret-key"));
        var target = home.Store.RequestContent(Movie, "all", "balanced");
        var result = await service.SearchAsync(new ReleaseSearchInput(target.Id), CancellationToken.None);
        var release = Assert.Single(result.Items);
        Assert.Empty(home.Store.GetDownloads());
        var first = await service.GrabAsync(release.Id, false, CancellationToken.None);
        home.Store.SetDownloadIntent(first.Id, "paused");
        var repeated = await service.GrabAsync(release.Id, false, CancellationToken.None);
        Assert.Equal(first.Id, repeated.Id);
        Assert.Equal("paused", repeated.DesiredState);
        Assert.Equal(target.Id, repeated.Acquisition!.ContentId);
        Assert.Single(home.Store.GetDownloads());
        Assert.Single(handler.Urls, url => url.Contains("t=get"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WrongHashAndUnapprovedRedirectCannotCreateATransfer(bool redirect)
    {
        using var home = new TestWorkspace();
        var torrentBytes = (await new TorrentCreator().CreateAsync(new TorrentFileSource(home.Write("Example.Movie.2020.1080p.mkv")))).Encode();
        var handler = new Handler
        {
            TorrentBytes = torrentBytes, Redirect = redirect,
            ReleaseXml = Rss.Replace("</item>", "<torznab:attr name=\"infohash\" value=\"0000000000000000000000000000000000000000\"/></item>")
        };
        using var engine = new TorrentEngine(new DownloadFiles(home.Paths), home.Store, new ConfigurationBuilder().Build(), new EphemeralDataProtectionProvider());
        var service = new IndexerService(home.Store, new Factory(handler), new EphemeralDataProtectionProvider(), engine, new EventHub());
        service.Save(null, new IndexerInput("Fixture", "https://indexer.test/api", "torznab", "secret-key"));
        var target = home.Store.RequestContent(Movie, "all", "balanced");
        var release = Assert.Single((await service.SearchAsync(new ReleaseSearchInput(target.Id), CancellationToken.None)).Items);
        await Assert.ThrowsAsync<DomainException>(() => service.GrabAsync(release.Id, false, CancellationToken.None));
        Assert.Empty(home.Store.GetDownloads());
        Assert.DoesNotContain(handler.Urls, url => url.Contains("unapproved.test"));
    }
    private sealed class Factory(Handler handler):IHttpClientFactory {public HttpClient CreateClient(string name)=>new(handler,false);}
    private sealed class Handler:HttpMessageHandler
    {
        public List<string> Urls {get;}=[];public bool Throttled {get;set;}
        public byte[]? TorrentBytes { get; init; }
        public string ReleaseXml { get; init; } = Rss;
        public bool Redirect { get; init; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            Urls.Add(request.RequestUri!.ToString());
            if(Throttled)return Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests));
            if (request.RequestUri.Query.Contains("t=get") && Redirect)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Redirect) { Headers = { Location = new Uri("https://unapproved.test/private") } });
            if (request.RequestUri.Query.Contains("t=get") && TorrentBytes is not null)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(TorrentBytes) });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(request.RequestUri.Query.Contains("t=caps")?Caps:ReleaseXml,Encoding.UTF8,"application/xml")});
        }
    }
}
