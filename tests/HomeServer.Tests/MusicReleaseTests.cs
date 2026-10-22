using System.Net;
using System.Text;
using HomeServer.Core;
using HomeServer.Infrastructure;
using HomeServer.Music;
using Microsoft.Extensions.Caching.Memory;

namespace HomeServer.Tests;

public sealed class MusicReleaseTests
{
    private const string ReleaseId = "a12f798e-7611-4c85-bb83-38fa248233c1";
    private const string GroupId = "063caa57-2828-48d3-b5d5-14f5955ea3ac";
    [Fact]
    public async Task ReleaseAndRecordingIdsRemainDistinctAcrossDiscsAndVinylNumbers()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 20 });
        var handler = new Handler(false); var provider = new MusicBrainzProvider(new Clients(handler), cache);
        var releases = await provider.ReleasesAsync(GroupId, 25, CancellationToken.None);
        Assert.Equal(25, releases.Offset); Assert.Equal(26, releases.Total); Assert.Equal(2, Assert.Single(releases.Items).TrackCount);
        var details = await provider.ReleaseAsync(ReleaseId, CancellationToken.None);
        Assert.Equal(2, details.Tracks.Length); Assert.Equal("A1", details.Tracks[0].Number); Assert.Equal(2, details.Tracks[1].Disc);
        Assert.NotEqual(details.Tracks[0].Id, details.Tracks[0].RecordingId); Assert.Equal(GroupId, details.Metadata.GroupId);
    }
    [Fact]
    public async Task ExplicitAlbumMappingPreservesSourceTagsAndSurvivesRescans()
    {
        using var home = new TestWorkspace(); home.Store.CreateOwner(new Owner("Owner", "fixture-hash")); var owner = home.Store.GetAccount("1")!;
        var firstPath = home.Write("One.flac"); home.Write("Two.flac"); var library = home.AddLibrary(); await home.Scan(library.Id);
        var files = home.Store.GetCatalog(null, null, null, 0, 60).Items.OrderBy(file => file.RelativePath).ToArray();
        AudioTags Tags(string title, int number, string album = "Local album") => new(title, "Artist", "Artist", album, "2020", number, 1, null, null, null, null, null, -3, 16);
        home.Store.SaveMusicTags(files[0].Id, Tags("Wrong title", 1)); home.Store.SaveMusicTags(files[1].Id, Tags("Two", 2));
        var album = Assert.Single(home.Store.GetMusicAlbums(owner, 0).Items);
        using var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 10 }); var provider = new MusicBrainzProvider(new Clients(new Handler(false)), cache);
        var release = await provider.ReleaseAsync(ReleaseId, CancellationToken.None);
        var preview = home.Store.PreviewMusicMatch(album.Id, release, owner); Assert.False(preview.CanApply);
        home.Store.MatchMusicAlbum(album.Id, release, owner, [new(files[0].Id, 1, 1)]);
        Assert.Equal("Published album", home.Store.GetMusicAlbum(album.Id, owner)!.Title);
        Assert.Equal(release.Tracks[0].RecordingId, home.Store.GetMusicTracks(album.Id, owner).Items[0].RecordingMbid);
        home.Store.SaveMusicTags(files[0].Id, Tags("New tag text", 1, "Changed tag album"));
        Assert.Equal("One", home.Store.GetMedia(files[0].Id)!.Title);
        Assert.Equal(album.Id, home.Store.GetMusicTracks(album.Id, owner).Items[0].AlbumId);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, File.ReadAllBytes(firstPath));
        var other = home.Store.SaveAccount(null, new AccountInput("No access", AllLibraries: false), "fixture-hash");
        Assert.Null(home.Store.GetMusicAlbum(album.Id, other)); Assert.Throws<DomainException>(() => home.Store.PreviewMusicMatch(album.Id, release, other));
        home.Store.UnmatchMusicAlbum(album.Id); Assert.Equal("New tag text", home.Store.GetMedia(files[0].Id)!.Title);
    }
    [Fact]
    public async Task CoverProxyChecksRedirectHostsAndCachesOnlyImageBytes()
    {
        using var home = new TestWorkspace(); using var memory = new MemoryCache(new MemoryCacheOptions { SizeLimit = 20 });
        var handler = new Handler(false); var cache = new CoverArtCache(home.Paths, new Clients(handler), memory);
        var first = await cache.GetAsync("release", ReleaseId, CancellationToken.None); Assert.NotNull(first); Assert.Equal("image/png", first.Value.Mime);
        var calls = handler.Covers;
        await cache.GetAsync("release", ReleaseId, CancellationToken.None); Assert.Equal(calls, handler.Covers);
        Assert.False(CoverArtCache.Allowed(new Uri("https://archive.org.attacker.invalid/file.jpg")));
        var bad = new CoverArtCache(home.Paths, new Clients(new Handler(true)), memory);
        await Assert.ThrowsAsync<DomainException>(() => bad.GetAsync("release-group", GroupId, CancellationToken.None));
    }
    private sealed class Clients(Handler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, false) { BaseAddress = new Uri("https://musicbrainz.org/ws/2/") };
    }
    private sealed class Handler(bool badHost) : HttpMessageHandler
    {
        public int Covers { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!;
            if (url.Host == "coverartarchive.org")
            {
                Covers++; var response = new HttpResponseMessage(HttpStatusCode.TemporaryRedirect); response.Headers.Location = new Uri(badHost ? "https://untrusted.invalid/image" : "https://archive.org/image.png"); return Task.FromResult(response);
            }
            if (url.Host == "archive.org")
            {
                Covers++; Assert.Null(request.Headers.Authorization);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jfeoAAAAASUVORK5CYII=")) });
            }
            var item = $$$"""
                {"id":"{{{ReleaseId}}}","title":"Published album","date":"2020","country":"GB","status":"Official","release-group":{"id":"{{{GroupId}}}"},"artist-credit":[{"name":"Artist","joinphrase":""}],"media":[
                  {"position":1,"format":"Vinyl","track-count":1,"tracks":[{"id":"d51ffb67-3ae6-4e1d-b73e-23a837c6a0da","position":1,"number":"A1","title":"One","length":100000,"recording":{"id":"65191a93-de35-4507-b819-305b177c650a","title":"One"}}]},
                  {"position":2,"format":"CD","track-count":1,"tracks":[{"id":"8ce35fef-2b12-4b78-89c5-150bd0b267b1","position":1,"number":"1","title":"Two","length":100000,"recording":{"id":"84905fe0-8e30-4695-b210-f4f2185e03ba","title":"Two"}}]}
                ]}
                """;
            var json = url.AbsolutePath.EndsWith("/release", StringComparison.Ordinal) ? "{\"release-count\":26,\"releases\":[" + item + "]}" : item;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }
    }
}
