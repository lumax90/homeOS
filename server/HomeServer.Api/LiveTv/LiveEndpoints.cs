using HomeServer.Core;
using HomeServer.Identity;
using HomeServer.Infrastructure;
using Microsoft.AspNetCore.Http.Features;

namespace HomeServer.LiveTv;
public static class LiveEndpoints
{
 public static void MapLiveTv(this WebApplication app,RouteGroupBuilder api)
 {
  app.MapGet("/internal/live/{token}/{key}",(string token,string key,HttpContext context,LiveInputRelay relay)=>relay.ServeAsync(token,key,context)).AllowAnonymous();
   var group=api.MapGroup("/live");group.MapGet("/sources",(MediaStore store)=>Results.Ok(store.LiveSources())).RequireAuthorization("admin");
   group.MapGet("/public-lists",(LivePublicCatalog catalog)=>Results.Ok(catalog.List())).RequireAuthorization("admin");
   group.MapGet("/public-lists/{id}",async(string id,LivePublicCatalog catalog,CancellationToken token)=>Results.Ok(await catalog.PreviewAsync(id,token))).RequireAuthorization("admin").RequireRateLimiting("watch-command");
   group.MapPost("/public-lists/{id}/add",(string id,CurrentAccount actor,HttpContext context,LivePublicCatalog catalog,LiveSources service)=>{LiveAccess.Command(actor,context);var source=catalog.Add(id,actor.User);return Results.Ok(new{source,job=service.QueueWhenPossible(source,actor.User)});}).RequireAuthorization("admin").RequireRateLimiting("watch-command");
   group.MapGet("/library",(int? offset,string? q,string? category,string? source,string? view,CurrentAccount actor,MediaStore store)=>Results.Ok(store.BrowseLiveChannels(actor.User,offset??0,q,category,source,view??"all")));
   group.MapGet("/channels/{id}",(string id,CurrentAccount actor,MediaStore store)=>Results.Ok(store.LiveChannelDetails(id,actor.User)));
   group.MapGet("/channels/{id}/neighbours",(string id,CurrentAccount actor,MediaStore store)=>Results.Ok(store.LiveNeighbours(id,actor.User)));
   group.MapPut("/channels/{id}/favorite",(string id,LiveChannelPreference input,CurrentAccount actor,HttpContext context,MediaStore store,EventHub events)=>{LiveAccess.Command(actor,context);store.SetLiveFavorite(id,actor.User,input.Favorite);events.Publish("live.changed");return Results.NoContent();});
   group.MapGet("/channels/{id}/logo",async(string id,CurrentAccount actor,HttpContext context,LiveArtwork artwork,CancellationToken token)=>{var image=await artwork.GetAsync(id,actor.User,token);if(image is null)return Results.NotFound();context.Response.Headers.CacheControl="private, max-age=1800";return Results.File(image.Value.Path,image.Value.Mime);});
   group.MapGet("/sources/{id}/settings",(string id,LiveSources service)=>Results.Ok(service.PublicSettings(id))).RequireAuthorization("admin");
   group.MapPost("/sources",async(HttpContext context,CurrentAccount actor,MediaStore store,LiveSources service,CancellationToken token)=>{LiveAccess.Command(actor,context);var input=await ReadInput(context,token);if(store.ValidateLoginSession(actor.SessionId??"",actor.User.Id) is null)throw new DomainException("Oturum gerekli.",401);var source=service.Save(null,input,actor.User);return Results.Ok(new{source,job=service.QueueWhenPossible(source,actor.User)});}).RequireAuthorization("admin");
   group.MapPut("/sources/{id}",async(string id,HttpContext context,CurrentAccount actor,MediaStore store,LiveSources service,CancellationToken token)=>{LiveAccess.Command(actor,context);var input=await ReadInput(context,token);if(store.ValidateLoginSession(actor.SessionId??"",actor.User.Id) is null)throw new DomainException("Oturum gerekli.",401);var source=service.Save(id,input,actor.User);return Results.Ok(new{source,job=service.QueueWhenPossible(source,actor.User)});}).RequireAuthorization("admin");
  group.MapPost("/sources/{id}/refresh",(string id,CurrentAccount actor,HttpContext context,LiveSources service)=>{LiveAccess.Command(actor,context);return Results.Ok(service.Queue(id,actor.User));}).RequireAuthorization("admin");
   group.MapGet("/channels",(bool? all,CurrentAccount actor,MediaStore store)=>Results.Ok(store.LiveChannels(actor.User,all==true).Select(MediaStore.PublicLiveChannel)));
  group.MapPut("/channels/{id}",(string id,ChannelInput input,MediaStore store)=>{store.SetLiveChannel(id,input);return Results.NoContent();}).RequireAuthorization("admin");
  group.MapGet("/channels/{id}/guide",(string id,int? hours,CurrentAccount actor,MediaStore store)=>{_=store.RequireLiveChannel(id,actor.User);var programmes=store.LiveGuide(id,DateTimeOffset.UtcNow.AddHours(-2),DateTimeOffset.UtcNow.AddHours(Math.Clamp(hours??48,1,336)));return Results.Ok(programmes.Where(item=>actor.User.IsAdmin||actor.User.MaximumAge is null||item.MinimumAge is{}age&&age<=actor.User.MaximumAge||item.MinimumAge is null&&actor.User.AllowUnrated));});
   group.MapPost("/channels/{id}/play",async(string id,CurrentAccount actor,HttpContext context,MediaStore store,LivePlayback player,CancellationToken token)=>{LiveAccess.Command(actor,context);var input=context.Request.HasJsonContentType()?await context.Request.ReadFromJsonAsync<LivePlayInput>(token):null;return Results.Ok(await player.StartAsync(id,store.GetPlaybackActor(actor.User.Id,actor.SessionId),token,input));});
   group.MapPost("/sessions/{id}/start",async(string id,LiveConfigureInput input,CurrentAccount actor,HttpContext context,MediaStore store,LivePlayback player,CancellationToken token)=>{LiveAccess.Command(actor,context);return Results.Ok(await player.ConfigureAsync(id,store.GetPlaybackActor(actor.User.Id,actor.SessionId),input,token));});
  group.MapPost("/clients/{id}/close",async(string id,PlaybackClientClose input,CurrentAccount actor,HttpContext context,MediaStore store,LivePlayback player)=>{LiveAccess.Command(actor,context);await player.CloseClientAsync(id,input.Revision??0,store.GetPlaybackActor(actor.User.Id,actor.SessionId));return Results.NoContent();});
  group.MapPost("/sessions/{id}/heartbeat",(string id,CurrentAccount actor,MediaStore store,LivePlayback player)=>Results.Ok(player.Touch(id,store.GetPlaybackActor(actor.User.Id,actor.SessionId))));
  group.MapDelete("/sessions/{id}",async(string id,CurrentAccount actor,MediaStore store,LivePlayback player)=>{await player.CloseAsync(id,store.GetPlaybackActor(actor.User.Id,actor.SessionId));return Results.NoContent();});
  group.MapMethods("/sessions/{id}/{file}",["GET","HEAD"],(string id,string file,CurrentAccount actor,MediaStore store,LivePlayback player)=>player.Artifact(id,file,store.GetPlaybackActor(actor.User.Id,actor.SessionId)));
  group.MapGet("/recordings",(CurrentAccount actor,MediaStore store)=>Results.Ok(store.DvrRecordings(actor.User)));
  group.MapPost("/recordings",(DvrInput input,CurrentAccount actor,MediaStore store)=>Results.Ok(store.QueueDvr(input,actor.User)));
  group.MapPost("/recordings/timed",(TimedDvrInput input,CurrentAccount actor,MediaStore store)=>Results.Ok(store.QueueTimedDvr(input,actor.User)));
  group.MapPost("/recordings/{id}/cancel",(string id,ProductJobCommand input,CurrentAccount actor,MediaStore store)=>{store.CancelDvr(id,input.Revision,actor.User);return Results.NoContent();});
  group.MapGet("/rules",(CurrentAccount actor,MediaStore store)=>Results.Ok(store.DvrRules(actor.User)));
  group.MapPost("/rules",(DvrRuleInput input,CurrentAccount actor,MediaStore store)=>Results.Ok(store.SaveDvrRule(null,input,actor.User)));
  group.MapPut("/rules/{id}",(string id,DvrRuleInput input,CurrentAccount actor,MediaStore store)=>Results.Ok(store.SaveDvrRule(id,input,actor.User)));
 }
 private static async Task<LiveSourceInput> ReadInput(HttpContext context,CancellationToken token){if(context.Features.Get<IHttpMaxRequestBodySizeFeature>() is{IsReadOnly:false}feature)feature.MaxRequestBodySize=9*1024*1024;return await context.Request.ReadFromJsonAsync<LiveSourceInput>(token)??throw new DomainException("TV kaynağı gerekli.");}
}
