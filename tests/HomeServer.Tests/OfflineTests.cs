using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HomeServer.Core;
using HomeServer.Infrastructure;
using HomeServer.Offline;
using HomeServer.Playback;
using HomeServer.Scanning;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Data.Sqlite;

namespace HomeServer.Tests;

public sealed class OfflineTests
{
    [MediaFact]
    public async Task OriginalAndSubtitleChunksAreVerifiedReadOnlyAndBoundToTheAccountDevice()
    {
        using var home = new TestWorkspace(); var tools = Tools(home); var source = await Generate(home, tools);
        await File.WriteAllTextAsync(Path.Combine(home.Source, "Fixture.tr.srt"), "1\n00:00:00,100 --> 00:00:02,000\nİzole Türkçe altyazı\n", new UTF8Encoding(false));
        var item = await Scan(home, tools); var actor = Actor(home); using var subtitles = new SubtitleService(home.Paths, home.Store, tools); var analysis = new MediaAnalysisService(home.Store, tools);
        var subtitle = Assert.Single(subtitles.List(await analysis.InspectAsync(item.Id, CancellationToken.None)));
        using var service = Service(home, tools, analysis, subtitles); var hash = StateFolders.Hash(source);
        var job = await service.QueueAsync(item.Id, actor, new(Guid.NewGuid().ToString("N"), SubtitleId: subtitle.Id), CancellationToken.None);
        Assert.True(await service.RunOnceAsync(CancellationToken.None)); var manifest = service.Manifest(job.Id, actor); Assert.Equal(2, manifest.Assets.Length);
        var media = manifest.Assets.Single(asset => asset.Id == "media"); var chunk = await service.ReadChunkAsync(job.Id, "media", 0, media.Fingerprint, actor, CancellationToken.None);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(chunk.Data)), chunk.Hash); Assert.Equal(await File.ReadAllBytesAsync(source), chunk.Data); Assert.Equal(hash, StateFolders.Hash(source));
        var caption = manifest.Assets.Single(asset => asset.Id == "subtitle"); var text = await service.ReadChunkAsync(job.Id, "subtitle", 0, caption.Fingerprint, actor, CancellationToken.None); Assert.Contains("Türkçe", Encoding.UTF8.GetString(text.Data));
        var another = home.Store.CreateLoginSession("1", 1, "Second device"); var other = home.Store.GetPlaybackActor("1", another.Id);
        Assert.Equal(404, (await Assert.ThrowsAsync<DomainException>(() => service.ReadChunkAsync(job.Id, "media", 0, media.Fingerprint, other, CancellationToken.None))).StatusCode);
        home.Store.CancelOfflineExport(job.Id, home.Store.GetOfflineExport(job.Id)!.Revision, actor);
        Assert.Equal(410, Assert.Throws<DomainException>(() => service.Manifest(job.Id, actor)).StatusCode); Assert.Equal(hash, StateFolders.Hash(source));
    }
    [MediaFact]
    public async Task CompatibleMp4AndStyledBurnProduceVerifiedNativeMediaWithoutTouchingSource()
    {
        using var home = new TestWorkspace(); var tools = Tools(home); var source = await Generate(home, tools, "1280x720");
        await File.WriteAllTextAsync(Path.Combine(home.Source, "Fixture.en.srt"), "1\n00:00:00,000 --> 00:00:02,500\nISOLATED OFFLINE CAPTION\n");
        var item = await Scan(home, tools); var actor = Actor(home); var hash = StateFolders.Hash(source); var analysis = new MediaAnalysisService(home.Store, tools); using var subtitles = new SubtitleService(home.Paths, home.Store, tools);
        var track = Assert.Single(subtitles.List(await analysis.InspectAsync(item.Id, CancellationToken.None)));
        using var service = Service(home, tools, analysis, subtitles);
        var job = await service.QueueAsync(item.Id, actor, new(Guid.NewGuid().ToString("N"), "compatible-720", SubtitleId: track.Id, BurnSubtitle: true), CancellationToken.None);
        Assert.True(await service.RunOnceAsync(CancellationToken.None)); var prepared = home.Store.GetOfflineExport(job.Id)!; Assert.Equal("ready", prepared.Status);
        var manifest = service.Manifest(job.Id, actor); var asset = Assert.Single(manifest.Assets); Assert.Equal("video/mp4", asset.ContentType);
        var file = Path.Combine(home.Paths.Data, "offline-exports", job.Id, "media.mp4"); var probe = (await tools.ProbeAsync(file, CancellationToken.None))!;
        Assert.Equal("h264", probe.VideoCodec); Assert.Equal("aac", probe.AudioCodec); Assert.Equal(720, probe.Height); Assert.InRange(probe.Duration!.Value, 2.9, 3.2);
        var pixels = Path.Combine(home.Root, "caption.rgb"); await MediaProcess.RunAsync(tools.FfmpegExecutable, ["-hide_banner", "-loglevel", "error", "-nostdin", "-i", file, "-ss", "1", "-frames:v", "1", "-pix_fmt", "rgb24", "-f", "rawvideo", pixels], TimeSpan.FromSeconds(15), CancellationToken.None);
        Assert.True((await File.ReadAllBytesAsync(pixels)).Count(value => value > 200) > 100); Assert.Equal(hash, StateFolders.Hash(source));
    }
    [MediaFact]
    public async Task CachedSourceChangesCannotBeMixedIntoResumedChunks()
    {
        using var home = new TestWorkspace(); var tools = Tools(home); var source = await Generate(home, tools); var media = await Scan(home, tools); var actor = Actor(home);
        var analysis = new MediaAnalysisService(home.Store, tools); using var subtitles = new SubtitleService(home.Paths, home.Store, tools); using var service = Service(home, tools, analysis, subtitles);
        var job = await service.QueueAsync(media.Id, actor, new(Guid.NewGuid().ToString("N")), CancellationToken.None); await service.RunOnceAsync(CancellationToken.None);
        var asset = service.Manifest(job.Id, actor).Assets[0]; File.SetLastWriteTimeUtc(source, DateTime.UtcNow.AddMinutes(1));
        Assert.Equal(409, (await Assert.ThrowsAsync<DomainException>(() => service.ReadChunkAsync(job.Id, "media", 0, asset.Fingerprint, actor, CancellationToken.None))).StatusCode);
    }
    [MediaFact]
    public async Task CompatibleMusicUsesAnActualAacMp4AndRetainsItsSourceBytes()
    {
        using var home = new TestWorkspace(); var tools = Tools(home); var source = Path.Combine(home.Source, "Song.flac");
        await MediaProcess.RunAsync(tools.FfmpegExecutable, ["-hide_banner", "-loglevel", "error", "-nostdin", "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=44100", "-t", "1", "-c:a", "flac", source], TimeSpan.FromSeconds(15), CancellationToken.None);
        var media = await Scan(home, tools); var actor = Actor(home); var hash = StateFolders.Hash(source); var analysis = new MediaAnalysisService(home.Store, tools); using var subtitles = new SubtitleService(home.Paths, home.Store, tools); using var service = Service(home, tools, analysis, subtitles);
        var job = await service.QueueAsync(media.Id, actor, new(Guid.NewGuid().ToString("N"), "compatible-720"), CancellationToken.None); await service.RunOnceAsync(CancellationToken.None);
        var manifest = service.Manifest(job.Id, actor); Assert.Equal("audio/mp4", Assert.Single(manifest.Assets).ContentType);
        var output = await tools.ProbeAsync(Path.Combine(home.Paths.Data, "offline-exports", job.Id, "media.mp4"), CancellationToken.None); Assert.Equal("aac", output!.AudioCodec); Assert.Null(output.VideoCodec); Assert.Equal(hash, StateFolders.Hash(source));
    }
    [Fact]
    public async Task PublicationAndRestartFenceOldLeasesAndEnforceRenewalQuota()
    {
        using var home = new TestWorkspace(); home.Write("One.mp4"); home.Write("Two.mp4"); var library = home.AddLibrary(); await home.Scan(library.Id); var actor = Actor(home); var items = home.Store.GetCatalog(null, null, null, 0, 10).Items;
        home.Store.SaveOfflinePolicy("1", new(MaximumItems: 1));
        var queued = QueueFake(home, actor, items[0]); var first = home.Store.ClaimOfflineExport()!; home.Store.RecoverOfflineExports(); var next = home.Store.ClaimOfflineExport()!;
        var published = false; Assert.Throws<OperationCanceledException>(() => home.Store.CompleteOfflineExport(first, Manifest(first), () => published = true)); Assert.False(published); Assert.NotEqual(first.Lease, next.Lease);
        home.Store.CompleteOfflineExport(next, Manifest(next), () => {});
        Assert.Equal(429, Assert.Throws<DomainException>(() => QueueFake(home, actor, items[1])).StatusCode);
        using (var db = new SqliteConnection($"Data Source={Path.Combine(home.Paths.Data, "home.db")}"))
        {
            db.Open(); using var command = db.CreateCommand(); command.CommandText = "UPDATE offline_exports SET expires_at=$past WHERE id=$id"; command.Parameters.AddWithValue("$past", DateTimeOffset.UtcNow.AddDays(-1).ToString("O")); command.Parameters.AddWithValue("$id", queued.Id); command.ExecuteNonQuery();
        }
        QueueFake(home, actor, items[1]); Assert.Equal(429, Assert.Throws<DomainException>(() => home.Store.RenewOfflineExport(queued.Id, next.Revision, actor)).StatusCode);
        home.Store.SaveOfflinePolicy("1", new(Enabled: false, MaximumItems: 1, Revision: 1));
        Assert.Equal("revoked", home.Store.GetOfflineExport(queued.Id)!.Status); Assert.Equal(403, Assert.Throws<DomainException>(() => home.Store.RenewOfflineExport(queued.Id, next.Revision, actor)).StatusCode);
    }
    [Fact]
    public async Task OfflineProgressDetectsOtherDeviceChangesAndItsReceiptIsIdempotent()
    {
        using var home = new TestWorkspace(); home.Write("One.mp4"); home.Write("Two.mp4"); var library = home.AddLibrary(); await home.Scan(library.Id); var actor = Actor(home); var items = home.Store.GetCatalog(null, null, null, 0, 10).Items;
        var movie = new ProviderContent("tmdb", 42, "movie", "Isolated movie", "Isolated movie", "", "2020-01-01", null, null, "https://www.themoviedb.org/movie/42", []);
        foreach (var item in items) home.Store.MatchMedia(item.Id, movie, null, "");
        var media = home.Store.GetMedia(items[0].Id)!; var job = ReadyFake(home, actor, media); var baseline = home.Store.ProgressStamp(media.Id, "1");
        home.Store.SaveProgress(items[1].Id, new(80, 120), home.Store.GetAccount("1"));
        var input = new OfflineProgressInput(Guid.NewGuid().ToString("N"), 1, 45, baseline, "1"); var conflict = home.Store.SyncOfflineProgress(job.Id, input, actor);
        Assert.Equal("conflict", conflict.Status); Assert.Equal(80, conflict.PositionSeconds); Assert.Equal(80, home.Store.GetMedia(media.Id)!.PositionSeconds);
        var acceptedInput = input with { EventId = Guid.NewGuid().ToString("N"), ExpectedStamp = conflict.Stamp }; var accepted = home.Store.SyncOfflineProgress(job.Id, acceptedInput, actor);
        Assert.Equal("applied", accepted.Status); Assert.Equal(45, home.Store.GetMedia(media.Id)!.PositionSeconds);
        home.Store.SaveProgress(items[1].Id, new(90, 120), home.Store.GetAccount("1"));
        Assert.Equal(accepted, home.Store.SyncOfflineProgress(job.Id, acceptedInput, actor)); Assert.Equal(90, home.Store.GetMedia(media.Id)!.PositionSeconds);
        Assert.Throws<DomainException>(() => home.Store.SyncOfflineProgress(job.Id, acceptedInput with { PositionSeconds = 10 }, actor));
        var old = home.Store.SyncOfflineProgress(job.Id, acceptedInput with { EventId = Guid.NewGuid().ToString("N") }, actor); Assert.Equal("stale", old.Status); Assert.Equal(90, old.PositionSeconds);
    }
    [Fact]
    public async Task UnavailableVolumeKeepsTheDeviceGrantButAccountAndAssociationChangesRevokeIt()
    {
        using var home = new TestWorkspace(); home.Write("One.mp4"); var library = home.AddLibrary(); await home.Scan(library.Id); var actor = Actor(home); var media = Assert.Single(home.Store.GetCatalog(null, null, null, 0, 10).Items);
        var job = ReadyFake(home, actor, media); var absent = Path.Combine(home.Root, "temporarily-offline"); Directory.Move(home.Source, absent);
        try { Assert.Equal("ready", home.Store.RequireOfflineExport(job.Id, actor).Status); home.Store.ReconcileOfflineExports(); Assert.Equal("ready", home.Store.GetOfflineExport(job.Id)!.Status); }
        finally { Directory.Move(absent, home.Source); }
        home.Store.MatchMedia(media.Id, new("tmdb", 99, "movie", "Changed association", "Changed association", "", null, null, null, "https://www.themoviedb.org/movie/99", []), null, "");
        Assert.Equal(403, Assert.Throws<DomainException>(() => home.Store.RequireOfflineExport(job.Id, actor)).StatusCode); Assert.True(File.Exists(Path.Combine(home.Source, "One.mp4")));
    }
    [MediaFact]
    public async Task HttpExportRequiresCsrfAndTheExpectedLoginIdentity()
    {
        using var home = new TestWorkspace(); var tools = Tools(home); await Generate(home, tools); var media = await Scan(home, tools);
        await using var app = new Factory(home); using var client = app.CreateClient();
        var input = new OfflineInput(Guid.NewGuid().ToString("N")); Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync($"/api/offline/media/{media.Id}", input)).StatusCode);
        var session = await client.GetFromJsonAsync<JsonElement>("/api/session"); client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", session.GetProperty("csrfToken").GetString());
        (await client.PostAsJsonAsync("/api/setup", new Credentials("Offline owner", "offline-fixture-password"))).EnsureSuccessStatusCode();
        session = await client.GetFromJsonAsync<JsonElement>("/api/session"); client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/offline/media/{media.Id}", input)).StatusCode); client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", session.GetProperty("csrfToken").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/offline/media/{media.Id}", input)).StatusCode);
        var prepared = await client.PostAsJsonAsync($"/api/offline/media/{media.Id}", input with { ExpectedUserId = "1", ExpectedSessionId = session.GetProperty("sessionId").GetString() }); prepared.EnsureSuccessStatusCode();
        var job = (await prepared.Content.ReadFromJsonAsync<OfflineExport>())!; var store = app.Services.GetRequiredService<MediaStore>(); Assert.Equal("1", job.UserId);
        using var anonymous = app.CreateClient(); Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/offline/exports/{job.Id}")).StatusCode);
        (await client.PostAsync("/api/auth/logout", null)).EnsureSuccessStatusCode(); Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/offline/exports/{job.Id}/manifest")).StatusCode);
    }
    private static PlaybackActor Actor(TestWorkspace home)
    {
        home.Store.CreateOwner(new("Owner", "fixture")); var login = home.Store.CreateLoginSession("1", 1, "Isolated offline device"); return home.Store.GetPlaybackActor("1", login.Id);
    }
    private static OfflineExport QueueFake(TestWorkspace home, PlaybackActor actor, MediaItem media)
    {
        var path = Path.Combine(home.Source, media.RelativePath); var analyzed = new AnalyzedMedia(media, path, MediaAnalysisService.Fingerprint(new FileInfo(path)), new(120, null, null, "h264", "aac"));
        return home.Store.QueueOfflineExport(actor, analyzed, new(Guid.NewGuid().ToString("N")), 1024, DateTimeOffset.UtcNow);
    }
    private static OfflineManifest Manifest(OfflineExport job) => new(job.Id, job.MediaId, "Isolated", "video", "Test", 120, job.SourceFingerprint, job.MatchRevision, job.ExpiresAt, [new("media", "video/mp4", 4, job.SourceFingerprint, OfflineService.ChunkBytes, 1)], "", 0, false);
    private static OfflineExport ReadyFake(TestWorkspace home, PlaybackActor actor, MediaItem media)
    { QueueFake(home, actor, media); var job = home.Store.ClaimOfflineExport()!; home.Store.CompleteOfflineExport(job, Manifest(job), () => {}); return home.Store.GetOfflineExport(job.Id)!; }
    private static IConfiguration Configuration() => SubtitleBurnTests.Configuration();
    private static MediaTools Tools(TestWorkspace home) => new(Configuration(), home.Paths, NullLogger<MediaTools>.Instance);
    private static OfflineService Service(TestWorkspace home, MediaTools tools, MediaAnalysisService analysis, SubtitleService subtitles)
    { var encoding = new EncodingSupport(tools, home.Store); encoding.SetPreference("software"); return new(home.Paths, home.Store, analysis, tools, subtitles, encoding, new EventHub(), NullLogger<OfflineService>.Instance); }
    private static async Task<MediaItem> Scan(TestWorkspace home, MediaTools tools)
    { var library = home.AddLibrary(); home.Store.QueueScan(library.Id); await new LibraryScanner(home.Store, tools, new EventHub()).ScanAsync(home.Store.TryClaimJob()!, CancellationToken.None); return Assert.Single(home.Store.GetCatalog(null, null, null, 0, 10).Items); }
    private static async Task<string> Generate(TestWorkspace home, MediaTools tools, string size = "320x180")
    {
        var path = Path.Combine(home.Source, "Fixture.mkv");
        await MediaProcess.RunAsync(tools.FfmpegExecutable, ["-hide_banner", "-loglevel", "error", "-nostdin", "-f", "lavfi", "-i", $"color=c=black:s={size}:r=12", "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000", "-t", "3", "-c:v", "libx264", "-threads", "1", "-preset", "ultrafast", "-c:a", "aac", path], TimeSpan.FromSeconds(20), CancellationToken.None);
        return path;
    }
    private sealed class Factory(TestWorkspace home) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing"); builder.ConfigureAppConfiguration((_, config) => config.AddConfiguration(Configuration()).AddInMemoryCollection(new Dictionary<string, string?> { ["Home:DataPath"] = home.Paths.Data, ["Home:Downloads:LoopbackOnly"] = "true" }));
        }
    }
}
