using System.Net;
using System.Net.Sockets;
using HomeServer.Core;
using HomeServer.Downloads;
using HomeServer.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MonoTorrent;
using MonoTorrent.BEncoding;
using MonoTorrent.Client;

namespace HomeServer.Tests;

public sealed class DownloadTests
{
    [Theory]
    [InlineData("../escape.mp4")]
    [InlineData("folder/../../escape.mp4")]
    [InlineData("C:\\outside.mp4")]
    [InlineData("/outside.mp4")]
    [InlineData("movie.mp4:stream")]
    [InlineData("folder/NUL.mp4")]
    [InlineData("folder/film. ")]
    public void TorrentPathsArePortableAndBounded(string path) => Assert.Throws<DomainException>(() => DownloadFiles.ValidateRelative(path));

    [Fact]
    public void CommandsFenceStaleEngineUpdatesAndHashesDeduplicate()
    {
        using var home = new TestWorkspace();
        var first = home.Store.AddDownload(["v1", "v2"], "Film", "torrent", "protected", 100);
        var duplicate = home.Store.AddDownload(["v2"], "Other", "magnet", "protected", 0);
        Assert.Equal(first.Id, duplicate.Id);
        var paused = home.Store.SetDownloadIntent(first.Id, "paused");
        home.Store.UpdateDownload(first.Id, first.Revision, "downloading", "old", 50, 100);
        Assert.Equal("paused", home.Store.GetDownload(first.Id)!.Status);
        var resumed = home.Store.SetDownloadIntent(first.Id, "running");
        home.Store.UpdateDownload(first.Id, first.Revision, "failed", "stale failure", 50, 100);
        Assert.Equal("queued", home.Store.GetDownload(first.Id)!.Status);
        home.Store.UpdateDownload(first.Id, resumed.Revision, "downloading", "current", 75, 100, 10, 0, 1);
        home.Store.RecoverDownloads();
        Assert.Equal("queued", home.Store.GetDownload(first.Id)!.Status);
        Assert.Equal(0, home.Store.GetDownload(first.Id)!.DownloadSpeed);
        Assert.True(paused.Revision > first.Revision);
    }

    [Fact]
    public void MalformedAndExcessivelyNestedMetadataIsRejected()
    {
        Assert.Throws<DomainException>(() => TorrentEngine.Parse([1, 2, 3]));
        Assert.Throws<DomainException>(() => TorrentEngine.Parse(System.Text.Encoding.ASCII.GetBytes("d" + new string('l', 100) + new string('e', 101))));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LoopbackTorrentTransfersVerifiedBytesAndPublishesIdempotently(bool useMagnet)
    {
        using var home = new TestWorkspace();
        home.Store.CreateOwner(new("Owner", "fixture")); var login = home.Store.CreateLoginSession("1", 1, "Loopback fixture"); var actor = home.Store.DownloadActor("1", login.Id);
        home.Store.SaveDownloadBudget("1", new(MonthlyPayloadBytes: 1));
        var original = Wave(160_000);
        var sourcePath = home.Write("Local transfer.wav", original);
        var creator = new TorrentCreator { PieceLength = 16 * 1024 };
        var metadata = await creator.CreateAsync(new TorrentFileSource(sourcePath));
        var bytes = metadata.Encode();
        var torrent = Torrent.Load(bytes);
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        using var seed = new ClientEngine(new EngineSettingsBuilder
        {
            CacheDirectory = Path.Combine(home.Root, "seed-cache"), DhtEndPoint = null,
            ListenEndPoints = new Dictionary<string, IPEndPoint> { ["ipv4"] = new(IPAddress.Loopback, port) },
            AllowLocalPeerDiscovery = false, AllowPortForwarding = false
        }.ToSettings());
        var seedManager = await seed.AddAsync(torrent, home.Source, new TorrentSettingsBuilder { CreateContainingDirectory = false, AllowDht = false, AllowPeerExchange = false }.ToSettings());
        await seedManager.HashCheckAsync(false);
        await seedManager.StartAsync();
        var trackerBuilder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = home.Root, EnvironmentName = "Testing" });
        trackerBuilder.WebHost.UseUrls("http://127.0.0.1:0");
        trackerBuilder.Logging.ClearProviders();
        await using var tracker = trackerBuilder.Build();
        var peer = new byte[] { 127, 0, 0, 1, (byte)(port >> 8), (byte)(port & 255) };
        tracker.MapGet("/announce", () => Results.Bytes(new BEncodedDictionary
        {
            ["interval"] = new BEncodedNumber(1), ["peers"] = new BEncodedString(peer)
        }.Encode(), "text/plain"));
        await tracker.StartAsync();
        var trackerUrl = tracker.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single() + "/announce";
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Home:Downloads:LoopbackOnly"] = "true", ["Home:Downloads:MaximumDownloadKiB"] = "64"
        }).Build();
        var files = new DownloadFiles(home.Paths);
        var protection = new EphemeralDataProtectionProvider();
        using var engine = new TorrentEngine(files, home.Store, config, protection);
        var magnet = new MagnetLink(torrent.InfoHashes, "Local transfer", [trackerUrl]).ToV1String();
        var record = useMagnet ? engine.AddMagnet(magnet, principal: actor) : engine.AddTorrent(bytes, principal: actor);
        Assert.Equal(record.Id, engine.AddMagnet(new MagnetLink(torrent.InfoHashes).ToV1String(), principal: actor).Id);
        var denied = home.Store.BeginDownloadBudgetRun(record.Id, record.Revision, Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow);
        await Assert.ThrowsAsync<DownloadBudgetException>(() => engine.OpenAsync(home.Store.GetDownloadSource(record.Id) with { Budget = denied }, CancellationToken.None));
        Assert.False(Directory.Exists(Path.Combine(files.Root, "staging", record.Id))); Assert.Equal(0, home.Store.GetDownloadBudgetUsage("1", DateTimeOffset.UtcNow).CommittedBytes);
        home.Store.CloseDownloadBudgetRun(denied, DateTimeOffset.UtcNow); home.Store.SaveDownloadBudget("1", new(MonthlyPayloadBytes: original.Length, Revision: 1)); record = home.Store.GetDownload(record.Id)!;
        var budget = home.Store.BeginDownloadBudgetRun(record.Id, record.Revision, Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow);
        await using var session = await engine.OpenAsync(home.Store.GetDownloadSource(record.Id) with { Budget = budget }, CancellationToken.None);
        Assert.Equal(original.Length, home.Store.GetDownloadBudgetUsage("1", DateTimeOffset.UtcNow).ReservedBytes);
        await session.StartAsync();
        Assert.Equal(original.Length, home.Store.GetDownloadBudgetUsage("1", DateTimeOffset.UtcNow).CommittedBytes);
        if (!useMagnet) await ((TorrentEngine.Session)session).Manager.AddPeerAsync(new PeerInfo(new Uri($"ipv4://127.0.0.1:{port}")));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        while (!session.Snapshot.Complete) await Task.Delay(100, timeout.Token);
        Assert.Equal(original, await File.ReadAllBytesAsync(Path.Combine(session.Directory, "Local transfer.wav"), timeout.Token));
        await session.StopAsync();
        home.Store.RecordTransferCounters(record.Id, record.Revision, budget.Id, session.Snapshot.Uploaded, session.Snapshot.Received, 0, budget);
        Assert.NotEmpty(Directory.GetFiles(files.EngineCache, "*.fresume", SearchOption.AllDirectories));
        var importer = new DownloadImporter(files, home.Store, new EventHub());
        await importer.ImportAsync(record, session, timeout.Token);
        var imported = home.Store.GetDownload(record.Id)!;
        Assert.NotNull(imported.ImportedAt);
        Assert.NotNull(imported.ScanJobId);
        var scan = home.Store.TryClaimJob()!;
        await home.Scanner.ScanAsync(scan, timeout.Token);
        Assert.Single(home.Store.GetCatalog(null, null, imported.ImportedLibraryId, 0, 60).Items);
        var published = Path.Combine(files.LibraryRoot,home.Store.DownloadImportFile(record.Id,"Local transfer.wav"));
        Assert.Equal(original, await File.ReadAllBytesAsync(published, timeout.Token));
        await importer.ImportAsync(record, session, timeout.Token);
        Assert.Single(Directory.GetDirectories(files.LibraryRoot));
        Assert.Equal(original, File.ReadAllBytes(sourcePath));
        Assert.Equal("torrent", home.Store.GetDownloadSource(record.Id).Record.SourceKind);
        Assert.DoesNotContain(trackerUrl, home.Store.GetDownloadSource(record.Id).Source);
        home.Store.CloseDownloadBudgetRun(budget, DateTimeOffset.UtcNow); Assert.Equal(1, home.Store.GetDownloadBudgetUsage("1", DateTimeOffset.UtcNow).Acquisitions);
        await seedManager.StopAsync(TimeSpan.FromSeconds(5));
        await tracker.StopAsync();
    }

    private static byte[] Wave(int samples)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8); writer.Write(36 + samples * 2); writer.Write("WAVEfmt "u8);
        writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(44_100);
        writer.Write(88_200); writer.Write((short)2); writer.Write((short)16); writer.Write("data"u8); writer.Write(samples * 2);
        for (var i = 0; i < samples; i++) writer.Write((short)(Math.Sin(i * .05) * 2000));
        writer.Flush(); return stream.ToArray();
    }
}
