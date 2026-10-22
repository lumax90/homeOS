using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using HomeServer.Core;
using HomeServer.Identity;
using HomeServer.Infrastructure;
using HomeServer.LiveTv;
using Microsoft.AspNetCore.DataProtection;

namespace HomeServer.Shelves;
public sealed class PodcastService(MediaStore store,RemoteMediaHttp http,IDataProtectionProvider protection,EventHub events):IProductWorkHandler
{
 private readonly IDataProtector _protector=protection.CreateProtector("Home.Podcast.v1");public string[] Kinds=>["podcast-sync"];
 public PodcastFeed Add(string url,Account actor){_=RemoteMediaHttp.Validate(url);return store.AddPodcast(_protector.Protect(url),actor);}
 public ProductJob Queue(string id,Account actor){_=store.PodcastSecret(id,actor.Id);return store.QueueProductJob("podcast-sync",id,new{},actor);}
 public async Task RunAsync(ProductJob job,CancellationToken token)
 {
  var actor=store.RequireProductJob(job);var id=job.ScopeId!;try
  {
   var bytes=await http.ReadAsync(_protector.Unprotect(store.PodcastSecret(id,actor.Id)),false,8*1024*1024,token);using var reader=XmlReader.Create(new MemoryStream(bytes),new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=8*1024*1024});var doc=XDocument.Load(reader);var channel=doc.Descendants().FirstOrDefault(element=>element.Name.LocalName=="channel")??doc.Root!;
   static string Value(XElement element,string name)=>element.Elements().FirstOrDefault(child=>child.Name.LocalName==name)?.Value??"";
   static string Plain(string value,int max)=>System.Net.WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Replace(value,"<[^>]*>","",System.Text.RegularExpressions.RegexOptions.NonBacktracking))[..Math.Min(max,System.Net.WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Replace(value,"<[^>]*>","",System.Text.RegularExpressions.RegexOptions.NonBacktracking)).Length)];
   var title=Plain(Value(channel,"title"),500);var description=Plain(Value(channel,"description"),10000);var image=channel.Elements().FirstOrDefault(element=>element.Name.LocalName=="image")?.Attribute("href")?.Value??channel.Element("image")?.Element("url")?.Value;
   if(!Uri.TryCreate(image,UriKind.Absolute,out var imageUri)||imageUri.Scheme is not("http" or"https"))image=null;var episodes=new List<(PodcastEpisode,string,string,string)>();
   foreach(var item in channel.Elements().Where(element=>element.Name.LocalName is"item" or"entry").Take(2000))
   {
    var enclosure=item.Elements().FirstOrDefault(element=>element.Name.LocalName=="enclosure"||element.Name.LocalName=="link"&&element.Attribute("rel")?.Value=="enclosure");var url=enclosure?.Attribute("url")?.Value??enclosure?.Attribute("href")?.Value;if(url is null)continue;var uri=RemoteMediaHttp.Validate(url);var mime=enclosure?.Attribute("type")?.Value??"audio/mpeg";if(!mime.StartsWith("audio/",StringComparison.Ordinal))continue;
    var key=Value(item,"guid");if(key.Length==0)key=Value(item,"id");if(key.Length==0)key=uri.AbsoluteUri;var episodeId=PlaylistGuide.Id(id+key);var name=Plain(Value(item,"title"),500);var text=Plain(Value(item,"description"),10000);var date=DateTimeOffset.TryParse(Value(item,"pubDate"),CultureInfo.InvariantCulture,DateTimeStyles.None,out var at)?at.ToUniversalTime().ToString("O"):null;
    var rawDuration=Value(item,"duration");double? duration=double.TryParse(rawDuration,NumberStyles.Float,CultureInfo.InvariantCulture,out var seconds)?seconds:TimeSpan.TryParse(rawDuration,CultureInfo.InvariantCulture,out var time)?time.TotalSeconds:null;if(duration is<0 or>86400)duration=null;
    var explicitValue=Value(item,"explicit");var explicitContent=explicitValue.Equals("true",StringComparison.OrdinalIgnoreCase)||explicitValue.Equals("yes",StringComparison.OrdinalIgnoreCase);long? size=long.TryParse(enclosure?.Attribute("length")?.Value,out var length)&&length>0?length:null;
    episodes.Add((new(episodeId,id,name,text,date,duration,mime,size,explicitContent),key,_protector.Protect(uri.AbsoluteUri),PlaylistGuide.Id(uri.AbsoluteUri)));
   }
   _=store.RequireProductJob(job);store.UpdatePodcast(id,actor.Id,title,description,image,episodes.DistinctBy(row=>row.Item2).ToArray());store.FinishProductJob(job,new{episodes=episodes.Count});events.Publish("podcasts.changed");
  }catch(Exception ex){store.PodcastError(id,ex is DomainException?ex.Message:"Podcast beslemesi okunamadı.");throw;}
 }
 public async Task StreamAsync(string sessionId,PlaybackActor actor,HttpContext context)
 {
  var session=store.RequirePodcastSession(sessionId,actor,true);store.CheckTransientLimits(actor);var source=store.PodcastEpisodeUrl(session.EpisodeId);var range=context.Request.Headers.Range.ToString();
  using var response=await http.GetAsync(_protector.Unprotect(source.ProtectedUrl),false,context.RequestAborted,range.Length>0?range:null);
  if(!response.IsSuccessStatusCode){context.Response.StatusCode=(int)response.StatusCode;return;}context.Response.StatusCode=(int)response.StatusCode;var declared=store.RequirePodcastEpisode(session.EpisodeId,store.GetAccount(actor.UserId)!).Mime;context.Response.ContentType=declared.StartsWith("audio/",StringComparison.Ordinal)&&!declared.Any(char.IsControl)?declared:"application/octet-stream";context.Response.Headers.XContentTypeOptions="nosniff";context.Response.Headers.CacheControl="private, no-store";
  if(response.Content.Headers.ContentLength is{}length)context.Response.ContentLength=length;if(response.Content.Headers.ContentRange is{}contentRange)context.Response.Headers.ContentRange=contentRange.ToString();if(response.Headers.AcceptRanges.Contains("bytes"))context.Response.Headers.AcceptRanges="bytes";
  await using var stream=await response.Content.ReadAsStreamAsync(context.RequestAborted);var buffer=new byte[65536];var next=DateTimeOffset.UtcNow;int count;while((count=await stream.ReadAsync(buffer,context.RequestAborted))>0){if(DateTimeOffset.UtcNow>=next){_=store.RequirePodcastSession(sessionId,actor,true);store.CheckTransientLimits(actor);next=DateTimeOffset.UtcNow.AddSeconds(1);}await context.Response.Body.WriteAsync(buffer.AsMemory(0,count),context.RequestAborted);}
 }
}
public sealed class PodcastScheduler(MediaStore store,PodcastService podcasts):BackgroundService
{
 protected override async Task ExecuteAsync(CancellationToken token){while(!token.IsCancellationRequested){foreach(var feed in store.DuePodcasts())try{if(store.GetAccount(feed.User) is{Enabled:true}actor)podcasts.Queue(feed.Id,actor);}catch(DomainException ex){store.PodcastError(feed.Id,ex.Message);}try{await Task.Delay(60000,token);}catch(OperationCanceledException){break;}}}
}
public static class PodcastEndpoints
{
 public sealed record Subscribe(string Url);
 public static void MapPodcasts(this RouteGroupBuilder api)
 {
  api.MapGet("/podcasts",(CurrentAccount actor,MediaStore store)=>Results.Ok(store.Podcasts(actor.User)));
  api.MapPost("/podcasts",(Subscribe input,CurrentAccount actor,PodcastService service)=>{var feed=service.Add(input.Url,actor.User);return Results.Ok(new{feed,job=service.Queue(feed.Id,actor.User)});});
  api.MapDelete("/podcasts/{id}",(string id,CurrentAccount actor,MediaStore store)=>{store.RemovePodcast(id,actor.User);return Results.NoContent();});
  api.MapPost("/podcasts/{id}/refresh",(string id,CurrentAccount actor,PodcastService service)=>Results.Ok(service.Queue(id,actor.User)));
  api.MapGet("/podcasts/{id}/episodes",(string id,int? offset,CurrentAccount actor,MediaStore store)=>Results.Ok(store.PodcastEpisodes(id,actor.User,offset??0)));
  api.MapPost("/podcasts/episodes/{id}/play",(string id,CurrentAccount actor,MediaStore store)=>{var session=store.StartPodcastSession(id,store.GetPlaybackActor(actor.User.Id,actor.SessionId));return Results.Ok(new{id=session.Id,url=$"/api/podcasts/sessions/{session.Id}/stream",episode=store.RequirePodcastEpisode(id,actor.User)});});
  api.MapGet("/podcasts/sessions/{id}/stream",(string id,CurrentAccount actor,MediaStore store,PodcastService service,HttpContext context)=>service.StreamAsync(id,store.GetPlaybackActor(actor.User.Id,actor.SessionId),context));
  api.MapPost("/podcasts/sessions/{id}/heartbeat",(string id,CurrentAccount actor,MediaStore store)=>Results.Ok(store.RequirePodcastSession(id,store.GetPlaybackActor(actor.User.Id,actor.SessionId),true)));
  api.MapDelete("/podcasts/sessions/{id}",(string id,CurrentAccount actor,MediaStore store)=>{store.ClosePodcast(id,actor.User);return Results.NoContent();});
  api.MapPut("/podcasts/episodes/{id}/progress",(string id,PodcastProgressInput input,CurrentAccount actor,MediaStore store)=>{store.SavePodcastProgress(id,input,actor.User);return Results.NoContent();});
 }
}
