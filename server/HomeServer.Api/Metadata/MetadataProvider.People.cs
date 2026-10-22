using System.Text.Json;
using HomeServer.Core;

namespace HomeServer.Metadata;

public sealed partial class MetadataProvider
{
    public async Task<CatalogPerson[]> SearchPeopleAsync(string provider,string query,int page,CancellationToken token)
    {
        Validate(provider,"show");if(query.Length is <2 or >120 || page is <1 or >500)throw new DomainException("Kişi araması geçersiz.");
        using var json=await GetAsync(provider,provider=="tmdb"?$"search/person?query={Uri.EscapeDataString(query)}&page={page}&language={settings.Language}&include_adult=false":$"search/people?q={Uri.EscapeDataString(query)}",token);
        return (provider=="tmdb"?json.RootElement.GetProperty("results").EnumerateArray():json.RootElement.EnumerateArray()).Select(value=>Person(provider,provider=="tvmaze"?value.GetProperty("person"):value)).Take(100).ToArray();
    }
    public async Task<CatalogPerson> PersonAsync(string provider,int id,CancellationToken token)
    {
        Validate(provider,"show");if(id<=0)throw new DomainException("Kişi kimliği geçersiz.");using var json=await GetAsync(provider,provider=="tmdb"?$"person/{id}?language={settings.Language}":$"people/{id}",token);
        var person=Person(provider,json.RootElement);if(person.Id!=id)throw new DomainException("Kişi kimliği değişti.",502);return person;
    }
    public async Task<CatalogCredit[]> CreditsAsync(string provider,string kind,int id,CancellationToken token)
    {
        Validate(provider,kind);if(id<=0)throw new DomainException("İçerik kimliği geçersiz.");
        using var json=await GetAsync(provider,provider=="tmdb"?$"{(kind=="movie"?"movie":"tv")}/{id}/credits?language={settings.Language}":$"shows/{id}/cast",token);
        if(provider=="tvmaze")return json.RootElement.EnumerateArray().Take(200).Select(row=>new CatalogCredit(Person(provider,row.GetProperty("person")),Text(row.GetProperty("character"),"name")??"Oyuncu","Acting")).ToArray();
        var rows=new List<CatalogCredit>();foreach(var category in new[]{"cast","crew"})if(json.RootElement.TryGetProperty(category,out var array))foreach(var value in array.EnumerateArray().Take(300))rows.Add(new(Person(provider,value),Text(value,category=="cast"?"character":"job")??"",category=="cast"?"Acting":Text(value,"department")??""));return rows.ToArray();
    }
    public async Task<PersonWork[]> PersonWorksAsync(string provider,int id,string role,CancellationToken token)
    {
        Validate(provider,"show");if(id<=0 || role is not("all" or "cast" or "director" or "writer" or "crew"))throw new DomainException("Kişi/rol seçimi geçersiz.");var works=new List<PersonWork>();
        if(provider=="tmdb")
        {
            using var json=await GetAsync(provider,$"person/{id}/combined_credits?language={settings.Language}",token);
            foreach(var type in new[]{"cast","crew"})if(json.RootElement.TryGetProperty(type,out var items))foreach(var value in items.EnumerateArray())
            {
                var job=Text(value,"job")??"Actor";var department=Text(value,"department")??"Acting";if(role=="cast"&&type!="cast" || role=="crew"&&type!="crew" || role=="director"&&job!="Director" || role=="writer"&&department!="Writing")continue;
                var mediaType=Text(value,"media_type");if(mediaType is not("movie" or "tv"))continue;
                works.Add(new(ParseContent(provider,mediaType=="tv"?"show":"movie",value),[job]));if(works.Count>5000)throw new DomainException("Kişi filmografisi sınırı aşıyor.",413);
            }
        }
        else foreach(var category in new[]{"castcredits","crewcredits"})
        {
            if(role=="cast"&&category!="castcredits" || role=="crew"&&category!="crewcredits")continue;using var json=await GetAsync(provider,$"people/{id}/{category}?embed=show",token);
            foreach(var value in json.RootElement.EnumerateArray())
            {
                var job=Text(value,"type")??"Actor";if(role=="director"&&!job.Contains("Director",StringComparison.OrdinalIgnoreCase) || role=="writer"&&!job.Contains("Writer",StringComparison.OrdinalIgnoreCase))continue;
                if(!value.TryGetProperty("_embedded",out var embedded)||!embedded.TryGetProperty("show",out var show))continue;works.Add(new(ParseContent(provider,"show",show),[job]));if(works.Count>5000)throw new DomainException("Filmografi sınırı aşıyor.",413);
            }
        }
        return works.GroupBy(work=>(work.Metadata.Provider,work.Metadata.Kind,work.Metadata.ProviderId)).Select(group=>new PersonWork(group.First().Metadata,group.SelectMany(work=>work.Roles).Distinct().ToArray())).OrderByDescending(work=>work.Metadata.ReleaseDate).ToArray();
    }
    public async Task<CatalogFeedPage> CatalogFeedPageAsync(string provider,string kind,string externalId,string role,int page,CancellationToken token)
    {
        if(!int.TryParse(externalId,out var id)||id<1||page is <1 or >50)throw new DomainException("Kaynak liste kimliği veya sayfa geçersiz.");
        if(kind=="person") { var person=await PersonAsync(provider,id,token);var works=await PersonWorksAsync(provider,id,role,token);return new(person.Name,works.Skip((page-1)*50).Take(50).Select(work=>work.Metadata).ToArray(),page,page*50<works.Length); }
        if(provider!="tmdb"||kind is not("list" or "collection"))throw new DomainException("Liste/seri koleksiyonu TMDB kaynağı gerektirir.");
        using var json=await GetAsync(provider,$"{kind}/{id}?language={settings.Language}&page={page}",token);var root=json.RootElement;
        var key=kind=="collection"?"parts":"items";var rows=root.GetProperty(key).EnumerateArray().Select(value=>ParseContent(provider,Text(value,"media_type")=="tv"?"show":"movie",value)).ToArray();
        if(rows.Length>1000)throw new DomainException("Kaynak koleksiyonu 1000 kayıt sınırını aşıyor.",413);
        var more=kind=="list"&&root.TryGetProperty("total_pages",out var pages)&&pages.GetInt32()>page;
        return new(Text(root,"name")??"Sağlayıcı listesi",rows,page,more);
    }
    private static CatalogPerson Person(string provider,JsonElement value)
    {
        var id=value.GetProperty("id").GetInt32();return new(provider,id,Text(value,"name")??"",provider=="tmdb"?TmdbImage(Text(value,"profile_path"),"w185"):TvmazeImage(value,"medium"),Text(value,"known_for_department"),Text(value,"biography"),provider=="tmdb"?$"https://www.themoviedb.org/person/{id}":$"https://www.tvmaze.com/people/{id}");
    }
}
