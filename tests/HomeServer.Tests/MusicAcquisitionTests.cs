using System.Net;
using System.Text;
using HomeServer.Core;
using HomeServer.Downloads;
using HomeServer.Infrastructure;
using HomeServer.Music;
using HomeServer.Sources;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace HomeServer.Tests;

public sealed class MusicAcquisitionTests
{
    private const string ReleaseId = "7a65d5cb-bcbf-4b0d-b6e0-9a7a49a156cd";
    private const string GroupId = "d9c25204-9b3b-4c2c-802f-ed47bfbb2c9a";
    private static MusicRelease Release => new(new(ReleaseId, GroupId, "Fixture Album", "Fixture Artist", "2020", "TR", "Official", ["Digital Media"], 2),
        [new("1bda5c1a-2c21-43f3-a3f1-c84b7b611146", "ec670060-53aa-4283-94d5-af423fc5a8a9", 1, 1, "1", "One", "Fixture Artist", 250),
         new("9a1db52a-c43d-4dc1-8fdf-54659f5d3f18", "c7e6f8d4-c3cf-49f7-8464-59c82f26c024", 1, 2, "2", "Two", "Fixture Artist", 250)]);

    [Fact]
    public void MusicSourceIdentityAndQualityRejectDifferentAlbumsAndLossyPayloads()
    {
        var rules = new MusicQualityRules(LosslessOnly: true);
        var good = new RawRelease("", "Fixture.Artist-Fixture.Album-2020-FLAC", "torrent", 1024, 10, null, "", null, []);
        Assert.True(MusicReleaseEvaluator.Evaluate(good, Release, rules).Eligible);
        Assert.False(MusicReleaseEvaluator.Evaluate(good with { Title = "Other.Artist-Fixture.Album-FLAC" }, Release, rules).Eligible);
        Assert.False(MusicReleaseEvaluator.Evaluate(good with { Title = "Fixture.Artist-Fixture.Album-MP3-320kbps" }, Release, rules).Eligible);
        Assert.True(MusicReleaseEvaluator.Evaluate(good with { Title = "Fixture.Artist-Fixture.Album-MP3-320kbps" }, Release, rules with { LosslessOnly = false, MinimumBitrateKbps = 256 }).Eligible);
    }
    [MediaFact]
    public async Task NativeTaggedAlbumValidatesPublishesAndLinksRecordingIdsWithoutChangingSource()
    {
        using var home = new TestWorkspace(); home.Store.CreateOwner(new Owner("Owner", "fixture-hash"));
        var config = Configuration(); var tools = new MediaTools(config, home.Paths, NullLogger<MediaTools>.Instance);
        foreach (var track in Release.Tracks)
            await MediaProcess.RunAsync(tools.FfmpegExecutable, ["-hide_banner", "-loglevel", "error", "-nostdin", "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=44100", "-t", "0.25", "-c:a", "flac",
                "-metadata", "title=" + track.Title, "-metadata", "artist=Fixture Artist", "-metadata", "album_artist=Fixture Artist", "-metadata", "album=Fixture Album", "-metadata", "track=" + track.Position,
                "-metadata", "disc=1", "-metadata", "MUSICBRAINZ_ALBUMID=" + ReleaseId, "-metadata", "MUSICBRAINZ_RELEASEGROUPID=" + GroupId,
                "-metadata", "MUSICBRAINZ_TRACKID=" + track.RecordingId, Path.Combine(home.Source, track.Title + ".flac")], TimeSpan.FromSeconds(10), CancellationToken.None);
        var sourceHashes = Directory.GetFiles(home.Source).ToDictionary(file => Path.GetFileName(file)!, StateFolders.Hash);
        var indexer = home.Store.SaveIndexer(null, new IndexerInput("Fixture", "https://fixture.invalid/api", "torznab", null), "protected");
        var raw = new RawRelease("fixture", "Fixture.Artist-Fixture.Album-FLAC", "torrent", 1024, 10, null, "", null, []);
        var rules = new MusicQualityRules(LosslessOnly: true);
        var result = home.Store.SaveMusicSource(indexer, raw, Release, rules, MusicReleaseEvaluator.Evaluate(raw, Release, rules), "protected-url");
        var download = home.Store.AddDownload(["music-fixture-hash"], raw.Title, "torrent", "protected", 1024, music: new MusicDownloadContext(result.Id, Release, rules));
        await using var session = new AlbumSession(home.Source);
        var work = home.Store.GetMusicAcquisition(download.Id)!;
        await Assert.ThrowsAsync<DomainException>(() => MusicAcquisitionValidator.ValidateAsync(download, session, work with { Release = Release with { Metadata = Release.Metadata with { GroupId = Guid.NewGuid().ToString() } } }, home.Store, tools, CancellationToken.None));
        var files = new DownloadFiles(home.Paths); var importer = new DownloadImporter(files, home.Store, new EventHub(), tools);
        await importer.ImportAsync(download, session, CancellationToken.None);
        var imported = home.Store.GetDownload(download.Id)!;
        Assert.Equal("music", home.Store.GetLibrary(imported.ImportedLibraryId!)!.Kind);
        var scanner = new HomeServer.Scanning.LibraryScanner(home.Store, tools, new EventHub()); await scanner.ScanAsync(home.Store.TryClaimJob()!, CancellationToken.None);
        home.Store.LinkMusicAcquisition(download.Id);
        Assert.Equal("linked", home.Store.GetMusicAcquisition(download.Id)!.Status);
        var owner = home.Store.GetAccount("1")!; var album = Assert.Single(home.Store.GetMusicAlbums(owner, 0).Items);
        Assert.Equal(ReleaseId, album.ReleaseMbid); Assert.Equal(2, home.Store.GetMusicTracks(album.Id, owner).Total);
        Assert.Equal(sourceHashes, Directory.GetFiles(home.Source).ToDictionary(file => Path.GetFileName(file)!, StateFolders.Hash));
        using var memory = new MemoryCache(new MemoryCacheOptions { SizeLimit = 10 });
        var tagger = new MusicTagService(home.Store, new HomeServer.Playback.MediaAnalysisService(home.Store, tools), tools, files, new CoverArtCache(home.Paths, new Clients(new SourceHandler()), memory));
        var first = home.Store.GetMusicTracks(album.Id, owner).Items[0];
        var preview = await tagger.PreviewAsync(first.MediaId, true, CancellationToken.None);
        await tagger.QueueAsync(first.MediaId, new MusicTagInput(preview.Fingerprint, ExpectedMetadataHash: preview.MetadataHash), CancellationToken.None);
        await tagger.ExecuteAsync(home.Store.ClaimMusicTask()!, CancellationToken.None);
        await scanner.ScanAsync(home.Store.TryClaimJob()!, CancellationToken.None);
        await importer.ImportAsync(home.Store.GetDownload(download.Id)!, session, CancellationToken.None);
        await scanner.ScanAsync(home.Store.TryClaimJob()!, CancellationToken.None); home.Store.LinkMusicAcquisition(download.Id);
        Assert.Equal("linked", home.Store.GetMusicAcquisition(download.Id)!.Status);
        Assert.Equal(2, home.Store.GetMusicTracks(album.Id, owner).Total);
        Assert.Equal(sourceHashes, Directory.GetFiles(home.Source).ToDictionary(file => Path.GetFileName(file)!, StateFolders.Hash));
    }
    [Fact]
    public async Task OrderedArtistIdsVerifyJoinedCreditsWithoutAcceptingDifferentIdentities()
    {
        using var home = new TestWorkspace(); home.Store.CreateOwner(new("Owner", "isolated"));
        MusicArtistCredit[] artists = [new(Guid.NewGuid().ToString(), "Artist A", "Artist A", " feat. "), new(Guid.NewGuid().ToString(), "Artist B", "Artist B")];
        var release = Release with { Metadata = Release.Metadata with { Artist = "Artist A feat. Artist B", ArtistCredits = artists } };
        var rules = new MusicQualityRules(LosslessOnly: true, CompleteAlbum: false); var indexer = home.Store.SaveIndexer(null, new("Fixture", "https://fixture.invalid/api", "torznab", null), "protected");
        var raw = new RawRelease("fixture", "Artist.A-Artist.B-Fixture.Album-FLAC", "torrent", 4, 10, null, "", null, []);
        var result = home.Store.SaveMusicSource(indexer, raw, release, rules, MusicReleaseEvaluator.Evaluate(raw, release, rules), "protected-url");
        var download = home.Store.AddDownload(["credit-import-fixture"], raw.Title, "torrent", "protected", 4, music: new(result.Id, release, rules));
        home.Write("One.flac"); await using var session = new AlbumSession(home.Source); var work = home.Store.GetMusicAcquisition(download.Id)!;
        var tags = new AudioTags("One", "Artist A; Artist B", "Artist A; Artist B", "Fixture Album", "2020", 1, 1, null, null, null, null, null, null, 16, ArtistCredits: artists, AlbumArtistCredits: artists);
        await MusicAcquisitionValidator.ValidateAsync(download, session, work, home.Store, new TagProbe(tags), CancellationToken.None);
        var wrong = tags with { AlbumArtistCredits = [artists[0] with { Mbid = Guid.NewGuid().ToString() }, artists[1]] };
        Assert.Throws<DomainException>(() => MusicCredits.Normalize([artists[0] with { Name = new string('x', 501) }], ""));
        await Assert.ThrowsAsync<DomainException>(() => MusicAcquisitionValidator.ValidateAsync(download, session, work, home.Store, new TagProbe(wrong), CancellationToken.None));
    }
    private sealed class TagProbe(AudioTags tags) : IMediaProbe
    {
        public bool FfprobeAvailable => true;
        public Task<ProbeResult?> ProbeAsync(string path, CancellationToken token) => Task.FromResult<ProbeResult?>(new(.25, null, null, null, "flac", Music: tags));
    }
    [Fact]
    public async Task MusicCapabilityQueryIsUsedWithoutCreatingADownload()
    {
        using var home = new TestWorkspace(); var handler = new SourceHandler(); var clients = new Clients(handler);
        using var memory = new MemoryCache(new MemoryCacheOptions { SizeLimit = 20 }); var metadata = new MusicBrainzProvider(clients, memory);
        var protection = new EphemeralDataProtectionProvider(); using var engine = new TorrentEngine(new DownloadFiles(home.Paths), home.Store, Configuration(), protection);
        var service = new IndexerService(home.Store, clients, protection, engine, new EventHub()); service.Save(null, new IndexerInput("Music source", "https://fixture.invalid/api", "torznab", "fixture-key"));
        var result = await service.SearchMusicAsync(new MusicSearchInput(ReleaseId, new MusicQualityRules()), metadata, CancellationToken.None);
        Assert.True(Assert.Single(result.Items).Evaluation.Eligible);
        Assert.Contains(handler.Urls, url => url.Contains("t=music") && url.Contains("artist=") && url.Contains("album="));
        Assert.Empty(home.Store.GetDownloads());
    }
    private static IConfiguration Configuration() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Home:Downloads:LoopbackOnly"] = "true",
        ["Home:FfmpegPath"] = Environment.GetEnvironmentVariable("HOME_TEST_FFMPEG"), ["Home:FfprobePath"] = Environment.GetEnvironmentVariable("HOME_TEST_FFPROBE") }).Build();
    private sealed class AlbumSession(string path) : ITorrentSession
    {
        public string Directory => path;
        public IReadOnlyList<TorrentFileSnapshot> Files => System.IO.Directory.GetFiles(path).Select(file => new TorrentFileSnapshot(Path.GetFileName(file), file, new FileInfo(file).Length)).ToArray();
        public TorrentSnapshot Snapshot => new("Fixture Album", "completed", 100, Files.Sum(file => file.Length), 0, 0, 0, true);
        public Task StartAsync() => Task.CompletedTask; public Task StopAsync() => Task.CompletedTask; public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class Clients(SourceHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, false) { BaseAddress = new Uri("https://musicbrainz.org/ws/2/") };
    }
    private sealed class SourceHandler : HttpMessageHandler
    {
        public List<string> Urls { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Urls.Add(request.RequestUri!.ToString()); string body; string mime;
            if (request.RequestUri.Host == "musicbrainz.org")
            {
                mime = "application/json"; body = $$$"""{"id":"{{{ReleaseId}}}","title":"Fixture Album","release-group":{"id":"{{{GroupId}}}"},"artist-credit":[{"name":"Fixture Artist"}],"media":[{"position":1,"track-count":1,"tracks":[{"id":"1bda5c1a-2c21-43f3-a3f1-c84b7b611146","position":1,"number":"1","title":"One","recording":{"id":"ec670060-53aa-4283-94d5-af423fc5a8a9"}}]}]}""";
            }
            else if (request.RequestUri.Query.Contains("t=caps")) { mime = "application/xml"; body = "<caps><limits max='100'/><searching><music-search available='yes' supportedParams='artist,album'/></searching></caps>"; }
            else { mime = "application/xml"; body = "<rss version='2.0' xmlns:torznab='http://torznab.com/schemas/2015/feed'><channel><item><title>Fixture.Artist-Fixture.Album-FLAC</title><guid>fixture</guid><enclosure url='https://fixture.invalid/download' length='1024' type='application/x-bittorrent'/><torznab:attr name='seeders' value='10'/></item></channel></rss>"; }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, mime) });
        }
    }
}
