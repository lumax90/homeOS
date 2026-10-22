using System.Text.Json;
using HomeServer.Core;
using HomeServer.Infrastructure;
using HomeServer.Music;
using Microsoft.Extensions.Caching.Memory;

if(args.Length==2&&args[0]=="--autocomplete")
{
    using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(45));using var cached=new MemoryCache(new MemoryCacheOptions{SizeLimit=20});using var client=new PublicMusicClient();var music=new MusicBrainzProvider(client,cached);
    var prefixes=new List<object>();foreach(var (query,expectedName) in new[]{("Metallic","Metallica"),("Daft P","Daft Punk")})
    {
        var page=await music.SearchArtistsAsync(query,0,timeout.Token,autocomplete:true);var match=page.Items.FirstOrDefault(item=>item.Name.Equals(expectedName,StringComparison.OrdinalIgnoreCase))??throw new InvalidDataException("The real prefix query did not return "+expectedName);
        prefixes.Add(new{query,matched=match.Name,match.Id,page.Total,returned=page.Items.Count});
    }
    var result=JsonSerializer.Serialize(new{verifiedAt=DateTimeOffset.UtcNow,provider="MusicBrainz",prefixes,mediaRequests=0,liveStateTouched=false},new JsonSerializerOptions(JsonSerializerDefaults.Web){WriteIndented=true});using var output=new StreamWriter(new FileStream(args[1],FileMode.CreateNew,FileAccess.Write));output.Write(result);Console.WriteLine(result);return;
}

// Public text metadata only. No radio session, artwork URL, stock video, media file or live database is opened.
var root=Path.Combine(Path.GetTempPath(),"opencode","home-music-probe",Guid.NewGuid().ToString("N"));
var paths=new HomePaths(root,Path.Combine(root,"thumbnails"),null);var store=new MediaStore(paths);var preferences=new MusicExperiencePreferences(paths,store,new EventHub());
using var budget=new CancellationTokenSource(TimeSpan.FromSeconds(90));using var directory=new RadioDirectory(paths,preferences);
var jazz=await directory.SearchAsync("1",null,"jazz",null,0,budget.Token);var turkish=await directory.SearchAsync("1",null,null,"TR",0,budget.Token);
if(jazz.Items.Length==0||turkish.Items.Length==0)throw new InvalidDataException("Public radio metadata was not returned.");
using var memory=new MemoryCache(new MemoryCacheOptions{SizeLimit=100});using var factory=new PublicMusicClient();var provider=new MusicBrainzProvider(factory,memory);
var albums=await provider.SearchAlbumsAsync("Kind of Blue","Miles Davis",0,budget.Token);var exact=albums.Items.Where(album=>MusicOnlineMetadata.Same(album.Title,"Kind of Blue")&&MusicOnlineMetadata.Same(album.Artist,"Miles Davis")).ToArray();
if(albums.Total>albums.Items.Count||exact.Length!=1)throw new InvalidDataException("Public metadata did not yield one complete exact album match.");
var report=JsonSerializer.Serialize(new{verifiedAt=DateTimeOffset.UtcNow,radio=new{jazz=jazz.Items.Length,turkish=turkish.Items.Length,examples=jazz.Items.Take(3).Select(s=>new{s.Id,s.Name,s.Country,s.Codec})},musicBrainz=new{exact[0].Id,exact[0].Title,exact[0].Artist,exact[0].Date},radioStreamsStarted=0,artworkRequests=0,stockMediaRequests=0,liveStateTouched=false},new JsonSerializerOptions(JsonSerializerDefaults.Web){WriteIndented=true});
if(args.Length==1){using var output=new StreamWriter(new FileStream(args[0],FileMode.CreateNew,FileAccess.Write));output.Write(report);}Console.WriteLine(report);

sealed class PublicMusicClient:IHttpClientFactory,IDisposable
{
    private readonly HttpClient _client=new(new SocketsHttpHandler{UseProxy=false,UseCookies=false,AllowAutoRedirect=false}){BaseAddress=new("https://musicbrainz.org/ws/2/"),Timeout=TimeSpan.FromSeconds(20)};
    public PublicMusicClient(){_client.DefaultRequestHeaders.UserAgent.ParseAdd("HomeMediaServer/"+BuildInfo.Version+" (personal music library)");}
    public HttpClient CreateClient(string name)=>_client;
    public void Dispose()=>_client.Dispose();
}
