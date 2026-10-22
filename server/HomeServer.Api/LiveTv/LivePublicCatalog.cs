using System.Text;
using HomeServer.Core;
using HomeServer.Infrastructure;
using Microsoft.Extensions.Caching.Memory;

namespace HomeServer.LiveTv;

public sealed record LivePublicList(string Id,string Name,string Category,string PlaylistUrl,string Description,string Provider="IPTV-org",string License="Unlicense",string SourceUrl="https://github.com/iptv-org/iptv");
public sealed class LivePublicCatalog(ILiveHttp http,IMemoryCache cache,LiveSources sources,MediaStore store)
{
    public static readonly LivePublicList[] Lists=[
        new("iptv-tr","Türkiye","country","https://iptv-org.github.io/iptv/countries/tr.m3u","Türkiye’de yayın yapan kanallar."),
        new("iptv-us","Amerika Birleşik Devletleri","country","https://iptv-org.github.io/iptv/countries/us.m3u","Amerika’dan yerel ve ulusal yayınlar."),
        new("iptv-gb","Birleşik Krallık","country","https://iptv-org.github.io/iptv/countries/uk.m3u","Birleşik Krallık kanal listesi."),
        new("iptv-de","Almanya","country","https://iptv-org.github.io/iptv/countries/de.m3u","Almanya’dan açık yayınlar."),
        new("iptv-fr","Fransa","country","https://iptv-org.github.io/iptv/countries/fr.m3u","Fransa kanal listesi."),
        new("iptv-az","Azerbaycan","country","https://iptv-org.github.io/iptv/countries/az.m3u","Azerbaycan kanal listesi."),
        new("iptv-news","Haber","category","https://iptv-org.github.io/iptv/categories/news.m3u","Dünyadan haber yayınları."),
        new("iptv-music","Müzik","category","https://iptv-org.github.io/iptv/categories/music.m3u","Müzik kanalları ve klipler."),
        new("iptv-documentary","Belgesel","category","https://iptv-org.github.io/iptv/categories/documentary.m3u","Belgesel ve keşif kanalları."),
        new("iptv-kids","Çocuk","category","https://iptv-org.github.io/iptv/categories/kids.m3u","Çocuk ve animasyon kanalları."),
        new("iptv-sports","Spor","category","https://iptv-org.github.io/iptv/categories/sports.m3u","Açık spor yayınları."),
        new("iptv-relax","Dinlenme","category","https://iptv-org.github.io/iptv/categories/relax.m3u","Sakin manzaralar ve atmosfer yayınları.")
    ];
    private readonly SemaphoreSlim _gate=new(2,2);
    public LivePublicList Require(string id)=>Lists.FirstOrDefault(list=>list.Id==id)??throw new DomainException("Açık kanal listesi bulunamadı.",404);
    public object List(){var configured=store.LiveSources();return Lists.Select(list=>new{list,source=configured.FirstOrDefault(source=>source.PresetId==list.Id)}).ToArray();}
    public async Task<object> PreviewAsync(string id,CancellationToken token)
    {
        var list=Require(id);var key="live-public:"+id;if(cache.TryGetValue<object>(key,out var saved))return saved!;
        await _gate.WaitAsync(token);try{if(cache.TryGetValue<object>(key,out saved))return saved!;var input=await LiveHttpRead.ReadAsync(http,list.PlaylistUrl,false,8*1024*1024,token);var parsed=PlaylistGuide.Playlist(Encoding.UTF8.GetString(input.Bytes),input.Uri);var result=new{total=parsed.Length,groups=parsed.GroupBy(c=>c.Group).Select(group=>new{name=group.Key,count=group.Count()}).OrderByDescending(group=>group.count).Take(20).ToArray(),channels=parsed.Take(12).Select(channel=>new{channel.Name,channel.Group}).ToArray()};cache.Set(key,result,new MemoryCacheEntryOptions{Size=1,AbsoluteExpirationRelativeToNow=TimeSpan.FromMinutes(30)});return result;}finally{_gate.Release();}
    }
    public LiveSource Add(string id,Account actor){var list=Require(id);return sources.Save(null,new(list.Name+" · IPTV-org",PlaylistUrl:list.PlaylistUrl),actor,list.Id);}
}
