using System.Net;
using System.Text;
using HomeServer.Core;
using HomeServer.Infrastructure;
using HomeServer.Music;
using HomeServer.Playback;
using Microsoft.Extensions.Caching.Memory;

namespace HomeServer.Tests;

public sealed class MusicAutomationTests
{
    private const string Artist = "763cd17e-9d42-4321-8e88-3957a1300a9a";
    private const string Group = "caafa493-8b5c-40a0-803e-95a690d5a30e";
    private const string ReleaseId = "7a65d5cb-bcbf-4b0d-b6e0-9a7a49a156cd";
    private static MusicBrainzAlbum Album => new(Group, "Fixture Album", "Fixture Artist", "2020-01-01", "Album", "https://musicbrainz.org/release-group/" + Group, []);
    private static MusicRelease Release => new(new(ReleaseId, Group, Album.Title, Album.Artist, "2020-01-01", "TR", "Official", ["CD"], 1),
        [new("1bda5c1a-2c21-43f3-a3f1-c84b7b611146", "ec670060-53aa-4283-94d5-af423fc5a8a9", 1, 1, "1", "Fixture Song", Album.Artist, 1000)]);
    private static readonly MusicMonitorSettings Automatic = new(Enabled: true, Mode: "all", Search: true, AutoGrab: true);

    [Fact]
    public async Task DisabledAndMetadataOnlyMonitorsNeverSearchOrAcquire()
    {
        using var home = new TestWorkspace(); home.Store.CreateOwner(new Owner("Owner", "fixture")); using var harness = new Harness(home.Store);
        var monitor = home.Store.SaveMusicMonitor("artist", Artist, Album.Artist, new(new MusicMonitorSettings()), home.Store.GetAccount("1")!);
        Assert.False(await harness.Coordinator.RunOnceAsync(CancellationToken.None)); Assert.Equal(0, harness.Handler.Calls);
        monitor = home.Store.SaveMusicMonitor("artist", Artist, Album.Artist, new(new MusicMonitorSettings(Enabled: true, Mode: "all"), monitor.Revision), home.Store.GetAccount("1")!);
        Assert.True(await harness.Coordinator.RunOnceAsync(CancellationToken.None));
        Assert.Single(home.Store.GetMonitoredMusicAlbums(monitor.Id).Items); Assert.Equal(0, harness.Source.Searches); Assert.Empty(home.Store.GetDownloads());
        Assert.Contains(home.Store.GetNotifications("1"), item => item.Kind == "music");
    }
    [Fact]
    public void MetadataPaginationAndOverridesSurviveErrorsAndStaleRevisions()
    {
        using var home = new TestWorkspace(); home.Store.CreateOwner(new Owner("Owner", "fixture")); var owner = home.Store.GetAccount("1")!;
        var monitor = home.Store.SaveMusicMonitor("artist", Artist, Album.Artist, new(new MusicMonitorSettings(Enabled: true, Mode: "all")), owner);
        var now = DateTimeOffset.UtcNow.AddSeconds(1); home.Store.RecordMusicMonitorPage(monitor, new([Album], 2, 0, 50), now);
        monitor = home.Store.GetMusicMonitor(monitor.Id)!; Assert.Equal(1, monitor.Cursor);
        home.Store.FailMusicMonitor(monitor, "Fixture provider offline", now); Assert.Single(home.Store.GetMonitoredMusicAlbums(monitor.Id).Items);
        home.Store.OverrideMonitoredMusicAlbum(monitor.Id, Group, new(false, monitor.Revision), owner.Id);
        Assert.Throws<DomainException>(() => home.Store.OverrideMonitoredMusicAlbum(monitor.Id, Group, new(true, monitor.Revision), owner.Id));
        var updated = home.Store.GetMusicMonitor(monitor.Id)!;
        home.Store.RecordMusicMonitorPage(monitor, new([Album with { Title = "Stale" }], 2, 1, 50), now); Assert.Equal("Fixture Album", Assert.Single(home.Store.GetMonitoredMusicAlbums(monitor.Id).Items).Metadata.Title);
        home.Store.RecordMusicMonitorPage(updated, new([Album with { Title = "Updated" }], 2, 1, 50), now);
        var actual = Assert.Single(home.Store.GetMonitoredMusicAlbums(monitor.Id).Items); Assert.False(actual.Wanted); Assert.Equal("Updated", actual.Metadata.Title);
    }
    [Fact]
    public async Task AutomaticAcquisitionIsDurableDeduplicatedAndCancellationSuppressesReplacement()
    {
        using var home = new TestWorkspace(); home.Store.CreateOwner(new Owner("Owner", "fixture")); using var harness = new Harness(home.Store);
        AddMonitor(home.Store, Automatic, harness.Clock.GetUtcNow());
        await harness.Coordinator.RunOnceAsync(CancellationToken.None); var download = Assert.Single(home.Store.GetDownloads());
        Assert.Equal("grabbed", Assert.Single(home.Store.GetMusicAutomationJobs("1")).Status);
        await harness.Coordinator.RunOnceAsync(CancellationToken.None); Assert.Equal(1, harness.Source.Grabs);
        home.Store.SetDownloadIntent(download.Id, "cancelled"); await harness.Coordinator.RunOnceAsync(CancellationToken.None);
        Assert.Equal("blocked", Assert.Single(home.Store.GetMusicAutomationJobs("1")).Status);
        harness.Clock.Advance(TimeSpan.FromDays(1)); await harness.Coordinator.RunOnceAsync(CancellationToken.None); Assert.Single(home.Store.GetDownloads()); Assert.Equal(1, harness.Source.Grabs);
    }
    [Fact]
    public async Task NotificationOnlyDelayRecoveryAndBadReleaseBlocklistUseFreshSearchResults()
    {
        using var home = new TestWorkspace(); home.Store.CreateOwner(new Owner("Owner", "fixture")); using var harness = new Harness(home.Store);
        var monitor = AddMonitor(home.Store, Automatic with { DelayMinutes = 30 }, harness.Clock.GetUtcNow());
        await harness.Coordinator.RunOnceAsync(CancellationToken.None); Assert.Empty(home.Store.GetDownloads()); Assert.Equal("waiting", Assert.Single(home.Store.GetMusicAutomationJobs("1")).Status);
        harness.Clock.Advance(TimeSpan.FromMinutes(31));
        var job = home.Store.ClaimMusicAutomationJob(harness.Clock.GetUtcNow())!; home.Store.RecoverMusicAutomation(); Assert.Equal("queued", home.Store.GetMusicAutomationJob(job.Id)!.Status);
        await harness.Coordinator.RunOnceAsync(CancellationToken.None); Assert.Equal(2, harness.Source.Searches);
        var download = Assert.Single(home.Store.GetDownloads()); home.Store.UpdateDownload(download.Id, download.Revision, "failed", "Fixture checksum failed", 100, 1024);
        await harness.Coordinator.RunOnceAsync(CancellationToken.None); Assert.Single(home.Store.GetMusicBlockedReleases());
        home.Store.SaveMusicMonitor(monitor.Kind, monitor.Mbid, monitor.Name, new(Automatic with { AutoGrab = false }, monitor.Revision), home.Store.GetAccount("1")!);
        harness.Clock.Advance(TimeSpan.FromMinutes(10)); await harness.Coordinator.RunOnceAsync(CancellationToken.None); Assert.Equal(1, harness.Source.Grabs);
        home.Store.UnblockMusicRelease(Group, Assert.Single(home.Store.GetMusicBlockedReleases()).Fingerprint);
        harness.Clock.Advance(TimeSpan.FromDays(1)); await harness.Coordinator.RunOnceAsync(CancellationToken.None);
        Assert.Contains(home.Store.GetMusicAutomationJobs("1"), value => value.Status == "suggested"); Assert.Equal(1, harness.Source.Grabs);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CommitFenceRejectsRevokedRuleAndRevokedAccount(bool account)
    {
        using var home = new TestWorkspace(); home.Store.CreateOwner(new Owner("Owner", "fixture"));
        var admin = home.Store.SaveAccount(null, new AccountInput("Music admin", Role: "admin"), "fixture"); using var harness = new Harness(home.Store);
        var monitor = AddMonitor(home.Store, Automatic, harness.Clock.GetUtcNow(), admin);
        harness.Source.BeforeGrab = () => {
            if (account) home.Store.SaveAccount(admin.Id, new AccountInput("Music admin", Enabled: false));
            else home.Store.SaveMusicMonitor(monitor.Kind, monitor.Mbid, monitor.Name, new(new MusicMonitorSettings(), monitor.Revision), admin);
        };
        await harness.Coordinator.RunOnceAsync(CancellationToken.None); Assert.Empty(home.Store.GetDownloads());
        Assert.Equal("cancelled", Assert.Single(home.Store.GetMusicAutomationJobs(admin.Id)).Status);
    }
    [Fact]
    public async Task DailyMusicBudgetCountsGrabsAcrossArtistRules()
    {
        using var home = new TestWorkspace(); home.Store.CreateOwner(new Owner("Owner", "fixture")); using var harness = new Harness(home.Store);
        var settings = Automatic with { MaximumDailyGrabs = 1 }; AddMonitor(home.Store, settings, harness.Clock.GetUtcNow());
        await harness.Coordinator.RunOnceAsync(CancellationToken.None); Assert.Single(home.Store.GetDownloads());
        var other = home.Store.SaveMusicMonitor("artist", Guid.NewGuid().ToString(), "Other fixture artist", new(settings), home.Store.GetAccount("1")!);
        home.Store.RecordMusicMonitorPage(other, new([Album with { Id = Guid.NewGuid().ToString(), Title = "Other fixture album" }], 1, 0, 50), harness.Clock.GetUtcNow());
        await harness.Coordinator.RunOnceAsync(CancellationToken.None);
        Assert.Equal(1, harness.Source.Searches); Assert.Equal(1, harness.Source.Grabs); Assert.Contains(home.Store.GetMusicAutomationJobs("1"), job => job.Status == "waiting" && job.Message.Contains("Günlük"));
    }
    [Fact]
    public async Task ActualLocalCutoffAndOfflineVolumeBlockAutomaticReacquisition()
    {
        using var home = new TestWorkspace(); home.Store.CreateOwner(new Owner("Owner", "fixture")); var path = home.Write("Song.flac"); var library = home.Store.AddLibrary(new AddLibrary("Music", home.Source, "music")); await home.Scan(library.Id);
        var media = Assert.Single(home.Store.GetCatalog(null, null, null, 0, 60).Items);
        var tags = new AudioTags("Fixture Song", Album.Artist, Album.Artist, Album.Title, "2020-01-01", 1, 1, Artist, Group, ReleaseId, Release.Tracks[0].RecordingId, null, null, 16);
        var stream = new MediaStreamInfo(0, "audio", "flac", "und", "", true, false, false, null, null, 2, "stereo", 44100, 900000, null, null, null, null);
        var probe = new ProbeResult(1, null, null, null, "flac", Streams: [stream], Music: tags); home.Store.SaveProbe(media.Id, probe, false); home.Store.SaveAnalysis(media.Id, MediaAnalysisService.Fingerprint(new FileInfo(path)), probe);
        using var harness = new Harness(home.Store); AddMonitor(home.Store, Automatic with { AllowUpgrades = true }, harness.Clock.GetUtcNow());
        await harness.Coordinator.RunOnceAsync(CancellationToken.None); Assert.Equal(0, harness.Source.Searches); Assert.Empty(home.Store.GetDownloads());
        File.Move(path, path + ".offline"); var target = home.Store.MusicTarget(Release); Assert.True(target.Offline); Assert.False(MusicAutomationCoordinator.NeedsSearch(target, Automatic));
        File.Move(path + ".offline", path);
        var lossy = MusicReleaseEvaluator.Quality("mp3", 320, 0, 44100); var lossless = MusicReleaseEvaluator.Quality("flac", 900, 16, 44100); var high = MusicReleaseEvaluator.Quality("flac", 2000, 24, 96000);
        Assert.True(MusicReleaseEvaluator.Better(lossless, lossy)); Assert.True(MusicReleaseEvaluator.Better(high, lossless)); Assert.False(MusicReleaseEvaluator.Better(lossy, lossless));
        Assert.False(MusicReleaseEvaluator.Better(high with { SampleRate = 22050 }, lossless));
    }
    [Fact]
    public void MusicMetadataProfilesHonorTypesReleaseDatesAndMemberBoundaries()
    {
        using var home = new TestWorkspace(); home.Store.CreateOwner(new Owner("Owner", "fixture"));
        var member = home.Store.SaveAccount(null, new AccountInput("Member"), "fixture");
        Assert.Throws<DomainException>(() => home.Store.SaveMusicMonitor("artist", Artist, "Fixture", new(Automatic), member));
        var monitor = home.Store.SaveMusicMonitor("artist", Artist, "Fixture", new(new MusicMonitorSettings(Enabled: true)), member);
        Assert.False(MusicMonitorPolicy.Wanted(monitor, Album));
        Assert.False(MusicMonitorPolicy.Wanted(monitor, Album with { Date = DateTime.UtcNow.AddDays(5).ToString("yyyy-MM-dd"), SecondaryTypes = ["Live"] }));
        Assert.True(MusicMonitorPolicy.Wanted(monitor, Album with { Date = DateTime.UtcNow.AddDays(5).ToString("yyyy-MM-dd") }));
        Assert.False(MusicMonitorPolicy.Released(DateTime.UtcNow.Year.ToString(), DateTimeOffset.UtcNow));
        Assert.False(MusicMonitorPolicy.AcceptRelease(Release.Metadata with { Status = "Bootleg" }, MusicMonitorPolicy.Validate(Automatic), DateTimeOffset.UtcNow));
        var high = new RawRelease("hires", "Fixture.Artist-Fixture.Album-FLAC-24-96", "torrent", 1024, 10, null, "", null, []);
        var evaluation = MusicReleaseEvaluator.Evaluate(high, Release, new MusicQualityRules(MinimumBitDepth: 24, MinimumSampleRate: 96000));
        Assert.True(evaluation.Eligible); Assert.Equal(3, evaluation.Quality!.Tier);
    }
    private static MusicMonitor AddMonitor(MediaStore store, MusicMonitorSettings settings, DateTimeOffset now, Account? actor = null)
    {
        var monitor = store.SaveMusicMonitor("artist", Artist, Album.Artist, new(settings), actor ?? store.GetAccount("1")!);
        store.RecordMusicMonitorPage(monitor, new([Album], 1, 0, 50), now); return store.GetMusicMonitor(monitor.Id)!;
    }
    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow.AddSeconds(2); public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan value) => _now += value;
    }
    private sealed class Harness : IDisposable
    {
        private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = 30 });
        public Handler Handler { get; } = new(); public Clock Clock { get; } = new(); public Source Source { get; } public MusicAutomationCoordinator Coordinator { get; }
        public Harness(MediaStore store) { Source = new(store); Coordinator = new(store, new MusicBrainzProvider(new Clients(Handler), _cache), Source, new EventHub(), Clock); }
        public void Dispose() => _cache.Dispose();
    }
    private sealed class Source(MediaStore store) : IMusicAcquisition
    {
        public int Searches { get; private set; } public int Grabs { get; private set; } public Action? BeforeGrab { get; set; }
        public Task<MusicSearchResult> SearchMusicAsync(MusicSearchInput input, MusicBrainzProvider metadata, CancellationToken token)
        {
            Searches++; var indexer = store.GetIndexers().FirstOrDefault() ?? store.SaveIndexer(null, new IndexerInput("Fixture", "https://fixture.invalid/api", "torznab", null), "fixture-protected");
            var raw = new RawRelease("fixture-album", "Fixture.Artist-Fixture.Album-FLAC", "torrent", 1024, 10, null, "", null, []);
            var result = store.SaveMusicSource(indexer, raw, Release, input.Rules, MusicReleaseEvaluator.Evaluate(raw, Release, input.Rules), "fixture-url");
            return Task.FromResult(new MusicSearchResult([result], [new(indexer.Id, indexer.Name, 1, null)]));
        }
        public Task<DownloadRecord> GrabMusicAsync(string id, CancellationToken token, MusicAutomationLease? automation = null)
        {
            Grabs++; BeforeGrab?.Invoke(); var saved = store.GetMusicSource(id);
            return Task.FromResult(store.AddDownload([saved.Result.Fingerprint], saved.Result.Title, "torrent", "fixture-protected", 1024, music: new MusicDownloadContext(id, saved.Release, saved.Rules, automation)));
        }
    }
    private sealed class Clients(Handler handler) : IHttpClientFactory { public HttpClient CreateClient(string name) => new(handler, false) { BaseAddress = new Uri("https://musicbrainz.org/ws/2/") }; }
    private sealed class Handler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++; string json;
            if (request.RequestUri!.AbsolutePath.EndsWith("release-group")) json = $$$"""{"release-group-count":1,"release-groups":[{"id":"{{{Group}}}","title":"Fixture Album","first-release-date":"2020-01-01","primary-type":"Album","secondary-types":[],"artist-credit":[{"name":"Fixture Artist"}]}]}""";
            else if (request.RequestUri.AbsolutePath.EndsWith("release")) json = $$$"""{"release-count":1,"releases":[{"id":"{{{ReleaseId}}}","title":"Fixture Album","date":"2020-01-01","country":"TR","status":"Official","artist-credit":[{"name":"Fixture Artist"}],"media":[{"format":"CD","track-count":1}]}]}""";
            else json = $$$"""{"id":"{{{ReleaseId}}}","title":"Fixture Album","date":"2020-01-01","country":"TR","status":"Official","release-group":{"id":"{{{Group}}}"},"artist-credit":[{"name":"Fixture Artist"}],"media":[{"position":1,"format":"CD","track-count":1,"tracks":[{"id":"{{{Release.Tracks[0].Id}}}","position":1,"number":"1","title":"Fixture Song","length":1000,"recording":{"id":"{{{Release.Tracks[0].RecordingId}}}"}}]}]}""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }
    }
}
