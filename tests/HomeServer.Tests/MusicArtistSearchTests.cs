using System.Text.Json;
using HomeServer.Core;
using HomeServer.Music;
using Microsoft.Extensions.Caching.Memory;

namespace HomeServer.Tests;

public sealed class MusicArtistSearchTests
{
    private static object Artists()=>new{count=1,artists=new[]{new{id="aaaaaaaa-1111-4111-8111-111111111111",name="An artist",country="TR"}}};
    [Theory]
    [InlineData("Metallic","artist:(Metallic*)")]
    [InlineData("Daft P","artist:(\"Daft\" AND P*)")]
    [InlineData("OR Love","artist:(\"OR\" AND Love*)")]
    [InlineData("AC/DC","artist:(\"AC\" AND DC*)")]
    public async Task AutocompleteUsesLiteralEarlierWordsAndOneTrailingPrefixWithAliases(string input,string prefix)
    {
        using var handler=new MusicExperienceTests.Handler(_=>MusicExperienceTests.Json(Artists()));using var cache=new MemoryCache(new MemoryCacheOptions{SizeLimit=10});var provider=new MusicBrainzProvider(new MusicExperienceTests.Factory(handler),cache);
        var result=await provider.SearchArtistsAsync(input,0,CancellationToken.None,autocomplete:true);Assert.Equal("An artist",Assert.Single(result.Items).Name);var url=Uri.UnescapeDataString(Assert.Single(handler.Uris).Query);Assert.Contains(prefix,url);Assert.Contains("OR alias:",url);Assert.Contains("^4",url);Assert.Contains("limit=25",url);
        await provider.SearchArtistsAsync(input,0,CancellationToken.None,autocomplete:true);Assert.Equal(1,handler.Calls);
    }
    [Fact]
    public async Task ExistingExactSearchIsRetainedAndShortOrOversizedInputsAreRejected()
    {
        using var handler=new MusicExperienceTests.Handler(_=>MusicExperienceTests.Json(Artists()));using var cache=new MemoryCache(new MemoryCacheOptions{SizeLimit=10});var provider=new MusicBrainzProvider(new MusicExperienceTests.Factory(handler),cache);await provider.SearchArtistsAsync("Daft P",0,CancellationToken.None);var url=Uri.UnescapeDataString(Assert.Single(handler.Uris).Query);Assert.Contains("artist:\"Daft P\"",url);Assert.DoesNotContain('*',url);
        await Assert.ThrowsAsync<DomainException>(()=>provider.SearchArtistsAsync("a",0,CancellationToken.None,true));await Assert.ThrowsAsync<DomainException>(()=>provider.SearchArtistsAsync(new string('a',121),0,CancellationToken.None,true));Assert.Equal(1,handler.Calls);
    }
    private sealed class Delayed:HttpMessageHandler
    {
        public int Calls;public readonly TaskCompletionSource Started=new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){Calls++;if(Calls==1){Started.TrySetResult();await Task.Delay(Timeout.Infinite,token);}return MusicExperienceTests.Json(Artists());}
    }
    [Fact]
    public async Task SupersededArtistQueriesCancelActiveAndWaitingRequestsWithoutSpendingProviderCalls()
    {
        using var handler=new Delayed();using var cache=new MemoryCache(new MemoryCacheOptions{SizeLimit=10});var provider=new MusicBrainzProvider(new MusicExperienceTests.Factory(handler),cache);using var first=new CancellationTokenSource();using var next=new CancellationTokenSource();
        var active=provider.SearchArtistsAsync("Old",0,first.Token,true);await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));var waiting=provider.SearchArtistsAsync("Older",0,next.Token,true);next.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>waiting);Assert.Equal(1,handler.Calls);first.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>active);
        Assert.Single((await provider.SearchArtistsAsync("New",0,CancellationToken.None,true)).Items);Assert.Equal(2,handler.Calls);
    }
}
