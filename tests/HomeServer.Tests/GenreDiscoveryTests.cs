using System.Net;
using System.Text.Json;
using HomeServer.Core;
using HomeServer.Metadata;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;

namespace HomeServer.Tests;

public sealed class GenreDiscoveryTests
{
    [Fact]
    public async Task OfficialGenresKeepMovieAndTvIdentitiesLanguageAndCredentialBoundaries()
    {
        using var home = new TestWorkspace(); using var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 }); var handler = new Handler();
        var settings = Settings(home); var provider = new MetadataProvider(new Factory(handler), settings, cache);
        var movies = await provider.GenresAsync("tmdb", "movie", CancellationToken.None); var shows = await provider.GenresAsync("tmdb", "show", CancellationToken.None);
        Assert.Contains(movies, genre => genre.Id == 28 && genre.Name == "Aksiyon"); Assert.DoesNotContain(shows, genre => genre.Id == 28); Assert.Contains(shows, genre => genre.Id == 10759 && genre.Name == "Aksiyon ve Macera");
        await provider.GenresAsync("tmdb", "movie", CancellationToken.None); Assert.Equal(2, handler.Requests.Count);
        var uiEnglish = await provider.GenresAsync("tmdb", "movie", CancellationToken.None, "en-US");
        Assert.Equal("tr-TR", settings.Language); Assert.Contains(uiEnglish, genre => genre.Id == 28 && genre.Name == "Action");
        var englishLabels = await provider.DiscoverAsync("tmdb", "movie", "popular", null, 1, CancellationToken.None, 35, "en-US");
        Assert.Equal("Comedy", Assert.Single(Assert.Single(englishLabels.Items).Genres)); Assert.Equal("tr-TR", settings.Language);
        Assert.Equal(400, (await Assert.ThrowsAsync<DomainException>(() => provider.GenresAsync("tmdb", "movie", CancellationToken.None, "fr-FR"))).StatusCode);
        settings.Save(new(null, Language: "en-US")); var english = await provider.GenresAsync("tmdb", "movie", CancellationToken.None); Assert.Contains(english, genre => genre.Id == 35 && genre.Name == "Comedy");
        Assert.All(handler.Requests.Where(address => address.AbsolutePath.Contains("/genre/")), address => Assert.StartsWith("/3/genre/", address.AbsolutePath));
        Assert.Equal(400, (await Assert.ThrowsAsync<DomainException>(() => provider.DiscoverAsync("tmdb", "show", "popular", null, 1, CancellationToken.None, 28))).StatusCode);
        Assert.Equal(400, (await Assert.ThrowsAsync<DomainException>(() => provider.DiscoverAsync("tvmaze", "show", "popular", null, 1, CancellationToken.None, 35))).StatusCode);
        settings.Save(new(null, true)); Assert.Equal(409, (await Assert.ThrowsAsync<DomainException>(() => provider.GenresAsync("tmdb", "movie", CancellationToken.None))).StatusCode);
        Assert.Empty(home.Store.GetDownloads());
    }
    [Theory]
    [InlineData("movie", 28, "movie")]
    [InlineData("show", 10759, "tv")]
    public async Task GenreBrowsingQueriesTheProviderCatalogAndSeparatesFilteredPages(string kind, int genre, string type)
    {
        using var home = new TestWorkspace(); using var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 }); var handler = new Handler();
        var provider = new MetadataProvider(new Factory(handler), Settings(home), cache);
        var action = await provider.DiscoverAsync("tmdb", kind, "popular", null, 2, CancellationToken.None, genre);
        Assert.Equal(42, Assert.Single(action.Items).ProviderId); Assert.Equal(kind, action.Items[0].Kind); Assert.NotEmpty(action.Items[0].Genres); Assert.Equal(3, action.NextPage);
        var address = handler.Requests.Last(); var query = QueryHelpers.ParseQuery(address.Query);
        Assert.Equal($"/3/discover/{type}", address.AbsolutePath); Assert.Equal(genre.ToString(), query["with_genres"]); Assert.Equal("2", query["page"]); Assert.Equal("popularity.desc", query["sort_by"]); Assert.Equal("false", query["include_adult"]);
        var comedy = await provider.DiscoverAsync("tmdb", kind, "popular", null, 2, CancellationToken.None, 35); Assert.Equal(43, Assert.Single(comedy.Items).ProviderId); Assert.Equal("Komedi", Assert.Single(comedy.Items[0].Genres));
        await provider.DiscoverAsync("tmdb", kind, "top", null, 1, CancellationToken.None, genre); query = QueryHelpers.ParseQuery(handler.Requests.Last().Query); Assert.Equal("vote_average.desc", query["sort_by"]); Assert.Equal("200", query["vote_count.gte"]);
        await provider.DiscoverAsync("tmdb", kind, "upcoming", null, 1, CancellationToken.None, genre); query = QueryHelpers.ParseQuery(handler.Requests.Last().Query);
        Assert.True(query.ContainsKey(kind == "movie" ? "primary_release_date.gte" : "air_date.gte")); Assert.True(query.ContainsKey(kind == "movie" ? "primary_release_date.lte" : "air_date.lte"));
        Assert.Empty(home.Store.GetDownloads());
    }
    [Fact]
    public async Task GenreAndTitleSearchContinuePastEmptyPagesWithoutLosingTheProviderCursor()
    {
        using var home = new TestWorkspace(); using var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 }); var handler = new Handler();
        var provider = new MetadataProvider(new Factory(handler), Settings(home), cache);
        var first = await provider.DiscoverAsync("tmdb", "movie", "popular", "Isolated film", 1, CancellationToken.None, 28);
        Assert.Empty(first.Items); Assert.True(first.HasMore); Assert.Equal(5, first.NextPage); Assert.Equal(1, first.Page);
        var next = await provider.DiscoverAsync("tmdb", "movie", "popular", "Isolated film", first.NextPage!.Value, CancellationToken.None, 28);
        Assert.Equal(new[] { 105, 106 }, next.Items.Select(item => item.ProviderId)); Assert.False(next.HasMore); Assert.Null(next.NextPage);
        var searches = handler.Requests.Where(address => address.AbsolutePath.Contains("/search/", StringComparison.Ordinal)).Select(address => QueryHelpers.ParseQuery(address.Query)).ToArray();
        Assert.Equal(new[] { "1", "2", "3", "4", "5", "6" }, searches.Select(query => query["page"].ToString()));
        Assert.All(searches, query => { Assert.Equal("Isolated film", query["query"]); Assert.False(query.ContainsKey("with_genres")); });
    }
    private static MetadataSettings Settings(TestWorkspace home)
    {
        var settings = new MetadataSettings(home.Store, new ConfigurationBuilder().Build(), new EphemeralDataProtectionProvider()); settings.Save(new("isolated-genre-token-123456789")); return settings;
    }
    private sealed class Factory(Handler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, false) { BaseAddress = new("https://api.themoviedb.org/3/") };
    }
    private sealed class Handler : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var address = request.RequestUri!; Requests.Add(address); Assert.Equal("Bearer", request.Headers.Authorization!.Scheme); Assert.Equal("isolated-genre-token-123456789", request.Headers.Authorization.Parameter);
            var query = QueryHelpers.ParseQuery(address.Query); var english = query["language"] == "en-US"; var tv = address.AbsolutePath.Contains("/tv", StringComparison.Ordinal); string json;
            if (address.AbsolutePath.Contains("/genre/", StringComparison.Ordinal)) json = JsonSerializer.Serialize(new { genres = new[] { new { id = tv ? 10759 : 28, name = english ? "Action" : tv ? "Aksiyon ve Macera" : "Aksiyon" }, new { id = 35, name = english ? "Comedy" : "Komedi" }, new { id = 99, name = "Belgesel" } } });
            else
            {
                var page = int.Parse(query["page"].ToString());
                var rows = address.AbsolutePath.Contains("/search/", StringComparison.Ordinal) ? new[] { Row(100 + page, page < 5 ? 99 : 28) } : new[] { Row(42, tv ? 10759 : 28), Row(43, 35) };
                json = JsonSerializer.Serialize(new { page, total_pages = 6, results = rows });
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") });
        }
        private static object Row(int id, int genre) => new { id, title = "Isolated film " + id, name = "Isolated series " + id, genre_ids = new[] { genre }, adult = false };
    }
}
