using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using HomeServer.Core;
using HomeServer.Infrastructure;
using Microsoft.AspNetCore.WebUtilities;

namespace HomeServer.Sources;
public static partial class SourceAdapterRegistry
{
 public static SourceAdapterManifest[] Builtins=>[
  new(1,"internet-archive","Internet Archive","1.0.0","Provider terms; Home adapter","https://archive.org/developers/","archive","torrent","/advancedsearch.php"),
   new(1,"nyaa-rss","Nyaa RSS","1.0.0","Provider terms; Home adapter","https://nyaa.si/","nyaa","torrent","/"),
   new(1,"yts","YTS","1.0.0","Provider terms; Home adapter","https://yts.gg/api","yts","torrent","/api/v2/list_movies.json",PageSize:20),
   new(1,"eztv","EZTV","1.0.0","Provider terms; Home adapter","https://eztvx.to/api/","eztv","torrent","/api/get-torrents",PageSize:100),
   new(1,"the-pirate-bay","The Pirate Bay","1.0.0","Provider terms; Home adapter","https://thepiratebay.org/","apibay","torrent","/q.php",PageSize:100),
   new(1,"torrents-csv","Torrents.csv","1.0.0","Provider terms; Home adapter","https://torrents-csv.com/","torrents-csv","torrent","/service/search",PageSize:25)];
 public static SourceAdapterManifest Find(string id,MediaStore store)=>Builtins.Concat(store.SourceAdapters()).FirstOrDefault(adapter=>adapter.Id==id)??throw new DomainException("Adaptör bulunamadı veya devre dışı.",404);
  public static void Validate(SourceAdapterManifest value)
  {
   if(value.SearchPath is null||value.SearchPath.Contains('\\')||value.SearchPath.Any(char.IsControl)||value.Version is null||value.Version.Split('.').Length!=3||value.Homepage is null||value.Homepage.Length>2048||value.DownloadTemplate?.Length>8192||value.QueryParameter is null||value.OffsetParameter is null||value.LimitParameter is null||value.CredentialName is null||value.ItemsPath is null||value.Fields?.Values.Any(path=>path is null)==true)throw new DomainException("Adaptör yolu veya sürümü geçersiz.");
  if(value.SchemaVersion!=1||string.IsNullOrWhiteSpace(value.Id)||value.Id.Length>60||value.Id.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c!='-'&&c!='.')||Builtins.Any(adapter=>adapter.Id==value.Id)||string.IsNullOrWhiteSpace(value.Name)||value.Name.Length>100||!Version.TryParse(value.Version,out _)||string.IsNullOrWhiteSpace(value.License)||value.License.Length>500||!Uri.TryCreate(value.Homepage,UriKind.Absolute,out var homepage)||homepage.Scheme!="https"||value.Format is not("json" or"rss")||value.Protocol is not("torrent" or"usenet")||value.SearchPath.Length>300||!value.SearchPath.StartsWith('/')||value.SearchPath.StartsWith("//",StringComparison.Ordinal)||value.SearchPath.Contains('?')||value.SearchPath.Contains('#')||value.PageSize is<1 or>100)throw new DomainException("Adaptör v1 sözleşmesi geçersiz.");
  if(value.Fields is null||!value.Fields.ContainsKey("title")||!value.Fields.ContainsKey("id")||!value.Fields.ContainsKey("url")&&value.DownloadTemplate is null||value.Fields.Count>20||value.Parameters?.Count>20||value.ItemsPath.Length>200||value.Authentication is not("none" or"query" or"bearer" or"header"))throw new DomainException("Adaptör alanları/yeteneği eksik.");
  foreach(var path in value.Fields.Values.Append(value.ItemsPath))if(path.Length>200||path.Split('.').Length>16||path.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c!='.'&&c!='_'&&c!='-'))throw new DomainException("JSON alan yolu geçersiz.");
  foreach(var key in new[]{value.QueryParameter,value.OffsetParameter,value.LimitParameter,value.CredentialName})if(key.Length>100||key.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c!='-'&&c!='_'))throw new DomainException("Adaptör parametre adı geçersiz.");
  if(value.Authentication=="header"&&value.CredentialName!="Authorization"&&!value.CredentialName.StartsWith("X-",StringComparison.OrdinalIgnoreCase))throw new DomainException("Kimlik başlığı Authorization veya X- ile başlamalı.");
 }
  public static IndexerCapabilities Capabilities(SourceAdapterManifest manifest)=>manifest.Format switch
  {
   "yts"=>new(manifest.PageSize,new(){["search"]=["q"],["movie-search"]=["q","imdbid"]},new(){[2000]="Movies"}),
   "eztv"=>new(manifest.PageSize,new(){["search"]=[],["tv-search"]=["imdbid"]},new(){[5000]="TV"}),
   "torrents-csv"=>new(manifest.PageSize,new(){["movie-search"]=["q"],["tv-search"]=["q"],["music-search"]=["q"]},new()),
   "apibay"=>new(manifest.PageSize,new(){["search"]=["q"],["movie-search"]=["q","imdbid"],["tv-search"]=["q","imdbid"],["music-search"]=["q"]},new()),
   _=>new(manifest.PageSize,new(){["search"]=["q"],["movie-search"]=["q"],["tv-search"]=["q"],["music-search"]=["q"],["book-search"]=["q"]},new())
  };
  public static (string Url,Dictionary<string,string> Headers) Request(SourceAdapterManifest manifest,IndexerSecret source,Dictionary<string,string?> parameters,string credential)
  {
   if(!manifest.SearchPath.StartsWith('/')||manifest.SearchPath.StartsWith("//",StringComparison.Ordinal)||manifest.SearchPath.Contains('\\')||manifest.SearchPath.Any(char.IsControl))throw new DomainException("Adaptör sorgusu geçersiz bir göreli yol içeriyor.",409);
  var endpoint=new Uri(source.Info.Endpoint);var query=new Dictionary<string,string?>();var search=parameters.GetValueOrDefault("q")??string.Join(' ',parameters.GetValueOrDefault("artist"),parameters.GetValueOrDefault("album"));var offset=int.TryParse(parameters.GetValueOrDefault("offset"),out var n)?n:0;
   if(manifest.Format is "yts" or "eztv" or "apibay" or "torrents-csv")NativeQuery(manifest,parameters,query,search,offset);
   else if(manifest.Format=="archive")
  {var type=parameters.GetValueOrDefault("t");var mediaType=type=="music"?"audio":type=="book"?"texts":"movies";var term=string.IsNullOrWhiteSpace(search)?"mediatype:"+mediaType:$"mediatype:{mediaType} AND ({search.Replace("\\","\\\\").Replace("\"","\\\"")})";query["q"]=term;query["output"]="json";query["rows"]=manifest.PageSize.ToString();query["page"]=(offset/manifest.PageSize+1).ToString();query["fl[]"]="identifier,title,year,mediatype,item_size,addeddate";}
  else if(manifest.Format=="nyaa"){query["page"]="rss";query["q"]=search;query["p"]=(offset/manifest.PageSize+1).ToString();}
  else{foreach(var pair in manifest.Parameters??[])query[pair.Key]=pair.Value;query[manifest.QueryParameter]=search;query[manifest.OffsetParameter]=offset.ToString();query[manifest.LimitParameter]=manifest.PageSize.ToString();}
  var headers=new Dictionary<string,string>();if(credential.Length>0){if(manifest.Authentication=="query")query[manifest.CredentialName]=credential;else if(manifest.Authentication=="bearer")headers["Authorization"]="Bearer "+credential;else if(manifest.Authentication=="header")headers[manifest.CredentialName]=credential;}
   var address=new Uri(endpoint,manifest.SearchPath);if(address.Scheme!=endpoint.Scheme||address.IdnHost!=endpoint.IdnHost||address.Port!=endpoint.Port||address.UserInfo.Length>0)throw new DomainException("Adaptör sorgusu kaynak origin'i dışında.",409);
   return(QueryHelpers.AddQueryString(address.AbsoluteUri,query),headers);
 }
  public static AdapterReleasePage Parse(SourceAdapterManifest manifest,byte[] bytes,Uri endpoint,int offset)
  {
   if(manifest.Format is "yts" or "eztv" or "apibay" or "torrents-csv")return ParseNative(manifest,bytes,offset);
  if(manifest.Format is"rss" or"nyaa")
  {
    using var reader=XmlReader.Create(new MemoryStream(bytes),new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=8*1024*1024});var xml=XDocument.Load(reader);
    var channel=xml.Root?.Name.LocalName=="rss"?xml.Root.Elements().FirstOrDefault(element=>element.Name.LocalName=="channel"):null;
    if(channel is null)throw new DomainException("Kaynak RSS yanıtı yerine farklı bir sayfa döndürdü; bağlantıyı kontrol et.",502);
    var rows=channel.Elements().Where(element=>element.Name.LocalName=="item").Take(101).ToArray();var results=new List<RawRelease>();
   foreach(var row in rows.Take(100))
   {string? Text(string key)=>row.Elements().FirstOrDefault(element=>element.Name.LocalName.Equals(key,StringComparison.OrdinalIgnoreCase))?.Value;var title=Text("title")??"";var url=row.Elements().FirstOrDefault(element=>element.Name.LocalName=="enclosure")?.Attribute("url")?.Value??Text("link")??"";var size=Size(Text("size"));var hash=Text("infoHash");if(title.Length==0||title.Length>1000||url.Length==0)continue;results.Add(new(Text("guid")??url,title,manifest.Protocol,size,int.TryParse(Text("seeders"),out var seeders)?seeders:null,DateTimeOffset.TryParse(Text("pubDate"),out var date)?date.ToString("O"):null,Absolute(endpoint,url),hash,new()));}
   return new(results,manifest.Format!="nyaa"&&rows.Length>=manifest.PageSize);
  }
  using var json=JsonDocument.Parse(bytes);var root=json.RootElement;var array=manifest.Format=="archive"?root.GetProperty("response").GetProperty("docs"):Select(root,manifest.ItemsPath);if(array.ValueKind!=JsonValueKind.Array)throw new DomainException("Adaptör sonuç dizisi bulunamadı.",502);var values=new List<RawRelease>();
  foreach(var row in array.EnumerateArray().Take(100))
  {
   string? Read(string name)=>Text(Select(row,manifest.Format=="archive"?name:manifest.Fields!.GetValueOrDefault(name)??""));
   var id=Read(manifest.Format=="archive"?"identifier":"id");var title=Read("title");if(string.IsNullOrEmpty(id)||string.IsNullOrEmpty(title)||id.Length>1000||title.Length>1000)continue;
   var url=manifest.Format=="archive"?new Uri(endpoint,$"/download/{Uri.EscapeDataString(id)}/{Uri.EscapeDataString(id)}_archive.torrent").AbsoluteUri:Read("url");
   if(manifest.DownloadTemplate is{}template)url=template.Replace("{id}",Uri.EscapeDataString(id),StringComparison.Ordinal);if(string.IsNullOrWhiteSpace(url))continue;
   values.Add(new(id,title,manifest.Protocol,Size(Read(manifest.Format=="archive"?"item_size":"size")),int.TryParse(Read("seeders"),out var seeds)?seeds:null,Read(manifest.Format=="archive"?"addeddate":"published"),Absolute(endpoint,url),Read("infohash"),new()));
  }
  var more=manifest.Format=="archive"?root.GetProperty("response").GetProperty("numFound").GetInt32()>offset+values.Count:array.GetArrayLength()>=manifest.PageSize;return new(values,more);
 }
 private static JsonElement Select(JsonElement root,string path){if(path.Length==0)return root;foreach(var part in path.Split('.')){if(root.ValueKind==JsonValueKind.Object&&root.TryGetProperty(part,out var child))root=child;else if(root.ValueKind==JsonValueKind.Array&&int.TryParse(part,out var index)&&index>=0&&index<root.GetArrayLength())root=root[index];else return default;}return root;}
 private static string? Text(JsonElement element)=>element.ValueKind is JsonValueKind.String?element.GetString():element.ValueKind is JsonValueKind.Number?element.GetRawText():null;
 private static string Absolute(Uri endpoint,string value)=>value.StartsWith("magnet:",StringComparison.OrdinalIgnoreCase)?value:new Uri(endpoint,value).AbsoluteUri;
 private static long? Size(string? value){if(long.TryParse(value,out var bytes)&&bytes>=0)return bytes;if(value is null)return null;var parts=value.Split(' ',StringSplitOptions.RemoveEmptyEntries);if(parts.Length!=2||!double.TryParse(parts[0],NumberStyles.Float,CultureInfo.InvariantCulture,out var n)||!double.IsFinite(n)||n<0)return null;var multiplier=parts[1].ToLowerInvariant()switch{"kib"=>1024d,"mib"=>1048576d,"gib"=>1073741824d,"tib"=>1099511627776d,"kb"=>1000d,"mb"=>1e6,"gb"=>1e9,_=>0};return multiplier>0&&n*multiplier<long.MaxValue?(long)(n*multiplier):null;}
}
