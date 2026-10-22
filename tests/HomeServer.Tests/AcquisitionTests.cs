using HomeServer.Core;
using HomeServer.Downloads;
using HomeServer.Infrastructure;
using HomeServer.Sources;
using Microsoft.Data.Sqlite;

namespace HomeServer.Tests;

public sealed class AcquisitionTests
{
    private static ProviderContent Movie => new("tmdb", 42, "movie", "Example Movie", "Example Movie", "", "2020-01-01", null, null, "https://www.themoviedb.org/movie/42", []);

    [Fact]
    public async Task VerifiedReleasePublishesThenLinksWithoutProviderNetworkAndIsIdempotent()
    {
        using var home = new TestWorkspace();
        var (download, session, files) = Prepare(home, Movie, "Example.Movie.2020.1080p.WEB-DL.mkv");
        var importer = new DownloadImporter(files, home.Store, new EventHub(), new Probe());
        await importer.ImportAsync(download, session, CancellationToken.None);
        Assert.Equal("waiting", home.Store.GetDownload(download.Id)!.Acquisition!.Status);
        var scan = home.Store.TryClaimJob()!;
        await home.Scanner.ScanAsync(scan, CancellationToken.None);
        home.Store.LinkAcquisition(download.Id);
        var media = Assert.Single(home.Store.GetCatalog(null, null, null, 0, 60).Items);
        Assert.Equal(Movie.ProviderId, media.Content!.ProviderId);
        Assert.Equal("linked", home.Store.GetDownload(download.Id)!.Acquisition!.Status);
        home.Store.LinkAcquisition(download.Id);
        Assert.Single(home.Store.GetVersions(media.Id));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, await File.ReadAllBytesAsync(session.Files[0].FullPath));
        Assert.Empty(home.Store.PendingAcquisitionLinks());
    }

    [Theory]
    [InlineData("Example.Movie.2021.1080p.mkv")]
    [InlineData("Example.Movie.Sequel.2020.1080p.mkv")]
    [InlineData("Example.Movie.2020.S01E01.1080p.mkv")]
    [InlineData("audio-only.flac")]
    public async Task WrongIdentityCannotBePublishedOrFulfillAnExistingRequest(string filename)
    {
        using var home = new TestWorkspace();
        var (download, session, files) = Prepare(home, Movie, filename);
        var importer = new DownloadImporter(files, home.Store, new EventHub(), new Probe());
        await Assert.ThrowsAsync<DomainException>(() => importer.ImportAsync(download, session, CancellationToken.None));
        Assert.Null(home.Store.GetDownload(download.Id)!.ImportedAt);
        Assert.Empty(home.Store.GetLibraries());
        Assert.Empty(Directory.GetDirectories(files.LibraryRoot));
        Assert.True(File.Exists(session.Files[0].FullPath));
    }

    [Fact]
    public async Task ActualProbeQualityOverridesReleaseNameAndPreservesExistingLibrary()
    {
        using var home = new TestWorkspace();
        var library = home.AddLibrary(); home.Write("Old.Version.mp4"); await home.Scan(library.Id);
        var before = Assert.Single(home.Store.GetCatalog(null, null, null, 0, 60).Items);
        var (download, session, files) = Prepare(home, Movie, "Example.Movie.2020.1080p.mkv");
        var importer = new DownloadImporter(files, home.Store, new EventHub(), new Probe { Width = 640, Height = 480 });
        var error = await Assert.ThrowsAsync<DomainException>(() => importer.ImportAsync(download, session, CancellationToken.None));
        Assert.Contains("gerçek çözünürlüğü", error.Message);
        Assert.Equal(before.Id, Assert.Single(home.Store.GetCatalog(null, null, null, 0, 60).Items).Id);
        Assert.Null(home.Store.GetDownload(download.Id)!.ImportedAt);
    }

    [Fact]
    public async Task ExplicitImportReviewChangesOnlyTheNewValidationSnapshot()
    {
        using var home = new TestWorkspace();
        var (download, session, files) = Prepare(home, Movie, "Example.Movie.2020.2160p.mkv");
        var importer = new DownloadImporter(files, home.Store, new EventHub(), new Probe { Width = 3840, Height = 1600 });
        await Assert.ThrowsAsync<DomainException>(() => importer.ImportAsync(download, session, CancellationToken.None));
        var original = home.Store.GetAcquisition(download.Id)!.ProfileJson;
        home.Store.UpdateDownload(download.Id, download.Revision, "failed", "Profile mismatch", 100, 4);
        var reviewed = home.Store.ReviewImportProfile(download.Id, "archive");
        Assert.True(reviewed.Revision > download.Revision);
        Assert.NotEqual(original, home.Store.GetAcquisition(download.Id)!.ProfileJson);
        using (var db = new SqliteConnection($"Data Source={Path.Combine(home.Paths.Data, "home.db")}"))
        {
            db.Open();
            using var query = db.CreateCommand();
            query.CommandText = "SELECT r.profile_json FROM release_results r JOIN acquisition_links a ON a.release_id=r.id WHERE a.download_id=$id";
            query.Parameters.AddWithValue("$id", download.Id);
            Assert.Equal(original, query.ExecuteScalar() as string);
        }
        await importer.ImportAsync(reviewed, session, CancellationToken.None);
        Assert.NotNull(home.Store.GetDownload(download.Id)!.ImportedAt);
        Assert.Throws<DomainException>(() => home.Store.ReviewImportProfile(download.Id, "balanced"));
    }

    [Fact]
    public async Task NewPauseIntentFencesPublicationEvenAfterValidation()
    {
        using var home = new TestWorkspace();
        var (download, session, files) = Prepare(home, Movie, "Example.Movie.2020.1080p.mkv");
        var probe = new Probe { OnRead = () => home.Store.SetDownloadIntent(download.Id, "paused") };
        var importer = new DownloadImporter(files, home.Store, new EventHub(), probe);
        await Assert.ThrowsAsync<OperationCanceledException>(() => importer.ImportAsync(download, session, CancellationToken.None));
        Assert.Null(home.Store.GetDownload(download.Id)!.ImportedAt);
        Assert.Empty(Directory.GetDirectories(files.LibraryRoot));
    }

    [Fact]
    public async Task LinkingNeverOverridesAUsersLaterManualMatch()
    {
        using var home = new TestWorkspace();
        var (download, session, files) = Prepare(home, Movie, "Example.Movie.2020.1080p.mkv");
        await new DownloadImporter(files, home.Store, new EventHub(), new Probe()).ImportAsync(download, session, CancellationToken.None);
        await home.Scanner.ScanAsync(home.Store.TryClaimJob()!, CancellationToken.None);
        var media = Assert.Single(home.Store.GetCatalog(null, null, null, 0, 60).Items);
        home.Store.MatchMedia(media.Id, Movie with { ProviderId = 43, Title = "Manual correction" }, null, "My edition");
        Assert.Throws<DomainException>(() => home.Store.LinkAcquisition(download.Id));
        Assert.Equal(43, home.Store.GetMedia(media.Id)!.Content!.ProviderId);
        Assert.Equal("My edition", home.Store.GetMedia(media.Id)!.Content!.Edition);
    }

    [Fact]
    public async Task OneEpisodeDoesNotPretendTheWholeShowIsAvailable()
    {
        using var home = new TestWorkspace();
        var show = Movie with { Provider = "tvmaze", Kind = "show", Title = "Example Show", OriginalTitle = "Example Show" };
        var target = home.Store.RequestContent(show, "all", "balanced");
        var sync = home.Store.ClaimTrackingSync()!.Value;
        home.Store.CompleteTrackingSync(target.Id, sync.Revision, show, [new(1, 1, 1, "First", "", "2020-01-01", null), new(2, 1, 2, "Second", "", "2020-01-02", null)]);
        var (download, session, files) = Prepare(home, show, "Example.Show.S01E01.1080p.mkv", 1, 1);
        await new DownloadImporter(files, home.Store, new EventHub(), new Probe()).ImportAsync(download, session, CancellationToken.None);
        await home.Scanner.ScanAsync(home.Store.TryClaimJob()!, CancellationToken.None);
        home.Store.LinkAcquisition(download.Id);
        Assert.Equal(1, home.Store.GetContent(target.Id)!.MissingCount);
        Assert.Equal("missing", home.Store.GetEpisodes(target.Id).Single(e => e.EpisodeNumber == 2).State);
        Assert.Equal("available", home.Store.GetEpisodes(target.Id).Single(e => e.EpisodeNumber == 1).State);
    }

    private static (DownloadRecord, Session, DownloadFiles) Prepare(TestWorkspace home, ProviderContent target, string filename, int? season = null, int? episode = null)
    {
        var entry = home.Store.RequestContent(target, "all", "balanced");
        var source = home.Store.SaveIndexer(null, new IndexerInput("Fixture source", "https://fixture.invalid/api", "torznab", null), "protected");
        var profile = home.Store.GetQualityProfile("balanced");
        var raw = new RawRelease("fixture", filename, "torrent", 4, 10, null, "", null, []);
        var release = home.Store.SaveRelease(source, entry, new ReleaseSearchInput(entry.Id, season, episode), profile, raw,
            ReleaseEvaluator.Evaluate(raw, target, profile.Rules, season, episode), "protected-url");
        var download = home.Store.AddDownload([Guid.NewGuid().ToString("N")], filename, "torrent", "protected", 4, new AcquisitionContext(entry.Id, season, episode, release.Id));
        var files = new DownloadFiles(home.Paths);
        var root = files.Payload(download.Id);
        var path = Path.Combine(root, filename);
        File.WriteAllBytes(path, [1, 2, 3, 4]);
        return (download, new Session(root, [new TorrentFileSnapshot(filename, path, 4)]), files);
    }

    private sealed class Probe : IMediaProbe
    {
        public bool FfprobeAvailable => true;
        public int Width { get; init; } = 1920;
        public int Height { get; init; } = 800;
        public Action? OnRead { get; init; }
        public Task<ProbeResult?> ProbeAsync(string path, CancellationToken cancellationToken)
        {
            OnRead?.Invoke();
            return Task.FromResult<ProbeResult?>(new ProbeResult(120, Width, Height, "h264", "aac", ["en"]));
        }
    }
    private sealed class Session(string directory, TorrentFileSnapshot[] files) : ITorrentSession
    {
        public TorrentSnapshot Snapshot => new("Fixture", "completed", 100, 4, 0, 0, 0, true);
        public IReadOnlyList<TorrentFileSnapshot> Files => files;
        public string Directory => directory;
        public Task StartAsync() => Task.CompletedTask;
        public Task StopAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
