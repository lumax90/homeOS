using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using HomeServer.Core;
using HomeServer.Infrastructure;

namespace HomeServer.LiveTv;
public static partial class PlaylistGuide
{
 public static string Id(string value)=>Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..32];
 public static ParsedChannel[] Playlist(string text,Uri? source=null)
 {
   if(text.Length>8*1024*1024||!text.TrimStart('\uFEFF','\r','\n',' ').StartsWith("#EXTM3U",StringComparison.OrdinalIgnoreCase))throw new DomainException("Geçerli M3U kanal listesi gerekli.",422);
   if(text.Contains("#EXT-X-TARGETDURATION",StringComparison.OrdinalIgnoreCase)||text.Contains("#EXT-X-STREAM-INF",StringComparison.OrdinalIgnoreCase))throw new DomainException("Bu adres bir kanal yayını. Tek kanal bağlantısı olarak ekle.",422);
   var result=new List<ParsedChannel>();string? name=null;var attributes=new Dictionary<string,string>();var headers=new Dictionary<string,string>();var entries=0;
   foreach(var raw in text.Split('\n'))
   {
    var line=raw.Trim();if(line.StartsWith("#EXTINF:",StringComparison.OrdinalIgnoreCase)){if(++entries>10000)throw new DomainException("M3U 10.000 kanal sınırını aşıyor.",413);headers=new();attributes=new(StringComparer.OrdinalIgnoreCase);var comma=Separator(line);var header=comma<0?line:line[..comma];foreach(Match match in Attribute().Matches(header))attributes[match.Groups[1].Value]=match.Groups[2].Success?match.Groups[2].Value:match.Groups[3].Value;name=comma>=0?line[(comma+1)..].Trim():attributes.GetValueOrDefault("tvg-name")??"";continue;}
    if(name is not null&&line.StartsWith("#EXTVLCOPT:",StringComparison.OrdinalIgnoreCase)){var at=line.IndexOf('=');if(at>11)Header(headers,line[11..at],line[(at+1)..]);continue;}
    if(name is null||line.Length==0||line.StartsWith('#'))continue;
    var pipe=line.IndexOf('|');if(pipe>=0){foreach(var part in line[(pipe+1)..].Split('&')){var equal=part.IndexOf('=');if(equal>0)Header(headers,Uri.UnescapeDataString(part[..equal]),Uri.UnescapeDataString(part[(equal+1)..]).Replace('+',' '));}line=line[..pipe];}
    if(!Uri.TryCreate(source is null?line:new Uri(source,line).AbsoluteUri,UriKind.Absolute,out var candidate))throw new DomainException("Medya kaynağı için HTTP/HTTPS adresi gerekli.",422);
    if(candidate.Scheme is "rtmp" or "rtmps" or "rtsp" or "srt" or "udp" or "rtp"){name=null;continue;}
    // URI fragments are client hints and are never part of an HTTP request target.
    if(candidate.Scheme is "http" or "https"&&candidate.Fragment.Length>0)candidate=new UriBuilder(candidate){Fragment=""}.Uri;
    var uri=RemoteMediaHttp.Validate(candidate.AbsoluteUri);var epg=attributes.GetValueOrDefault("tvg-id");var group=attributes.GetValueOrDefault("group-title")??"";var title=string.IsNullOrWhiteSpace(name)?attributes.GetValueOrDefault("tvg-name")??uri.Host:name;
    if(epg?.Length>300||title.Any(char.IsControl)||group.Any(char.IsControl))throw new DomainException("Kanal bilgileri geçersiz.",422);
    string? logo=null;if(attributes.GetValueOrDefault("tvg-logo") is{Length:>0} image)try{logo=RemoteMediaHttp.Validate(source is null?image:new Uri(source,image).AbsoluteUri).AbsoluteUri;}catch(Exception ex)when(ex is DomainException or UriFormatException){}
    result.Add(new(Id((epg??"")+"|"+title+"|"+group),title[..Math.Min(300,title.Length)],group[..Math.Min(160,group.Length)],epg,logo,uri.AbsoluteUri,attributes.GetValueOrDefault("radio")?.Equals("true",StringComparison.OrdinalIgnoreCase)==true,result.Count+1,headers.Count>0?headers:null));name=null;if(result.Count>10000)throw new DomainException("M3U 10.000 kanal sınırını aşıyor.",413);
  }
   if(result.Count==0)throw new DomainException("Listede kanal bulunamadı; önceki katalog korundu.",422);return result.DistinctBy(channel=>channel.Key).ToArray();
 }
 private static int Separator(string line){char quote='\0';for(var i=8;i<line.Length;i++){var c=line[i];if(quote!='\0'){if(c==quote)quote='\0';}else if(c is '\'' or '"')quote=c;else if(c==',')return i;}return -1;}
 private static void Header(Dictionary<string,string> values,string key,string value)
 {
   var name=key.ToLowerInvariant()switch{"http-user-agent" or "user-agent"=>"User-Agent","http-referrer" or "referrer" or "referer"=>"Referer","http-origin" or "origin"=>"Origin",_=>null};if(name is null)return;
   if(string.IsNullOrWhiteSpace(value))return;if(value.Length>2048||value.Any(char.IsControl))throw new DomainException("Yayın HTTP başlıkları geçersiz.",422);
   // Community feeds sometimes contain placeholders such as "No response" in optional referrers.
   if(name is "Referer" or "Origin")try{_=RemoteMediaHttp.Validate(value);}catch(DomainException){return;}values[name]=value;
 }
 public static LiveProgramme[] Guide(byte[] bytes,IReadOnlyList<LiveChannel> channels)
 {
  using var reader=XmlReader.Create(new MemoryStream(bytes),new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=32*1024*1024});var doc=XDocument.Load(reader);var result=new List<LiveProgramme>();var known=channels.Where(channel=>!string.IsNullOrEmpty(channel.EpgId)).GroupBy(channel=>channel.EpgId!).ToDictionary(group=>group.Key,group=>group.ToArray());
  foreach(var row in doc.Descendants("programme"))
  {
   var epg=row.Attribute("channel")?.Value;if(epg is null||!known.TryGetValue(epg,out var mapped))continue;var start=Time(row.Attribute("start")?.Value);var end=Time(row.Attribute("stop")?.Value);if(start is null||end<=start||end is null||end<DateTimeOffset.UtcNow.AddDays(-2)||start>DateTimeOffset.UtcNow.AddDays(30))continue;
   var title=row.Elements("title").FirstOrDefault()?.Value??"";if(title.Length==0)continue;var description=row.Elements("desc").FirstOrDefault()?.Value??"";var episode=row.Elements("episode-num").FirstOrDefault()?.Value;var subtitle=row.Elements("sub-title").FirstOrDefault()?.Value;
   var rating=row.Element("rating")?.Element("value")?.Value;var age=rating is null?null:Age(rating);
   foreach(var channel in mapped){var key=episode is{Length:>0}?MediaPaths.SearchKey(title)+"|"+episode:subtitle is{Length:>0}?MediaPaths.SearchKey(title)+"|"+MediaPaths.SearchKey(subtitle):channel.Id+"|"+start.Value.ToString("O");result.Add(new(Id(channel.Id+start.Value.ToString("O")),channel.Id,start.Value.ToString("O"),end.Value.ToString("O"),title[..Math.Min(500,title.Length)],description[..Math.Min(10000,description.Length)],Id(key),row.Element("previously-shown") is not null?true:row.Element("new") is not null?false:null,age));}
   if(result.Count>100000)throw new DomainException("EPG program sınırı aşıldı.",413);
  }return result.ToArray();
 }
 private static DateTimeOffset? Time(string? value)
 {if(value is null)return null;var parts=value.Trim().Split(' ',StringSplitOptions.RemoveEmptyEntries);var stamp=parts[0].PadRight(14,'0');var zone=parts.Length>1?parts[1]:"+0000";if(zone.Length==5)zone=zone.Insert(3,":");return DateTimeOffset.TryParseExact(stamp+" "+zone,"yyyyMMddHHmmss zzz",CultureInfo.InvariantCulture,DateTimeStyles.None,out var result)?result.ToUniversalTime():null;}
 private static int? Age(string value){var match=Regex.Match(value,@"\d{1,2}",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(50));if(match.Success&&int.TryParse(match.Value,out var age)&&age<=21)return age;return value.ToUpperInvariant()switch{"G" or"TV-G" or"TV-Y"=>0,"PG" or"TV-PG"=>10,"R" or"TV-MA"=>18,_=>null};}
  [GeneratedRegex("([a-zA-Z0-9_-]+)\\s*=\\s*(?:\"([^\"]*)\"|'([^']*)')",RegexOptions.NonBacktracking)]private static partial Regex Attribute();
}
