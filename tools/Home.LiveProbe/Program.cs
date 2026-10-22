using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HomeServer.Infrastructure;
using HomeServer.LiveTv;

// Public playlist TEXT only. No stream, logo, XMLTV payload, live state or playback command is accessed.
using var remote=new RemoteMediaHttp();var http=new LiveHttp(remote);using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(3));var rows=new List<object>();
foreach(var list in LivePublicCatalog.Lists)
{
    var response=await LiveHttpRead.ReadAsync(http,list.PlaylistUrl,false,8*1024*1024,timeout.Token);var channels=PlaylistGuide.Playlist(Encoding.UTF8.GetString(response.Bytes),response.Uri);
    if(channels.Length==0)throw new InvalidDataException("Empty public playlist: "+list.Id);
    rows.Add(new{list.Id,list.PlaylistUrl,list.License,channels=channels.Length,bytes=response.Bytes.Length,sha256=Convert.ToHexStringLower(SHA256.HashData(response.Bytes)),headerOverrides=channels.Count(c=>c.Headers is{Count:>0}),examples=channels.Take(3).Select(c=>new{c.Name,c.Group})});
}
var result=JsonSerializer.Serialize(new{verifiedAt=DateTimeOffset.UtcNow,publicLists=rows,streamsStarted=0,logosRequested=0,liveStateTouched=false},new JsonSerializerOptions(JsonSerializerDefaults.Web){WriteIndented=true});if(args.Length==1){using var output=new StreamWriter(new FileStream(args[0],FileMode.CreateNew,FileAccess.Write));output.Write(result);}Console.WriteLine(result);
