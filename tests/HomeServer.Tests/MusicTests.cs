using System.Net;
using System.Text;
using System.Text.Json;
using HomeServer.Core;
using HomeServer.Infrastructure;
using HomeServer.Music;
using HomeServer.Scanning;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace HomeServer.Tests;

public sealed class MusicTests
{
    [Fact]
    public void LrcTimingSupportsMultipleStampsAndOffsets()
    {
        var lines = LyricsService.Parse("[ar:Artist]\n[00:01.50][00:03.50]Türkçe satır\n[offset:500]\n");
        Assert.Equal(new double?[] { 2, 4 }, lines.Select(line => line.Seconds));
        Assert.All(lines, line => Assert.Equal("Türkçe satır", line.Text));
    }
    [MediaFact]
    public async Task NativeTagsKeepDiscNumbersMetadataAndReadOnlySources()
    {
        using var home = new TestWorkspace(); home.Store.CreateOwner(new Owner("Owner", "fixture-hash"));
        var ffmpeg = Environment.GetEnvironmentVariable("HOME_TEST_FFMPEG")!;
        for (var disc = 1; disc <= 2; disc++)
        {
            var directory = Path.Combine(home.Source, "Album", "CD" + disc); Directory.CreateDirectory(directory);
            await MediaProcess.RunAsync(ffmpeg, ["-hide_banner", "-loglevel", "error", "-nostdin", "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000", "-t", "0.5", "-c:a", "flac",
                "-metadata", "title=Parça " + disc, "-metadata", "artist=Fixture Ensemble", "-metadata", "album_artist=Fixture Ensemble", "-metadata", "album=Two Discs", "-metadata", "track=1/10", "-metadata", "disc=" + disc + "/2",
                "-metadata", "replaygain_track_gain=-4.5 dB", "-metadata", "lyrics=Türkçe sözler", Path.Combine(directory, "audio.flac")], TimeSpan.FromSeconds(10), CancellationToken.None);
        }
        var files = Directory.GetFiles(home.Source, "*.flac", SearchOption.AllDirectories); var hashes = files.Select(StateFolders.Hash).ToArray();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Home:FfmpegPath"] = ffmpeg, ["Home:FfprobePath"] = Environment.GetEnvironmentVariable("HOME_TEST_FFPROBE") }).Build();
        var scanner = new LibraryScanner(home.Store, new MediaTools(config, home.Paths, NullLogger<MediaTools>.Instance), new EventHub());
        var library = home.Store.AddLibrary(new AddLibrary("Music", home.Source, "music")); home.Store.QueueScan(library.Id);
        await scanner.ScanAsync(home.Store.TryClaimJob()!, CancellationToken.None);
        var owner = home.Store.GetAccount("1")!; var album = Assert.Single(home.Store.GetMusicAlbums(owner, 0).Items);
        Assert.Equal("Two Discs", album.Title); Assert.Equal(2, album.Tracks);
        var tracks = home.Store.GetMusicTracks(album.Id, owner).Items;
        Assert.Equal(new[] { 1, 2 }, tracks.Select(track => track.Disc));
        Assert.All(tracks, track => { Assert.Equal(1, track.Number); Assert.Equal(-4.5, track.ReplayGainDb); Assert.Contains("Türkçe", track.Lyrics); });
        Assert.Equal(2, home.Store.GetCatalog("ensemble", "audio", null, 0, 60, owner).Total);
        home.Store.QueueScan(library.Id); await scanner.ScanAsync(home.Store.TryClaimJob()!, CancellationToken.None);
        Assert.Equal(album.Id, Assert.Single(home.Store.GetMusicAlbums(owner, 0).Items).Id);
        Assert.Equal(hashes, files.Select(StateFolders.Hash).ToArray());
    }

    [Fact]
    public async Task PlaylistsAndQueueArePrivateAndRespectRevokedLibraryAccess()
    {
        using var home = new TestWorkspace(); home.Store.CreateOwner(new Owner("Owner", "fixture-hash"));
        home.Write("One.flac"); home.Write("Two.mp3"); var library = home.AddLibrary(); await home.Scan(library.Id);
        var files = home.Store.GetCatalog(null, null, null, 0, 60).Items;
        var one = home.Store.SaveAccount(null, new AccountInput("One", AllLibraries: false, LibraryIds: [library.Id]), "fixture-hash");
        var two = home.Store.SaveAccount(null, new AccountInput("Two"), "fixture-hash");
        var playlist = home.Store.SavePlaylist(null, new PlaylistInput("Mine", files.Select(file => file.Id).ToArray()), one);
        Assert.Empty(home.Store.GetPlaylists(two)); Assert.Throws<DomainException>(() => home.Store.GetPlaylistItems(playlist.Id, two));
        var client = Guid.NewGuid().ToString("N");
        home.Store.SaveQueue(new QueueInput(files.Select(file => file.Id).ToArray(), 1, true, "all", client, 2), one);
        home.Store.SaveQueue(new QueueInput(files.Select(file => file.Id).ToArray(), 0, false, "off", client, 1), one);
        Assert.Equal(1, home.Store.GetQueue(one).Index); Assert.Empty(home.Store.GetQueue(two).Items);
        var restricted = home.Store.SaveAccount(one.Id, new AccountInput("One", AllLibraries: false));
        Assert.Empty(home.Store.GetQueue(restricted).Items); Assert.Empty(home.Store.GetPlaylistItems(playlist.Id, restricted));
        Assert.Throws<DomainException>(() => home.Store.SavePlaylist(null, new PlaylistInput("Hidden", [files[0].Id]), restricted));
    }

    [Fact]
    public async Task MusicBrainzCoreMetadataAndPaginationNormalizeWithoutSupplementaryData()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 });
        var handler = new Handler(); var provider = new MusicBrainzProvider(new Factory(handler), cache);
        var artists = await provider.SearchArtistsAsync("Artist", 0, CancellationToken.None);
        var artist = Assert.Single(artists.Items); Assert.Equal("Fixture Artist", artist.Name);
        var albums = await provider.AlbumsAsync(artist.Id, 50, CancellationToken.None);
        Assert.Equal(50, albums.Offset); Assert.Equal(51, albums.Total);
        Assert.Equal("Fixture Artist feat. Guest", Assert.Single(albums.Items).Artist);
        await provider.AlbumsAsync(artist.Id, 0, CancellationToken.None, allStatuses: true);
        Assert.Contains(handler.Urls, url => url.Contains("release-group-status=all", StringComparison.Ordinal));
        Assert.All(handler.Urls, url => { Assert.Contains("fmt=json", url); Assert.DoesNotContain("genres", url); Assert.DoesNotContain("ratings", url); });
    }
    private sealed class Factory(Handler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, false) { BaseAddress = new Uri("https://musicbrainz.org/ws/2/") };
    }
    private sealed class Handler : HttpMessageHandler
    {
        public List<string> Urls { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Urls.Add(request.RequestUri!.ToString());
            var json = request.RequestUri.AbsolutePath.Contains("release-group")
                ? """{"release-group-count":51,"release-groups":[{"id":"caafa493-8b5c-40a0-803e-95a690d5a30e","title":"Fixture Album","first-release-date":"2020","primary-type":"Album","artist-credit":[{"name":"Fixture Artist","joinphrase":" feat. "},{"name":"Guest"}]}]}"""
                : """{"count":1,"artists":[{"id":"763cd17e-9d42-4321-8e88-3957a1300a9a","name":"Fixture Artist","sort-name":"Artist, Fixture","country":"TR"}]}""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }
    }
}
