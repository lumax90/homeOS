using System.Net;
using System.Text;
using HomeServer.Core;
using HomeServer.Music;
using Microsoft.Extensions.Caching.Memory;

namespace HomeServer.Tests;

public sealed class MusicCatalogProviderTests
{
    private const string Group = "11111111-1111-4111-8111-111111111111";
    private const string Artist = "22222222-2222-4222-8222-222222222222";
    private const string Guest = "33333333-3333-4333-8333-333333333333";
    private const string Recording = "44444444-4444-4444-8444-444444444444";
    [Fact]
    public async Task CreditedNamesAndJoinPhrasesRemainSeparateFromArtistIdentity()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 20 }); var provider = new MusicBrainzProvider(new Clients(), cache);
        var album = await provider.AlbumAsync(Group, CancellationToken.None); Assert.Equal("Alias feat. Guest", album.Artist);
        Assert.Equal(2, album.ArtistCredits!.Length); Assert.Equal("Canonical artist", album.ArtistCredits[0].Name); Assert.Equal("Alias", album.ArtistCredits[0].CreditedName); Assert.Equal(Artist, album.ArtistCredits[0].Mbid); Assert.Equal(" feat. ", album.ArtistCredits[0].JoinPhrase);
        var relations = await provider.AlbumRelationsAsync(Group, CancellationToken.None); var relation = Assert.Single(relations); Assert.Equal("release-group", relation.TargetType); Assert.Equal("backward", relation.Direction); Assert.StartsWith("https://musicbrainz.org/release-group/", relation.ProviderUrl);
        var recording = await provider.RecordingAsync(Recording, CancellationToken.None); Assert.True(recording.Video); Assert.Equal(Recording, recording.Id);
    }
    [Theory]
    [InlineData("release")]
    [InlineData("release-group")]
    public async Task PublicAlbumCollectionsDistinguishGroupsAndDeduplicateReleaseEditions(string type)
    {
        using var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 20 }); var provider = new MusicBrainzProvider(new Clients(), cache);
        var result = await provider.CollectionPageAsync(new(Group, "Fixture", type), "album", 0, CancellationToken.None);
        Assert.Equal(2, result.EntityIds.Length); Assert.Equal(type == "release" ? 1 : 2, result.Items.Length); Assert.Equal(Group, result.Items[0].Mbid);
    }
    private sealed class Clients : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new Handler()) { BaseAddress = new("https://musicbrainz.org/ws/2/") };
    }
    private sealed class Handler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!; string json;
            var credits = $$"""[{"name":"Alias","joinphrase":" feat. ","artist":{"id":"{{Artist}}","name":"Canonical artist","sort-name":"Artist, Canonical"} },{"name":"Guest","artist":{"id":"{{Guest}}","name":"Guest"} }]""";
            var group = $$"""{"id":"{{Group}}","title":"Fixture album","artist-credit":{{credits}},"primary-type":"Album"}""";
            if (uri.Query.Contains("collection="))
            {
                var type = uri.AbsolutePath.EndsWith("release-group", StringComparison.Ordinal) ? "release-group" : "release";
                var first = type == "release-group" ? group : $$"""{"id":"{{Artist}}","title":"Edition A","artist-credit":{{credits}},"release-group":{{group}}}""";
                var second = type == "release-group" ? group.Replace(Group, Guest) : $$"""{"id":"{{Guest}}","title":"Edition B","artist-credit":{{credits}},"release-group":{{group}}}""";
                json = $$"""{"{{type}}-count":2,"{{type}}-offset":0,"{{type}}s":[{{first}},{{second}}]}""";
            }
            else if (uri.AbsolutePath.Contains("recording/")) json = $$"""{"id":"{{Recording}}","title":"Video version","video":true,"artist-credit":{{credits}},"relations":[]}""";
            else if (uri.Query.Contains("release-group-rels")) json = $$"""{"id":"{{Group}}","relations":[{"type":"single from","direction":"backward","target-type":"release-group","release-group":{{group}}},{"type":"streaming","target-type":"url","url":{"resource":"javascript:invalid"} }]}""";
            else json = group;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }
    }
}
