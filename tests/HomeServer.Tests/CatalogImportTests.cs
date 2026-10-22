using HomeServer.Core;
using HomeServer.Downloads;
using HomeServer.Infrastructure;
using HomeServer.Metadata;
using HomeServer.Scanning;
using HomeServer.Sources;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace HomeServer.Tests;

public sealed class CatalogImportTests
{
    [MediaFact]
    public async Task NativeMultipartMovieAndMultiEpisodeImportsKeepPayloadAndCanonicalCoverage()
    {
        foreach (var show in new[] { false, true })
        {
            using var home = new TestWorkspace(); home.Store.CreateOwner(new Owner("Owner", "fixture"));
            var tools = new MediaTools(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Home:FfmpegPath"] = Environment.GetEnvironmentVariable("HOME_TEST_FFMPEG"), ["Home:FfprobePath"] = Environment.GetEnvironmentVariable("HOME_TEST_FFPROBE") }).Build(), home.Paths, NullLogger<MediaTools>.Instance);
            var metadata = new ProviderContent("tmdb", show ? 42 : 43, show ? "show" : "movie", show ? "Fixture Show" : "Fixture Movie", show ? "Fixture Show" : "Fixture Movie", "", "2020-01-01", null, null, "https://www.themoviedb.org/", []);
            var content = home.Store.RequestContent(metadata, "all", "balanced");
            if (show) home.Store.RefreshCatalogIdentity(content.Id, content.CatalogRevision, metadata, [new(1001, 1, 1, "One", "", "2020-01-01", null), new(1002, 1, 2, "Two", "", "2020-01-02", null)]);
            var names = show ? new[] { "Fixture.Show.S01E01E02.720p.WEB-DL.mkv" } : new[] { "Fixture.Movie.2020.CD1.720p.WEB-DL.mkv", "Fixture.Movie.2020.CD2.720p.WEB-DL.mkv" };
            foreach (var name in names) await MediaProcess.RunAsync(tools.FfmpegExecutable, ["-hide_banner", "-loglevel", "error", "-nostdin", "-f", "lavfi", "-i", "color=c=black:s=1280x720:r=24", "-t", "0.25", "-c:v", "libx264", "-threads", "1", "-pix_fmt", "yuv420p", Path.Combine(home.Source, name)], TimeSpan.FromSeconds(20), CancellationToken.None);
            var hashes = names.ToDictionary(name => name, name => StateFolders.Hash(Path.Combine(home.Source, name)));
            var indexer = home.Store.SaveIndexer(null, new("Fixture", "https://fixture.invalid/api", "torznab", null), "protected"); var profile = home.Store.GetQualityProfile("balanced");
            var release = new RawRelease("fixture", show ? names[0] : "Fixture.Movie.2020.720p.WEB-DL", "torrent", 100000, 10, null, "", null, []);
            var input = new ReleaseSearchInput(content.Id, show ? 1 : null, show ? 1 : null); var saved = home.Store.SaveRelease(indexer, home.Store.GetContent(content.Id)!, input, profile, release, ReleaseEvaluator.Evaluate(release, metadata, profile.Rules, input.Season, input.Episode), "protected");
            var download = home.Store.AddDownload(["fixture-composition"], release.Title, "torrent", "protected", 100000, new(content.Id, input.Season, input.Episode, saved.Id));
            await using var session = new PayloadSession(home.Source); var importer = new DownloadImporter(new DownloadFiles(home.Paths), home.Store, new EventHub(), tools);
            await importer.ImportAsync(download, session, CancellationToken.None);
            var scanner = new LibraryScanner(home.Store, tools, new EventHub()); await scanner.ScanAsync(home.Store.TryClaimJob()!, CancellationToken.None); home.Store.LinkAcquisition(download.Id);
            var catalog = home.Store.GetContentFiles(content.Id);
            if (show) { Assert.Equal(2, Assert.Single(catalog).Episodes!.Length); Assert.All(home.Store.GetEpisodes(content.Id), ep => Assert.Equal("available", ep.State)); }
            else { var edition = Assert.Single(home.Store.GetMovieEditions(content.Id)); Assert.True(edition.Complete); Assert.Equal(2, edition.Files.Count); Assert.Equal(new[] { 1, 2 }, edition.Files.Select(file => file.MoviePart!.Number)); }
            await importer.ImportAsync(home.Store.GetDownload(download.Id)!, session, CancellationToken.None); await scanner.ScanAsync(home.Store.TryClaimJob()!, CancellationToken.None); home.Store.LinkAcquisition(download.Id);
            Assert.Equal(catalog.Select(file => file.Id).Order(), home.Store.GetContentFiles(content.Id).Select(file => file.Id).Order());
            Assert.Equal(hashes, names.ToDictionary(name => name, name => StateFolders.Hash(Path.Combine(home.Source, name))));
        }
    }
    private sealed class PayloadSession(string path) : ITorrentSession
    {
        public string Directory => path;
        public IReadOnlyList<TorrentFileSnapshot> Files => System.IO.Directory.GetFiles(path).Select(file => new TorrentFileSnapshot(Path.GetFileName(file), file, new FileInfo(file).Length)).ToArray();
        public TorrentSnapshot Snapshot => new("Fixture", "completed", 100, Files.Sum(file => file.Length), 0, 0, 0, true);
        public Task StartAsync() => Task.CompletedTask; public Task StopAsync() => Task.CompletedTask; public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
