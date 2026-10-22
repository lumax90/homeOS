using System.Net;
using HomeServer.Core;
using HomeServer.Downloads;
using HomeServer.Infrastructure;
using HomeServer.Sources;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MonoTorrent;

namespace HomeServer.Tests;

public sealed class TorrentResolutionTests
{
    [Fact]
    public async Task PublicSourceMetadataIsHashCheckedAndNeverForwardsCredentialsOrStartsPayload()
    {
        using var home = new TestWorkspace();
        var bytes = (await new TorrentCreator().CreateAsync(new TorrentFileSource(home.Write("Isolated.2020.1080p.mp4")))).Encode();
        var torrent = TorrentEngine.Parse(bytes);
        var handler = new Handler(bytes);
        var protection = new EphemeralDataProtectionProvider();
        using var transport = new SourceTransportService(home.Store, new Clients(handler), protection);
        var source = AddSourceDownload(home, transport, torrent, "yts");
        var resolver = new TorrentMetadataResolver(home.Store, transport);
        var magnet = new MagnetLink(torrent.InfoHashes, "Isolated");
        var enriched = resolver.WithTrackers(source, magnet);
        Assert.Equal(torrent.InfoHashes, enriched.InfoHashes);
        Assert.Equal(TorrentMetadataResolver.PublicTrackers, enriched.AnnounceUrls);
        var supplied = await resolver.TryHttpAsync(source, enriched, CancellationToken.None);
        Assert.Equal(bytes, supplied);
        Assert.Single(handler.Requests);
        Assert.Equal("yts.gg", handler.Requests[0].Host);
        Assert.Equal("/torrent/download/" + torrent.InfoHashes.V1!.ToHex(), handler.Requests[0].AbsolutePath);
        Assert.Equal("magnet", home.Store.GetDownload(source.Record.Id)!.SourceKind);
        Assert.False(Directory.Exists(Path.Combine(home.Paths.Data, "downloads", "staging", source.Record.Id)));
        var other = new MagnetLink(InfoHash.FromHex(new string('a', 40)));
        var rejected = await Assert.ThrowsAsync<DomainException>(() => resolver.TryHttpAsync(source, other, CancellationToken.None));
        Assert.Contains("kimliği", rejected.Message);
        handler.Status = HttpStatusCode.Redirect;
        Assert.Null(await resolver.TryHttpAsync(source, magnet, CancellationToken.None));
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task PrivateOrManualMagnetsKeepTheirTrackerScopeAndCancellationDoesNotPublishMetadata()
    {
        using var home = new TestWorkspace();
        var bytes = (await new TorrentCreator().CreateAsync(new TorrentFileSource(home.Write("Isolated.2020.1080p.mp4")))).Encode();
        var torrent = TorrentEngine.Parse(bytes);
        var handler = new Handler(bytes);
        using var transport = new SourceTransportService(home.Store, new Clients(handler), new EphemeralDataProtectionProvider());
        var source = AddSourceDownload(home, transport, torrent, "custom-torznab");
        var resolver = new TorrentMetadataResolver(home.Store, transport);
        var bare = new MagnetLink(torrent.InfoHashes);
        Assert.Same(bare, resolver.WithTrackers(source, bare));
        Assert.Null(await resolver.TryHttpAsync(source, bare, CancellationToken.None));
        Assert.Empty(handler.Requests);
        var privateMagnet = new MagnetLink(torrent.InfoHashes, "Private", ["https://private.invalid/announce"]);
        Assert.Same(privateMagnet, resolver.WithTrackers(source, privateMagnet));
        home.Store.SetDownloadIntent(source.Record.Id, "paused");
        Assert.Throws<OperationCanceledException>(() => home.Store.SaveResolvedTorrent(source.Record.Id, "late", source.Record.Revision));
        var resumed = home.Store.SetDownloadIntent(source.Record.Id, "running");
        Assert.Throws<OperationCanceledException>(() => home.Store.SaveResolvedTorrent(source.Record.Id, "stale", source.Record.Revision));
        Assert.Equal("magnet", home.Store.GetDownload(source.Record.Id)!.SourceKind);
        home.Store.SaveResolvedTorrent(source.Record.Id, "current", resumed.Revision);
        Assert.Equal("current", home.Store.GetDownloadSource(source.Record.Id).Source);
    }

    [Fact]
    public async Task MetadataTimeoutBecomesAnActionableFailureInsteadOfSilentRetries()
    {
        using var home = new TestWorkspace();
        var record = home.Store.AddDownload([new string('b', 40)], "Isolated timeout", "magnet", "protected", 0);
        var events = new EventHub(); var sessions = new TimeoutSessions();
        using var worker = new DownloadWorker(home.Store, sessions, new DownloadImporter(new DownloadFiles(home.Paths), home.Store, events),
            events, new ConfigurationBuilder().Build(), NullLogger<DownloadWorker>.Instance);
        await worker.StartAsync(CancellationToken.None);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            while (home.Store.GetDownload(record.Id)!.Status != "failed") await Task.Delay(30, deadline.Token);
            await Task.Delay(700, deadline.Token);
            Assert.Equal(1, sessions.Calls);
            Assert.Contains("zaman aşımına", home.Store.GetDownload(record.Id)!.Message);
            home.Store.RecoverDownloads();
            Assert.Equal("failed", home.Store.GetDownload(record.Id)!.Status);
            Assert.Equal("queued", home.Store.SetDownloadIntent(record.Id, "running").Status);
        }
        finally { await worker.StopAsync(CancellationToken.None); }
    }

    private static DownloadSource AddSourceDownload(TestWorkspace home, SourceTransportService transport, Torrent torrent, string provider)
    {
        var indexer = home.Store.SaveIndexer(null, new("Isolated source", "https://movies-api.accel.li/", "torznab", null), "protected-source-key");
        using (var db = new SqliteConnection($"Data Source={Path.Combine(home.Paths.Data, "home.db")}"))
        {
            db.Open(); using var update = db.CreateCommand();
            update.CommandText = "UPDATE indexers SET provider_id=$provider WHERE id=$id";
            update.Parameters.AddWithValue("$provider", provider); update.Parameters.AddWithValue("$id", indexer.Id); update.ExecuteNonQuery();
        }
        indexer = home.Store.GetIndexer(indexer.Id).Info;
        home.Store.SaveSourceTransport(indexer.Id, null, transport.ProtectSettings(new(Cookie: "private-cookie", Headers: new() { ["Authorization"] = "private-header" })), 0);
        indexer = home.Store.GetIndexer(indexer.Id).Info;
        var movie = new ProviderContent("tmdb", 9001, "movie", "Isolated", "Isolated", "", "2020-01-01", null, null, "https://fixture.invalid/movie", []);
        var target = home.Store.RequestContent(movie, "all", "balanced");
        var raw = new RawRelease("isolated", "Isolated.2020.1080p", "torrent", torrent.Size, 1, null,
            new MagnetLink(torrent.InfoHashes).ToV1String(), torrent.InfoHashes.V1!.ToHex(), []);
        var profile = home.Store.GetQualityProfile("balanced");
        var release = home.Store.SaveRelease(indexer, target, new(target.Id), profile, raw, ReleaseEvaluator.Evaluate(raw, movie, profile.Rules), "protected-release");
        var record = home.Store.AddDownload(TorrentEngine.Hashes(torrent.InfoHashes), "Isolated", "magnet", "protected-magnet", 0, new(target.Id, null, null, release.Id));
        return home.Store.GetDownloadSource(record.Id);
    }
    private sealed class TimeoutSessions : ITorrentSessions
    {
        public int Calls;
        public Task<ITorrentSession> OpenAsync(DownloadSource source, CancellationToken token)
        { Interlocked.Increment(ref Calls); return Task.FromException<ITorrentSession>(new OperationCanceledException("Transport deadline")); }
    }
    private sealed class Clients(Handler handler) : IHttpClientFactory
    { public HttpClient CreateClient(string name) => new(handler, false); }
    private sealed class Handler(byte[] bytes) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); Requests.Add(request.RequestUri!);
            Assert.False(request.Headers.Contains("Cookie")); Assert.Null(request.Headers.Authorization);
            return Task.FromResult(new HttpResponseMessage(Status) { Content = new ByteArrayContent(bytes), Headers = { Location = new Uri("https://unapproved.invalid/torrent") } });
        }
    }
}
