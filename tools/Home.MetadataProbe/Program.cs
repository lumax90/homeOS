using System.Text.Json;
using HomeServer.Gaming;

// Read-only public metadata contract check. No filesystem ROM access, image request or live state mutation.
if(args.Length is not(6 or 7)||args[0]!="--id"||args[2]!="--title"||args[4]!="--system"||args.Length==7&&args[6]!="--cover-index"||!long.TryParse(args[1],out var id)||id<=0)throw new ArgumentException("Use --id <public candidate ID> --title <public title> --system <known system> [--cover-index]");
var system=NativeSystems.Require(args[5]);using var client=new HasheousMetadataClient();using var budget=new CancellationTokenSource(TimeSpan.FromSeconds(60));
var data=await client.DetailsAsync(system.Id,new(id,args[3],system.Name,"",null,null,[],[]),budget.Token);
if(data.Id!=id||!HasheousMetadataClient.PlatformMatches(system.Id,data.Platform))throw new InvalidDataException("The public candidate identity changed.");
string? chosenCover=null;int? directoryEntries=null;
if(args.Length==7&&system.ArtworkDatabase is{} database)
{
    using var http=new HttpClient(new SocketsHttpHandler{UseProxy=false,UseCookies=false,AllowAutoRedirect=false}){Timeout=TimeSpan.FromSeconds(20)};using var response=await http.GetAsync("https://thumbnails.libretro.com/"+Uri.EscapeDataString(database)+"/Named_Boxarts/",HttpCompletionOption.ResponseHeadersRead,budget.Token);response.EnsureSuccessStatusCode();
    await using var stream=await response.Content.ReadAsStreamAsync(budget.Token);using var bytes=new MemoryStream();var buffer=new byte[8192];int count;while((count=await stream.ReadAsync(buffer,budget.Token))>0){if(bytes.Length+count>8*1024*1024)throw new InvalidDataException("Artwork directory exceeds its bounded metadata size.");bytes.Write(buffer,0,count);}
    var names=RomArtwork.ParseDirectory(System.Text.Encoding.UTF8.GetString(bytes.ToArray()));directoryEntries=names.Length;var covers=names.Select(name=>new RomCover(name,Path.GetFileNameWithoutExtension(name),""));chosenCover=RomMatching.Cover(covers,data.Name,data.Name,"auto",data.Regions)?.Name;if(chosenCover is null)throw new InvalidDataException("The public directory did not yield a matching cover filename.");
}
Console.WriteLine(JsonSerializer.Serialize(new{verifiedAt=DateTimeOffset.UtcNow,provider="Hasheous",system=system.Id,id=data.Id,data.Name,data.Platform,data.Publisher,data.Year,data.DetailsPartial,successfulSelectionContract=true,directoryEntries,chosenCover,romAccessed=false,imageRequested=false,liveDataMutated=false},new JsonSerializerOptions(JsonSerializerDefaults.Web){WriteIndented=true}));
