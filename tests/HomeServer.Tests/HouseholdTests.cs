using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using HomeServer.Core;
using HomeServer.Infrastructure;
using HomeServer.Notifications;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HomeServer.Tests;

public sealed class HouseholdTests
{
    private const string Password = "Household-test-2026!";
    private static ProviderContent Movie(int id = 42) => new("tmdb", id, "movie", "Fixture " + id, "Fixture " + id, "", "2020-01-01", null, null, "https://www.themoviedb.org/movie/" + id, []);

    [Fact]
    public async Task LibraryAclProtectsCountsFilesArtTracksAndManagementCommands()
    {
        using var home = new TestWorkspace();
        home.Write("Shared.mp4"); var shared = home.AddLibrary(); await home.Scan(shared.Id);
        var secretPath = Path.Combine(home.Root, "private"); Directory.CreateDirectory(secretPath); File.WriteAllBytes(Path.Combine(secretPath, "Secret.mp4"), [1, 2, 3]);
        var hidden = home.Store.AddLibrary(new AddLibrary("Hidden", secretPath, "movies")); await home.Scan(hidden.Id);
        var sharedFile = Assert.Single(home.Store.GetCatalog(null, null, shared.Id, 0, 60).Items);
        var secretFile = Assert.Single(home.Store.GetCatalog(null, null, hidden.Id, 0, 60).Items);
        home.Store.MatchMedia(sharedFile.Id, Movie(), null, ""); home.Store.MatchMedia(secretFile.Id, Movie(), null, "");
        await using var app = new Factory(home); using var admin = app.CreateClient(); await Setup(admin);
        var user = await Create(admin, new AccountInput("Member", AllLibraries: false, LibraryIds: [shared.Id]));
        using var member = app.CreateClient(); await Login(member, "Member");
        var catalog = (await member.GetFromJsonAsync<CatalogPage>("/api/catalog"))!;
        Assert.Equal(1, catalog.Total); Assert.Equal(sharedFile.Id, Assert.Single(catalog.Items).Id);
        Assert.Equal(1, catalog.Items[0].Content!.VersionCount);
        var libraries = await member.GetFromJsonAsync<Library[]>("/api/libraries"); Assert.Equal("", Assert.Single(libraries!).Path);
        Assert.Equal(1, (await member.GetFromJsonAsync<LibraryStats>("/api/stats"))!.Items);
        foreach (var suffix in new[] { "", "/stream", "/thumbnail", "/versions", "/tracks", "/subtitles/embedded-1", "/artwork/poster" })
            Assert.Equal(HttpStatusCode.NotFound, (await member.GetAsync($"/api/media/{secretFile.Id}{suffix}")).StatusCode);
        foreach (var suffix in new[] { "/pcm?fingerprint=fixture&start=0&frames=1", "/favorite", "/similar", "/analysis" })
            Assert.Equal(HttpStatusCode.NotFound, (await member.GetAsync($"/api/music/media/{secretFile.Id}{suffix}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await member.PostAsync($"/api/music/media/{secretFile.Id}/prepare", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await member.PostAsJsonAsync($"/api/offline/media/{secretFile.Id}", new OfflineInput(Guid.NewGuid().ToString("N")))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync("/api/users/1/offline-policy")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync("/api/music/tasks")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PostAsync($"/api/music/media/{sharedFile.Id}/analyze", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await member.GetAsync($"/api/media/{secretFile.Id}/sequence")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PostAsJsonAsync($"/api/content/{sharedFile.Content?.Id ?? home.Store.GetMedia(sharedFile.Id)!.Content!.Id}/identity/merge", new { provider = "tvmaze", kind = "show", providerId = 335 })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PutAsJsonAsync($"/api/media/{sharedFile.Id}/episodes", new { provider = "tvmaze", providerId = 335, episodes = new[] { new { season = 1, episode = 1 } } })).StatusCode);
        foreach (var path in new[] { "/api/subtitles/sync/capabilities", "/api/subtitles/sync/jobs", $"/api/subtitles/media/{sharedFile.Id}/policy", $"/api/subtitles/media/{sharedFile.Id}/versions" })
            Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await member.PutAsJsonAsync($"/api/media/{secretFile.Id}/progress", new PlaybackProgress(5, 10))).StatusCode);
        foreach (var path in new[] { "/api/users", "/api/jobs", "/api/downloads", "/api/indexers", "/api/automation", "/api/automation/jobs", "/api/operations/backups", "/api/usenet/servers", "/api/usenet/tools", "/api/notification-channels", "/api/notification-channels/capabilities", "/api/notification-deliveries?all=true" }) Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync(path)).StatusCode);
        var channel = app.Services.GetRequiredService<NotificationSettings>().Save(null, new("1", "Private admin channel", "webhook", false, ["request"], Url: "https://notify.example.test/hook?key=private-target-key"));
        var delivery = app.Services.GetRequiredService<MediaStore>().QueueNotificationTest(channel.Id, channel.Revision, DateTimeOffset.UtcNow);
        Assert.Empty((await member.GetFromJsonAsync<NotificationDelivery[]>("/api/notification-deliveries"))!);
        Assert.Equal(HttpStatusCode.NotFound, (await member.GetAsync($"/api/notification-deliveries/{delivery.Id}/attempts")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await member.PostAsJsonAsync($"/api/notification-deliveries/{delivery.Id}/cancel", new NotificationDeliveryCommand(delivery.Revision))).StatusCode);
        Assert.DoesNotContain("private-target-key", await admin.GetStringAsync("/api/notification-channels"));
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PostAsJsonAsync("/api/libraries", new AddLibrary("Escape", secretPath, "movies"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PutAsJsonAsync("/api/metadata/settings", new MetadataPreferences("unauthorized-token"))).StatusCode);
        var updated = await admin.PutAsJsonAsync($"/api/users/{user.Id}", new AccountInput("Member", AllLibraries: false)); updated.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await member.GetAsync("/api/catalog")).StatusCode);
        await Login(member, "Member");
        Assert.Empty((await member.GetFromJsonAsync<CatalogPage>("/api/catalog"))!.Items);
    }

    [Fact]
    public async Task PlaybackHistoryIsPrivateAndSharedAcrossOnlyTheUsersVersions()
    {
        using var home = new TestWorkspace(); home.Write("Version1.mp4"); home.Write("Version2.mp4");
        var library = home.AddLibrary(); await home.Scan(library.Id);
        var media = home.Store.GetCatalog(null, null, null, 0, 60).Items;
        foreach (var file in media) home.Store.MatchMedia(file.Id, Movie(), null, "");
        await using var app = new Factory(home); using var admin = app.CreateClient(); await Setup(admin);
        await Create(admin, new AccountInput("One")); await Create(admin, new AccountInput("Two"));
        using var one = app.CreateClient(); using var two = app.CreateClient(); await Login(one, "One"); await Login(two, "Two");
        (await one.PutAsJsonAsync($"/api/media/{media[0].Id}/progress", new { positionSeconds = 45, durationSeconds = 120, userId = "1" })).EnsureSuccessStatusCode();
        (await two.PutAsJsonAsync($"/api/media/{media[1].Id}/progress", new PlaybackProgress(15, 120))).EnsureSuccessStatusCode();
        Assert.Equal(45, (await one.GetFromJsonAsync<MediaItem>($"/api/media/{media[1].Id}"))!.PositionSeconds);
        Assert.Equal(15, (await two.GetFromJsonAsync<MediaItem>($"/api/media/{media[0].Id}"))!.PositionSeconds);
        Assert.Equal(0, (await admin.GetFromJsonAsync<MediaItem>($"/api/media/{media[0].Id}"))!.PositionSeconds);
        Assert.Single((await one.GetFromJsonAsync<MediaItem[]>("/api/continue"))!);
        Assert.Empty((await admin.GetFromJsonAsync<MediaItem[]>("/api/continue"))!);
    }

    [Fact]
    public async Task PushHttpCommandsBindToTheAuthenticatedUserAndRequireCsrf()
    {
        using var home = new TestWorkspace(); await using var app = new Factory(home); using var admin = app.CreateClient(); await Setup(admin);
        var user = await Create(admin, new AccountInput("Push member")); var otherUser = await Create(admin, new AccountInput("Other push member"));
        using var member = app.CreateClient(); using var other = app.CreateClient(); await Login(member, "Push member"); await Login(other, "Other push member");
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync("/api/push/configuration")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PutAsJsonAsync("/api/push/configuration", new PushServerInput(true, "mailto:member@example.test", 0))).StatusCode);
        var updated = await admin.PutAsJsonAsync("/api/push/configuration", new PushServerInput(true, "mailto:operator@example.test", 0)); updated.EnsureSuccessStatusCode();
        var config = (await updated.Content.ReadFromJsonAsync<PushServerConfiguration>())!;
        var session = await member.GetFromJsonAsync<JsonElement>("/api/session");
        using var key = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256); var parameters = key.ExportParameters(false);
        var input = new BrowserPushInput("https://fcm.googleapis.com/fcm/send/private-subscription", new(WebEncoders.Base64UrlEncode(new byte[] { 4 }.Concat(parameters.Q.X!).Concat(parameters.Q.Y!).ToArray()), WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(16))),
            config.PublicKey!, config.KeyRevision, user.Id, session.GetProperty("sessionId").GetString()!, "Isolated member browser", ["request"]);
        member.DefaultRequestHeaders.Remove("X-CSRF-TOKEN"); Assert.Equal(HttpStatusCode.BadRequest, (await member.PostAsJsonAsync("/api/push/device", input)).StatusCode); await Csrf(member);
        Assert.Equal(HttpStatusCode.Conflict, (await member.PostAsJsonAsync("/api/push/device", input with { ExpectedUserId = otherUser.Id })).StatusCode);
        var response = await member.PostAsJsonAsync("/api/push/device", input); response.EnsureSuccessStatusCode(); var subscribed = (await response.Content.ReadFromJsonAsync<BrowserPushRegistration>())!;
        var own = await member.GetStringAsync("/api/push/device"); Assert.Contains(subscribed.BindingId, own); Assert.DoesNotContain("private-subscription", own); Assert.DoesNotContain(input.Keys.Auth, own);
        var hidden = await other.GetFromJsonAsync<JsonElement>("/api/push/device"); Assert.Equal(JsonValueKind.Null, hidden.GetProperty("current").ValueKind); Assert.Equal(0, hidden.GetProperty("devices").GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/api/push/devices/{subscribed.ChannelId}?revision={subscribed.Revision}")).StatusCode);
        (await member.PostAsync("/api/auth/logout", null)).EnsureSuccessStatusCode(); Assert.Null(app.Services.GetRequiredService<MediaStore>().GetBrowserPushBinding(subscribed.ChannelId));
    }

    [Fact]
    public async Task RequestApprovalQuotaAndNotificationsAreScopedToTheRequester()
    {
        using var home = new TestWorkspace(); await using var app = new Factory(home); using var admin = app.CreateClient(); await Setup(admin);
        var user = await Create(admin, new AccountInput("Requester", MonthlyRequestLimit: 1));
        using var member = app.CreateClient(); await Login(member, "Requester");
        var response = await member.PostAsJsonAsync("/api/content/requests", new ContentRequestInput("tmdb", "movie", 42)); response.EnsureSuccessStatusCode();
        var content = (await response.Content.ReadFromJsonAsync<ContentEntry>())!;
        Assert.Equal("pending", content.RequestStatus);
        (await member.PostAsJsonAsync("/api/content/requests", new ContentRequestInput("tmdb", "movie", 42))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.TooManyRequests, (await member.PostAsJsonAsync("/api/content/requests", new ContentRequestInput("tmdb", "movie", 43))).StatusCode);
        var request = Assert.Single((await member.GetFromJsonAsync<RequestEntry[]>("/api/requests"))!);
        Assert.Equal(user.Id, request.RequestedBy);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PostAsJsonAsync($"/api/requests/{request.Id}/decision", new RequestDecision("approved"))).StatusCode);
        (await admin.PostAsJsonAsync($"/api/requests/{request.Id}/decision", new RequestDecision("approved", "Welcome"))).EnsureSuccessStatusCode();
        Assert.Equal("approved", (await member.GetFromJsonAsync<ContentEntry>($"/api/content/{content.Id}"))!.RequestStatus);
        Assert.Equal(2, (await member.GetFromJsonAsync<RequestEvent[]>($"/api/requests/{request.Id}/history"))!.Length);
        Assert.NotEmpty((await member.GetFromJsonAsync<NotificationItem[]>("/api/notifications"))!);
        await Create(admin, new AccountInput("Other")); using var other = app.CreateClient(); await Login(other, "Other");
        Assert.Empty((await other.GetFromJsonAsync<RequestEntry[]>("/api/requests"))!);
        Assert.Empty((await other.GetFromJsonAsync<NotificationItem[]>("/api/notifications"))!);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/requests/{request.Id}/history")).StatusCode);
        Assert.Empty(home.Store.GetDownloads());
    }

    [Fact]
    public void ScopedRequestsAndAvailabilityDoNotCrossUserBoundaries()
    {
        using var home = new TestWorkspace();
        home.Store.CreateOwner(new Owner("Root", "fixture-hash"));
        var one = home.Store.SaveAccount(null, new AccountInput("Season one", AutoApprove: true), "fixture-hash");
        var two = home.Store.SaveAccount(null, new AccountInput("Season two", AutoApprove: true), "fixture-hash");
        var show = Movie() with { Kind = "show" };
        var first = home.Store.RequestContent(show, "all", "balanced", one, 1);
        home.Store.RequestContent(show, "all", "balanced", two, 2);
        var sync = home.Store.ClaimTrackingSync()!.Value;
        home.Store.CompleteTrackingSync(first.Id, sync.Revision, show, [new(11, 1, 1, "One", "", "2020-01-01", null), new(21, 2, 1, "Two", "", "2020-01-01", null)]);
        Assert.Equal(1, home.Store.GetContent(first.Id, one)!.MissingCount);
        Assert.Equal(1, home.Store.GetContent(first.Id, two)!.MissingCount);
        home.Store.CancelContentRequest(first.Id, one);
        Assert.Empty(home.Store.GetTracked(one));
        Assert.Single(home.Store.GetTracked(two));
        Assert.Equal("all", home.Store.GetContent(first.Id)!.MonitorMode);
    }

    [Fact]
    public async Task ChangingPasswordAndRevokingASessionRejectOldCookies()
    {
        using var home = new TestWorkspace(); await using var app = new Factory(home);
        using var first = app.CreateClient(); using var second = app.CreateClient(); await Setup(first); await Login(second, "Admin");
        Assert.Equal(HttpStatusCode.Forbidden, (await first.PutAsJsonAsync("/api/account/password", new PasswordChange("wrong", "Changed-password-2026!"))).StatusCode);
        (await first.PutAsJsonAsync("/api/account/password", new PasswordChange(Password, "Changed-password-2026!"))).EnsureSuccessStatusCode();
        await Csrf(first);
        Assert.Equal(HttpStatusCode.Unauthorized, (await second.GetAsync("/api/catalog")).StatusCode);
        var sessions = await first.GetFromJsonAsync<LoginSession[]>("/api/account/sessions");
        var current = Assert.Single(sessions!); Assert.True(current.Current);
        (await first.DeleteAsync($"/api/account/sessions/{current.Id}")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await first.GetAsync("/api/catalog")).StatusCode);
        await Csrf(second);
        Assert.Equal(HttpStatusCode.Unauthorized, (await second.PostAsJsonAsync("/api/auth/login", new Credentials("Admin", Password))).StatusCode);
        (await second.PostAsJsonAsync("/api/auth/login", new Credentials("Admin", "Changed-password-2026!"))).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task InvitationIsSingleUseAndANameConflictDoesNotConsumeIt()
    {
        using var home = new TestWorkspace(); await using var app = new Factory(home); using var admin = app.CreateClient(); await Setup(admin);
        var inviteResponse = await admin.PostAsJsonAsync("/api/users/invitations", new AccountInput("", Role: "viewer", CanDiscover: false, AllLibraries: false));
        inviteResponse.EnsureSuccessStatusCode(); var invite = (await inviteResponse.Content.ReadFromJsonAsync<Invitation>())!;
        using var guest = app.CreateClient(); await Csrf(guest);
        Assert.Equal(HttpStatusCode.Conflict, (await guest.PostAsJsonAsync("/api/invitations/accept", new AcceptInvitation(invite.Token, "Admin", Password))).StatusCode);
        (await guest.PostAsJsonAsync("/api/invitations/accept", new AcceptInvitation(invite.Token, "Guest", Password))).EnsureSuccessStatusCode();
        await Csrf(guest);
        Assert.Equal(HttpStatusCode.Forbidden, (await guest.GetAsync("/api/discovery?provider=tmdb&kind=movie")).StatusCode);
        Assert.Equal("viewer", (await guest.GetFromJsonAsync<Account>("/api/account"))!.Role);
        using var replay = app.CreateClient(); await Csrf(replay);
        Assert.Equal(HttpStatusCode.NotFound, (await replay.PostAsJsonAsync("/api/invitations/accept", new AcceptInvitation(invite.Token, "Replay", Password))).StatusCode);
        Assert.Equal(2, (await admin.GetFromJsonAsync<Account[]>("/api/users"))!.Length);
    }

    private static async Task<Account> Create(HttpClient client, AccountInput input)
    {
        var response = await client.PostAsJsonAsync("/api/users", new CreateAccount(input, Password)); response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<Account>())!;
    }
    private static async Task Setup(HttpClient client)
    {
        await Csrf(client); (await client.PostAsJsonAsync("/api/setup", new Credentials("Admin", Password))).EnsureSuccessStatusCode(); await Csrf(client);
    }
    private static async Task Login(HttpClient client, string name)
    {
        await Csrf(client); (await client.PostAsJsonAsync("/api/auth/login", new Credentials(name, Password))).EnsureSuccessStatusCode(); await Csrf(client);
    }
    private static async Task Csrf(HttpClient client)
    {
        var state = await client.GetFromJsonAsync<JsonElement>("/api/session"); client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", state.GetProperty("csrfToken").GetString());
    }
    private sealed class Factory(TestWorkspace home) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Home:DataPath"] = home.Paths.Data, ["Home:FfmpegPath"] = "absent", ["Home:FfprobePath"] = "absent", ["Home:Downloads:LoopbackOnly"] = "true", ["Home:Tmdb:Token"] = "fixture-token-not-live"
            }));
            builder.ConfigureTestServices(services =>
            {
                services.AddHttpClient("tmdb").ConfigurePrimaryHttpMessageHandler(() => new ProviderFixture());
                services.AddHttpClient("notifications").ConfigurePrimaryHttpMessageHandler(() => new NotificationFixture());
                services.AddHttpClient("web-push").ConfigurePrimaryHttpMessageHandler(() => new NotificationFixture());
            });
        }
    }
    private sealed class ProviderFixture : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var id = int.Parse(request.RequestUri!.AbsolutePath.Split('/').Last());
            var json = JsonSerializer.Serialize(new { id, title = "Fixture " + id, original_title = "Fixture " + id, release_date = "2020-01-01", genres = Array.Empty<string>() });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }
    }
    private sealed class NotificationFixture : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created));
    }
}
