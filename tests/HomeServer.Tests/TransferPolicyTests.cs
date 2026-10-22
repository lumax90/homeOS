using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using HomeServer.Core;
using HomeServer.Downloads;
using HomeServer.Infrastructure;
using HomeServer.Sources;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using MonoTorrent;
using MonoTorrent.Client;

namespace HomeServer.Tests;

public sealed class TransferPolicyTests
{
    [Fact]
    public void CountersAreIdempotentAndCommandsFenceMaintenanceAndTelemetry()
    {
        using var home = new TestWorkspace(); var download = home.Store.AddDownload(["hash"], "Fixture", "torrent", "protected", 100);
        home.Store.RecordTransferCounters(download.Id, download.Revision, "run", 40, 100, 60);
        home.Store.RecordTransferCounters(download.Id, download.Revision, "run", 40, 100, 60);
        home.Store.RecordTransferCounters(download.Id, download.Revision, "run", 20, 80, 30);
        Assert.Equal(40, home.Store.GetDownload(download.Id)!.UploadedBytes); Assert.Equal(60, home.Store.GetDownload(download.Id)!.SeedSeconds);
        home.Store.SaveTransferPolicy(download.Id, new TransferPolicy(Priority: 1, SeedRatio: 2));
        var current = home.Store.GetDownload(download.Id)!;
        Assert.Equal(1, current.Priority);
        home.Store.RecordTransferCounters(download.Id, download.Revision, "stale", 999, 999, 999);
        Assert.Equal(40, home.Store.GetDownload(download.Id)!.UploadedBytes);
        var paused = home.Store.SetDownloadIntent(download.Id, "paused");
        home.Store.SetDownloadIntent(download.Id, "running"); var cleaned = false;
        Assert.Throws<DomainException>(() => home.Store.CleanDownloadPayload(download.Id, paused.Revision, () => cleaned = true)); Assert.False(cleaned);
        home.Store.SetDownloadIntent(download.Id, "paused"); home.Store.ArchiveDownload(download.Id);
        Assert.Empty(home.Store.GetDownloads()); Assert.Single(home.Store.GetDownloads(true));
        home.Store.RestoreDownload(download.Id); Assert.Equal("paused", Assert.Single(home.Store.GetDownloads()).DesiredState);
    }

    [Fact]
    public void SeedTargetsRespectSourceCriteriaAndMonotonicAccounting()
    {
        using var home = new TestWorkspace(); var download = home.Store.AddDownload(["hash"], "Fixture", "torrent", "protected", 100);
        download = download with { ReceivedBytes = 100, UploadedBytes = 200, SeedSeconds = 600 };
        var policy = new TransferPolicy(SeedRatio: 1, SeedMinutes: 60, SeedStop: "either");
        Assert.True(DownloadWorker.SeedSatisfied(download, policy, new SourceSeedRules(2, 60, SeedType: "ratio")));
        Assert.False(DownloadWorker.SeedSatisfied(download, policy, new SourceSeedRules(2, 60, SeedType: "both")));
        Assert.True(DownloadWorker.SeedSatisfied(download, policy, new SourceSeedRules(2, 60, SeedType: "either")));
        Assert.False(DownloadWorker.SeedSatisfied(download, policy with { SeedStop = "both" }, new SourceSeedRules()));
        Assert.False(DownloadWorker.SeedSatisfied(download, new TransferPolicy(), new SourceSeedRules()));
        var rules = ReleaseEvaluator.SeedRules(new RawRelease("", "", "torrent", 1, 1, null, "", null,
            new() { ["minimumratio"] = ["1.5"], ["minimumseedtime"] = ["3601"], ["seedtype"] = ["both"], ["downloadvolumefactor"] = ["0"] }));
        Assert.Equal(61, rules.MinimumMinutes); Assert.Equal(0, rules.DownloadFactor); Assert.Equal("both", rules.SeedType);
    }

    [Fact]
    public async Task PartialTorrentSelectionIsVerifiedAndCleanupPreservesSourceAndPublishedMedia()
    {
        using var home = new TestWorkspace();
        var wanted = RandomNumberGenerator.GetBytes(64 * 1024); var ignored = RandomNumberGenerator.GetBytes(512 * 1024);
        var original = home.Write("Wanted.mkv", wanted); home.Write("Ignored.mkv", ignored);
        var creator = new TorrentCreator { PieceLength = 16 * 1024 };
        var encoded = (await creator.CreateAsync(new TorrentFileSource(home.Source))).Encode(); var torrent = Torrent.Load(encoded);
        using var portListener = new TcpListener(IPAddress.Loopback, 0); portListener.Start(); var port = ((IPEndPoint)portListener.LocalEndpoint).Port; portListener.Stop();
        using var seed = new ClientEngine(new EngineSettingsBuilder { CacheDirectory = Path.Combine(home.Root, "seed"), DhtEndPoint = null,
            ListenEndPoints = new Dictionary<string, IPEndPoint> { ["v4"] = new(IPAddress.Loopback, port) }, AllowLocalPeerDiscovery = false, AllowPortForwarding = false }.ToSettings());
        var manager = await seed.AddAsync(torrent, home.Source, new TorrentSettingsBuilder { CreateContainingDirectory = false, AllowDht = false, AllowPeerExchange = false }.ToSettings());
        await manager.HashCheckAsync(false); await manager.StartAsync();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Home:Downloads:LoopbackOnly"] = "true" }).Build();
        var paths = new DownloadFiles(home.Paths); using var engine = new TorrentEngine(paths, home.Store, config, new EphemeralDataProtectionProvider());
        var record = engine.AddTorrent(encoded); var file = engine.ListFiles(record.Id).Single(file => file.Path.EndsWith("Wanted.mkv", StringComparison.Ordinal));
        var policy = new TransferPolicy(DownloadKiB: 64, UploadKiB: 32, SeedRatio: 0, Files: [file.Path], FilePriorities: new() { [file.Path] = 4 });
        engine.ValidateSelection(record.Id, policy); home.Store.SaveTransferPolicy(record.Id, policy); record = home.Store.GetDownload(record.Id)!;
        string published;
        await using (var session = await engine.OpenAsync(home.Store.GetDownloadSource(record.Id), CancellationToken.None))
        {
            var actual = ((TorrentEngine.Session)session).Manager;
            Assert.Equal(64 * 1024, actual.Settings.MaximumDownloadRate); Assert.Equal(32 * 1024, actual.Settings.MaximumUploadRate);
            Assert.Equal(Priority.High, actual.Files.Single(value => value.Path.EndsWith("Wanted.mkv", StringComparison.Ordinal)).Priority);
            home.Store.SaveTransferDefaults(new TransferDefaults(128, 64, 80)); await engine.ApplyLimitsAsync(CancellationToken.None);
            Assert.Equal(128 * 1024, actual.Engine!.Settings.MaximumDownloadRate);
            await engine.SetNntpActiveAsync(true); Assert.Equal(64 * 1024, actual.Engine.Settings.MaximumDownloadRate);
            await engine.SetNntpActiveAsync(false); Assert.Equal(128 * 1024, actual.Engine.Settings.MaximumDownloadRate);
            await session.StartAsync(); await actual.AddPeerAsync(new PeerInfo(new Uri($"ipv4://127.0.0.1:{port}")));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            while (!session.Snapshot.Complete) await Task.Delay(50, timeout.Token);
            Assert.False(actual.Complete); Assert.Single(session.Files);
            await session.StopAsync(); Assert.Equal(wanted, await File.ReadAllBytesAsync(session.Files[0].FullPath, timeout.Token));
            var importer = new DownloadImporter(paths, home.Store, new EventHub()); await importer.ImportAsync(record, session, timeout.Token);
            published = Path.Combine(paths.LibraryRoot, record.Id, file.Path); Assert.Equal(wanted, await File.ReadAllBytesAsync(published, timeout.Token));
        }
        var paused = home.Store.SetDownloadIntent(record.Id, "paused");
        home.Store.CleanDownloadPayload(record.Id, paused.Revision, () => engine.CleanPayload(record.Id));
        Assert.False(Directory.Exists(Path.Combine(paths.Root, "staging", record.Id)));
        Assert.Equal(wanted, File.ReadAllBytes(published)); Assert.Equal(wanted, File.ReadAllBytes(original));
        Assert.NotNull(home.Store.GetDownload(record.Id)!.PayloadCleanedAt);
        await manager.StopAsync();
    }

    [Fact]
    public void CustomFormatsAreExplainableStableAndCheckActualCodec()
    {
        var content = new ProviderContent("tmdb", 1, "movie", "Example", "Example", "", "2020-01-01", null, null, "https://www.themoviedb.org/movie/1", []);
        var rules = new QualityRules([1080, 720], 10, 0, [], [], [], false, CustomFormats: [new("HEVC", 100, Codecs: ["hevc"], Required: true)], MinimumCustomScore: 100);
        var raw = new RawRelease("", "Example.2020.1080p.WEB-DL.x265.REPACK2", "torrent", 1024, 1, DateTimeOffset.UtcNow.ToString("O"), "", null, []);
        var evaluation = ReleaseEvaluator.Evaluate(raw, content, rules);
        Assert.True(evaluation.Eligible); Assert.Equal(2, evaluation.Parsed.ProperVersion); Assert.Equal(100, evaluation.CustomScore);
        var seeded = ReleaseEvaluator.Evaluate(raw with { Seeders = 10000 }, content, rules);
        Assert.Equal(evaluation.QualityScore, seeded.QualityScore);
        Assert.True(ReleaseEvaluator.PreferenceScore(raw, rules) > ReleaseEvaluator.PreferenceScore(raw with { Title = "Example.2020.1080p.WEB-DL.x265" }, rules));
        Assert.NotEmpty(ReleaseEvaluator.ActualPreferenceRejections(raw, rules, new ProbeResult(100, 1920, 1080, "h264", "aac")));
        Assert.Empty(ReleaseEvaluator.ActualPreferenceRejections(raw, rules, new ProbeResult(100, 1920, 1080, "hevc", "aac")));
        Assert.False(ReleaseEvaluator.Evaluate(raw with { Protocol = "usenet" }, content, rules with { Protocols = ["torrent"] }).Eligible);
        Assert.False(ReleaseEvaluator.Evaluate(raw with { PublishedAt = DateTimeOffset.UtcNow.AddYears(-1).ToString("O") }, content, rules with { MaximumAgeDays = 30 }).Eligible);
    }

    [Fact]
    public void RegexBudgetRejectsPathologicalFormatsAndUnknownCurrentQualityStopsBlindUpgrades()
    {
        var rules = new QualityRules([1080], 10, 0, [], [], [], false, CustomFormats: [new("Budget", 10, Pattern: "^(a+)+$", Required: true)]);
        var result = ReleaseEvaluator.Evaluate(new RawRelease("", new string('a', 4000) + "!", "torrent", 100, 1, null, "", null, []),
            new ProviderContent("tmdb", 1, "movie", "Test", "Test", "", null, null, null, "", []), rules);
        Assert.Contains(result.Rejections, value => value.Contains("süre sınırını"));
        Assert.Throws<DomainException>(() => ReleaseEvaluator.ValidatePreferences(rules with { CustomFormats = [new("Invalid", Pattern: "[")] }));
    }
    [Fact]
    public async Task SameResolutionUpgradeRequiresKnownExistingQualityAndStrongerPreference()
    {
        using var home = new TestWorkspace(); home.Write("Example.2020.1080p.WEB-DL.x265.mkv");
        var library = home.AddLibrary(); await home.Scan(library.Id);
        var content = new ProviderContent("tmdb", 1, "movie", "Example", "Example", "", "2020-01-01", null, null, "https://www.themoviedb.org/movie/1", []);
        var saved = home.Store.RequestContent(content, "all", "balanced"); var media = home.Store.GetCatalog(null, null, library.Id, 0, 60).Items.Single();
        home.Store.MatchMedia(media.Id, content, null, ""); home.Store.SaveProbe(media.Id, new ProbeResult(100, 1920, 1080, "hevc", "aac"), false);
        Assert.Empty(home.Store.AutomationTargets(saved.Id, new AutomationInput(Enabled: true, AllowUpgrades: true)));
        var target = Assert.Single(home.Store.AutomationTargets(saved.Id, new AutomationInput(Enabled: true, AllowUpgrades: true, SameQualityUpgrades: true)));
        Assert.NotNull(target.ExistingQualityScore);
        var raw = new RawRelease("", "Example.2020.1080p.WEB-DL.x265.REPACK2", "torrent", 1000, 10, null, "", null, []);
        Assert.True(ReleaseEvaluator.Evaluate(raw, content, home.Store.GetQualityProfile("balanced").Rules).QualityScore > target.ExistingQualityScore);
        var unknown = media with { RelativePath = "Unknown.mkv" }; Assert.Null(home.Store.ExistingPreferenceScore(unknown, home.Store.GetQualityProfile("balanced").Rules));
    }
}
