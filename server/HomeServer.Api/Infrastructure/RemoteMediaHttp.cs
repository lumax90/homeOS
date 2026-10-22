using System.Net;
using System.Net.Sockets;
using HomeServer.Core;

namespace HomeServer.Infrastructure;
public sealed class RemoteMediaHttp:IDisposable
{
 private readonly HttpClient _public=Create(false);private readonly HttpClient _lan=Create(true);
 public static bool Allowed(IPAddress address,bool lan)
 {
  if(address.IsIPv4MappedToIPv6)address=address.MapToIPv4();if(IPAddress.IsLoopback(address)||address.Equals(IPAddress.Any)||address.Equals(IPAddress.IPv6Any)||address.IsIPv6Multicast||address.IsIPv6LinkLocal)return false;
  var bytes=address.GetAddressBytes();if(address.AddressFamily==AddressFamily.InterNetwork){if(bytes[0] is 0 or >=224||bytes[0]==169&&bytes[1]==254)return false;return lan||!(bytes[0]==10||bytes[0]==172&&bytes[1] is>=16 and<=31||bytes[0]==192&&bytes[1]==168);}
  return lan||(bytes[0]&0xfe)!=0xfc;
 }
 public static Uri Validate(string value)
 {if(value.Length>8192||!Uri.TryCreate(value,UriKind.Absolute,out var uri)||uri.Scheme is not("http" or"https")||uri.UserInfo.Length>0||uri.Fragment.Length>0)throw new DomainException("Medya kaynağı için HTTP/HTTPS adresi gerekli.");return uri;}
 private static HttpClient Create(bool lan)=>new(new SocketsHttpHandler{AllowAutoRedirect=false,UseProxy=false,AutomaticDecompression=DecompressionMethods.GZip|DecompressionMethods.Deflate,ConnectTimeout=TimeSpan.FromSeconds(10),PooledConnectionLifetime=TimeSpan.FromMinutes(2),ConnectCallback=async(context,token)=>
 {
  var addresses=await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host,token);foreach(var address in addresses.Where(address=>Allowed(address,lan))){var socket=new Socket(address.AddressFamily,SocketType.Stream,ProtocolType.Tcp);try{await socket.ConnectAsync(new IPEndPoint(address,context.DnsEndPoint.Port),token);return new NetworkStream(socket,true);}catch{socket.Dispose();}}
  throw new HttpRequestException("Kaynağın ağ adresine izin verilmiyor veya bağlantı kurulamadı.");
 }}){Timeout=Timeout.InfiniteTimeSpan};
  public async Task<HttpResponseMessage> GetAsync(string address,bool lan,CancellationToken token,string? range=null,IReadOnlyDictionary<string,string>? headers=null)
 {
  var uri=Validate(address);for(var hop=0;hop<5;hop++)
  {
    using var request=new HttpRequestMessage(HttpMethod.Get,uri);request.Headers.UserAgent.ParseAdd("Home/"+BuildInfo.Version);if(range is not null)request.Headers.TryAddWithoutValidation("Range",range);
    if(headers is not null){if(headers.Count>3)throw new DomainException("Yayın HTTP başlıkları geçersiz.");foreach(var pair in headers){if(pair.Key is not("User-Agent" or "Referer" or "Origin")||pair.Value.Length>2048||pair.Value.Any(char.IsControl))throw new DomainException("Yayın HTTP başlıkları geçersiz.");if(pair.Key is "Referer" or "Origin")_=Validate(pair.Value);request.Headers.Remove(pair.Key);request.Headers.TryAddWithoutValidation(pair.Key,pair.Value);}}
   var response=await(lan?_lan:_public).SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token);if((int)response.StatusCode is>=300 and<400&&response.Headers.Location is{}location){var next=location.IsAbsoluteUri?location:new Uri(uri,location);response.Dispose();if(uri.Scheme=="https"&&next.Scheme!="https")throw new DomainException("Kaynak HTTPS bağlantısını düşürmeye çalıştı.",502);uri=Validate(next.AbsoluteUri);continue;}return response;
  }throw new DomainException("Kaynak çok fazla yönlendirme döndürdü.",502);
 }
 public async Task<byte[]> ReadAsync(string url,bool lan,int limit,CancellationToken token)
 {using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(45));using var response=await GetAsync(url,lan,timeout.Token);if(!response.IsSuccessStatusCode)throw new DomainException($"Kaynak HTTP {(int)response.StatusCode} döndürdü.",502);await using var stream=await response.Content.ReadAsStreamAsync(timeout.Token);return await Metadata.MetadataProvider.ReadLimitedAsync(stream,limit,timeout.Token);}
 public void Dispose(){_public.Dispose();_lan.Dispose();}
}
