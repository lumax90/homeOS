using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HomeServer.Core;

namespace HomeServer.Gaming;

public sealed record RomFingerprint(string Name,long Bytes,string Sha256,string Md5,string Sha1);
public sealed record RomMetadataCandidate(long Id,string Name,string Platform,string Publisher,string? Description,string? Year,string[] Regions,string[] Languages,bool HashMatch=false,string? Edition=null,bool DetailsPartial=false);
internal sealed class HasheousRemoteException(int code):Exception("Hasheous returned a remote tool error.") { public int Code {get;}=code; }

/// <summary>Original client for Hasheous' documented public lookup/search tools. No server code or ROM payload is adopted.</summary>
public sealed class HasheousMetadataClient:IDisposable
{
    private readonly HttpClient _http;private readonly SemaphoreSlim _requests=new(1,1);private DateTimeOffset _next;private long _id;
    public HasheousMetadataClient(HttpMessageHandler? handler=null){_http=new(handler??new SocketsHttpHandler{UseProxy=false,UseCookies=false,AllowAutoRedirect=false}){Timeout=TimeSpan.FromSeconds(20)};_http.DefaultRequestHeaders.UserAgent.ParseAdd("Home-ROM-Metadata/0.40.0");}
    private async Task<JsonElement> CallAsync(string tool,object arguments,CancellationToken token)
    {
        await _requests.WaitAsync(token);try
        {
            var wait=_next-DateTimeOffset.UtcNow;if(wait>TimeSpan.Zero)await Task.Delay(wait,token);_next=DateTimeOffset.UtcNow.AddSeconds(1);var id=Interlocked.Increment(ref _id);
            using var request=new HttpRequestMessage(HttpMethod.Post,"https://hasheous.org/api/v1/Mcp"){Content=JsonContent.Create(new{jsonrpc="2.0",id,method="tools/call",@params=new{name=tool,arguments}})};request.Headers.Accept.ParseAdd("application/json");
            using var response=await _http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token);
            if(response.StatusCode==HttpStatusCode.TooManyRequests)throw new DomainException("Oyun bilgisi kaynağı meşgul. Biraz sonra yeniden dene.",429);
            if(!response.IsSuccessStatusCode||response.Content.Headers.ContentLength>2*1024*1024)throw new DomainException("Oyun bilgisi kaynağına erişilemiyor.",502);
            await using var stream=await response.Content.ReadAsStreamAsync(token);using var data=new MemoryStream();var buffer=new byte[8192];int read;while((read=await stream.ReadAsync(buffer,token))>0){if(data.Length+read>2*1024*1024)throw new DomainException("Oyun bilgisi yanıtı çok büyük.",502);data.Write(buffer,0,read);}
            try
            {
                using var json=JsonDocument.Parse(data.ToArray(),new JsonDocumentOptions{MaxDepth=32});var root=json.RootElement;if(root.ValueKind!=JsonValueKind.Object||!root.TryGetProperty("id",out var answerId)||answerId.ValueKind!=JsonValueKind.Number||!answerId.TryGetInt64(out var actual)||actual!=id)throw new DomainException("Oyun bilgisi kaynağı yanıtı geçersiz.",502);
                if(root.TryGetProperty("error",out var remote)&&remote.ValueKind==JsonValueKind.Object&&remote.TryGetProperty("code",out var code)&&code.ValueKind==JsonValueKind.Number&&code.TryGetInt32(out var errorCode))throw new HasheousRemoteException(errorCode);
                if(!root.TryGetProperty("result",out var result)||result.ValueKind!=JsonValueKind.Object)throw new DomainException("Oyun bilgisi kaynağı yanıtı geçersiz.",502);
                if(result.TryGetProperty("isError",out var error)&&error.ValueKind==JsonValueKind.True)throw new HasheousRemoteException(-32000);
                if(!result.TryGetProperty("structuredContent",out var payload)||payload.ValueKind!=JsonValueKind.Object||payload.TryGetProperty("error",out _))throw new DomainException("Oyun bilgisi kaynağı yanıtı geçersiz.",502);return payload.Clone();
            }catch(JsonException){throw new DomainException("Oyun bilgisi kaynağı yanıtı geçersiz.",502);}
        }finally{_requests.Release();}
    }
    public async Task<RomMetadataCandidate[]> LookupAsync(GameRom rom,RomFingerprint fingerprint,CancellationToken token)
    {
        JsonElement result;try{result=await CallAsync("hasheous_lookup_hashes",new{md5=fingerprint.Md5,sha1=fingerprint.Sha1,sha256=fingerprint.Sha256,limit=20},token);}catch(HasheousRemoteException){throw new DomainException("Oyun bilgisi kaynağı geçici bir hata döndürdü. Otomatik olarak yeniden denenecek.",502);}
        return Games(result).Where(g=>PlatformMatches(rom.System,Text(g,"platform"))).Select(g=>Parse(g,HasMatchingRom(g,fingerprint))).Where(g=>g.Id>0&&g.Name.Length>0).ToArray();
    }
    public async Task<RomMetadataCandidate[]> SearchAsync(string system,string query,CancellationToken token)
    {
        if(string.IsNullOrWhiteSpace(query)||query.Trim().Length is <3 or >160||query.Any(char.IsControl))throw new DomainException("Oyun araması için en az üç karakter yaz.",400);
        var platform=system switch{"nes"=>"Nintendo Entertainment System","megadrive"=>"Mega Drive",_=>NativeSystems.Require(system).Name};JsonElement result;try{result=await CallAsync("hasheous_search_games",new{name=query.Trim(),platform,limit=25,includeRoms=false},token);}catch(HasheousRemoteException){throw new DomainException("Oyun bilgisi kaynağı geçici bir hata döndürdü. Otomatik olarak yeniden denenecek.",502);}
        return Games(result).Where(g=>PlatformMatches(system,Text(g,"platform"))).Select(g=>Parse(g,false)).Where(g=>g.Id>0&&g.Name.Length>0).ToArray();
    }
    public async Task<RomMetadataCandidate> DetailsAsync(string system,RomMetadataCandidate candidate,CancellationToken token)
    {
        try
        {
            var result=await CallAsync("hasheous_get_game_details",new{gameId=candidate.Id},token);if(!result.TryGetProperty("game",out var game)||game.ValueKind!=JsonValueKind.Object)throw new DomainException("Oyun bilgisi kaynağı yanıtı geçersiz.",502);
            var parsed=Parse(game,candidate.HashMatch);if(parsed.Id!=candidate.Id||!PlatformMatches(system,parsed.Platform))throw new DomainException("Oyun bilgisi seçilen konsolla eşleşmiyor.",409);return parsed;
        }
        catch(HasheousRemoteException error)when(error.Code==-32000)
        {
            // Hosted details currently fails while attaching a JsonNode already parented to an array.
            // Its public search returns the same identity/details fields. Require the SAME provider ID;
            // never substitute a similarly named edition or turn malformed data into a successful match.
            var found=(await SearchAsync(system,RomMatching.SearchTitle(candidate.Name),token)).FirstOrDefault(item=>item.Id==candidate.Id);
            if(found is null||!PlatformMatches(system,found.Platform))throw new DomainException("Seçilen oyun kaynağında artık bulunamadı. Eşleşmeleri yenile.",409);
            return found with{HashMatch=candidate.HashMatch,DetailsPartial=true};
        }
        catch(HasheousRemoteException){throw new DomainException("Oyun bilgisi kaynağı geçici bir hata döndürdü. Otomatik olarak yeniden denenecek.",502);}
    }
    private static IEnumerable<JsonElement> Games(JsonElement result)=>result.TryGetProperty("games",out var games)&&games.ValueKind==JsonValueKind.Array?games.EnumerateArray().Take(25).Where(g=>g.ValueKind==JsonValueKind.Object):[];
    private static bool HasMatchingRom(JsonElement game,RomFingerprint fingerprint)=>game.TryGetProperty("matchedRoms",out var roms)&&roms.ValueKind==JsonValueKind.Array&&roms.EnumerateArray().Take(10).Any(rom=>rom.ValueKind==JsonValueKind.Object&&rom.TryGetProperty("size",out var size)&&size.ValueKind==JsonValueKind.Number&&size.TryGetInt64(out var bytes)&&bytes==fingerprint.Bytes&&(Same(Text(rom,"sha256"),fingerprint.Sha256)||Same(Text(rom,"sha1"),fingerprint.Sha1)||Same(Text(rom,"md5"),fingerprint.Md5)));
    private static bool Same(string a,string b)=>a.Length>0&&a.Equals(b,StringComparison.OrdinalIgnoreCase);
    private static RomMetadataCandidate Parse(JsonElement game,bool matched)
    {
        var name=Text(game,"name");var description=NullText(game,"description",6000);if(description is not null&&RomMatching.TitleKey(description)==RomMatching.TitleKey(name))description=null;
        var rawYear=NullText(game,"year",32);var year=rawYear is null?null:System.Text.RegularExpressions.Regex.Match(rawYear,@"\b(?:19|20)\d{2}\b").Value;
        return new(game.TryGetProperty("id",out var id)&&id.ValueKind==JsonValueKind.Number&&id.TryGetInt64(out var value)?value:0,name,Text(game,"platform"),Text(game,"publisher"),description,string.IsNullOrEmpty(year)?null:year,List(game,"countries","country"),List(game,"languages","language"),matched,NullText(game,"category",80));
    }
    private static string? NullText(JsonElement item,string key,int max)=>Text(item,key,max) is{Length:>0} text?text:null;
    private static string Text(JsonElement item,string key,int max=256)
    {if(!item.TryGetProperty(key,out var value))return "";if(value.ValueKind==JsonValueKind.Object&&value.TryGetProperty("name",out var name))value=name;if(value.ValueKind is not(JsonValueKind.String or JsonValueKind.Number))return "";var text=value.ToString().Trim();return text.Length<=max&&!text.Any(c=>char.IsControl(c)&&c is not('\n' or '\r' or '\t'))?text:"";}
    private static string[] List(JsonElement game,string plural,string singular)
    {if(game.TryGetProperty(plural,out var list)&&list.ValueKind==JsonValueKind.Array)return list.EnumerateArray().Where(v=>v.ValueKind==JsonValueKind.String).Select(v=>v.GetString()!).Where(s=>s.Length<=64&&!s.Any(char.IsControl)).Take(16).ToArray();var text=Text(game,singular);return text.Length>0?[text]:[];}
    public static bool PlatformMatches(string system,string platform)
    {
        static string Key(string text)=>new(text.ToLowerInvariant().Where(char.IsAsciiLetterOrDigit).ToArray());var key=Key(platform);
        var names=system switch{"gb"=>new[]{"Game Boy","Nintendo Game Boy"},"gbc"=>["Game Boy Color","Nintendo Game Boy Color"],"gba"=>["Game Boy Advance","Nintendo Game Boy Advance"],"nes"=>["Nintendo Entertainment System","Nintendo NES","NES","Nintendo Famicom"],"snes"=>["Super Nintendo Entertainment System","Nintendo Super Nintendo Entertainment System","Super Nintendo","SNES","Nintendo Super Famicom"],"megadrive"=>["Sega Mega Drive","Sega Genesis","Mega Drive Genesis","Sega Mega Drive Genesis"],"ps1"=>["Sony PlayStation","PlayStation","Sony PlayStation 1","PlayStation 1","PSX"],_=>new[]{NativeSystems.Require(system).Name,NativeSystems.Require(system).ArtworkDatabase??""}};
        return key.Length>0&&(names.Any(name=>Key(name)==key)||Key(NativeSystems.Require(system).ArtworkDatabase??"")==key);
    }
    public void Dispose(){_http.Dispose();_requests.Dispose();}
}
