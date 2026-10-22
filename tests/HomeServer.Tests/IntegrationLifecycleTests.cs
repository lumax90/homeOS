using System.Net;
using System.Net.Sockets;
using System.Text;
using HomeServer.Core;
using HomeServer.Infrastructure;
using HomeServer.LiveTv;
using HomeServer.Playback;
using HomeServer.Sources;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HomeServer.Tests;

public sealed class IntegrationLifecycleTests
{
    private static string Id() => Guid.NewGuid().ToString("N");
    private static Account Owner(TestWorkspace home) { home.Store.CreateOwner(new("Isolated owner", "isolated")); return home.Store.GetAccount("1")!; }
    private static (LiveSource Source, LiveChannel Channel) Channel(TestWorkspace home, Account owner)
    {
        var source = home.Store.SaveLiveSource(null, new("Isolated TV", PlaylistUrl: "https://fixture.invalid/list.m3u"), "protected", owner);
        return (source, Assert.Single(home.Store.SyncLiveChannels(source, [new("isolated", "Isolated channel", "", null, null, "https://fixture.invalid/live.ts", false, 1)], value => value)));
    }
    private static (DvrRecording Work, DvrPublication Plan, string Payload, string TargetRoot) Publication(TestWorkspace home, Account owner)
    {
        var channel = Channel(home, owner).Channel; var scheduled = home.Store.QueueTimedDvr(new(Id(), channel.Id, null, 1, "Isolated recording"), owner);
        var work = home.Store.BeginDvrFinalization(home.Store.ClaimDvr(scheduled.Id)!);
        var staging = AppFiles.DirectoryIn(home.Paths.Data, "downloads", "staging", "dvr", work.Id); File.WriteAllText(Path.Combine(staging, ".home-dvr"), "Home.Dvr.v1");
        var payload = Path.Combine(staging, "complete.partial.ts"); File.WriteAllText(payload, "isolated verified payload");
        var root = AppFiles.DirectoryIn(home.Paths.Data, "downloads", "live-library"); var library = home.Store.AddLibrary(new("Isolated recordings", root, "movies"), true);
        var plan = new DvrPublication(work.Id, library.Id, home.Store.GetLibraryConfiguration(library.Id).Revision, Id(), "Isolated.ts", Path.GetFileName(payload), new FileInfo(payload).Length, StateFolders.Hash(payload), new(60, 320, 180, "h264", "aac"), false);
        return (work, plan, payload, root);
    }
    [Fact]
    public async Task DvrRestartAfterFilePromotionPublishesOneCatalogIdentityAndUnblocksScanning()
    {
        using var home = new TestWorkspace(); var owner = Owner(home); var fixture = Publication(home, owner);
        Assert.True(home.Store.BeginDvrPublication(fixture.Work, fixture.Plan)); var scan = home.Store.QueueScan(fixture.Plan.LibraryId); Assert.Null(home.Store.TryClaimJob());
        await VerifiedFilePublication.StageAsync(fixture.Payload, fixture.TargetRoot, fixture.Plan.FileName, fixture.Work.Id, fixture.Plan.Length, fixture.Plan.Sha256, CancellationToken.None);
        var target = VerifiedFilePublication.Promote(fixture.TargetRoot, fixture.Plan.FileName, fixture.Work.Id, fixture.Plan.Length, fixture.Plan.Sha256);
        Assert.Null(home.Store.GetDvr(fixture.Work.Id)!.MediaId); // The process stopped between the rename and database commit.
        var reopened = new MediaStore(home.Paths); reopened.Initialize(); reopened.RecoverDvr(); var recovered = reopened.ClaimDvr(fixture.Work.Id)!;
        var result = await new DvrPublisher(reopened, home.Paths, new EventHub()).PublishAsync(recovered, CancellationToken.None);
        Assert.Equal("completed", result.Status); Assert.Equal(fixture.Plan.MediaId, result.MediaId); Assert.Equal(fixture.Plan.Sha256, StateFolders.Hash(target));
        Assert.Equal(fixture.Plan.MediaId, Assert.Single(reopened.GetCatalog(null, null, null, 0, 10).Items).Id);
        Assert.Equal(result, reopened.CommitDvrPublication(recovered, () => throw new InvalidOperationException("A committed publication must not promote again.")));
        Assert.Equal(scan.Id, reopened.TryClaimJob()!.Id); Assert.False(Directory.Exists(Path.GetDirectoryName(fixture.Payload))); ForeignKeys(home);
    }
    [Fact]
    public async Task DvrConflictKeepsBothPayloadsAndCancellationCannotCrossThePublicationPoint()
    {
        using var home = new TestWorkspace(); var owner = Owner(home); var fixture = Publication(home, owner);
        var conflicting = Path.Combine(fixture.TargetRoot, fixture.Plan.FileName); File.WriteAllText(conflicting, "keep unrelated bytes"); var conflictHash = StateFolders.Hash(conflicting);
        Assert.True(home.Store.BeginDvrPublication(fixture.Work, fixture.Plan)); var publishing = home.Store.GetDvr(fixture.Work.Id)!;
        Assert.Throws<DomainException>(() => home.Store.CancelDvr(publishing.Id, publishing.Revision, owner));
        await Assert.ThrowsAsync<DomainException>(() => new DvrPublisher(home.Store, home.Paths, new EventHub()).PublishAsync(publishing, CancellationToken.None));
        Assert.Equal(conflictHash, StateFolders.Hash(conflicting)); Assert.Equal(fixture.Plan.Sha256, StateFolders.Hash(fixture.Payload)); Assert.Empty(home.Store.GetCatalog(null, null, null, 0, 10).Items);
        var other = home.Store.QueueTimedDvr(new(Id(), home.Store.GetDvr(publishing.Id)!.ChannelId, null, 2), owner); var cancelled = home.Store.BeginDvrFinalization(home.Store.ClaimDvr(other.Id)!);
        home.Store.CancelDvr(cancelled.Id, cancelled.Revision, owner);
        Assert.Throws<OperationCanceledException>(() => home.Store.BeginDvrPublication(cancelled, fixture.Plan with { RecordingId = cancelled.Id }));
    }
    [Fact]
    public void TimedDvrCommandsArePayloadBoundAndCaptureAttemptsSurviveRestart()
    {
        using var home = new TestWorkspace(); var owner = Owner(home); var channel = Channel(home, owner).Channel;
        var input = new TimedDvrInput(Id(), channel.Id, null, 60, "Isolated timer"); var first = home.Store.QueueTimedDvr(input, owner);
        Assert.Equal(first.Id, home.Store.QueueTimedDvr(input, owner).Id); Assert.Throws<DomainException>(() => home.Store.QueueTimedDvr(input with { Minutes = 30 }, owner));
        var work = home.Store.ClaimDvr(first.Id)!; for (var attempt = 1; attempt <= 3; attempt++) Assert.Equal(attempt, home.Store.NextDvrCaptureAttempt(work));
        home.Store.RecoverDvr(); Assert.Throws<OperationCanceledException>(() => home.Store.RequireDvr(work)); work = home.Store.ClaimDvr(first.Id)!;
        for (var attempt = 4; attempt <= 6; attempt++) Assert.Equal(attempt, home.Store.NextDvrCaptureAttempt(work)); Assert.Equal(0, home.Store.NextDvrCaptureAttempt(work));
        var rule=home.Store.SaveDvrRule(null,new(channel.Id,"Isolated series"),owner);var source=home.Store.GetLiveSource(channel.SourceId);
        home.Store.SaveLiveSource(source.Id,new(source.Name,Enabled:false,ExpectedRevision:source.Revision),"protected",owner);
        Assert.False(home.Store.SaveDvrRule(rule.Id,new(channel.Id,rule.Title,Enabled:false,ExpectedRevision:rule.Revision),owner).Enabled);
    }
    private static ReceiverCopy PublishCopy(TestWorkspace home, MediaStore store, Account owner, MediaItem media, byte[] data)
    {
        var source = store.PlaybackSource(media.Id, owner); var job = store.QueueProductJob("receiver-prepare", media.Id, new { fingerprint = source.Fingerprint, matchRevision = media.MatchRevision }, owner);
        job = store.ClaimProductJob(["receiver-prepare"])!; var root = AppFiles.DirectoryIn(home.Paths.Data, "receiver-copies", job.Id); var output = Path.Combine(root, "media.partial.mp4"); File.WriteAllBytes(output, data);
        var copy = new ReceiverCopy(media.Id, source.Fingerprint, media.MatchRevision, Path.Combine(root, "media.mp4"), data.Length, MediaAnalysisService.Fingerprint(new FileInfo(output)), StateFolders.Hash(output));
        store.PublishReceiverCopy(job, copy, () => File.Move(output, copy.Path, false)); return copy;
    }
    [Fact]
    public async Task ReceiverCopiesAndCommandsKeepTheirSnapshotAndOnlyObservedPositionIsSaved()
    {
        using var home = new TestWorkspace(); var owner = Owner(home); home.Write("Isolated.mp4"); var library = home.AddLibrary(); await home.Scan(library.Id); var media = Assert.Single(home.Store.GetCatalog(null, null, null, 0, 10).Items);
        var first = PublishCopy(home, home.Store, owner, media, [1, 2, 3, 4]); var login = home.Store.CreateLoginSession(owner.Id, 1, "Isolated sender"); var actor = home.Store.GetPlaybackActor(owner.Id, login.Id);
        var admitted = home.Store.AdmitPlayback(actor, Id(), 1, media.Id, "preparing", "isolated", DateTimeOffset.UtcNow).Lease;
        home.Store.BindPlayback(admitted, new(null, media.Id, "receiver-file", "", 0, 120, [], null, "ready"), DateTimeOffset.UtcNow);
        var token = new string('a', 64); var receiver = home.Store.CreateReceiver(actor, admitted.Id, token, "copy", "Isolated receiver", "cast", null, first);
        var second = PublishCopy(home, home.Store, owner, media, [5, 6, 7, 8]); Assert.Equal(second.Path, home.Store.GetReceiverCopy(media.Id)!.Path); Assert.Equal(first.Path, home.Store.RequireReceiverToken(token).Artifact!.Path); Assert.True(home.Store.ReceiverCopyReferenced(first.Path));
        var observed = home.Store.ObserveReceiver(receiver, actor, new(23, 120, false, .5, "playing", "")); Assert.Equal(23, home.Store.GetMedia(media.Id, owner)!.PositionSeconds);
        var command = new ReceiverControl("seek", 80, CommandId: Id(), ExpectedRevision: observed.Revision); var admission = home.Store.BeginReceiverCommand(receiver.Id, command, actor);
        Assert.Null(admission.Result); Assert.Equal(23, home.Store.GetMedia(media.Id, owner)!.PositionSeconds);
        var applied = home.Store.ObserveReceiver(admission.Grant, actor, new(79, 120, false, .5, "playing", "")); home.Store.CompleteReceiverCommand(receiver.Id, command.CommandId!, applied);
        Assert.Equal(applied, home.Store.BeginReceiverCommand(receiver.Id, command, actor).Result); Assert.Equal(79, home.Store.GetMedia(media.Id, owner)!.PositionSeconds);
        Assert.Throws<DomainException>(() => home.Store.BeginReceiverCommand(receiver.Id, command with { PositionSeconds = 81 }, actor));
        home.Store.EndPlaybackLease(admitted.Id, "isolated-revocation", DateTimeOffset.UtcNow); Assert.True(home.Store.GetReceiver(receiver.Id)!.Closed); Assert.Throws<DomainException>(() => home.Store.RequireReceiverToken(token));
    }
    [Fact]
    public async Task CancelledReceiverPreparationCannotPublishAndDlnaTargetCannotBeClaimedTwice()
    {
        using var home = new TestWorkspace(); var owner = Owner(home); var sourcePath = home.Write("Isolated.mp4"); var sourceHash = StateFolders.Hash(sourcePath); var library = home.AddLibrary(); await home.Scan(library.Id); var media = Assert.Single(home.Store.GetCatalog(null, null, null, 0, 10).Items);
        var job = home.Store.QueueProductJob("receiver-prepare", media.Id, new { isolated = true }, owner); job = home.Store.ClaimProductJob(["receiver-prepare"])!; home.Store.CancelProductJob(job.Id, job.Revision, owner);
        Assert.Throws<OperationCanceledException>(() => home.Store.PublishReceiverCopy(job, new(media.Id, "unused", 0, "unused", 1), () => throw new InvalidOperationException("Cancelled work must not publish.")));
        var login = home.Store.CreateLoginSession(owner.Id, 1, "Isolated sender"); var actor = home.Store.GetPlaybackActor(owner.Id, login.Id);
        var device = home.Store.SaveRenderDevice(new("", "uuid:isolated", "Isolated renderer", "192.168.1.250", "http://192.168.1.250/control", "urn:schemas-upnp-org:service:AVTransport:1", null, null, "video/mp4", DateTimeOffset.UtcNow.ToString("O")));
        PlaybackLease Admit() { var value = home.Store.AdmitPlayback(actor, Id(), 1, media.Id, "preparing", Id(), DateTimeOffset.UtcNow).Lease; return home.Store.BindPlayback(value, new(null, media.Id, "receiver-file", "", 0, 120, [], null, "ready"), DateTimeOffset.UtcNow); }
        var first = Admit(); var second = Admit(); var grant = home.Store.CreateReceiver(actor, first.Id, new string('b',64), "original", device.Name, "dlna", device, null);
        Assert.Throws<DomainException>(() => home.Store.CreateReceiver(actor, second.Id, new string('c',64), "original", device.Name, "dlna", device, null));
        home.Store.SaveRenderDevice(device with { Address = "192.168.1.251", TransportUrl = "http://192.168.1.251/control" }); Assert.Equal(device.TransportUrl, home.Store.GetReceiver(grant.Id)!.Device!.TransportUrl);
        home.Store.CloseReceiver(grant.Id); Assert.NotNull(home.Store.CreateReceiver(actor, second.Id, new string('c',64), "original", device.Name, "dlna", device, null)); Assert.Equal(sourceHash, StateFolders.Hash(sourcePath));
    }
    [Fact]
    public async Task ReceiverFileRangesAreScopedToAnImmutableCopyAndRevocationPrecedesConditionalResponses()
    {
        using var home = new TestWorkspace(); var owner = Owner(home); home.Write("Isolated.mp4"); var library = home.AddLibrary(); await home.Scan(library.Id); var media = Assert.Single(home.Store.GetCatalog(null, null, null, 0, 10).Items);
        var bytes = Enumerable.Range(0,256).Select(value => (byte)value).ToArray(); var copy = PublishCopy(home, home.Store, owner, media, bytes);
        await using var app = new ReceiverFactory(home); using var client = app.CreateClient(); var store = app.Services.GetRequiredService<MediaStore>();
        var login = store.CreateLoginSession(owner.Id, 1, "Isolated range receiver"); var actor = store.GetPlaybackActor(owner.Id, login.Id); var playing = await app.Services.GetRequiredService<PlaybackSessions>().StartReceiverFileAsync(actor, new(media.Id, Id()), CancellationToken.None);
        var token = new string('d',64); var grant = store.CreateReceiver(actor, playing.Id!, token, "copy", "Isolated receiver", "cast", null, copy); var url = "/receiver/" + token + "/media";
        using var range = new HttpRequestMessage(HttpMethod.Get, url); range.Headers.Range = new(10,29); using var response = await client.SendAsync(range);
        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode); Assert.Equal(bytes[10..30], await response.Content.ReadAsByteArrayAsync()); Assert.Equal("bytes 10-29/256", response.Content.Headers.ContentRange!.ToString());
        using var head = new HttpRequestMessage(HttpMethod.Head,url); using var header = await client.SendAsync(head); Assert.Equal(256, header.Content.Headers.ContentLength);
        using var invalid = new HttpRequestMessage(HttpMethod.Get,url); invalid.Headers.Range = new(900,901); using var missing = await client.SendAsync(invalid); Assert.Equal(HttpStatusCode.RequestedRangeNotSatisfiable, missing.StatusCode);
        store.CloseReceiver(grant.Id); using var conditional = new HttpRequestMessage(HttpMethod.Get,url); conditional.Headers.IfNoneMatch.Add(response.Headers.ETag!); using var revoked = await client.SendAsync(conditional); Assert.Equal(HttpStatusCode.Gone, revoked.StatusCode);
        Assert.Equal(StateFolders.Hash(copy.Path), copy.Sha256);
    }
    [Fact]
    public void SourceConfigurationChangesInvalidateExistingAndInFlightSearchResults()
    {
        using var home = new TestWorkspace(); var old = home.Store.SaveIndexer(null, new("Isolated", "https://fixture.invalid/api", "torznab", null), "protected");
        var movie = new ProviderContent("tmdb", 9876, "movie", "Isolated", "Isolated", "", "2020-01-01", null, null, "https://www.themoviedb.org/movie/9876", []); var target = home.Store.RequestContent(movie, "all", "balanced"); var profile = home.Store.GetQualityProfile("balanced");
        var release = new RawRelease("isolated", "Isolated.2020.1080p", "torrent", 12345, 10, null, "https://fixture.invalid/file.torrent", null, []); var evaluation = ReleaseEvaluator.Evaluate(release, movie, profile.Rules);
        var saved = home.Store.SaveRelease(old, target, new(target.Id), profile, release, evaluation, "protected");
        home.Store.SaveSourceTransport(old.Id, "nyaa-rss", "protected-network", 0);
        Assert.Throws<DomainException>(() => home.Store.RequireCurrentRelease(saved.Id)); Assert.Throws<DomainException>(() => home.Store.SaveRelease(old, target, new(target.Id), profile, release, evaluation, "protected"));
        var manifest = new SourceAdapterManifest(1,"isolated","Isolated","1.0.0","Isolated test fixture","https://fixture.invalid","json","torrent","/\\other.invalid/query",Fields:new(){["id"]="id",["title"]="title",["url"]="url"});
        Assert.Throws<DomainException>(() => SourceAdapterRegistry.Validate(manifest)); Assert.Throws<DomainException>(() => SourceAdapterRegistry.Request(manifest,home.Store.GetIndexer(old.Id),[],"isolated-credential"));
        Assert.True(home.Store.GetIndexer(old.Id).Info.Revision > old.Revision); ForeignKeys(home);
    }
    [Fact]
    public async Task ProxyReconfigurationKeepsAnOpenResponseAliveAndClearsRequestedSecrets()
    {
        using var home = new TestWorkspace(); var info = home.Store.SaveIndexer(null, new("Isolated proxy", "http://fixture.invalid/api", "torznab", null), "protected");
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var proxy = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}";
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(15)); var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var transport = new SourceTransportService(home.Store, new UnusedFactory(), new EphemeralDataProtectionProvider());
        transport.Save(info.Id, new(null, new(ProxyUrl: proxy, Cookie: "isolated=cookie", ProxyPassword: "isolated-password", Headers: new() { ["X-Isolated"] = "isolated-header" })));
        var firstServer = Serve(resume.Task, "first-"); using var first = await transport.SendAsync(home.Store.GetIndexer(info.Id), "http://fixture.invalid/first", "query", budget.Token);
        var revision = transport.Read(info.Id).Revision; transport.Save(info.Id, new(null, new(ProxyUrl: proxy), revision, ClearProxyPassword: true, ClearCookie: true, ClearHeaders: true));
        var secondServer = Serve(Task.CompletedTask, "second"); using var second = await transport.SendAsync(home.Store.GetIndexer(info.Id), "http://fixture.invalid/second", "query", budget.Token);
        Assert.Equal("second", await second.Content.ReadAsStringAsync(budget.Token)); resume.TrySetResult(); Assert.Equal("first-", await first.Content.ReadAsStringAsync(budget.Token));
        var firstHeaders = await firstServer; var secondHeaders = await secondServer; Assert.Contains("isolated=cookie", firstHeaders); Assert.DoesNotContain("isolated=cookie", secondHeaders); Assert.DoesNotContain("X-Isolated", secondHeaders);
        var settings = transport.Read(info.Id).Settings; Assert.Null(settings.Cookie); Assert.Null(settings.ProxyPassword); Assert.Null(settings.Headers);
        async Task<string> Serve(Task release, string text)
        {
            using var connection = await listener.AcceptTcpClientAsync(budget.Token); await using var stream = connection.GetStream(); using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true); var headers = new StringBuilder();
            while (await reader.ReadLineAsync(budget.Token) is { Length: > 0 } line) headers.AppendLine(line);
            await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 6\r\nConnection: close\r\n\r\n" + text[..3]), budget.Token); await stream.FlushAsync(budget.Token);
            await release.WaitAsync(budget.Token); await stream.WriteAsync(Encoding.ASCII.GetBytes(text[3..]), budget.Token); return headers.ToString();
        }
    }
    private static void ForeignKeys(TestWorkspace home)
    {
        using var db = new SqliteConnection($"Data Source={Path.Combine(home.Paths.Data,"home.db")}"); db.Open(); using var command = db.CreateCommand(); command.CommandText = "PRAGMA foreign_key_check"; using var rows = command.ExecuteReader(); Assert.False(rows.Read());
    }
    private sealed class UnusedFactory : IHttpClientFactory { public HttpClient CreateClient(string name) => throw new InvalidOperationException("Only the isolated loopback proxy is allowed."); }
    private sealed class ReceiverFactory(TestWorkspace home) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment("Testing").ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Home:DataPath"] = home.Paths.Data, ["Home:Downloads:LoopbackOnly"] = "true", ["Home:FfmpegPath"] = "absent", ["Home:FfprobePath"] = "absent"
        }));
    }
}
