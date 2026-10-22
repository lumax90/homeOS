using HomeServer.Core;
using HomeServer.Infrastructure;
using HomeServer.Sources;

namespace HomeServer.Tests;

public sealed class AutomationTests
{
    private static ProviderContent Movie => new("tmdb", 42, "movie", "Example Movie", "Example Movie", "", "2020-01-01", null, null, "https://www.themoviedb.org/movie/42", []);
    [Fact]
    public async Task DisabledAndNotificationOnlyRulesNeverCreateTransfers()
    {
        using var home = new TestWorkspace(); home.Store.CreateOwner(new Owner("Owner", "fixture-hash"));
        var content = home.Store.RequestContent(Movie, "all", "balanced");
        var clock = new Clock(); var source = new Source(home.Store);
        var worker = new AutomationCoordinator(home.Store, source, new EventHub(), clock);
        Assert.False(await worker.RunOnceAsync(CancellationToken.None)); Assert.Equal(0, source.Searches);
        home.Store.SaveAutomationRule(content.Id, new AutomationInput(Enabled: true));
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True(await worker.RunOnceAsync(CancellationToken.None));
        Assert.Equal("suggested", Assert.Single(home.Store.GetAutomationJobs()).Status);
        Assert.Empty(home.Store.GetDownloads()); Assert.Equal(0, source.Grabs);
        Assert.Contains(home.Store.GetNotifications("1"), item => item.Kind == "automation");
    }
    [Fact]
    public async Task AutomaticSelectionCreatesOneDurableTransferAndHonorsInFlightRevocation()
    {
        using var home = new TestWorkspace(); home.Store.CreateOwner(new("Owner", "fixture")); var content = home.Store.RequestContent(Movie, "all", "balanced");
        var clock = new Clock(); var source = new Source(home.Store); var worker = new AutomationCoordinator(home.Store, source, new EventHub(), clock);
        home.Store.SaveAutomationRule(content.Id, new AutomationInput(Enabled: true, AutoGrab: true)); clock.Advance(TimeSpan.FromSeconds(1));
        await worker.RunOnceAsync(CancellationToken.None); await worker.RunOnceAsync(CancellationToken.None);
        Assert.Single(home.Store.GetDownloads()); Assert.Equal(1, source.Grabs);
        Assert.Equal("grabbed", Assert.Single(home.Store.GetAutomationJobs()).Status);
        using var race = new TestWorkspace(); race.Store.CreateOwner(new("Owner", "fixture")); var raced = race.Store.RequestContent(Movie, "all", "balanced");
        var racingSource = new Source(race.Store) { BeforeGrab = () => race.Store.SaveAutomationRule(raced.Id, new AutomationInput()) };
        var racingWorker = new AutomationCoordinator(race.Store, racingSource, new EventHub(), new Clock());
        race.Store.SaveAutomationRule(raced.Id, new AutomationInput(Enabled: true, AutoGrab: true));
        await racingWorker.RunOnceAsync(CancellationToken.None);
        Assert.Empty(race.Store.GetDownloads());
        Assert.Equal("cancelled", Assert.Single(race.Store.GetAutomationJobs()).Status);
    }
    [Fact]
    public async Task DelayRefreshesCandidatesAndFailedReleasesAreBlocked()
    {
        using var home = new TestWorkspace(); home.Store.CreateOwner(new("Owner", "fixture")); var content = home.Store.RequestContent(Movie, "all", "balanced");
        var clock = new Clock(); var source = new Source(home.Store); var worker = new AutomationCoordinator(home.Store, source, new EventHub(), clock);
        home.Store.SaveAutomationRule(content.Id, new AutomationInput(Enabled: true, AutoGrab: true, DelayMinutes: 30)); clock.Advance(TimeSpan.FromSeconds(1));
        await worker.RunOnceAsync(CancellationToken.None);
        Assert.Equal("waiting", Assert.Single(home.Store.GetAutomationJobs()).Status); Assert.Empty(home.Store.GetDownloads());
        clock.Advance(TimeSpan.FromMinutes(31)); await worker.RunOnceAsync(CancellationToken.None);
        var download = Assert.Single(home.Store.GetDownloads()); Assert.Equal(2, source.Searches);
        home.Store.UpdateDownload(download.Id, download.Revision, "failed", "Corrupt fixture", 100, 1024);
        await worker.RunOnceAsync(CancellationToken.None);
        Assert.Single(home.Store.GetBlockedReleases());
        clock.Advance(TimeSpan.FromHours(7)); await worker.RunOnceAsync(CancellationToken.None);
        Assert.Single(home.Store.GetDownloads()); Assert.Equal(1, source.Grabs);
    }
    [Fact]
    public async Task PendingFutureAndCutoffSatisfiedContentIsNotAcquired()
    {
        using var home = new TestWorkspace(); home.Store.CreateOwner(new Owner("Owner", "fixture-hash"));
        var user = home.Store.SaveAccount(null, new AccountInput("Member"), "fixture-hash");
        var pending = home.Store.RequestContent(Movie, "all", "balanced", user);
        home.Store.SaveAutomationRule(pending.Id, new AutomationInput(Enabled: true, AutoGrab: true));
        var source = new Source(home.Store); var clock = new Clock(); var worker = new AutomationCoordinator(home.Store, source, new EventHub(), clock);
        await worker.RunOnceAsync(CancellationToken.None); Assert.Equal(0, source.Searches);
        var future = home.Store.RequestContent(Movie with { ProviderId = 43, ReleaseDate = DateTime.UtcNow.AddDays(30).ToString("yyyy-MM-dd") }, "all", "balanced");
        home.Store.SaveAutomationRule(future.Id, new AutomationInput(Enabled: true, AutoGrab: true));
        clock.Advance(TimeSpan.FromSeconds(1)); await worker.RunOnceAsync(CancellationToken.None); Assert.Equal(0, source.Searches);
        home.Store.RequestContent(Movie, "all", "balanced");
        home.Write("Example.Movie.2020.mkv"); var library = home.AddLibrary(); await home.Scan(library.Id);
        var media = Assert.Single(home.Store.GetCatalog(null, null, null, 0, 60).Items);
        home.Store.SaveProbe(media.Id, new ProbeResult(120, 3840, 1600, "hevc", "aac"), false); home.Store.MatchMedia(media.Id, Movie, null, "");
        home.Store.SaveAutomationRule(pending.Id, new AutomationInput(Enabled: true, AutoGrab: true, AllowUpgrades: true, CutoffResolution: 2160));
        clock.Advance(TimeSpan.FromSeconds(1)); await worker.RunOnceAsync(CancellationToken.None); Assert.Equal(0, source.Searches);
    }
    [Fact]
    public async Task DailyQuotaAppliesAcrossEpisodeJobs()
    {
        using var home = new TestWorkspace(); home.Store.CreateOwner(new("Owner", "fixture"));
        var show = Movie with { Kind = "show", Title = "Example Show", OriginalTitle = "Example Show" };
        var content = home.Store.RequestContent(show, "all", "balanced"); var sync = home.Store.ClaimTrackingSync()!.Value;
        home.Store.CompleteTrackingSync(content.Id, sync.Revision, show, [new(11, 1, 1, "One", "", "2020-01-01", null), new(12, 1, 2, "Two", "", "2020-01-02", null)]);
        home.Store.SaveAutomationRule(content.Id, new AutomationInput(Enabled: true, AutoGrab: true, MaximumDailyGrabs: 1));
        var clock = new Clock(); var source = new Source(home.Store); var worker = new AutomationCoordinator(home.Store, source, new EventHub(), clock);
        await worker.RunOnceAsync(CancellationToken.None); await worker.RunOnceAsync(CancellationToken.None);
        Assert.Single(home.Store.GetDownloads()); Assert.Equal(1, source.Grabs);
        Assert.Contains(home.Store.GetAutomationJobs(), job => job.Status == "waiting" && job.Message.Contains("Günlük"));
    }

    [Fact]
    public async Task UserCancellationSuppressesAutomaticReplacementAndRecoveryKeepsIntent()
    {
        using var home = new TestWorkspace(); home.Store.CreateOwner(new("Owner", "fixture")); var content = home.Store.RequestContent(Movie, "all", "balanced"); var clock = new Clock();
        home.Store.SaveAutomationRule(content.Id, new AutomationInput(Enabled: true, AutoGrab: true));
        home.Store.ScheduleAutomation(clock.GetUtcNow()); var claimed = home.Store.ClaimAutomationJob(clock.GetUtcNow())!;
        home.Store.RecoverAutomationJobs(); Assert.Equal("queued", home.Store.GetAutomationJob(claimed.Id)!.Status);
        var source = new Source(home.Store); var worker = new AutomationCoordinator(home.Store, source, new EventHub(), clock);
        await worker.RunOnceAsync(CancellationToken.None); var download = Assert.Single(home.Store.GetDownloads());
        home.Store.SetDownloadIntent(download.Id, "cancelled"); await worker.RunOnceAsync(CancellationToken.None);
        clock.Advance(TimeSpan.FromDays(1)); await worker.RunOnceAsync(CancellationToken.None);
        Assert.Equal(1, source.Grabs); Assert.Single(home.Store.GetDownloads());
    }
    [Fact]
    public async Task AccountBudgetWaitDoesNotSearchOrBlockTheReleaseAndPolicyChangeWakesIt()
    {
        using var home = new TestWorkspace(); home.Store.CreateOwner(new("Owner", "fixture"));
        var content = home.Store.RequestContent(Movie, "all", "balanced"); var clock = new Clock(); var source = new Source(home.Store);
        home.Store.SaveAutomationRule(content.Id, new(Enabled: true, AutoGrab: true)); home.Store.SaveDownloadBudget("1", new(MonthlyAcquisitions: 0));
        var worker = new AutomationCoordinator(home.Store, source, new EventHub(), clock);
        for (var index = 0; index < 7; index++) { home.Store.WakeAutomation(content.Id); clock.Advance(TimeSpan.FromSeconds(1)); await worker.RunOnceAsync(CancellationToken.None); }
        Assert.Equal(0, source.Searches); Assert.Equal("waiting", Assert.Single(home.Store.GetAutomationJobs()).Status); Assert.Empty(home.Store.GetBlockedReleases());
        home.Store.SaveDownloadBudget("1", new(MonthlyAcquisitions: 2, Revision: 1)); clock.Advance(TimeSpan.FromSeconds(1)); await worker.RunOnceAsync(CancellationToken.None);
        var download = Assert.Single(home.Store.GetDownloads()); Assert.Equal("1", download.Ownership!.UserId); Assert.Equal("video-automation", download.Ownership.Origin);
        Assert.Equal(1, home.Store.GetDownloadBudgetUsage("1", DateTimeOffset.UtcNow).Acquisitions);
    }
    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow.AddSeconds(2);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now += duration;
    }
    private sealed class Source(MediaStore store) : IReleaseAcquisition
    {
        public int Searches { get; private set; }
        public int Grabs { get; private set; }
        public Action? BeforeGrab { get; init; }
        public Task<ReleaseSearchResult> SearchAsync(ReleaseSearchInput input, CancellationToken token, QualityProfile? profileOverride = null)
        {
            Searches++;
            var indexer = store.GetIndexers().FirstOrDefault() ?? store.SaveIndexer(null, new IndexerInput("Fixture", "https://fixture.invalid/api", "torznab", null), "encrypted-fixture");
            var target = store.GetContent(input.ContentId)!; var profile = profileOverride ?? store.GetQualityProfile(target.QualityProfileId);
            var title = target.Metadata.Kind == "movie" ? "Example.Movie.2020.1080p.WEB-DL" : $"Example.Show.S{input.Season:00}E{input.Episode:00}.1080p.WEB-DL";
            var raw = new RawRelease($"stable-fixture-{input.ContentId}-{input.Season}-{input.Episode}", title, "torrent", 1024, 10, DateTimeOffset.UtcNow.AddDays(-1).ToString("O"), "", null, []);
            var release = store.SaveRelease(indexer, target, input, profile, raw, ReleaseEvaluator.Evaluate(raw, target.Metadata, profile.Rules, input.Season, input.Episode), "protected-fixture");
            return Task.FromResult(new ReleaseSearchResult([release], [new(indexer.Id, indexer.Name, 1, null)]));
        }
        public Task<DownloadRecord> GrabAsync(string id, bool allowRejected, CancellationToken token, AutomationLease? automation = null)
        {
            Grabs++; BeforeGrab?.Invoke();
            var release = store.GetRelease(id);
            return Task.FromResult(store.AddDownload([MediaStore.StableFingerprint(release.Result.Fingerprint)], release.Result.Title, "torrent", "encrypted-fixture", 1024,
                new AcquisitionContext(release.ContentId, release.Season, release.Episode, id, automation)));
        }
    }
}
