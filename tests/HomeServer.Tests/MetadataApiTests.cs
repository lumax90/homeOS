using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HomeServer.Core;
using HomeServer.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HomeServer.Tests;

public sealed class MetadataApiTests
{
    [Fact]
    public async Task GenreDiscoveryRequiresLoginAndReturnsTheSelectedProviderGenre()
    {
        using var home = new TestWorkspace(); await using var factory = new MetadataFactory(home); using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/discovery/genres?provider=tmdb&kind=movie")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/discovery/personal?kind=movie&shelf=watchlist")).StatusCode);
        await SignIn(client); (await client.PutAsJsonAsync("/api/metadata/settings", new MetadataPreferences("private-tmdb-test-token-123456789"))).EnsureSuccessStatusCode();
        var genres = (await client.GetFromJsonAsync<DiscoveryGenre[]>("/api/discovery/genres?provider=tmdb&kind=movie"))!; Assert.Contains(genres, genre => genre.Id == 35 && genre.Name == "Komedi");
        var english = (await client.GetFromJsonAsync<DiscoveryGenre[]>("/api/discovery/genres?provider=tmdb&kind=movie&language=en-US"))!;
        Assert.Contains(english, genre => genre.Id == 28 && genre.Name == "Action");
        Assert.Equal("tr-TR", (await client.GetFromJsonAsync<MetadataStatus>("/api/metadata/settings"))!.Language);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/discovery/genres?provider=tmdb&kind=movie&language=invalid")).StatusCode);
        var result = (await client.GetFromJsonAsync<DiscoveryPage>("/api/discovery?provider=tmdb&kind=movie&collection=popular&genre=18"))!;
        Assert.Equal(42, Assert.Single(result.Items).ProviderId); Assert.Equal("Dram", Assert.Single(result.Items[0].Genres));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/discovery?provider=tmdb&kind=movie&genre=99999")).StatusCode);
        Assert.Empty(await client.GetFromJsonAsync<DownloadRecord[]>("/api/downloads") ?? []);
    }
    [Fact]
    public async Task RealCatalogContractSupportsFileIndependentRequestsWithoutTransfers()
    {
        using var home = new TestWorkspace();
        await using var factory = new MetadataFactory(home);
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/discovery?provider=tmdb&kind=movie")).StatusCode);
        await SignIn(client);
        (await client.PutAsJsonAsync("/api/metadata/settings", new MetadataPreferences("private-tmdb-test-token-123456789"))).EnsureSuccessStatusCode();
        var discovery = await client.GetFromJsonAsync<DiscoveryPage>("/api/discovery?provider=tmdb&kind=movie&collection=trending&page=2");
        Assert.Equal(2, discovery!.Page);
        Assert.True(discovery.HasMore);
        Assert.Equal(42, Assert.Single(discovery.Items).ProviderId);
        var response = await client.PostAsJsonAsync("/api/content/requests", new ContentRequestInput("tmdb", "movie", 42));
        response.EnsureSuccessStatusCode();
        var entry = (await response.Content.ReadFromJsonAsync<ContentEntry>())!;
        Assert.Equal(0, entry.FileCount);
        Assert.True(entry.Requested);
        var saved = await client.GetFromJsonAsync<ContentEntry[]>("/api/content");
        Assert.Equal(entry.Id, Assert.Single(saved!).Id);
        var personal = (await client.GetFromJsonAsync<DiscoveryPersonalPage>("/api/discovery/personal?kind=movie&shelf=watchlist&limit=1"))!;
        Assert.Equal(1, personal.Total); Assert.Equal(entry.Id, Assert.Single(personal.Items).Id);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/discovery/personal?kind=movie&shelf=invalid")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/discovery/personal?kind=movie&shelf=watchlist&limit=1000")).StatusCode);
        Assert.Empty(await client.GetFromJsonAsync<DownloadRecord[]>("/api/downloads") ?? []);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/calendar?from=invalid")).StatusCode);
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/content/requests", new ContentRequestInput("tmdb", "movie", 42))).StatusCode);
    }
    [Fact]
    public async Task TmdbUsesServerSideBearerTokenAndReturnsNormalizedMovieIdentity()
    {
        using var home = new TestWorkspace();
        home.Write("A.Film.mp4"); var library = home.AddLibrary(); await home.Scan(library.Id);
        var media = Assert.Single(home.Store.GetCatalog(null, null, null, 0, 60).Items);
        await using var factory = new MetadataFactory(home);
        using var client = factory.CreateClient(); await SignIn(client);
        const string secret = "private-tmdb-test-token-123456789";
        (await client.PutAsJsonAsync("/api/metadata/settings", new MetadataPreferences(secret))).EnsureSuccessStatusCode();
        var results = await client.GetFromJsonAsync<ProviderContent[]>("/api/metadata/search?provider=tmdb&kind=movie&q=Film");
        Assert.Equal(42, Assert.Single(results!).ProviderId);
        var response = await client.PutAsJsonAsync($"/api/media/{media.Id}/match", new MatchRequest("tmdb", "movie", 42));
        response.EnsureSuccessStatusCode();
        var item = (await response.Content.ReadFromJsonAsync<MediaItem>())!;
        Assert.Equal("Bir Film", item.Title);
        Assert.Equal("https://image.tmdb.org/t/p/w500/poster.jpg", item.Content!.PosterUrl);
        Assert.DoesNotContain(secret, await client.GetStringAsync("/api/metadata/settings"));
        (await client.PutAsJsonAsync("/api/metadata/settings", new MetadataPreferences(null, true))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await client.GetAsync("/api/metadata/search?provider=tmdb&kind=movie&q=Film")).StatusCode);
    }

    [Fact]
    public async Task TvmazeMatchUsesAuthoritativeEpisodeAndFailurePreservesTheLink()
    {
        using var home = new TestWorkspace();
        home.Write("Sherlock.S01E01.mp4");
        var library = home.AddLibrary(); await home.Scan(library.Id);
        var media = Assert.Single(home.Store.GetCatalog(null, null, null, 0, 60).Items);
        await using var factory = new MetadataFactory(home);
        using var client = factory.CreateClient();
        await SignIn(client);
        var results = await client.GetFromJsonAsync<ProviderContent[]>("/api/metadata/search?provider=tvmaze&kind=show&q=Sherlock");
        Assert.Equal("Sherlock", Assert.Single(results!).Title);
        var response = await client.PutAsJsonAsync($"/api/media/{media.Id}/match", new MatchRequest("tvmaze", "show", 335, 1, 1));
        response.EnsureSuccessStatusCode();
        var matched = (await response.Content.ReadFromJsonAsync<MediaItem>())!;
        Assert.Equal("Sherlock · S01E01", matched.Title);
        Assert.Equal("A Study in Pink", matched.Content!.EpisodeTitle);
        Assert.Equal("Plain summary", matched.Content.Overview);
        Assert.Null(matched.Content.PosterUrl); // The fixture supplied a forbidden origin.
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync($"/api/media/{media.Id}/match", new MatchRequest("tvmaze", "show", 335, 1, 99))).StatusCode);
        var preserved = await client.GetFromJsonAsync<MediaItem>($"/api/media/{media.Id}");
        Assert.Equal(matched.Content.Id, preserved!.Content!.Id);
        Assert.Equal(1, preserved.Content.EpisodeNumber);
        Assert.Equal(HttpStatusCode.Conflict, (await client.GetAsync("/api/metadata/search?provider=tmdb&kind=movie&q=Film")).StatusCode);
    }

    private static async Task SignIn(HttpClient client)
    {
        var session = await client.GetFromJsonAsync<JsonElement>("/api/session");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", session.GetProperty("csrfToken").GetString());
        (await client.PostAsJsonAsync("/api/setup", new Credentials("Test", "private-test-password"))).EnsureSuccessStatusCode();
        session = await client.GetFromJsonAsync<JsonElement>("/api/session");
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", session.GetProperty("csrfToken").GetString());
    }

    private sealed class MetadataFactory(TestWorkspace home) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Home:DataPath"] = home.Paths.Data, ["Home:Downloads:LoopbackOnly"] = "true",
                ["Home:Tmdb:Token"] = "", ["Home:FfmpegPath"] = "absent", ["Home:FfprobePath"] = "absent"
            }));
            builder.ConfigureTestServices(services =>
            {
                services.AddHttpClient("tvmaze").ConfigurePrimaryHttpMessageHandler(() => new TvmazeFixture());
                services.AddHttpClient("tmdb").ConfigurePrimaryHttpMessageHandler(() => new TmdbFixture());
            });
        }
    }

    private sealed class TmdbFixture : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("api.themoviedb.org", request.RequestUri!.Host);
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            Assert.Equal("private-tmdb-test-token-123456789", request.Headers.Authorization.Parameter);
            const string movie = """{"id":42,"title":"Bir Film","original_title":"A Film","overview":"Özet","release_date":"2020-01-01","poster_path":"/poster.jpg","genre_ids":[18],"genres":[{"name":"Drama"}]}""";
            var json = request.RequestUri.AbsolutePath.Contains("/genre/") ? request.RequestUri.Query.Contains("language=en-US") ? """{"genres":[{"id":28,"name":"Action"},{"id":35,"name":"Comedy"},{"id":18,"name":"Drama"}]}""" : """{"genres":[{"id":28,"name":"Aksiyon"},{"id":35,"name":"Komedi"},{"id":18,"name":"Dram"}]}"""
                : request.RequestUri.AbsolutePath.Contains("/search/") || request.RequestUri.AbsolutePath.Contains("/trending/") || request.RequestUri.AbsolutePath.Contains("/discover/")
                    ? "{\"page\":2,\"total_pages\":3,\"results\":[" + movie + "]}" : movie;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") });
        }
    }

    private sealed class TvmazeFixture : HttpMessageHandler
    {
        private const string Show = """{"id":335,"name":"Sherlock","summary":"<p>Plain summary</p>","premiered":"2010-07-25","genres":["Drama"],"image":{"original":"http://127.0.0.1/private.jpg"}}""";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("api.tvmaze.com", request.RequestUri!.Host);
            string? response = request.RequestUri.AbsolutePath switch
            {
                "/search/shows" => "[{\"show\":" + Show + "}]",
                "/shows/335" => Show,
                "/shows/335/episodebynumber" when request.RequestUri.Query.Contains("number=1") && !request.RequestUri.Query.Contains("number=99")
                    => """{"id":1001,"name":"A Study in Pink","season":1,"number":1,"summary":"<p>Episode overview</p>","airdate":"2010-07-25","image":null}""",
                _ => null
            };
            return Task.FromResult(new HttpResponseMessage(response is null ? HttpStatusCode.NotFound : HttpStatusCode.OK)
                { Content = new StringContent(response ?? "{}", System.Text.Encoding.UTF8, "application/json") });
        }
    }
}
