using System.Text;
using HomeServer.Core;
using HomeServer.Infrastructure;
using HomeServer.Sources;
using Microsoft.AspNetCore.WebUtilities;
using MonoTorrent;

namespace HomeServer.Tests;

public sealed class SourceNativeTests
{
    [Fact]
    public void LoginOrChallengePageCannotPassAsAnEmptyRssSource()
    {
        var manifest=SourceAdapterRegistry.Builtins.Single(value=>value.Id=="nyaa-rss");
        Assert.Throws<DomainException>(()=>SourceAdapterRegistry.Parse(manifest,Encoding.UTF8.GetBytes("<html><body>Login required</body></html>"),new("https://fixture.invalid/"),0));
        Assert.Empty(SourceAdapterRegistry.Parse(manifest,Encoding.UTF8.GetBytes("<rss><channel><title>Isolated empty feed</title></channel></rss>"),new("https://fixture.invalid/"),0).Items);
    }
    public const string Yts="""{"status":"ok","data":{"movie_count":2,"movies":[{"title":"Isolated Movie","year":2020,"imdb_code":"tt1234567","language":"en","torrents":[{"hash":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA","quality":"720p","type":"bluray","video_codec":"x264","size_bytes":800000000,"seeds":12,"date_uploaded_unix":1700000000},{"hash":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb","quality":"1080p","type":"bluray","video_codec":"x265","size_bytes":1200000000,"seeds":20,"date_uploaded_unix":1700000001}]}]}}""";
    [Fact]
    public void YtsPreservesEachVerifiedTorrentQualityAndAuthoritativeMovieIdentity()
    {
        var manifest=SourceAdapterRegistry.Builtins.Single(value=>value.Id=="yts");var result=SourceAdapterRegistry.Parse(manifest,Encoding.UTF8.GetBytes(Yts),new("https://fixture.invalid/"),0);
        Assert.Equal(2,result.Items.Count);Assert.True(result.More);Assert.Contains("720p",result.Items[0].Title);Assert.Contains("1080p",result.Items[1].Title);
        Assert.Equal(800000000,result.Items[0].SizeBytes);Assert.Equal("tt1234567",Assert.Single(result.Items[0].Attributes["imdbid"]));Assert.Equal("en",Assert.Single(result.Items[0].Attributes["language"]));
        Assert.All(result.Items,value=>Assert.True(MagnetLink.TryParse(value.Url,out _)));Assert.NotEqual(result.Items[0].Guid,result.Items[1].Guid);
        Assert.Empty(SourceAdapterRegistry.Parse(manifest,Encoding.UTF8.GetBytes("{\"status\":\"ok\",\"data\":{\"movie_count\":0}}"),new("https://fixture.invalid/"),0).Items);
    }
    [Theory]
    [InlineData("eztv","""{"torrents_count":1,"torrents":[{"title":"Isolated.Series.S01E02.1080p","hash":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","imdb_id":"1234567","size_bytes":"1200000000","seeds":20,"date_released_unix":1700000000}]}""")]
    [InlineData("the-pirate-bay","""[{"id":"1","name":"Isolated.Movie.2020.1080p","info_hash":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","imdb":"tt1234567","size":"1200000000","seeders":"20","added":"1700000000"},{"id":"0","name":"No results returned","info_hash":"0000000000000000000000000000000000000000","size":"0"}]""")]
    [InlineData("torrents-csv","""{"torrents":[{"id":501,"name":"Isolated.Movie.2020.1080p","infohash":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","size_bytes":1200000000,"seeders":20,"created_unix":1700000000}],"next":501}""")]
    public void NativeJsonSourcesNormalizeHashesSizesAndUnixDatesWithoutFollowingLinks(string id,string json)
    {
        var manifest=SourceAdapterRegistry.Builtins.Single(value=>value.Id==id);var result=SourceAdapterRegistry.Parse(manifest,Encoding.UTF8.GetBytes(json),new("https://fixture.invalid/"),0);var release=Assert.Single(result.Items);
        Assert.Equal(new string('a',40),release.InfoHash);Assert.Equal(1200000000,release.SizeBytes);Assert.Equal(20,release.Seeders);Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1700000000),DateTimeOffset.Parse(release.PublishedAt!));Assert.StartsWith("magnet:?xt=urn:btih:",release.Url);
        if(id=="torrents-csv"){Assert.True(result.More);Assert.Equal("501",result.NextCursor);}else{Assert.False(result.More);Assert.Equal("tt1234567",Assert.Single(release.Attributes["imdbid"]));}
    }
    [Fact]
    public void NativeRequestsOnlyAdvertiseSupportedIdentityAndPagingMechanisms()
    {
        using var home=new TestWorkspace();var source=home.Store.SaveIndexer(null,new("Isolated","https://fixture.invalid/","torznab",null),"protected");var secret=home.Store.GetIndexer(source.Id);
        var yts=SourceAdapterRegistry.Builtins.Single(value=>value.Id=="yts");var request=SourceAdapterRegistry.Request(yts,secret,new(){["imdbid"]="1234567",["offset"]="20"},"");var query=QueryHelpers.ParseQuery(new Uri(request.Url).Query);Assert.Equal("tt1234567",query["query_term"]);Assert.Equal("2",query["page"]);
        var eztv=SourceAdapterRegistry.Builtins.Single(value=>value.Id=="eztv");Assert.DoesNotContain("movie-search",SourceAdapterRegistry.Capabilities(eztv).Searches.Keys);Assert.Throws<DomainException>(()=>SourceAdapterRegistry.Request(eztv,secret,new(){["q"]="No identity"},""));
        request=SourceAdapterRegistry.Request(eztv,secret,new(){["imdbid"]="1234567",["offset"]="100"},"");query=QueryHelpers.ParseQuery(new Uri(request.Url).Query);Assert.Equal("1234567",query["imdb_id"]);Assert.Equal("2",query["page"]);
        var csv=SourceAdapterRegistry.Builtins.Single(value=>value.Id=="torrents-csv");Assert.DoesNotContain("search",SourceAdapterRegistry.Capabilities(csv).Searches.Keys);request=SourceAdapterRegistry.Request(csv,secret,new(){["q"]="Isolated Movie",["after"]="501"},"");query=QueryHelpers.ParseQuery(new Uri(request.Url).Query);Assert.Equal("501",query["after"]);Assert.False(query.ContainsKey("page"));
    }
}
