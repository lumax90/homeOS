using HomeServer.Core;
using HomeServer.Infrastructure;

namespace HomeServer.LiveTv;

public interface ILiveHttp
{
    Task<HttpResponseMessage> GetAsync(string url,bool allowLan,CancellationToken token,string? range=null,IReadOnlyDictionary<string,string>? headers=null);
}
public sealed class LiveHttp(RemoteMediaHttp http):ILiveHttp
{
    public Task<HttpResponseMessage> GetAsync(string url,bool allowLan,CancellationToken token,string? range=null,IReadOnlyDictionary<string,string>? headers=null)=>http.GetAsync(url,allowLan,token,range,headers);
}
public sealed record LiveTextResource(byte[] Bytes,Uri Uri);
public static class LiveHttpRead
{
    public static async Task<LiveTextResource> ReadAsync(ILiveHttp http,string url,bool lan,int maximum,CancellationToken token)
    {
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(35));
        using var response=await http.GetAsync(url,lan,timeout.Token);if(!response.IsSuccessStatusCode)throw new DomainException($"Kaynak HTTP {(int)response.StatusCode} döndürdü.",502);
        await using var stream=await response.Content.ReadAsStreamAsync(timeout.Token);return new(await Metadata.MetadataProvider.ReadLimitedAsync(stream,maximum,timeout.Token),response.RequestMessage?.RequestUri??new Uri(url));
    }
}
