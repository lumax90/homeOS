using HomeServer.Core;
using HomeServer.Identity;
using HomeServer.Infrastructure;

namespace HomeServer.Music;
public static class RadioEndpoints
{
    public static void MapMusicRadio(this RouteGroupBuilder api)
    {
        api.MapGet("/music/radio/stations",async(string? q,string? tag,string? country,int? offset,CurrentAccount actor,RadioDirectory directory,CancellationToken token)=>{actor.RequireUnratedCatalog();return Results.Ok(await directory.SearchAsync(actor.User.Id,q,tag,country,offset??0,token));}).RequireRateLimiting("watch-command");
        api.MapGet("/music/radio/favorites",(CurrentAccount actor,RadioDirectory directory)=>Results.Ok(MusicAccess.Online(actor.User)?directory.Favorites(actor.User.Id):[]));
        api.MapPut("/music/radio/favorites/{id}",async(string id,RadioFavoriteCommand input,CurrentAccount actor,HttpContext context,RadioDirectory directory,MediaStore store,EventHub events,CancellationToken token)=>{MusicAccess.RequireCommand(actor,context);actor.RequireUnratedCatalog();var station=await directory.StationAsync(actor.User.Id,id,token);if(store.ValidateLoginSession(actor.SessionId??"",actor.User.Id) is not{} current||!MusicAccess.Online(current))throw new DomainException("Oturum gerekli.",401);directory.Favorite(actor.User.Id,station,input);events.Publish("music.radio.changed");return Results.NoContent();}).RequireRateLimiting("watch-command");
        api.MapPost("/music/radio/play",async(RadioPlayRequest input,CurrentAccount actor,HttpContext context,RadioPlayback playback,CancellationToken token)=>{MusicAccess.RequireCommand(actor,context);return Results.Ok(await playback.StartAsync(actor.User,actor.SessionId??"",input,token));}).RequireRateLimiting("watch-command");
        api.MapPost("/music/radio/sessions/{id}/heartbeat",(string id,CurrentAccount actor,HttpContext context,RadioPlayback playback)=>{MusicAccess.RequireCommand(actor,context);playback.Touch(id,actor.User,actor.SessionId??"");return Results.NoContent();}).RequireRateLimiting("watch-command");
        api.MapDelete("/music/radio/sessions/{id}",(string id,CurrentAccount actor,HttpContext context,RadioPlayback playback)=>{MusicAccess.RequireCommand(actor,context);playback.End(id,actor.User,actor.SessionId??"");return Results.NoContent();});
        api.MapGet("/music/radio/sessions/{id}/stream/{key}",async(string id,string key,CurrentAccount actor,HttpContext context,RadioPlayback playback)=>await playback.ServeAsync(id,key,actor.User,actor.SessionId??"",context));
    }
}
