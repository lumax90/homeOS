using System.Security.Cryptography;
using System.Text.Json;
using HomeServer.Core;
using HomeServer.Infrastructure;

namespace HomeServer.Sources;

public sealed class SourceCatalog(MediaStore store)
{
    private static SourceProvider Torrent(string id,string name,string description,string endpoint,string homepage,string help,params string[] kinds)
        =>new(id,name,description,"torznab","public",kinds,endpoint,id,homepage,help,false);
    private static SourceProvider Usenet(string id,string name,string endpoint,string homepage)
        =>new(id,name,"Üyeliğine ait API anahtarıyla film, dizi ve diğer Usenet yayınlarını ara.","newznab","account",["movie","show","music","book"],endpoint,null,homepage,homepage,true);
    public static SourceProvider[] Builtins =>
    [
        Torrent("yts","YTS","Filmler; IMDb kimliğiyle arama ve farklı çözünürlüklerde torrent sürümleri.","https://movies-api.accel.li/","https://yts.gg/","https://yts.gg/api","movie"),
        Torrent("eztv","EZTV","Diziler; Home'daki IMDb kimliği üzerinden bölüm yayınlarını bulur.","https://eztvx.to/","https://eztvx.to/","https://eztvx.to/api/","show"),
        Torrent("the-pirate-bay","The Pirate Bay","API Bay üzerinden genel torrent araması. Sağlayıcının döndürdüğü sınırlı sonuç listesi kullanılır.","https://apibay.org/","https://thepiratebay.org/","https://thepiratebay.org/","movie","show","music"),
        Torrent("torrents-csv","Torrents.csv","Genel torrent araması; sonuçlar cursor ile sayfalanır. En az üç karakterle arama yapar.","https://torrents-csv.com/","https://torrents-csv.com/","https://codeberg.org/heretic/torrents-csv-server","movie","show","music"),
        Torrent("nyaa-rss","Nyaa","Anime ve ilgili yayınlar için doğrudan RSS araması.","https://nyaa.si/","https://nyaa.si/","https://nyaa.si/","movie","show","anime","music","book"),
        Torrent("internet-archive","Internet Archive","Torrent bağlantısı sunan video, ses ve kitap arşivi kayıtları.","https://archive.org/","https://archive.org/","https://archive.org/developers/","movie","show","music","book"),
        Usenet("nzbgeek","NZBgeek","https://api.nzbgeek.info/api","https://nzbgeek.info/"),
        Usenet("drunkenslug","DrunkenSlug","https://drunkenslug.com/api","https://drunkenslug.com/"),
        Usenet("nzbfinder","NZB Finder","https://nzbfinder.ws/api","https://nzbfinder.ws/"),
        Usenet("nzbplanet","NZBPlanet","https://api.nzbplanet.net/api","https://nzbplanet.net/"),
        Usenet("dognzb","DOGnzb","https://api.dognzb.cr/api","https://dognzb.cr/"),
        Usenet("nzblife","NZB.life / NZB.su","https://api.nzb.life/api","https://nzb.life/")
    ];
    public IReadOnlyList<SourceProvider> List()=>Builtins.Concat(store.SourceAdapters().Select(manifest=>new SourceProvider("adapter:"+manifest.Id,manifest.Name,"Yüklediğin Home adaptörü. Kaynağın adresini ve gerekiyorsa API anahtarını gir.",manifest.Protocol=="torrent"?"torznab":"newznab",manifest.Authentication=="none"?"public":"account",["movie","show","music","book"],null,manifest.Id,manifest.Homepage,manifest.Homepage,manifest.Authentication!="none",true))).ToArray();
    public SourceProvider Find(string id)
    {
        if(id is "custom-torznab" or "custom-newznab")return new(id,id=="custom-torznab"?"Özel torrent bağlantısı":"Özel Usenet bağlantısı","Kendi Torznab/Newznab API bağlantın.",id=="custom-torznab"?"torznab":"newznab","custom",["movie","show","music","book"],null,null,"","",false);
        return List().FirstOrDefault(value=>value.Id==id)??throw new DomainException("Hazır kaynak tanımı bulunamadı.",404);
    }
    public string Fingerprint(SourceProvider provider)=>Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new{provider,adapter=provider.AdapterId is null?null:SourceAdapterRegistry.Find(provider.AdapterId,store)})));
}
