using System.Net;
using System.Text;
using HomeServer.Core;
using HomeServer.Infrastructure;
using HomeServer.Metadata;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;

namespace HomeServer.Tests;

public sealed class CatalogProviderTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TmdbAbsoluteOrderVerifiesItsParentAndExactOrdinalSequence(bool wrongShow)
    {
        using var home = new TestWorkspace(); using var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 20 });
        var settings = new MetadataSettings(home.Store, new ConfigurationBuilder().Build(), new EphemeralDataProtectionProvider()); settings.Save(new MetadataPreferences("isolated-fixture-token-not-a-live-credential"));
        var provider = new MetadataProvider(new Clients(new Handler(wrongShow)), settings, cache);
        if (wrongShow) await Assert.ThrowsAsync<DomainException>(() => provider.AbsoluteOrderAsync(42, "1234567890abcdef12345678", CancellationToken.None));
        else
        {
            var result = await provider.AbsoluteOrderAsync(42, "1234567890abcdef12345678", CancellationToken.None);
            Assert.Equal(new[] { 1, 2 }, result.Entries.Select(entry => entry.Number)); Assert.Equal(new[] { 3, 1 }, result.Entries.Select(entry => entry.Episode)); Assert.Equal(new[] { 1, 2 }, result.Entries.Select(entry => entry.Season));
            Assert.Equal(new int?[] { 1003, 2001 }, result.Entries.Select(entry => entry.ProviderId));
        }
    }
    [Fact]
    public async Task DailyProviderSelectionRejectsDateMismatchAndNormalizesAlternativeNames()
    {
        using var home = new TestWorkspace(); using var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 20 });
        var provider = new MetadataProvider(new Clients(new Handler(false)), new MetadataSettings(home.Store, new ConfigurationBuilder().Build(), new EphemeralDataProtectionProvider()), cache);
        var episode = Assert.Single(await provider.DailyEpisodesAsync("tvmaze", 42, "2020-01-02", CancellationToken.None)); Assert.Equal(7, episode.EpisodeNumber);
        await Assert.ThrowsAsync<DomainException>(() => provider.DailyEpisodesAsync("tvmaze", 42, "2020-01-03", CancellationToken.None));
        var names = await provider.AlternativeNamesAsync(new("tvmaze", 42, "show", "Fixture", "Fixture", "", null, null, null, "https://www.tvmaze.com/shows/42", []), CancellationToken.None);
        Assert.Equal(new[] { "Fixture Alias" }, names);
    }
    private sealed class Clients(HttpMessageHandler handler) : IHttpClientFactory { public HttpClient CreateClient(string name) => new(handler, false) { BaseAddress = new Uri(name == "tmdb" ? "https://api.themoviedb.org/3/" : "https://api.tvmaze.com/") }; }
    [Theory]
    [InlineData("valid")]
    [InlineData("parent")]
    [InlineData("duplicate")]
    [InlineData("ordinal")]
    public async Task DvdGroupsKeepProviderIdsAndRejectCorruptGroupMembership(string mode)
    {
        using var home = new TestWorkspace(); using var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 20 });
        var settings = new MetadataSettings(home.Store, new ConfigurationBuilder().Build(), new EphemeralDataProtectionProvider()); settings.Save(new("isolated-fixture-token"));
        var provider = new MetadataProvider(new Clients(new OrderHandler(mode)), settings, cache);
        if (mode != "valid") await Assert.ThrowsAsync<DomainException>(() => provider.EpisodeOrderDetailsAsync(42, "1234567890abcdef12345678", CancellationToken.None));
        else
        {
            var group = await provider.EpisodeOrderDetailsAsync(42, "1234567890abcdef12345678", CancellationToken.None);
            Assert.Equal(3, group.Type); Assert.Equal(64, group.Fingerprint.Length); Assert.Equal(0, Assert.Single(group.Groups).Position);
            Assert.Equal(new[] { 1003, 2001 }, group.Groups[0].Episodes.Select(episode => episode.ProviderId)); Assert.Equal(22, group.Groups[0].Episodes[0].RuntimeMinutes); Assert.Null(group.Groups[0].Episodes[1].RuntimeMinutes);
        }
    }
    private sealed class OrderHandler(string mode) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var json = request.RequestUri!.AbsolutePath.EndsWith("episode_groups", StringComparison.Ordinal)
                ? """{"id":42,"results":[{"id":"1234567890abcdef12345678","type":3,"name":"DVD fixture","description":"","episode_count":2}]}"""
                : $$$"""{"id":"1234567890abcdef12345678","type":3,"episode_count":2,"group_count":1,"groups":[{"id":"group-one","name":"Disc 1","order":0,"episodes":[{"show_id":{{{(mode == "parent" ? 43 : 42)}}},"id":1003,"season_number":1,"episode_number":3,"order":0,"name":"One","runtime":22},{"show_id":42,"id":{{{(mode == "duplicate" ? 1003 : 2001)}}},"season_number":2,"episode_number":1,"order":{{{(mode == "ordinal" ? 3 : 1)}}},"name":"Two","runtime":null}]}]}""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }
    }
    [Fact]
    public async Task AlternativeTitleResponseCannotSupplyAnotherMoviesIdentity()
    {
        using var home = new TestWorkspace(); using var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 20 });
        var settings = new MetadataSettings(home.Store, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Home:Tmdb:Token"] = "isolated-fixture-token" }).Build(), new EphemeralDataProtectionProvider());
        var provider = new MetadataProvider(new Clients(new Handler(true)), settings, cache);
        await Assert.ThrowsAsync<DomainException>(() => provider.AlternativeNamesAsync(new("tmdb", 42, "movie", "Fixture", "Fixture", "", null, null, null, "https://www.themoviedb.org/movie/42", []), CancellationToken.None));
    }
    private sealed class Handler(bool wrongShow) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            var json = path.EndsWith("episode_groups", StringComparison.Ordinal) ? """{"id":42,"results":[{"id":"1234567890abcdef12345678","type":2,"name":"Fixture absolute","description":"","episode_count":2}]}"""
                : path.Contains("episode_group/", StringComparison.Ordinal) ? $$$"""{"id":"1234567890abcdef12345678","type":2,"episode_count":2,"groups":[{"order":0,"episodes":[{"show_id":{{{(wrongShow ? 43 : 42)}}},"id":2001,"season_number":2,"episode_number":1,"order":1},{"show_id":42,"id":1003,"season_number":1,"episode_number":3,"order":0}]}]}"""
                : path.EndsWith("alternative_titles", StringComparison.Ordinal) ? """{"id":43,"titles":[{"title":"Other fixture title"}]}"""
                : path.EndsWith("akas", StringComparison.Ordinal) ? """[{"name":"Fixture Alias"},{"name":"Fixture Alias"}]"""
                : """[{"id":1007,"season":1,"number":7,"name":"Fixture daily","summary":"<p>Fixture</p>","airdate":"2020-01-02","image":null}]""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }
    }
}
