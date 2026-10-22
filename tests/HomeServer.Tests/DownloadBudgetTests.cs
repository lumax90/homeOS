using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HomeServer.Core;
using HomeServer.Downloads;
using HomeServer.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HomeServer.Tests;

public sealed class DownloadBudgetTests
{
    private static string Id() => Guid.NewGuid().ToString("N");
    private static DownloadPrincipal Owner(TestWorkspace home)
    {
        home.Store.CreateOwner(new("Owner", "isolated")); var login = home.Store.CreateLoginSession("1", 1, "Isolated budget device"); return home.Store.DownloadActor("1", login.Id);
    }
    private static DownloadRecord Add(MediaStore store, DownloadPrincipal actor, string? hash = null) => store.AddDownload([hash ?? Id()], "Isolated package", "torrent", "protected-fixture", 600, principal: actor);
    private static DownloadBudgetRun Begin(MediaStore store, DownloadRecord item, DateTimeOffset? now = null) => store.BeginDownloadBudgetRun(item.Id, item.Revision, Id(), now ?? DateTimeOffset.UtcNow);

    [Fact]
    public async Task AdmissionLimitIsAtomicAndRepeatingTheSameHashDoesNotSpendTwice()
    {
        using var home = new TestWorkspace(); var actor = Owner(home); home.Store.SaveDownloadBudget("1", new(MonthlyAcquisitions: 1));
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            try { return Add(home.Store, actor); } catch (DownloadBudgetException ex) { Assert.Equal("monthly-acquisitions", ex.Code); return null; }
        })));
        var added = Assert.Single(results.OfType<DownloadRecord>());
        Assert.Equal(added.Id, Add(home.Store, actor, added.InfoHash).Id);
        Assert.Single(home.Store.GetDownloads()); var usage = home.Store.GetDownloadBudgetUsage("1", DateTimeOffset.UtcNow); Assert.Equal(1, usage.Acquisitions); Assert.Equal(0, usage.CommittedBytes);
        Assert.Single(home.Store.GetDownloadBudgetHistory("1").Items, item => item.Action == "admission");
    }
    [Fact]
    public void ReservationReleaseResumeAndSelectionGrowthPreserveTheMonthlyLedger()
    {
        using var home = new TestWorkspace(); var actor = Owner(home); home.Store.SaveDownloadBudget("1", new(MonthlyPayloadBytes: 1000, MaximumConcurrent: 2));
        var first = Add(home.Store, actor); var second = Add(home.Store, actor); var a = Begin(home.Store, first); var b = Begin(home.Store, second); var now = DateTimeOffset.UtcNow;
        home.Store.ReserveDownloadPayload(a, 600, now);
        Assert.Equal("monthly-payload", Assert.Throws<DownloadBudgetException>(() => home.Store.ReserveDownloadPayload(b, 600, now)).Code);
        home.Store.CloseDownloadBudgetRun(a, now); home.Store.CommitDownloadPayloadStart(b, 600, now); home.Store.CommitDownloadPayloadStart(b, 600, now);
        Assert.Equal(600, home.Store.GetDownloadBudgetUsage("1", now).CommittedBytes);
        home.Store.SetDownloadIntent(second.Id, "paused"); home.Store.CloseDownloadBudgetRun(b, now);
        var resumed = home.Store.SetDownloadIntent(second.Id, "running"); var c = Begin(home.Store, resumed);
        home.Store.CommitDownloadPayloadStart(c, 800, now); home.Store.CommitDownloadPayloadStart(c, 300, now);
        var usage = home.Store.GetDownloadBudgetUsage("1", now); Assert.Equal(800, usage.CommittedBytes); Assert.Equal(0, usage.ReservedBytes); Assert.Equal(2, usage.Acquisitions);
        home.Store.CloseDownloadBudgetRun(c, now);
    }
    [Fact]
    public void MetadataReservationAcrossUtcMonthRolloverIsChargedOnlyToTheStartMonth()
    {
        using var home = new TestWorkspace(); var actor = Owner(home); home.Store.SaveDownloadBudget("1", new(MonthlyPayloadBytes: 700)); var item = Add(home.Store, actor);
        var next = MediaStore.NextBudgetPeriod(DateTimeOffset.UtcNow); var before = next.AddSeconds(-1); var run = Begin(home.Store, item, before);
        home.Store.ReserveDownloadPayload(run, 600, before); home.Store.CommitDownloadPayloadStart(run, 600, next);
        Assert.Equal(0, home.Store.GetDownloadBudgetUsage("1", before).CommittedBytes); Assert.Equal(0, home.Store.GetDownloadBudgetUsage("1", before).ReservedBytes);
        Assert.Equal(600, home.Store.GetDownloadBudgetUsage("1", next).CommittedBytes);
        Assert.Equal(new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero), MediaStore.NextBudgetPeriod(new DateTimeOffset(2026, 10, 31, 23, 30, 0, TimeSpan.FromHours(-3))));
    }
    [Fact]
    public void PolicyRevisionFencesPreStartWorkAndZeroBudgetStopsPreviouslyPaidPackages()
    {
        using var home = new TestWorkspace(); var actor = Owner(home); var item = Add(home.Store, actor); var run = Begin(home.Store, item); var now = DateTimeOffset.UtcNow;
        home.Store.ReserveDownloadPayload(run, 600, now); home.Store.SaveDownloadBudget("1", new(MonthlyPayloadBytes: 0));
        Assert.Throws<OperationCanceledException>(() => home.Store.CommitDownloadPayloadStart(run, 600, now)); home.Store.CloseDownloadBudgetRun(run, now);
        Assert.Equal(0, home.Store.GetDownloadBudgetUsage("1", now).CommittedBytes);
        Assert.Equal(409, Assert.Throws<DomainException>(() => home.Store.SaveDownloadBudget("1", new())).StatusCode);
        home.Store.SaveDownloadBudget("1", new(MonthlyPayloadBytes: 1000, Revision: 1)); var current = home.Store.GetDownload(item.Id)!; var started = Begin(home.Store, current);
        home.Store.CommitDownloadPayloadStart(started, 600, now); home.Store.CloseDownloadBudgetRun(started, now);
        home.Store.SaveDownloadBudget("1", new(MonthlyPayloadBytes: 0, Revision: 2)); var denied = Begin(home.Store, home.Store.GetDownload(item.Id)!);
        Assert.Equal("payload-disabled", Assert.Throws<DownloadBudgetException>(() => home.Store.CommitDownloadPayloadStart(denied, 600, now)).Code);
    }
    [Fact]
    public void StagingCleanupStartsANewBudgetGenerationAndArchiveCannotEraseUsage()
    {
        using var home = new TestWorkspace(); var actor = Owner(home); var original = home.Write("Read-only source.mkv"); var hash = StateFolders.Hash(original); var item = Add(home.Store, actor); var run = Begin(home.Store, item); var now = DateTimeOffset.UtcNow;
        home.Store.CommitDownloadPayloadStart(run, 600, now); home.Store.CloseDownloadBudgetRun(run, now);
        var paused = home.Store.SetDownloadIntent(item.Id, "paused"); home.Store.ArchiveDownload(item.Id); home.Store.RestoreDownload(item.Id);
        Assert.Equal(600, home.Store.GetDownload(item.Id)!.Ownership!.ChargedBytes);
        home.Store.CleanDownloadPayload(item.Id, paused.Revision, () => {}); var current = home.Store.SetDownloadIntent(item.Id, "running"); Assert.Equal(1, current.Ownership!.Generation);
        Assert.Throws<OperationCanceledException>(() => home.Store.CommitDownloadPayloadStart(run, 600, now));
        var next = Begin(home.Store, current); home.Store.CommitDownloadPayloadStart(next, 600, now);
        Assert.Equal(1200, home.Store.GetDownloadBudgetUsage("1", now).CommittedBytes); Assert.Equal(1, home.Store.GetDownloadBudgetUsage("1", now).Acquisitions); Assert.Equal(hash, StateFolders.Hash(original));
    }
    [Fact]
    public void CounterReceiptsAcceptTheFinalCancelledSampleButNotDuplicateOrClosedRuns()
    {
        using var home = new TestWorkspace(); var actor = Owner(home); var item = Add(home.Store, actor); var run = Begin(home.Store, item); var now = DateTimeOffset.UtcNow;
        home.Store.RecordTransferCounters(item.Id, item.Revision, "network", 10, 100, 0, run);
        home.Store.SetDownloadIntent(item.Id, "paused"); home.Store.RecordTransferCounters(item.Id, item.Revision, "network", 20, 150, 0, run);
        home.Store.RecordTransferCounters(item.Id, item.Revision, "network", 20, 150, 0, run); home.Store.RecordTransferCounters(item.Id, item.Revision, "network", 10, 100, 0, run);
        home.Store.RecordTransferCounters(item.Id, item.Revision, "seeding", 30, 0, 5, run); home.Store.RecordTransferCounters(item.Id, item.Revision, "network", 20, 150, 0, run);
        var current = home.Store.GetDownload(item.Id)!; Assert.Equal("paused", current.Status); Assert.Equal(150, current.ReceivedBytes); Assert.Equal(50, current.UploadedBytes); Assert.Equal(5, current.SeedSeconds);
        home.Store.CloseDownloadBudgetRun(run, now); home.Store.RecordTransferCounters(item.Id, item.Revision, "network", 500, 500, 0, run);
        var usage = home.Store.GetDownloadBudgetUsage("1", now); Assert.Equal(150, usage.ReceivedBytes); Assert.Equal(50, usage.UploadedBytes);
    }
    [Fact]
    public void RestartReleasesReservationsAndSlotsWhileRetainingAcceptedUsage()
    {
        using var home = new TestWorkspace(); var actor = Owner(home); home.Store.SaveDownloadBudget("1", new(MaximumConcurrent: 1)); var first = Add(home.Store, actor); var second = Add(home.Store, actor); var now = DateTimeOffset.UtcNow;
        var run = Begin(home.Store, first); home.Store.ReserveDownloadPayload(run, 600, now);
        Assert.Equal("concurrency", Assert.Throws<DownloadBudgetException>(() => Begin(home.Store, second)).Code);
        var reopened = new MediaStore(home.Paths); reopened.Initialize(); reopened.RecoverDownloadBudgets(now);
        var usage = reopened.GetDownloadBudgetUsage("1", now); Assert.Equal(0, usage.ReservedBytes); Assert.Equal(0, usage.ActiveTransfers); Assert.Equal(2, usage.Acquisitions);
        Assert.Throws<OperationCanceledException>(() => reopened.CommitDownloadPayloadStart(run, 600, now));
        var next = Begin(reopened, second); reopened.CommitDownloadPayloadStart(next, 600, now); reopened.SetDownloadBudgetSeeding(next);
        Assert.NotNull(Begin(reopened, first)); Assert.Equal(600, reopened.GetDownloadBudgetUsage("1", now).CommittedBytes);
    }
    [Fact]
    public void BackgroundTransfersSurviveLogoutButRevokedAcquirePermissionHoldsThem()
    {
        using var home = new TestWorkspace(); Owner(home); var member = home.Store.SaveAccount(null, new("Downloader", CanAcquire: true), "isolated"); var login = home.Store.CreateLoginSession(member.Id, 1, "Fixture"); var actor = home.Store.DownloadActor(member.Id, login.Id);
        var item = Add(home.Store, actor); var run = Begin(home.Store, item); var now = DateTimeOffset.UtcNow;
        home.Store.RevokeLoginSession(login.Id, member.Id); Assert.True(home.Store.TouchDownloadBudgetRun(run, now));
        Assert.Equal(401, Assert.Throws<DomainException>(() => Add(home.Store, actor)).StatusCode);
        home.Store.SaveAccount(member.Id, new("Downloader", CanAcquire: false)); Assert.False(home.Store.TouchDownloadBudgetRun(run, now));
        Assert.Equal("budget-waiting", home.Store.GetDownload(item.Id)!.Status); Assert.Equal("account-access", home.Store.GetDownload(item.Id)!.Ownership!.WaitReason);
    }
    [Fact]
    public void OwnershipPreventsForeignDeduplicationAndLegacyAssignmentIsExplicit()
    {
        using var home = new TestWorkspace(); var owner = Owner(home); var member = home.Store.SaveAccount(null, new("Downloader", CanAcquire: true), "isolated"); var login = home.Store.CreateLoginSession(member.Id, 1, "Fixture"); var actor = home.Store.DownloadActor(member.Id, login.Id);
        var original = Add(home.Store, owner); Assert.Equal(404, Assert.Throws<DomainException>(() => Add(home.Store, actor, original.InfoHash)).StatusCode);
        Assert.Empty(home.Store.GetDownloads(viewer: member)); Assert.Equal(0, home.Store.GetDownloadBudgetUsage(member.Id, DateTimeOffset.UtcNow).Acquisitions);
        var legacy = home.Store.AddDownload([Id()], "Historical package", "torrent", "protected", 600); Assert.Null(legacy.Ownership!.UserId);
        var paused = home.Store.SetDownloadIntent(legacy.Id, "paused"); var assigned = home.Store.AssignDownloadAccount(legacy.Id, new(member.Id, paused.Revision), home.Store.GetAccount("1")!);
        Assert.Equal(member.Id, assigned.Ownership!.UserId); Assert.Single(home.Store.GetDownloads(viewer: member));
        Assert.Equal(1, home.Store.GetDownloadBudgetUsage(member.Id, DateTimeOffset.UtcNow).Acquisitions);
        Assert.Equal(409, Assert.Throws<DomainException>(() => home.Store.AssignDownloadAccount(legacy.Id, new("1", assigned.Revision), home.Store.GetAccount("1")!)).StatusCode);
    }
    [Fact]
    public async Task HttpAccountBoundariesCoverDownloadFilesControlsReviewAndBudgetSettings()
    {
        using var home = new TestWorkspace(); await using var app = new Factory(home); using var admin = app.CreateClient(); await Session(admin);
        (await admin.PostAsJsonAsync("/api/setup", new Credentials("Admin", "budget-fixture-password"))).EnsureSuccessStatusCode(); var adminSession = await Session(admin);
        var created = await admin.PostAsJsonAsync("/api/users", new CreateAccount(new("Member", CanAcquire: true), "budget-fixture-password")); created.EnsureSuccessStatusCode(); var member = (await created.Content.ReadFromJsonAsync<Account>())!;
        using var client = app.CreateClient(); await Session(client); (await client.PostAsJsonAsync("/api/auth/login", new Credentials("Member", "budget-fixture-password"))).EnsureSuccessStatusCode(); var memberSession = await Session(client);
        var store = app.Services.GetRequiredService<MediaStore>(); var first = Add(store, store.DownloadActor("1", adminSession.GetProperty("sessionId").GetString())); store.SetDownloadIntent(first.Id, "paused");
        var own = Add(store, store.DownloadActor(member.Id, memberSession.GetProperty("sessionId").GetString())); store.SetDownloadIntent(own.Id, "paused");
        Assert.Equal(own.Id, Assert.Single((await client.GetFromJsonAsync<DownloadRecord[]>("/api/downloads"))!).Id);
        foreach (var path in new[] { $"/api/downloads/{first.Id}", $"/api/downloads/{first.Id}/files", $"/api/downloads/{first.Id}/policy" }) Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync($"/api/downloads/{first.Id}/control", new DownloadCommand("resume"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync($"/api/downloads/{first.Id}/link", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync($"/api/music/downloads/{first.Id}/link", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/users/1/download-usage")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync($"/api/users/{member.Id}/download-budget", new DownloadBudgetPolicy())).StatusCode);
        var usage = (await client.GetFromJsonAsync<DownloadBudgetUsage>("/api/account/download-usage"))!; Assert.Equal(member.Id, usage.UserId); Assert.Equal(1, usage.Acquisitions);
        client.DefaultRequestHeaders.Remove("X-Home-User"); client.DefaultRequestHeaders.Add("X-Home-User", "1");
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/downloads/{own.Id}/control", new DownloadCommand("resume"))).StatusCode);
        await Session(client); client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/downloads/{own.Id}/control", new DownloadCommand("resume"))).StatusCode);
        (await admin.PutAsJsonAsync($"/api/users/{member.Id}/download-budget", new DownloadBudgetPolicy(MonthlyAcquisitions: 1))).EnsureSuccessStatusCode();
        Assert.Equal(2, (await admin.GetFromJsonAsync<DownloadRecord[]>("/api/downloads"))!.Length);
    }
    private static async Task<JsonElement> Session(HttpClient client)
    {
        var session = await client.GetFromJsonAsync<JsonElement>("/api/session");
        foreach (var (header, field) in new[] { ("X-CSRF-TOKEN", "csrfToken"), ("X-Home-User", "userId"), ("X-Home-Session", "sessionId") }) { client.DefaultRequestHeaders.Remove(header); if (session.GetProperty(field).GetString() is { Length: > 0 } value) client.DefaultRequestHeaders.Add(header, value); }
        return session;
    }
    private sealed class Factory(TestWorkspace home) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) { builder.UseEnvironment("Testing"); builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?> { ["Home:DataPath"] = home.Paths.Data, ["Home:Downloads:LoopbackOnly"] = "true", ["Home:FfmpegPath"] = "absent", ["Home:FfprobePath"] = "absent" })); }
    }
}
