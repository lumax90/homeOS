using System.IO.Compression;
using System.Text.Json;
using HomeServer.Core;
using HomeServer.Gaming;
using HomeServer.Infrastructure;
using HomeServer.Operations;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace HomeServer.Tests;

public sealed class ContainerUpdateTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private static ContainerUpdateCoordinator Coordinator(TestWorkspace home, ContainerRequestGate? requests = null, Clock? clock = null, char revision = 'a')
    {
        clock ??= new(); requests ??= new(clock);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["Home:Updates:Automatic"] = "true", ["HOME_SOURCE_REVISION"] = new string(revision, 40) }).Build();
        return new(home.Paths, home.Store, new(home.Paths, home.Store), requests, config, clock,
            NullLogger<ContainerUpdateCoordinator>.Instance, supported: true);
    }
    private static ContainerUpdateJournal Journal(TestWorkspace home) => JsonSerializer.Deserialize<ContainerUpdateJournal>(
        File.ReadAllText(Path.Combine(home.Paths.Data, "updates", "container", "history.json")), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    private static GameManifest Game() => new("auto-update-fixture", "Isolated update fixture", "Tests", "https://fixture.invalid/game", "https://fixture.invalid/source", "fixture", "fixture.1", "isolated-test", "MIT", "web", "1/1", [], [], []);

    [Fact]
    public void RequestAdmissionIsAtomicAndWrongOwnersCannotReleaseTheLease()
    {
        var clock = new Clock(); var gate = new ContainerRequestGate(clock);
        using (var request = gate.Enter()) { Assert.NotNull(request); Assert.False(gate.Freeze("one", TimeSpan.FromMinutes(8))); }
        Assert.True(gate.Freeze("one", TimeSpan.FromMinutes(8))); Assert.Null(gate.Enter());
        gate.Release("other"); Assert.Null(gate.Enter());
        clock.Now = clock.Now.AddMinutes(9);
        using var resumed = gate.Enter(); Assert.NotNull(resumed);
    }

    [Fact]
    public async Task EventSubscriptionsDoNotPreventUpdatesWhileOrdinaryRequestsAndNewAdmissionAreFenced()
    {
        using var home = new TestWorkspace(); var gate = new ContainerRequestGate(TimeProvider.System);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = home.Root, EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        await using var app = builder.Build();
        app.Use((context, next) => ContainerUpdateMiddleware.InvokeAsync(context, next, gate, true));
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        app.MapGet("/api/events", async context =>
        {
            context.Response.ContentType = "text/event-stream";
            await context.Response.WriteAsync(": ready\n\n"); await context.Response.Body.FlushAsync();
            try { await Task.Delay(Timeout.Infinite, context.RequestAborted); } catch (OperationCanceledException) { }
        });
        app.MapGet("/api/hold", async () => { started.TrySetResult(); await finish.Task; return Results.Ok(); });
        app.MapGet("/api/probe", () => Results.Ok()); app.MapGet("/api/health", () => Results.Ok());
        await app.StartAsync(); using var client = app.GetTestClient(); using var lifetime = new CancellationTokenSource();
        using var subscription = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/api/events"), HttpCompletionOption.ResponseHeadersRead, lifetime.Token);
        try
        {
            Assert.True(gate.Freeze("events-do-not-block", TimeSpan.FromMinutes(1))); gate.Release("events-do-not-block");
            var request = client.GetAsync("/api/hold"); await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(gate.Freeze("active-request", TimeSpan.FromMinutes(1))); finish.TrySetResult(); (await request).EnsureSuccessStatusCode();
            Assert.True(gate.Freeze("prepared", TimeSpan.FromMinutes(1)));
            Assert.Equal(System.Net.HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/api/probe")).StatusCode);
            (await client.GetAsync("/api/health")).EnsureSuccessStatusCode(); gate.Release("prepared");
        }
        finally { finish.TrySetResult(); lifetime.Cancel(); }
    }

    [Fact]
    public async Task BusyGamesDeferUpdatesWithoutCreatingBackupsOrClosingTheGame()
    {
        using var home = new TestWorkspace(); home.Store.CreateOwner(new("Fixture owner", "isolated-hash"));
        var actor = home.Store.GetAccount("1")!; var login = home.Store.CreateLoginSession("1", 1, "isolated update fixture");
        var session = home.Store.StartGameSession(Game(), new(Guid.NewGuid().ToString("N"), new string('a', 64)), actor, login.Id);
        var response = await Coordinator(home).PrepareAsync(CancellationToken.None);
        Assert.Equal(75, response.ExitCode); Assert.Equal("busy", response.State);
        Assert.Equal(session.Id, Assert.Single(home.Store.GetActiveGameSessions()).Id);
        Assert.Empty(new BackupService(home.Paths, home.Store).List()); Assert.False(home.Store.ContainerUpdateBlocked);
    }

    [Fact]
    public async Task ActiveRequestsDeferWhileIdlePreparationPreservesPausedTransfersAndBlocksNewWork()
    {
        using var home = new TestWorkspace(); var clock = new Clock(); var gate = new ContainerRequestGate(clock);
        home.Store.CreateOwner(new("Fixture owner", "isolated-hash"));
        var transfer = home.Store.AddDownload(["isolated-update-hash"], "Isolated paused fixture", "torrent", "no-network-fixture", 4);
        home.Store.SetDownloadIntent(transfer.Id, "paused");
        var library = home.AddLibrary(); home.Store.QueueScan(library.Id);
        var coordinator = Coordinator(home, gate, clock);
        using (var request = gate.Enter()) Assert.Equal(75, (await coordinator.PrepareAsync(CancellationToken.None)).ExitCode);
        var result = await coordinator.PrepareAsync(CancellationToken.None);
        Assert.Equal(0, result.ExitCode); Assert.NotNull(result.BackupId); Assert.Null(gate.Enter());
        Assert.True(home.Store.ContainerUpdateBlocked); Assert.Null(home.Store.TryClaimJob()); Assert.Null(home.Store.ClaimSystemJob());
        Assert.Null(home.Store.ClaimProductJob(["fixture"])); Assert.Null(home.Store.ClaimOfflineExport());
        Assert.Equal("paused", home.Store.GetDownload(transfer.Id)!.DesiredState);
        Assert.Equal(503, Assert.Throws<DomainException>(() => home.Store.SetDownloadIntent(transfer.Id, "running")).StatusCode);
        Assert.Equal("prepared", Assert.Single(Journal(home).Attempts).State);
        await coordinator.CancelPreparationAsync(CancellationToken.None);
        Assert.False(home.Store.ContainerUpdateBlocked); using var resumed = gate.Enter(); Assert.NotNull(resumed);
        Assert.NotNull(home.Store.TryClaimJob()); Assert.Equal("paused", home.Store.GetDownload(transfer.Id)!.DesiredState);
    }

    [Fact]
    public async Task ActiveTransfersDeferWithoutChangingIntent()
    {
        using var home = new TestWorkspace(); home.Store.CreateOwner(new("Fixture owner", "isolated-hash"));
        var transfer = home.Store.AddDownload(["isolated-active-hash"], "Isolated queued fixture", "torrent", "no-network-fixture", 4);
        Assert.Equal(75, (await Coordinator(home).PrepareAsync(CancellationToken.None)).ExitCode);
        Assert.Equal("running", home.Store.GetDownload(transfer.Id)!.DesiredState);
        Assert.Empty(new BackupService(home.Paths, home.Store).List());
    }

    [Fact]
    public async Task PreparationFailureReopensAdmissionAndPreservesTheConflictingFile()
    {
        using var home = new TestWorkspace(); var gate = new ContainerRequestGate(TimeProvider.System);
        Directory.CreateDirectory(home.Paths.Data); var blocked = Path.Combine(home.Paths.Data, "backups"); await File.WriteAllTextAsync(blocked, "original fixture");
        var result = await Coordinator(home, gate).PrepareAsync(CancellationToken.None);
        Assert.Equal(75, result.ExitCode); Assert.Equal("failed", result.State); Assert.False(home.Store.ContainerUpdateBlocked);
        using var request = gate.Enter(); Assert.NotNull(request); Assert.Equal("original fixture", await File.ReadAllTextAsync(blocked));
    }

    [Fact]
    public async Task ExpiredPreparationRecoversWithoutAnUpdaterCallback()
    {
        using var home = new TestWorkspace(); var clock = new Clock(); var gate = new ContainerRequestGate(clock); var coordinator = Coordinator(home, gate, clock);
        Assert.Equal(0, (await coordinator.PrepareAsync(CancellationToken.None)).ExitCode);
        clock.Now = clock.Now.AddMinutes(9); await coordinator.ExpireAsync(CancellationToken.None);
        Assert.False(home.Store.ContainerUpdateBlocked); using var request = gate.Enter(); Assert.NotNull(request);
        Assert.Equal("expired", Assert.Single(Journal(home).Attempts).State);
    }

    [Fact]
    public async Task RestartRecordsTheNewRevisionAndRetainsTheBackup()
    {
        using var home = new TestWorkspace(); var prepared = await Coordinator(home, revision: 'a').PrepareAsync(CancellationToken.None);
        Assert.Equal(0, prepared.ExitCode);
        await Coordinator(home, revision: 'b').CompleteStartupAsync(CancellationToken.None);
        var result = Assert.Single(Journal(home).Attempts);
        Assert.Equal("updated", result.State); Assert.Equal(new string('a', 40), result.FromRevision); Assert.Equal(new string('b', 40), result.ToRevision);
        Assert.True(File.Exists(new BackupService(home.Paths, home.Store).FilePath(result.BackupId)));
    }

    [Fact]
    public async Task AutomaticRetentionNeverDeletesManualBackups()
    {
        using var home = new TestWorkspace(); var service = new BackupService(home.Paths, home.Store);
        var manual = await service.CreateAsync(Guid.NewGuid().ToString("N"), CancellationToken.None); var original = await File.ReadAllBytesAsync(service.FilePath(manual.Id));
        var coordinator = Coordinator(home);
        for (var count = 0; count < 5; count++) { Assert.Equal(0, (await coordinator.PrepareAsync(CancellationToken.None)).ExitCode); await coordinator.CancelPreparationAsync(CancellationToken.None); }
        Assert.Equal(3, Journal(home).Attempts.Length); Assert.Equal(4, service.List().Count);
        Assert.False(await service.DeleteContainerUpdateAsync(manual.Id, CancellationToken.None)); Assert.Equal(original, await File.ReadAllBytesAsync(service.FilePath(manual.Id)));
        foreach (var row in Journal(home).Attempts)
        {
            using var zip = ZipFile.OpenRead(service.FilePath(row.BackupId)); using var entry = zip.GetEntry("manifest.json")!.Open();
            Assert.Equal("container-update", JsonSerializer.Deserialize<BackupManifest>(entry)!.Purpose);
        }
    }

    [Fact]
    public async Task DisabledPreferenceSurvivesCoordinatorRecreation()
    {
        using var home = new TestWorkspace(); Coordinator(home).Configure(new(false));
        var next = Coordinator(home); Assert.False(next.Enabled); Assert.Equal("disabled", (await next.PrepareAsync(CancellationToken.None)).State);
        Assert.Empty(new BackupService(home.Paths, home.Store).List());
    }

    [Fact]
    public async Task MalformedHistoryIsPreservedAndDoesNotFreezeTheServer()
    {
        using var home = new TestWorkspace(); var directory = Path.Combine(home.Paths.Data, "updates", "container"); Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "history.json"); await File.WriteAllTextAsync(path, "invalid original fixture");
        var gate = new ContainerRequestGate(TimeProvider.System);
        Assert.Equal(75, (await Coordinator(home, gate).PrepareAsync(CancellationToken.None)).ExitCode);
        Assert.Equal("invalid original fixture", await File.ReadAllTextAsync(path)); Assert.False(home.Store.ContainerUpdateBlocked);
        using var request = gate.Enter(); Assert.NotNull(request);
    }
}
