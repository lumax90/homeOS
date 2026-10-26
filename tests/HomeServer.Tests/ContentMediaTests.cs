using System.Text.Json;
using HomeServer.Core;
using HomeServer.Metadata;

namespace HomeServer.Tests;

public sealed class ContentMediaTests
{
    [Fact]
    public void OnlyBoundedYouTubeTrailersAndTeasersForTheExactTitleAreAccepted()
    {
        // Isolated metadata contracts; no actual video or provider media is acquired.
        using var json=JsonDocument.Parse("""
            {"id":42,"results":[
              {"key":"Original001","site":"YouTube","type":"Trailer","official":false,"name":"ORIGINAL isolated trailer"},
              {"key":"Original002","site":"YouTube","type":"Trailer","official":true,"name":"ORIGINAL isolated official trailer"},
              {"key":"Original003","site":"YouTube","type":"Clip"},
              {"key":"Original004","site":"Vimeo","type":"Trailer"},
              {"key":"../arbitrary","site":"YouTube","type":"Trailer"}]}
            """);
        var values=MetadataProvider.ReadTrailers(json.RootElement,42);Assert.Equal(2,values.Length);Assert.Equal("Original002",values[0].Key);
        Assert.Throws<DomainException>(()=>MetadataProvider.ReadTrailers(json.RootElement,99));
    }
    [Fact]
    public void GalleryKeepsLandscapeProviderImagesNotUnsafePathsOrPortraits()
    {
        using var json=JsonDocument.Parse("""
            {"id":42,"backdrops":[{"file_path":"/original-isolated.jpg","width":1280,"height":720},
            {"file_path":"https://example.invalid/unsafe.jpg","width":1280,"height":720},
            {"file_path":"/portrait.jpg","width":500,"height":900}]}
            """);
        var image=Assert.Single(MetadataProvider.ReadGallery("tmdb",json.RootElement,42));Assert.StartsWith("https://image.tmdb.org/",image.Url);
        Assert.Throws<DomainException>(()=>MetadataProvider.ReadGallery("tmdb",json.RootElement,99));
    }
}
