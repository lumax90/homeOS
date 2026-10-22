using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using HomeServer.Core;

namespace HomeServer.Sources;

public static class ZnabParser
{
    private static XDocument Document(byte[] data)
    {
        using var stream=new MemoryStream(data);
        using var reader=XmlReader.Create(stream,new XmlReaderSettings {DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=8*1024*1024});
        try
        {
            var document=XDocument.Load(reader);
            if(document.Root?.Name.LocalName=="error")throw new DomainException((string?)document.Root.Attribute("code") is "100" or "101"?"Kaynak kimlik bilgilerini reddetti.":"Kaynak API isteğini reddetti.",502);
            return document;
        }
        catch(XmlException){throw new DomainException("Kaynak geçerli bir XML yanıtı döndürmedi.",502);}
    }
    public static IndexerCapabilities Capabilities(byte[] data)
    {
        var doc=Document(data);if(doc.Root?.Name.LocalName!="caps")throw new DomainException("Kaynak yetenek yanıtı (caps) bulunamadı.",502);
        var searching=doc.Root.Elements().FirstOrDefault(e=>e.Name.LocalName=="searching");
        var searches=searching?.Elements().Where(e=>string.Equals((string?)e.Attribute("available"),"yes",StringComparison.OrdinalIgnoreCase))
            .GroupBy(e=>e.Name.LocalName,StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g=>g.Key,g=>g.SelectMany(e=>((string?)e.Attribute("supportedParams")??"q").Split(',',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries))
                .Select(value=>value.ToLowerInvariant()).Distinct().ToArray(),StringComparer.OrdinalIgnoreCase)??[];
        var categories=new Dictionary<int,string>();
        foreach(var category in doc.Descendants().Where(e=>e.Name.LocalName is "category" or "subcat"))
            if(int.TryParse((string?)category.Attribute("id"),out var id))categories[id]=(string?)category.Attribute("name")??id.ToString();
        var limit=doc.Root.Elements().FirstOrDefault(e=>e.Name.LocalName=="limits");
        return new IndexerCapabilities(int.TryParse((string?)limit?.Attribute("max"),out var max)?Math.Clamp(max,1,1000):100,searches,categories);
    }
    public static (IReadOnlyList<RawRelease> Items,bool More) Releases(byte[] data,string protocol,Uri endpoint,int pageSize=100)
    {
        var doc=Document(data);if(doc.Root?.Name.LocalName!="rss")throw new DomainException("Kaynak RSS sonuçları döndürmedi.",502);
        var result=new List<RawRelease>();var seen=new HashSet<string>();
        var channel=doc.Root.Elements().FirstOrDefault(e=>e.Name.LocalName=="channel")??throw new DomainException("Kaynak RSS kanalı bulunamadı.",502);
        var items=channel.Elements().Where(e=>e.Name.LocalName=="item").Take(1000).ToArray();
        foreach(var item in items)
        {
            string? Field(string name)=>item.Elements().FirstOrDefault(e=>e.Name.LocalName==name)?.Value;
            var title=Field("title")?.Trim();if(string.IsNullOrEmpty(title)||title.Length>2000)continue;
            var attributes=item.Elements().Where(e=>e.Name.LocalName=="attr"&&e.Attribute("name") is not null)
                .GroupBy(e=>((string)e.Attribute("name")!).ToLowerInvariant()).ToDictionary(g=>g.Key,g=>g.Select(e=>(string?)e.Attribute("value")??"").Distinct().ToArray());
            string? Attr(string name)=>attributes.TryGetValue(name,out var values)?values.FirstOrDefault():null;
            var enclosure=item.Elements().FirstOrDefault(e=>e.Name.LocalName=="enclosure");
            var url=Attr("magneturl")??(string?)enclosure?.Attribute("url")??Field("link");
            if(string.IsNullOrWhiteSpace(url)||url.Length>16000)continue;
            if(!Uri.TryCreate(endpoint,url,out var absolute))continue;
            if(absolute.Scheme is not ("magnet" or "http" or "https"))continue;
            var guid=Field("guid")??Attr("guid")??absolute.ToString();if(!seen.Add(guid))continue;
            long? size=long.TryParse(Attr("size")??(string?)enclosure?.Attribute("length"),out var s)&&s>=0?s:null;
            int? seeders=int.TryParse(Attr("seeders"),out var peers)&&peers>=0?peers:null;
            var published=DateTimeOffset.TryParse(Field("pubDate"),CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out var date)?date.ToUniversalTime().ToString("O"):null;
            var hash=Attr("infohash")?.Trim().ToLowerInvariant();
            if(hash is not null&&(hash.Length is not(40 or 64)||hash.Any(c=>!Uri.IsHexDigit(c))))hash=null;
            result.Add(new RawRelease(guid,title,protocol=="torznab"?"torrent":"usenet",size,seeders,published,absolute.ToString(),hash,attributes));
        }
        var response=doc.Descendants().FirstOrDefault(e=>e.Name.LocalName=="response");
        var more=int.TryParse((string?)response?.Attribute("total"),out var total)&&int.TryParse((string?)response?.Attribute("offset"),out var offset)
            ? (long)offset+items.Length<total : items.Length>=pageSize;
        return (result,more);
    }
}
