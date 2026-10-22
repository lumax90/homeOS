using System.Globalization;
using System.Text.Json;
using HomeServer.Core;

namespace HomeServer.Sources;

public static partial class SourceAdapterRegistry
{
    public static Dictionary<string,string?> ProbeParameters(SourceAdapterManifest manifest) => manifest.Format is "apibay" or "torrents-csv"
        ? new() { ["t"] = "search", ["q"] = "Big Buck Bunny", ["offset"] = "0" }
        : new() { ["t"] = "search", ["q"] = "", ["offset"] = "0" };
    private static void NativeQuery(SourceAdapterManifest manifest, Dictionary<string,string?> input, Dictionary<string,string?> query, string search, int offset)
    {
        var page = offset / manifest.PageSize + 1;
        var imdb = input.GetValueOrDefault("imdbid"); if (imdb?.StartsWith("tt", StringComparison.OrdinalIgnoreCase) == true) imdb = imdb[2..];
        if (imdb is not null && (imdb.Length is < 1 or > 12 || !imdb.All(char.IsAsciiDigit))) throw new DomainException("Kaynağın IMDb sorgusu geçersiz.");
        switch (manifest.Format)
        {
            case "yts":
                query["query_term"] = imdb is null ? search : "tt" + imdb; query["limit"] = manifest.PageSize.ToString(CultureInfo.InvariantCulture); query["page"] = page.ToString(CultureInfo.InvariantCulture);
                query["sort_by"] = "date_added"; query["order_by"] = "desc"; break;
            case "eztv":
                if (!string.IsNullOrWhiteSpace(search) && imdb is null) throw new DomainException("EZTV dizi araması için yapımın IMDb kimliği gerekli.",409);
                query["limit"] = manifest.PageSize.ToString(CultureInfo.InvariantCulture); query["page"] = page.ToString(CultureInfo.InvariantCulture); if (imdb is not null) query["imdb_id"] = imdb; break;
            case "apibay":
                query["q"] = imdb is not null ? "tt" + imdb : string.IsNullOrWhiteSpace(search) ? "top100:video" : search; query["cat"] = input.GetValueOrDefault("t") == "music" ? "100" : "0"; break;
            case "torrents-csv":
                if (search.Trim().Length < 3) throw new DomainException("Torrents.csv araması için en az üç karakter gerekli.");
                query["q"] = search; if (input.GetValueOrDefault("after") is { } cursor) query["after"] = cursor; break;
        }
    }
    private static string? ScalarText(JsonElement row, string name) => row.ValueKind == JsonValueKind.Object && row.TryGetProperty(name, out var value) ? Text(value) : null;
    private static long? Number(JsonElement row, string name) => long.TryParse(ScalarText(row,name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value >= 0 ? value : null;
    private static int? Seeds(JsonElement row,string name) => Number(row,name) is { } value ? (int)Math.Min(int.MaxValue,value) : null;
    private static string? Hash(string? value) => value is { Length:40 } && value.All(char.IsAsciiHexDigit) && value.Any(character=>character!='0') ? value.ToLowerInvariant() : null;
    private static string Magnet(string hash,string title) => $"magnet:?xt=urn:btih:{hash}&dn={Uri.EscapeDataString(title)}"+string.Concat(HomeServer.Downloads.TorrentMetadataResolver.PublicTrackers.Select(value=>"&tr="+Uri.EscapeDataString(value)));
    private static string? Unix(JsonElement row,string key)
    {
        var seconds=Number(row,key); return seconds is >= 0 and <= 253402300799 ? DateTimeOffset.FromUnixTimeSeconds(seconds.Value).ToString("O") : null;
    }
    private static Dictionary<string,string[]> Attributes(string? imdb,string? language=null)
    {
        var result=new Dictionary<string,string[]>(); var id=imdb?.StartsWith("tt",StringComparison.OrdinalIgnoreCase)==true?imdb[2..]:imdb;
        if(id is{Length:>0 and<=12}&&id.All(char.IsAsciiDigit)&&id.Any(character=>character!='0'))result["imdbid"]=["tt"+id];
        if(language is{Length:>0 and<=20})result["language"]=[language]; return result;
    }
    private static AdapterReleasePage ParseNative(SourceAdapterManifest manifest,byte[] bytes,int offset)
    {
        using var json=JsonDocument.Parse(bytes); var root=json.RootElement; var results=new List<RawRelease>(); bool more; string? next=null;
        if(manifest.Format=="yts")
        {
            if(ScalarText(root,"status")!="ok"||!root.TryGetProperty("data",out var data)||Number(data,"movie_count") is not{}count)throw new DomainException("YTS arama yanıtı doğrulanamadı.",502);
            if(count==0)return new([],false);
            if(!data.TryGetProperty("movies",out var movies)||movies.ValueKind!=JsonValueKind.Array||movies.GetArrayLength()>50)throw new DomainException("YTS film dizisi eksik veya sınırı aşıyor.",502);
            foreach(var movie in movies.EnumerateArray())
            {
                var title=ScalarText(movie,"title"); var year=Number(movie,"year");
                if(string.IsNullOrWhiteSpace(title)||title.Length>800||!movie.TryGetProperty("torrents",out var torrents)||torrents.ValueKind!=JsonValueKind.Array)continue;
                foreach(var torrent in torrents.EnumerateArray().Take(20))
                {
                    var hash=Hash(ScalarText(torrent,"hash")); var size=Number(torrent,"size_bytes"); if(hash is null||size is not>0)continue;
                    var name=$"{title} ({year}) {ScalarText(torrent,"quality")} {ScalarText(torrent,"type")} {ScalarText(torrent,"video_codec")} -YTS";
                    results.Add(new(hash,name,"torrent",size,Seeds(torrent,"seeds"),Unix(torrent,"date_uploaded_unix"),Magnet(hash,name),hash,Attributes(ScalarText(movie,"imdb_code"),ScalarText(movie,"language"))));
                }
            }
            more=offset+movies.GetArrayLength()<count; return new(results.DistinctBy(value=>value.InfoHash).ToArray(),more);
        }
        JsonElement rows;
        if(manifest.Format=="apibay") { rows=root; more=false; }
        else
        {
            if(root.ValueKind!=JsonValueKind.Object||!root.TryGetProperty("torrents",out rows)||rows.ValueKind!=JsonValueKind.Array)throw new DomainException("Kaynağın torrent sonuç dizisi bulunamadı.",502);
            more=manifest.Format=="eztv"&&Number(root,"torrents_count") is{}count&&offset+rows.GetArrayLength()<count&&offset/manifest.PageSize+1<100;
            if(manifest.Format=="torrents-csv"&&Number(root,"next") is>0 and<=int.MaxValue){next=ScalarText(root,"next");more=rows.GetArrayLength()>0;}
        }
        if(rows.ValueKind!=JsonValueKind.Array||rows.GetArrayLength()>1000)throw new DomainException("Kaynağın sonuç dizisi sınırı geçersiz.",502);
        foreach(var row in rows.EnumerateArray().Take(100))
        {
            var title=ScalarText(row,manifest.Format=="eztv"?"title":"name"); var hash=Hash(ScalarText(row,manifest.Format=="eztv"?"hash":manifest.Format=="apibay"?"info_hash":"infohash"));
            var size=Number(row,manifest.Format=="apibay"?"size":"size_bytes"); if(string.IsNullOrWhiteSpace(title)||title.Length>1000||hash is null||size is not>0)continue;
            var date=Unix(row,manifest.Format=="eztv"?"date_released_unix":manifest.Format=="apibay"?"added":"created_unix");
            var attributes=Attributes(ScalarText(row,manifest.Format=="eztv"?"imdb_id":"imdb"));
            results.Add(new(hash,title,"torrent",size,Seeds(row,manifest.Format=="eztv"?"seeds":"seeders"),date,Magnet(hash,title),hash,attributes));
        }
        return new(results.DistinctBy(value=>value.InfoHash).ToArray(),more,next);
    }
}
