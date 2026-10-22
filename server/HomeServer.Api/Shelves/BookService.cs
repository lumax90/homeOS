using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using HomeServer.Core;
using HomeServer.Infrastructure;
using SharpCompress.Archives;
using ReaderOptions = SharpCompress.Readers.ReaderOptions;

namespace HomeServer.Shelves;
public sealed partial class BookService(MediaStore store)
{
 private static readonly HashSet<string> Tags=["p","div","span","section","article","h1","h2","h3","h4","h5","h6","blockquote","pre","code","strong","em","b","i","u","s","br","hr","ul","ol","li","dl","dt","dd","table","thead","tbody","tr","td","th","caption","figure","figcaption","a","img","sup","sub"];
 private static bool Image(string name)=>Path.GetExtension(name).ToLowerInvariant() is".jpg" or".jpeg" or".png" or".webp" or".gif" or".avif";
 private static string Key(string name)=>Convert.ToBase64String(Encoding.UTF8.GetBytes(name)).TrimEnd('=').Replace('+','-').Replace('/','_');
 private static string FromKey(string key){if(key.Length>3000)throw new DomainException("Belge kaynak anahtarı geçersiz.");var value=key.Replace('-','+').Replace('_','/');try{return Encoding.UTF8.GetString(Convert.FromBase64String(value.PadRight((value.Length+3)/4*4,'=')));}catch(FormatException){throw new DomainException("Belge kaynak anahtarı geçersiz.");}}
 private static XDocument Xml(byte[] bytes){using var reader=XmlReader.Create(new MemoryStream(bytes),new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=4*1024*1024});return XDocument.Load(reader);}
 private static byte[] Bytes(Stream stream,long declared,int maximum)
 {using(stream){if(declared<0||declared>maximum)throw new DomainException("Belge kaynağı boyut sınırını aşıyor.",413);using var output=new MemoryStream();var buffer=new byte[32768];int count;while((count=stream.Read(buffer))>0){if(output.Length+count>maximum)throw new DomainException("Açılan belge kaynağı sınırı aşıyor.",413);output.Write(buffer,0,count);}return output.ToArray();}}
 private static string Resolve(string current,string href)
 {var root=new Uri("https://book.invalid/"+current);var uri=new Uri(root,href);if(uri.Host!="book.invalid"||uri.Scheme!="https")throw new DomainException("Kitap kaynağı arşivin dışında.",403);return Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'));}
 private static (string Title,string[] Chapters) Epub(ZipArchive zip)
 {
  if(zip.Entries.Count>10000)throw new DomainException("EPUB giriş sayısı sınırı aşıyor.",413);var container=zip.GetEntry("META-INF/container.xml")??throw new DomainException("EPUB container bilgisi eksik.");var xml=Xml(Bytes(container.Open(),container.Length,1024*1024));var packageName=xml.Descendants().FirstOrDefault(element=>element.Name.LocalName=="rootfile")?.Attribute("full-path")?.Value??throw new DomainException("EPUB paket yolu eksik.");
  var package=zip.GetEntry(packageName)??throw new DomainException("EPUB paketi bulunamadı.");var doc=Xml(Bytes(package.Open(),package.Length,4*1024*1024));var items=doc.Descendants().Where(element=>element.Name.LocalName=="item").Where(element=>element.Attribute("id") is not null&&element.Attribute("href") is not null).ToDictionary(element=>element.Attribute("id")!.Value,element=>Resolve(packageName,element.Attribute("href")!.Value));
  var chapters=doc.Descendants().Where(element=>element.Name.LocalName=="itemref").Select(element=>element.Attribute("idref")?.Value).Where(id=>id is not null&&items.ContainsKey(id)).Select(id=>items[id!]).ToArray();if(chapters.Length is<1 or>2000)throw new DomainException("EPUB bölüm listesi boş veya sınırı aşıyor.");return(doc.Descendants().FirstOrDefault(element=>element.Name.LocalName=="title")?.Value??"",chapters);
 }
 public BookInfo Info(string id,Account actor)
 {
  var source=store.ShelfSource(id,actor);if(source.Asset.Kind=="pdf")return new("pdf",source.Asset.Title,[],0);
  if(source.Asset.Kind=="text"){if(source.Asset.SizeBytes>20*1024*1024)throw new DomainException("Metin kitap 20 MiB sınırını aşıyor.");var length=File.ReadAllText(source.Path).Length;return new("text",source.Asset.Title,Enumerable.Range(0,Math.Max(1,(length+9999)/10000)).Select(index=>new BookChapter(index.ToString(),$"Bölüm {index+1}")).ToArray(),0);}
  if(source.Asset.Kind=="epub"){using var zip=ZipFile.OpenRead(source.Path);var book=Epub(zip);return new("epub",string.IsNullOrWhiteSpace(book.Title)?source.Asset.Title:book.Title,book.Chapters.Select((name,index)=>new BookChapter(Key(name),$"Bölüm {index+1}")).ToArray(),book.Chapters.Length);}
  if(source.Asset.Kind=="comic"){using var archive=ArchiveFactory.OpenArchive([new FileInfo(source.Path)],ReaderOptions.ForFilePath);var names=archive.Entries.Where(entry=>!entry.IsDirectory&&entry.Key is{}key&&Image(key)).Select(entry=>entry.Key!).OrderBy(Natural,StringComparer.OrdinalIgnoreCase).ToArray();if(names.Length>5000)throw new DomainException("Çizgi roman sayfa sınırını aşıyor.");return new("comic",source.Asset.Title,names.Select((name,index)=>new BookChapter(Key(name),$"Sayfa {index+1}")).ToArray(),names.Length);}
  throw new DomainException("Bu dosya kitap/çizgi roman değil.");
 }
 public string Chapter(string id,int index,Account actor,string pathBase)
 {
  var source=store.ShelfSource(id,actor);if(source.Asset.Kind is not("text" or"epub"))throw new DomainException("Metin bölümü bulunamadı.",404);string content;
  if(source.Asset.Kind=="text"){var text=File.ReadAllText(source.Path);if(text.Length>20*1024*1024||index<0||index*10000>=Math.Max(1,text.Length))throw new DomainException("Bölüm bulunamadı.",404);content="<pre>"+System.Net.WebUtility.HtmlEncode(text.Substring(index*10000,Math.Min(10000,text.Length-index*10000)))+"</pre>";}
  else
  {
   using var zip=ZipFile.OpenRead(source.Path);var book=Epub(zip);if(index<0||index>=book.Chapters.Length)throw new DomainException("Bölüm bulunamadı.",404);var name=book.Chapters[index];var entry=zip.GetEntry(name)??throw new DomainException("Bölüm dosyası bulunamadı.",404);var document=Xml(Bytes(entry.Open(),entry.Length,4*1024*1024));var body=document.Descendants().FirstOrDefault(element=>element.Name.LocalName=="body")??document.Root!;var clean=new XElement("article");foreach(var node in body.Nodes())foreach(var safe in Sanitize(node,name,id,pathBase))clean.Add(safe);content=clean.ToString(SaveOptions.DisableFormatting);
  }
  return "<!doctype html><html lang=\"tr\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><style>body{margin:0 auto;padding:28px;max-width:760px;font:20px/1.75 Georgia,serif;color:#e4e7e2;background:#172027}img{max-width:100%;height:auto}pre{white-space:pre-wrap;word-break:break-word}a{color:#b9dfcb}table{max-width:100%;border-collapse:collapse}td,th{padding:8px}</style></head><body>"+content+"</body></html>";
 }
 private static IEnumerable<XNode> Sanitize(XNode node,string current,string id,string pathBase)
 {
  if(node is XText text){yield return new XText(text.Value);yield break;}if(node is not XElement element)yield break;var name=element.Name.LocalName.ToLowerInvariant();if(name is"script" or"style" or"iframe" or"object" or"embed" or"form" or"link")yield break;
  if(!Tags.Contains(name)){foreach(var child in element.Nodes())foreach(var safe in Sanitize(child,current,id,pathBase))yield return safe;yield break;}
  var output=new XElement(name);foreach(var attribute in element.Attributes())if(attribute.Name.LocalName is"id" or"title" or"alt" or"lang" or"dir" or"colspan" or"rowspan")output.SetAttributeValue(attribute.Name.LocalName,attribute.Value);
  if(name=="img"&&element.Attribute("src")?.Value is{}src){string? resource=null;try{resource=Resolve(current,src);}catch(DomainException){}if(resource is null||!Image(resource))yield break;output.SetAttributeValue("src",pathBase+"/api/shelves/"+id+"/resource?key="+Key(resource));}
  if(name=="a"&&element.Attribute("href")?.Value is{}href&&href.StartsWith('#'))output.SetAttributeValue("href",href);
  foreach(var child in element.Nodes())foreach(var safe in Sanitize(child,current,id,pathBase))output.Add(safe);yield return output;
 }
 public (byte[] Data,string Mime) Resource(string id,string key,Account actor)
 {
  var source=store.ShelfSource(id,actor);var name=FromKey(key);if(!Image(name))throw new DomainException("Kitap kaynağı resim değil.",403);byte[] bytes;
  if(source.Asset.Kind=="epub"){using var zip=ZipFile.OpenRead(source.Path);var entry=zip.GetEntry(name)??throw new DomainException("Kitap resmi bulunamadı.",404);bytes=Bytes(entry.Open(),entry.Length,32*1024*1024);}
  else if(source.Asset.Kind=="comic"){using var archive=ArchiveFactory.OpenArchive([new FileInfo(source.Path)],ReaderOptions.ForFilePath);var entry=archive.Entries.FirstOrDefault(entry=>entry.Key==name&&!entry.IsDirectory)??throw new DomainException("Çizgi roman sayfası bulunamadı.",404);if(entry.IsEncrypted)throw new DomainException("Şifreli çizgi roman önce kullanıcı tarafından açılmalı.",409);bytes=Bytes(entry.OpenEntryStream(),entry.Size,32*1024*1024);}
  else throw new DomainException("Belge resmi bulunamadı.",404);return(bytes,Path.GetExtension(name).ToLowerInvariant()switch{".png"=>"image/png",".webp"=>"image/webp",".gif"=>"image/gif",".avif"=>"image/avif",_=>"image/jpeg"});
 }
 private static string Natural(string value)=>Digits().Replace(value,match=>match.Value.PadLeft(20,'0'));
 [GeneratedRegex(@"\d+",RegexOptions.NonBacktracking)]private static partial Regex Digits();
}
