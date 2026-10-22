using System.Net;
using System.Net.Sockets;
using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using HomeServer.Core;
using HomeServer.Infrastructure;

namespace HomeServer.Playback;

public sealed class DlnaController(MediaStore store)
{
 private readonly SemaphoreSlim _discovery=new(1,1);
 private static bool Private(IPAddress ip)=>ip.AddressFamily==AddressFamily.InterNetwork&&!IPAddress.IsLoopback(ip)&&(ip.GetAddressBytes()[0]==10||ip.GetAddressBytes()[0]==192&&ip.GetAddressBytes()[1]==168||ip.GetAddressBytes()[0]==172&&ip.GetAddressBytes()[1] is>=16 and<=31||ip.IsIPv6LinkLocal);
 private static HttpClient Client()=>new(new SocketsHttpHandler{AllowAutoRedirect=false,UseProxy=false,ConnectTimeout=TimeSpan.FromSeconds(3)}){Timeout=TimeSpan.FromSeconds(8)};
 private static XDocument Xml(string text){using var reader=XmlReader.Create(new StringReader(text),new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=256*1024});return XDocument.Load(reader);}
 private static string? Value(XElement root,string name)=>root.Elements().FirstOrDefault(element=>element.Name.LocalName==name)?.Value;
 private static Uri Local(Uri baseUri,string relative)
 {var uri=new Uri(baseUri,relative);if(uri.Scheme is not("http" or "https")||uri.UserInfo.Length>0||uri.Host!=baseUri.Host||!IPAddress.TryParse(uri.Host,out var ip)||!Private(ip))throw new DomainException("DLNA kontrol adresi keşfedilen cihaz dışında.",409);return uri;}
 public async Task<IReadOnlyList<RenderDevice>> DiscoverAsync(CancellationToken token)
 {
  if(!await _discovery.WaitAsync(0,token))throw new DomainException("Cihaz keşfi zaten çalışıyor.",429);
  try
  {
   using var udp=new UdpClient(AddressFamily.InterNetwork);udp.MulticastLoopback=false;var message=Encoding.ASCII.GetBytes("M-SEARCH * HTTP/1.1\r\nHOST: 239.255.255.250:1900\r\nMAN: \"ssdp:discover\"\r\nMX: 2\r\nST: urn:schemas-upnp-org:device:MediaRenderer:1\r\n\r\n");await udp.SendAsync(message,new IPEndPoint(IPAddress.Parse("239.255.255.250"),1900),token);
   using var budget=CancellationTokenSource.CreateLinkedTokenSource(token);budget.CancelAfter(TimeSpan.FromSeconds(4));var locations=new Dictionary<string,Uri>();
   while(!budget.IsCancellationRequested&&locations.Count<32)
   {
    UdpReceiveResult reply;try{reply=await udp.ReceiveAsync(budget.Token);}catch(OperationCanceledException)when(!token.IsCancellationRequested){break;}
    if(!Private(reply.RemoteEndPoint.Address)||reply.Buffer.Length>16384)continue;var line=Encoding.ASCII.GetString(reply.Buffer).Split('\n').FirstOrDefault(line=>line.StartsWith("location:",StringComparison.OrdinalIgnoreCase));if(line is null||!Uri.TryCreate(line[9..].Trim(),UriKind.Absolute,out var uri)||uri.Scheme is not("http" or "https")||uri.UserInfo.Length>0)continue;
    if(!IPAddress.TryParse(uri.Host,out var address)||!address.Equals(reply.RemoteEndPoint.Address))continue;locations[uri.AbsoluteUri]=uri;
   }
   foreach(var uri in locations.Values)try
   {
    using var client=Client();using var response=await client.GetAsync(uri,HttpCompletionOption.ResponseHeadersRead,token);response.EnsureSuccessStatusCode();await using var body=await response.Content.ReadAsStreamAsync(token);var doc=Xml(Encoding.UTF8.GetString(await Metadata.MetadataProvider.ReadLimitedAsync(body,256*1024,token)));
    var device=doc.Descendants().FirstOrDefault(element=>element.Name.LocalName=="device"&&Value(element,"deviceType")?.Contains("MediaRenderer",StringComparison.Ordinal)==true);if(device is null)continue;
    var services=device.Descendants().Where(element=>element.Name.LocalName=="service").ToArray();var transport=services.FirstOrDefault(element=>Value(element,"serviceType")?.Contains(":AVTransport:",StringComparison.Ordinal)==true);if(transport is null)continue;
    var render=services.FirstOrDefault(element=>Value(element,"serviceType")?.Contains(":RenderingControl:",StringComparison.Ordinal)==true);var manager=services.FirstOrDefault(element=>Value(element,"serviceType")?.Contains(":ConnectionManager:",StringComparison.Ordinal)==true);var protocols="";
    if(manager is not null){var info=await Soap(Local(uri,Value(manager,"controlURL")??""),Value(manager,"serviceType")!,"GetProtocolInfo",[],token);protocols=info.Descendants().FirstOrDefault(element=>element.Name.LocalName=="Sink")?.Value??"";}
    var udn=Value(device,"UDN");if(string.IsNullOrEmpty(udn)||udn.Length>200)continue;
    store.SaveRenderDevice(new("",udn,(Value(device,"friendlyName")??"DLNA alıcısı")[..Math.Min(160,(Value(device,"friendlyName")??"DLNA alıcısı").Length)],uri.Host,Local(uri,Value(transport,"controlURL")??"").AbsoluteUri,Value(transport,"serviceType")!,render is null?null:Local(uri,Value(render,"controlURL")??"").AbsoluteUri,render is null?null:Value(render,"serviceType"),protocols[..Math.Min(16000,protocols.Length)],DateTimeOffset.UtcNow.ToString("O")));
   }catch(Exception ex)when(ex is HttpRequestException or DomainException or XmlException or TaskCanceledException){}
   return store.RenderDevices();
  }
  finally{_discovery.Release();}
 }
 private static async Task<XDocument> Soap(Uri uri,string service,string action,(string Key,string Value)[] values,CancellationToken token)
 {
   if(uri.Scheme is not("http" or"https")||uri.UserInfo.Length>0||!IPAddress.TryParse(uri.Host,out var ip)||!Private(ip))throw new DomainException("DLNA yalnız keşfedilmiş yerel cihazlara bağlanır.",403);
  XNamespace soap="http://schemas.xmlsoap.org/soap/envelope/";XNamespace protocol=service;
  var document=new XDocument(new XElement(soap+"Envelope",new XAttribute(XNamespace.Xmlns+"s",soap),new XAttribute(soap+"encodingStyle","http://schemas.xmlsoap.org/soap/encoding/"),new XElement(soap+"Body",new XElement(protocol+action,values.Select(value=>new XElement(value.Key,value.Value))))));
  using var client=Client();using var request=new HttpRequestMessage(HttpMethod.Post,uri){Content=new StringContent(document.ToString(SaveOptions.DisableFormatting),Encoding.UTF8,"text/xml")};request.Headers.TryAddWithoutValidation("SOAPAction",$"\"{service}#{action}\"");using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token);await using var body=await response.Content.ReadAsStreamAsync(token);var xml=Xml(Encoding.UTF8.GetString(await Metadata.MetadataProvider.ReadLimitedAsync(body,256*1024,token)));if(!response.IsSuccessStatusCode||xml.Descendants().Any(element=>element.Name.LocalName=="Fault"))throw new DomainException("DLNA alıcısı komutu reddetti; desteklenen biçim/işlemleri kontrol et.",409);return xml;
 }
 public async Task LoadAsync(RenderDevice device,string url,string mime,string title,CancellationToken token)
 {
  XNamespace didl="urn:schemas-upnp-org:metadata-1-0/DIDL-Lite/";XNamespace dc="http://purl.org/dc/elements/1.1/";XNamespace upnp="urn:schemas-upnp-org:metadata-1-0/upnp/";
  var meta=new XElement(didl+"DIDL-Lite",new XElement(didl+"item",new XAttribute("id","home"),new XAttribute("parentID","0"),new XAttribute("restricted","1"),new XElement(dc+"title",title),new XElement(upnp+"class",mime.StartsWith("audio",StringComparison.Ordinal)?"object.item.audioItem.musicTrack":"object.item.videoItem"),new XElement(didl+"res",new XAttribute("protocolInfo",$"http-get:*:{mime}:*"),url)));
  await Soap(new(device.TransportUrl),device.TransportType,"SetAVTransportURI",[("InstanceID","0"),("CurrentURI",url),("CurrentURIMetaData",meta.ToString(SaveOptions.DisableFormatting))],token);await CommandAsync(device,new("play"),token);
 }
  public async Task<XDocument> CommandAsync(RenderDevice device,ReceiverControl input,CancellationToken token)
 {
  var values=new List<(string,string)>{("InstanceID","0")};var action=input.Action switch{"play"=>"Play","pause"=>"Pause","stop"=>"Stop","seek"=>"Seek","status"=>"GetPositionInfo","volume"=>"SetVolume",_=>throw new DomainException("Alıcı komutu geçersiz.")};
  if(input.Action=="play")values.Add(("Speed","1"));if(input.Action=="seek"){if(input.PositionSeconds is not{}position||!double.IsFinite(position)||position is<0 or>28800)throw new DomainException("Konum geçersiz.");values.Add(("Unit","REL_TIME"));values.Add(("Target",TimeSpan.FromSeconds(position).ToString(@"hh\:mm\:ss")));}
  if(input.Action=="volume"){if(input.Volume is not{}volume||!double.IsFinite(volume)||volume is<0 or>1||device.RenderUrl is null||device.RenderType is null)throw new DomainException("Alıcı ses kontrolü desteklemiyor.");values.Add(("Channel","Master"));values.Add(("DesiredVolume",Math.Round(volume*100).ToString(System.Globalization.CultureInfo.InvariantCulture)));return await Soap(new(device.RenderUrl),device.RenderType,action,values.ToArray(),token);}
   return await Soap(new(device.TransportUrl),device.TransportType,action,values.ToArray(),token);
  }
  public sealed record Snapshot(string? TrackUrl,double? PositionSeconds,double? DurationSeconds,bool? Paused,double? Volume,string State);
  public async Task<Snapshot> ReadAsync(RenderDevice device,CancellationToken token)
  {
   var position=await Soap(new(device.TransportUrl),device.TransportType,"GetPositionInfo",[("InstanceID","0")],token);
   var transport=await Soap(new(device.TransportUrl),device.TransportType,"GetTransportInfo",[("InstanceID","0")],token);
   var address=Leaf(position,"TrackURI");
   if(string.IsNullOrEmpty(address))address=Leaf(await Soap(new(device.TransportUrl),device.TransportType,"GetMediaInfo",[("InstanceID","0")],token),"CurrentURI");
   var state=Leaf(transport,"CurrentTransportState")??"UNKNOWN";double? volume=null;
   if(device.RenderUrl is not null&&device.RenderType is not null)
    try{var value=await Soap(new(device.RenderUrl),device.RenderType,"GetVolume",[("InstanceID","0"),("Channel","Master")],token);if(double.TryParse(Leaf(value,"CurrentVolume"),NumberStyles.Float,CultureInfo.InvariantCulture,out var number)&&double.IsFinite(number)&&number is>=0 and<=100)volume=number/100;}
    catch(Exception ex)when(!token.IsCancellationRequested&&ex is DomainException or HttpRequestException or OperationCanceledException){ /* RenderingControl is optional; transport observations remain valid. */ }
   return new(address,Seconds(Leaf(position,"RelTime")),Seconds(Leaf(position,"TrackDuration")),state=="PLAYING"?false:state is "PAUSED_PLAYBACK" or"STOPPED"?true:null,volume,state);
  }
  private static string? Leaf(XDocument value,string key)=>value.Descendants().FirstOrDefault(element=>element.Name.LocalName==key)?.Value;
  private static double? Seconds(string? text)=>TimeSpan.TryParse(text,CultureInfo.InvariantCulture,out var value)&&value.TotalSeconds is>=0 and<=86400?value.TotalSeconds:null;
}
