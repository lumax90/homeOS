using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using HomeServer.Core;
using HomeServer.Infrastructure;
using HomeServer.Metadata;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HomeServer.Tests;

public sealed class PersonalCatalogTests
{
    private const string Password = "family-verification-2026!";
    private static ProviderContent Movie(int id, int? age) => new("tmdb", id, "movie", "Film " + id, "Film " + id, "", "2020-01-01", null, null, "https://www.themoviedb.org/movie/" + id, [], MinimumAge: age);

    [Fact]
    public async Task ParentalAccessAppliesToCountsVersionsFilesMetadataAndOldPlayerEvents()
    {
        using var home = new TestWorkspace(); home.Write("Family.mp4"); home.Write("OtherEdition.mp4"); home.Write("Adult.mp4"); home.Write("Unknown.mp3");
        var library = home.AddLibrary(); await home.Scan(library.Id);
        var files = home.Store.GetCatalog(null, null, null, 0, 60).Items;
        var family = files.Single(file => file.RelativePath == "Family.mp4"); var edition = files.Single(file => file.RelativePath == "OtherEdition.mp4"); var adult = files.Single(file => file.RelativePath == "Adult.mp4");
        family = home.Store.MatchMedia(family.Id, Movie(1, 0), null, ""); home.Store.MatchMedia(edition.Id, Movie(1, 0), null, ""); home.Store.MatchMedia(adult.Id, Movie(2, 18), null, "");
        home.Store.SetMediaAccess(edition.Id, new MediaAccessInput(18));
        await using var app = new Factory(home); using var owner = app.CreateClient(); await Setup(owner);
        var child = await Create(owner, new AccountInput("Child", MaximumAge: 12, AllowUnrated: false)); using var client = app.CreateClient(); await Login(client, "Child");
        var catalog = (await client.GetFromJsonAsync<CatalogPage>("/api/catalog"))!;
        Assert.Equal(1, catalog.Total); Assert.Equal(1, Assert.Single(catalog.Items).Content!.VersionCount);
        Assert.Equal(1, (await client.GetFromJsonAsync<LibraryStats>("/api/stats"))!.Items);
        Assert.Equal(1, Assert.Single((await client.GetFromJsonAsync<Library[]>("/api/libraries"))!).ItemCount);
        foreach (var suffix in new[] { "", "/stream", "/thumbnail", "/versions", "/tracks", "/subtitles/embedded-1", "/artwork/poster" })
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/media/{adult.Id}{suffix}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/discovery/tmdb/movie/2")).StatusCode);
        var discovered = await client.GetFromJsonAsync<ProviderContent[]>("/api/metadata/search?provider=tmdb&kind=movie&q=fixture"); Assert.Equal(1, Assert.Single(discovered!).ProviderId);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"/api/media/{family.Id}/progress", new PlaybackProgress(50, 100, ExpectedUserId: "1"))).StatusCode);
        Assert.Equal(0, home.Store.GetMedia(family.Id, home.Store.GetAccount(child.Id))!.PositionSeconds);
        (await client.PutAsJsonAsync($"/api/content/{family.Content!.Id}/personal", new PersonalContent(true, 8))).EnsureSuccessStatusCode();
        (await owner.PutAsJsonAsync($"/api/content/{family.Content.Id}/presentation", new ContentPresentation(Blocked: true))).EnsureSuccessStatusCode();
        Assert.Empty((await client.GetFromJsonAsync<ContentEntry[]>("/api/favorites"))!);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/media/{family.Id}/stream")).StatusCode);
    }

    [Fact]
    public void LocalPresentationSurvivesProviderRefreshAndSharedCollectionsFilterEachViewer()
    {
        using var home = new TestWorkspace(); home.Store.CreateOwner(new Owner("Owner", "fixture-hash"));
        var owner = home.Store.GetAccount("1")!; var child = home.Store.SaveAccount(null, new AccountInput("Child", MaximumAge: 12, AllowUnrated: false), "fixture-hash");
        var family = home.Store.RequestContent(Movie(1, 0), "all", "balanced"); var adult = home.Store.SaveContent(Movie(2, 18), owner);
        home.Store.SetContentPresentation(family.Id, new ContentPresentation("Yerel başlık", "Yerel açıklama", 7, Tags: ["aile"]));
        var sync = home.Store.ClaimTrackingSync()!.Value;
        home.Store.CompleteTrackingSync(family.Id, sync.Revision, Movie(1, 0) with { Title = "Provider changed", Overview = "Provider changed" }, []);
        Assert.Equal("Yerel başlık", home.Store.GetContent(family.Id)!.Metadata.Title);
        Assert.Equal("Yerel açıklama", home.Store.GetContent(family.Id)!.Metadata.Overview);
        Assert.Equal(7, home.Store.GetContent(family.Id)!.Metadata.MinimumAge);
        var shared = home.Store.SaveCollection(null, new CollectionInput("Hanenin seçkisi", true), owner);
        home.Store.ChangeCollectionItem(shared.Id, family.Id, owner, false); home.Store.ChangeCollectionItem(shared.Id, adult.Id, owner, false);
        Assert.Equal(1, Assert.Single(home.Store.GetCollections(child)).Count); Assert.Equal(family.Id, Assert.Single(home.Store.GetCollectionItems(shared.Id, child)).Id);
        var own = home.Store.SaveCollection(null, new CollectionInput("Özel"), owner); Assert.DoesNotContain(home.Store.GetCollections(child), value => value.Id == own.Id);
        Assert.Throws<DomainException>(() => home.Store.ChangeCollectionItem(shared.Id, family.Id, child, true));
        home.Store.SetPersonalContent(family.Id, owner, new PersonalContent(true, 9));
        Assert.Empty(home.Store.GetFavorites(child)); Assert.Null(home.Store.GetPersonalContent(family.Id, child).Rating);
        Assert.Throws<DomainException>(() => home.Store.RequestContent(Movie(3, 18), "all", "balanced", child));
    }

    [Fact]
    public async Task PinSwitchRequiresATrustedDeviceAndRevocationInvalidatesItsSessions()
    {
        using var home = new TestWorkspace(); await using var app = new Factory(home); using var owner = app.CreateClient(); await Setup(owner);
        var child = await Create(owner, new AccountInput("Child", Role: "viewer"));
        (await owner.PutAsJsonAsync($"/api/users/{child.Id}/pin", new { pin = "123456" })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.PostAsJsonAsync("/api/profiles/switch", new ProfileSwitchInput(child.Id, "123456"))).StatusCode);
        (await owner.PostAsJsonAsync("/api/profiles/device", new DeviceProfileInput([child.Id]))).EnsureSuccessStatusCode();
        using var foreign = app.CreateClient(); await Csrf(foreign);
        Assert.False((await foreign.GetFromJsonAsync<JsonElement>("/api/profiles")).GetProperty("enabled").GetBoolean());
        Assert.Equal(HttpStatusCode.Forbidden, (await foreign.PostAsJsonAsync("/api/profiles/switch", new ProfileSwitchInput(child.Id, "123456"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.PostAsJsonAsync("/api/profiles/switch", new ProfileSwitchInput(child.Id, "999999"))).StatusCode);
        (await owner.PostAsJsonAsync("/api/profiles/switch", new ProfileSwitchInput(child.Id, "123456"))).EnsureSuccessStatusCode(); await Csrf(owner);
        Assert.Equal(child.Id, (await owner.GetFromJsonAsync<Account>("/api/account"))!.Id);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync("/api/users")).StatusCode);
        (await owner.DeleteAsync("/api/profiles/device")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await owner.GetAsync("/api/catalog")).StatusCode);
    }

    [Fact]
    public async Task PinAttemptBudgetIsDurableAndAppliesBeforeCorrectPinUse()
    {
        using var home = new TestWorkspace(); await using var app = new Factory(home); using var client = app.CreateClient(); await Setup(client);
        var child = await Create(client, new AccountInput("Child", Role: "viewer"));
        (await client.PutAsJsonAsync($"/api/users/{child.Id}/pin", new { pin = "123456" })).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync("/api/profiles/device", new DeviceProfileInput([child.Id]))).EnsureSuccessStatusCode();
        for (var attempt = 0; attempt < 5; attempt++) Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/profiles/switch", new ProfileSwitchInput(child.Id, "000000"))).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.PostAsJsonAsync("/api/profiles/switch", new ProfileSwitchInput(child.Id, "123456"))).StatusCode);
        Assert.Equal("1", (await client.GetFromJsonAsync<Account>("/api/account"))!.Id);
    }

    [Fact]
    public void DeviceGrantAndItsSessionsFollowAccountRevisions()
    {
        using var home = new TestWorkspace(); home.Store.CreateOwner(new Owner("Owner", "fixture-hash"));
        var owner = home.Store.GetAccount("1")!; var child = home.Store.SaveAccount(null, new AccountInput("Child", Role: "viewer"), "fixture-hash");
        var pinned = home.Store.SetAccountPin(child.Id, "fixture-pin-hash", home.Store.GetAccountSecret(child.Id)!.Version);
        var device = home.Store.CreateHouseholdDevice(owner, [child.Id]);
        var session = home.Store.CreateLoginSession(child.Id, pinned.Version, "fixture", device.Device.Id);
        Assert.NotNull(home.Store.ValidateLoginSession(session.Id, child.Id));
        home.Store.SetAccountPin(child.Id, "new-pin-hash", pinned.Version);
        Assert.Null(home.Store.ValidateLoginSession(session.Id, child.Id));
        Assert.Throws<DomainException>(() => home.Store.CreateLoginSession(child.Id, home.Store.GetAccountSecret(child.Id)!.Version, "fixture", device.Device.Id));
        home.Store.DeleteHouseholdDevice(device.Device.Id);
        Assert.Null(home.Store.GetHouseholdDevice(device.Token));
    }

    [Fact]
    public void UnknownRatingsAreNotConvertedToAnInventedAge()
    {
        using var document = JsonDocument.Parse("""{"content_ratings":{"results":[{"iso_3166_1":"TR","rating":"Unknown"},{"iso_3166_1":"US","rating":"TV-MA"}]}}""");
        var classification = AgeRatings.Read(document.RootElement); Assert.Equal(17, classification.Age); Assert.Equal("US: TV-MA", classification.Label);
        Assert.Null(AgeRatings.Normalize("US", "PG")); Assert.Equal(13, AgeRatings.Normalize("US", "PG-13"));
    }

    private static async Task<Account> Create(HttpClient client, AccountInput input)
    {
        var response = await client.PostAsJsonAsync("/api/users", new CreateAccount(input, Password)); response.EnsureSuccessStatusCode(); return (await response.Content.ReadFromJsonAsync<Account>())!;
    }
    private static async Task Setup(HttpClient client) { await Csrf(client); (await client.PostAsJsonAsync("/api/setup", new Credentials("Owner", Password))).EnsureSuccessStatusCode(); await Csrf(client); }
    private static async Task Login(HttpClient client, string name) { await Csrf(client); (await client.PostAsJsonAsync("/api/auth/login", new Credentials(name, Password))).EnsureSuccessStatusCode(); await Csrf(client); }
    private static async Task Csrf(HttpClient client) { var session = await client.GetFromJsonAsync<JsonElement>("/api/session"); client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN"); client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", session.GetProperty("csrfToken").GetString()); }
    private sealed class Factory(TestWorkspace home) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?> { ["Home:DataPath"] = home.Paths.Data, ["Home:Downloads:LoopbackOnly"] = "true", ["Home:FfmpegPath"] = "absent", ["Home:FfprobePath"] = "absent", ["Home:Tmdb:Token"] = "fixture" }));
            builder.ConfigureTestServices(services => services.AddHttpClient("tmdb").ConfigurePrimaryHttpMessageHandler(() => new Provider()));
        }
    }
    private sealed class Provider : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            object Item(int id, bool detail) => new { id, title = "Film " + id, original_title = "Film " + id, adult = id == 2,
                release_dates = new { results = detail ? new[] { new { iso_3166_1 = "US", release_dates = new[] { new { certification = id == 1 ? "G" : "NC-17" } } } } : [] } };
            var json = request.RequestUri!.AbsolutePath.Contains("search") ? JsonSerializer.Serialize(new { results = new[] { Item(1, false), Item(2, false) } })
                : JsonSerializer.Serialize(Item(int.Parse(request.RequestUri.AbsolutePath.Split('/').Last()), true));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }
    }
}
