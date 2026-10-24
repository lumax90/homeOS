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
public static class LiveHttpRetry
{
    public static async Task<HttpResponseMessage> OpenAsync(ILiveHttp http,string url,bool lan,Action validate,CancellationToken token,string? range=null,
        IReadOnlyDictionary<string,string>? headers=null,Func<TimeSpan,CancellationToken,Task>? delay=null)
    {
        delay ??= Task.Delay;
        for(var attempt=0;;attempt++)
        {
            token.ThrowIfCancellationRequested(); validate();
            try
            {
                var response=await http.GetAsync(url,lan,token,range,headers);
                if(attempt>=2 || (int)response.StatusCode is not(408 or 429 or 500 or 502 or 503 or 504))return response;
                var retry=response.Headers.RetryAfter?.Delta ?? (response.Headers.RetryAfter?.Date-DateTimeOffset.UtcNow);
                var wait=retry is{}value?TimeSpan.FromMilliseconds(Math.Clamp(value.TotalMilliseconds,0,2000)):TimeSpan.FromMilliseconds(attempt==0?250:750);
                response.Dispose(); await delay(wait,token);
            }
            catch(HttpRequestException)when(attempt<2){await delay(TimeSpan.FromMilliseconds(attempt==0?250:750),token);}
        }
    }
}
public static class LiveHttpRead
{
    public static async Task<LiveTextResource> ReadAsync(ILiveHttp http,string url,bool lan,int maximum,CancellationToken token)
    {
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(35));
        using var response=await http.GetAsync(url,lan,timeout.Token);if(!response.IsSuccessStatusCode)throw new DomainException($"Kaynak HTTP {(int)response.StatusCode} döndürdü.",502);
        await using var stream=await response.Content.ReadAsStreamAsync(timeout.Token);return new(await Metadata.MetadataProvider.ReadLimitedAsync(stream,maximum,timeout.Token),response.RequestMessage?.RequestUri??new Uri(url));
    }
}
