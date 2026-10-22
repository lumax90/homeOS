using HomeServer.Core;
using HomeServer.Identity;
using HomeServer.Infrastructure;

namespace HomeServer.Playback;

public static class PlaybackQuotaEndpoints
{
    public static void MapPlaybackQuotas(this RouteGroupBuilder api)
    {
        api.MapGet("/account/playback", async (bool? all, CurrentAccount actor, MediaStore store, PlaybackSessions playback) =>
        { await playback.ReconcileAsync(); return Results.Ok(playback.Usage(store.GetPlaybackActor(actor.User.Id, actor.SessionId), all ?? false)); });
        api.MapDelete("/account/playback/{id}", async (string id, CurrentAccount actor, MediaStore store, PlaybackSessions playback,LiveTv.LivePlayback live,Music.RadioPlayback radio) =>
        {
            if(id.StartsWith("radio-",StringComparison.Ordinal))radio.End(id[6..],actor.User,actor.SessionId??"",manage:true);
            else if(id.StartsWith("podcast-",StringComparison.Ordinal))store.ClosePodcast(id[8..],actor.User);
            else if(id.StartsWith("live-",StringComparison.Ordinal)){var session=store.GetLiveSession(id[5..]);if(session is not null){if(session.UserId!=actor.User.Id&&!actor.User.IsAdmin)throw new DomainException("Oynatma hesabı farklı.",403);await live.CloseAsync(session.Id,new(session.UserId,session.LoginId,session.DeviceId));}}
            else await playback.CloseAsync(id, store.GetPlaybackActor(actor.User.Id, actor.SessionId), manage: true);return Results.NoContent();
        });
        api.MapGet("/playback/limits", (MediaStore store) => Results.Ok(store.GetPlaybackLimits())).RequireAuthorization("admin");
        api.MapPut("/playback/limits", async (PlaybackLimits input, MediaStore store, PlaybackSessions playback, EventHub events) =>
        { var result = store.SavePlaybackLimits(input); await playback.ReconcileAsync(); events.Publish("playback.changed"); return Results.Ok(result); }).RequireAuthorization("admin");
        api.MapGet("/users/{id}/playback-limits", (string id, MediaStore store) => { _ = store.GetAccount(id) ?? throw new DomainException("Hesap bulunamadı.", 404); return Results.Ok(store.GetUserPlaybackLimits(id)); }).RequireAuthorization("admin");
        api.MapPut("/users/{id}/playback-limits", async (string id, UserPlaybackLimits input, MediaStore store, PlaybackSessions playback, EventHub events) =>
        { var result = store.SaveUserPlaybackLimits(id, input); await playback.ReconcileAsync(); events.Publish("playback.changed"); return Results.Ok(result); }).RequireAuthorization("admin");
        api.MapGet("/playback/devices", (CurrentAccount actor, MediaStore store) => Results.Ok(store.GetPlaybackDevices(actor.User.IsAdmin ? null : actor.User.Id, store.GetPlaybackActor(actor.User.Id, actor.SessionId).DeviceId)));
        api.MapPut("/playback/devices/{id}", async (string id, PlaybackDeviceInput input, MediaStore store, PlaybackSessions playback, EventHub events) =>
        { var result = store.SavePlaybackDevice(id, input); await playback.ReconcileAsync(); events.Publish("playback.changed"); return Results.Ok(result); }).RequireAuthorization("admin");
    }
}
