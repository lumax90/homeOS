using System.Text.Json;
using System.Text.RegularExpressions;
using HomeServer.Core;
using Microsoft.Extensions.Caching.Memory;

namespace HomeServer.Metadata;

public sealed partial class MetadataProvider
{
    public async Task<CatalogMedia> MediaAsync(string provider,string kind,int id,CancellationToken token)
    {
        Validate(provider,kind);if(id<=0)throw new DomainException("İçerik kimliği geçersiz.");
        var key=$"content-media:{provider}:{kind}:{id}:{settings.Language}";
        if(cache.TryGetValue<CatalogMedia>(key,out var cached))return cached!;
        CatalogTrailer[] trailers=[];CatalogImage[] images=[];string? trailerError=null,imageError=null;
        if(provider=="tmdb")
        {
            try
            {
                var prefix=$"{(kind=="movie"?"movie":"tv")}/{id}/videos";
                using var videos=await GetAsync(provider,prefix+"?language="+Uri.EscapeDataString(settings.Language),token);
                trailers=ReadTrailers(videos.RootElement,id);
                if(trailers.Length==0&&!settings.Language.Equals("en-US",StringComparison.OrdinalIgnoreCase))
                {using var fallback=await GetAsync(provider,prefix+"?language=en-US",token);trailers=ReadTrailers(fallback.RootElement,id);}
            }
            catch(DomainException error){trailerError=error.Message;}
        }
        try
        {
            using var gallery=await GetAsync(provider,provider=="tmdb"?$"{(kind=="movie"?"movie":"tv")}/{id}/images":$"shows/{id}/images",token);
            images=ReadGallery(provider,gallery.RootElement,id);
        }
        catch(DomainException error){imageError=error.Message;}
        var result=new CatalogMedia(trailers,images,trailerError,imageError);
        if(trailerError is null&&imageError is null)cache.Set(key,result,new MemoryCacheEntryOptions{AbsoluteExpirationRelativeToNow=TimeSpan.FromHours(4),Size=1});
        return result;
    }
    public static CatalogTrailer[] ReadTrailers(JsonElement root,int expectedId)
    {
        if(root.ValueKind!=JsonValueKind.Object||!root.TryGetProperty("id",out var id)||id.ValueKind!=JsonValueKind.Number||!id.TryGetInt32(out var actual)||actual!=expectedId
            ||!root.TryGetProperty("results",out var results)||results.ValueKind!=JsonValueKind.Array)throw new DomainException("Fragman bilgisi içerikle eşleşmedi.",502);
        var rows=new List<CatalogTrailer>();
        foreach(var value in results.EnumerateArray().Take(300))
        {
            if(value.ValueKind!=JsonValueKind.Object)continue;
            var key=Text(value,"key");var site=Text(value,"site");var type=Text(value,"type");
            if(site!="YouTube"||type is not("Trailer" or "Teaser")||key is null||!YouTubeKey().IsMatch(key))continue;
            var official=value.TryGetProperty("official",out var flag)&&flag.ValueKind==JsonValueKind.True;
            rows.Add(new(key,Text(value,"name")??type,site,type,official,Text(value,"iso_639_1")));
        }
        return rows.DistinctBy(row=>row.Key).OrderByDescending(row=>row.Official).ThenBy(row=>row.Type!="Trailer").Take(12).ToArray();
    }
    public static CatalogImage[] ReadGallery(string provider,JsonElement root,int expectedId)
    {
        var result=new List<CatalogImage>();
        if(provider=="tmdb")
        {
            if(root.ValueKind!=JsonValueKind.Object||!root.TryGetProperty("id",out var id)||id.ValueKind!=JsonValueKind.Number||!id.TryGetInt32(out var actual)||actual!=expectedId
                ||!root.TryGetProperty("backdrops",out var images)||images.ValueKind!=JsonValueKind.Array)throw new DomainException("Görsel bilgisi içerikle eşleşmedi.",502);
            foreach(var value in images.EnumerateArray().Take(300))
            {
                if(value.ValueKind!=JsonValueKind.Object)continue;
                var url=TmdbImage(Text(value,"file_path"),"w1280");var preview=TmdbImage(Text(value,"file_path"),"w780");
                var width=Dimension(value,"width");var height=Dimension(value,"height");
                if(url is not null&&preview is not null&&width>=200&&height>0&&width/(double)height>=1.3)result.Add(new(url,preview,width,height));
            }
        }
        else if(provider=="tvmaze"&&root.ValueKind==JsonValueKind.Array)
        {
            foreach(var value in root.EnumerateArray().Take(300))
            {
                if(value.ValueKind!=JsonValueKind.Object)continue;
                if(Text(value,"type") is not("background" or "banner")||!value.TryGetProperty("resolutions",out var resolutions)
                    ||resolutions.ValueKind!=JsonValueKind.Object||!resolutions.TryGetProperty("original",out var original)||original.ValueKind!=JsonValueKind.Object)continue;
                var url=Text(original,"url");var width=Dimension(original,"width");var height=Dimension(original,"height");
                if(AllowedImage(url)&&width>=200&&height>0&&width/(double)height>=1.3)result.Add(new(url!,url!,width,height));
            }
        }
        else throw new DomainException("Görsel sağlayıcısı geçersiz.");
        return result.DistinctBy(value=>value.Url).Take(24).ToArray();
    }
    private static int Dimension(JsonElement value,string name)=>value.TryGetProperty(name,out var field)&&field.ValueKind==JsonValueKind.Number&&field.TryGetInt32(out var dimension)&&dimension is >0 and <=20000?dimension:0;
    [GeneratedRegex(@"^[a-zA-Z0-9_-]{11}$")]private static partial Regex YouTubeKey();
}
