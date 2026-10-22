using System.Net;
using System.Text.Json;
using HomeServer.Core;
using HomeServer.Infrastructure;
using HomeServer.Operations;

namespace HomeServer.Music;

public sealed record RadioStation(string Id,string Name,string Url,string Country,string Language,string[] Tags,string Codec,int Bitrate,bool Hls,string? Homepage=null);
public sealed record RadioFavorite(RadioStation Station,string Category);
public sealed record RadioFavoriteCommand(bool Favorite,string Category="");
public sealed record RadioStationPage(RadioStation[] Items,int Offset,int Limit,bool HasMore);
public sealed class RadioDirectory(HomePaths paths,MusicExperiencePreferences preferences,HttpMessageHandler? handler=null):IDisposable
{
    private readonly HttpClient _http=new(handler??new SocketsHttpHandler{UseProxy=false,UseCookies=false,AllowAutoRedirect=false}){Timeout=TimeSpan.FromSeconds(15)};
    private readonly SemaphoreSlim _gate=new(1,1);private readonly object _files=new();private readonly Dictionary<string,(DateTimeOffset At,RadioStation[] Items,int Count)> _cache=new();
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    private static readonly string[] Mirrors=["de1.api.radio-browser.info","nl1.api.radio-browser.info","at1.api.radio-browser.info"];
    public async Task<RadioStationPage> SearchAsync(string user,string? query,string? tag,string? country,int offset,CancellationToken token)
    {
        if(!preferences.Read(user).RadioDirectory)throw new DomainException("Radyo kataloğu müzik ayarlarından kapatıldı.",409);
        query??="";tag??="";country??="";if(query.Length>120||tag.Length>50||country.Length is not(0 or 2)||offset is <0 or >10000||country.Any(c=>!char.IsAsciiLetter(c)))throw new DomainException("Radyo arama alanları geçersiz.");
        // Radio Browser treats countrycode= as an exact empty-country filter, not "all countries".
        var route=$"stations/search?hidebroken=true&order=clickcount&reverse=true&limit=48&offset={offset}";
        if(!string.IsNullOrWhiteSpace(query))route+="&name="+Uri.EscapeDataString(query.Trim());if(tag.Length>0)route+="&tag="+Uri.EscapeDataString(tag);if(country.Length>0)route+="&countrycode="+Uri.EscapeDataString(country.ToUpperInvariant());
        var result=await FetchAsync(route,token);return new(result.Items,offset,48,result.Count>=48&&offset+48<=10000);
    }
    public async Task<RadioStation> StationAsync(string user,string id,CancellationToken token)
    {
        if(!Guid.TryParseExact(id,"D",out var guid))throw new DomainException("Radyo kimliği geçersiz.");
        var saved=Favorites(user).FirstOrDefault(f=>f.Station.Id==guid.ToString());if(saved is not null)return saved.Station;
        if(!preferences.Read(user).RadioDirectory)throw new DomainException("Radyo kataloğu müzik ayarlarından kapatıldı.",409);
        return (await FetchAsync("stations/byuuid/"+guid,token)).Items.FirstOrDefault(s=>s.Id==guid.ToString())??throw new DomainException("Radyo istasyonu bulunamadı.",404);
    }
    private async Task<(RadioStation[] Items,int Count)> FetchAsync(string route,CancellationToken token)
    {
        await _gate.WaitAsync(token);try
        {
            if(_cache.TryGetValue(route,out var cached)&&DateTimeOffset.UtcNow-cached.At<TimeSpan.FromMinutes(15))return(cached.Items,cached.Count);
            foreach(var host in Mirrors.OrderBy(_=>Random.Shared.Next()))
            {
                try
                {
                    using var request=new HttpRequestMessage(HttpMethod.Get,"https://"+host+"/json/"+route);request.Headers.UserAgent.ParseAdd("Home-Music/0.41.0");using var response=await _http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token);if(!response.IsSuccessStatusCode)continue;
                    using var readTimeout=CancellationTokenSource.CreateLinkedTokenSource(token);readTimeout.CancelAfter(TimeSpan.FromSeconds(15));await using var stream=await response.Content.ReadAsStreamAsync(readTimeout.Token);var bytes=await Metadata.MetadataProvider.ReadLimitedAsync(stream,2*1024*1024,readTimeout.Token);var stations=Parse(bytes);using var json=JsonDocument.Parse(bytes);var count=json.RootElement.GetArrayLength();if(_cache.Count>=64)_cache.Remove(_cache.MinBy(p=>p.Value.At).Key);_cache[route]=(DateTimeOffset.UtcNow,stations,count);return(stations,count);
                }catch(HttpRequestException){}catch(JsonException){}catch(DomainException error)when(error.StatusCode is 413 or 502){}catch(OperationCanceledException)when(!token.IsCancellationRequested){}
            }
            throw new DomainException("Radyo kataloğuna şu anda ulaşılamıyor.",502);
        }finally{_gate.Release();}
    }
    public static RadioStation[] Parse(byte[] bytes)
    {
        using var json=JsonDocument.Parse(bytes,new JsonDocumentOptions{MaxDepth=16});if(json.RootElement.ValueKind!=JsonValueKind.Array)throw new DomainException("Radyo kataloğu yanıtı geçersiz.",502);var items=new List<RadioStation>();
        foreach(var item in json.RootElement.EnumerateArray().Take(100))
        {
            if(item.ValueKind!=JsonValueKind.Object)continue;
            string Text(string key,int max=512)=>item.TryGetProperty(key,out var value)&&value.ValueKind==JsonValueKind.String&&value.GetString() is{} text&&text.Length<=max&&!text.Any(char.IsControl)?text:"";
            int Number(string key)=>item.TryGetProperty(key,out var value)&&value.ValueKind==JsonValueKind.Number&&value.TryGetInt32(out var number)?number:0;
            if(item.ValueKind!=JsonValueKind.Object||!Guid.TryParseExact(Text("stationuuid"),"D",out var id)||Number("lastcheckok")!=1)continue;var name=Text("name",250);var url=Text("url_resolved",8192);if(url.Length==0)url=Text("url",8192);if(name.Length==0||!ValidPublicUrl(url))continue;
            var codec=Text("codec",40);if(codec.Contains("H264",StringComparison.OrdinalIgnoreCase))continue;
            items.Add(new(id.ToString(),name,url,Text("countrycode",2),Text("language",100),Text("tags",2000).Split(',',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).Where(t=>t.Length<=40).Select(t=>t.ToLowerInvariant()).Distinct().Take(12).ToArray(),codec,Math.Clamp(Number("bitrate"),0,2000),Number("hls")==1||new Uri(url).AbsolutePath.EndsWith(".m3u8",StringComparison.OrdinalIgnoreCase),ValidPublicUrl(Text("homepage",2048))?Text("homepage",2048):null));
        }
        return items.DistinctBy(s=>s.Id).Take(48).ToArray();
    }
    public static bool ValidPublicUrl(string url)
    {try{if(string.IsNullOrWhiteSpace(url))return false;var uri=RemoteMediaHttp.Validate(url);return uri.Port is >0 and <=65535&&(!IPAddress.TryParse(uri.Host,out var ip)||RemoteMediaHttp.Allowed(ip,false))&&!uri.Host.TrimEnd('.').Equals("localhost",StringComparison.OrdinalIgnoreCase)&&!uri.Host.TrimEnd('.').EndsWith(".localhost",StringComparison.OrdinalIgnoreCase)&&!uri.Host.TrimEnd('.').EndsWith(".local",StringComparison.OrdinalIgnoreCase);}catch(DomainException){return false;}}
    private string FilePath(string user){if(!Gaming.NativeSystems.SafeSegment(user))throw new DomainException("Radyo profili geçersiz.");return Path.Combine(paths.State,"music","radio",user+".json");}
    public RadioFavorite[] Favorites(string user)
    {lock(_files){var path=FilePath(user);if(!File.Exists(path))return [];path=MediaPaths.ResolveFile(paths.State,Path.GetRelativePath(paths.State,path));if(new FileInfo(path).Length>4*1024*1024)throw new DomainException("Radyo listesi çok büyük.",409);try{var items=JsonSerializer.Deserialize<RadioFavorite[]>(File.ReadAllText(path),Json)??[];return items.Where(f=>f?.Station is not null&&ValidPublicUrl(f.Station.Url)).Take(300).ToArray();}catch(JsonException){throw new DomainException("Radyo listesi okunamadı.",409);}}}
    public void Favorite(string user,RadioStation station,RadioFavoriteCommand input)
    {lock(_files){if(input.Category is null||input.Category.Length>50||input.Category.Any(char.IsControl))throw new DomainException("Radyo kategorisi geçersiz.");var items=Favorites(user).Where(f=>f.Station.Id!=station.Id).ToList();if(input.Favorite){if(items.Count>=300)throw new DomainException("En fazla 300 radyo favorisi saklanabilir.",409);items.Add(new(station,input.Category.Trim()));}AppFiles.DirectoryIn(paths.State,"music","radio");PackageTrust.AtomicJson(FilePath(user),items);}}
    public async Task ReportPlayAsync(string user,string station,CancellationToken token)
    {
        if(!preferences.Read(user).RadioDirectory)return;
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(2));
        try{using var request=new HttpRequestMessage(HttpMethod.Get,"https://"+Mirrors[Random.Shared.Next(Mirrors.Length)]+"/json/url/"+Guid.Parse(station));request.Headers.UserAgent.ParseAdd("Home-Music/"+BuildInfo.Version);using var response=await _http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,timeout.Token);}
        catch(Exception error)when(error is HttpRequestException or OperationCanceledException){}
    }
    public void Dispose(){_http.Dispose();_gate.Dispose();}
}
