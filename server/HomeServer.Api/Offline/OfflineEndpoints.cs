using HomeServer.Core;
using HomeServer.Identity;
using HomeServer.Infrastructure;

namespace HomeServer.Offline;

public static class OfflineEndpoints
{
    public static void MapOffline(this RouteGroupBuilder api)
    {
        api.MapGet("/offline/device", (CurrentAccount actor, MediaStore store) =>
        {
            var identity = store.GetPlaybackActor(actor.User.Id, actor.SessionId);
            return Results.Ok(new OfflineDeviceStatus(store.OfflineServerId, actor.User.Id, identity.DeviceId, identity.LoginId, actor.User.DisplayName, store.GetOfflinePolicy(actor.User.Id), store.GetOfflineExports(identity)));
        });
        api.MapPost("/offline/media/{id}", async (string id, OfflineInput input, CurrentAccount actor, MediaStore store, OfflineService offline, CancellationToken token) =>
        { actor.RequireMedia(id); if (input.ExpectedUserId != actor.User.Id || input.ExpectedSessionId != actor.SessionId) throw new DomainException("Çevrimdışı komut sırasında hesap değişti.", 409); return Results.Ok(await offline.QueueAsync(id, store.GetPlaybackActor(actor.User.Id, actor.SessionId), input, token)); });
        api.MapGet("/offline/exports/{id}", (string id, CurrentAccount actor, MediaStore store, OfflineService offline) => Results.Ok(offline.Describe(id, store.GetPlaybackActor(actor.User.Id, actor.SessionId))));
        api.MapGet("/offline/exports/{id}/manifest", (string id, CurrentAccount actor, MediaStore store, OfflineService offline) => Results.Ok(offline.Manifest(id, store.GetPlaybackActor(actor.User.Id, actor.SessionId))));
        api.MapGet("/offline/exports/{id}/chunks/{asset}/{index:int}", async (string id, string asset, int index, string fingerprint, CurrentAccount actor, HttpContext context, MediaStore store, OfflineService offline, CancellationToken token) =>
        {
            var chunk = await offline.ReadChunkAsync(id, asset, index, fingerprint, store.GetPlaybackActor(actor.User.Id, actor.SessionId), token);
            context.Response.Headers["X-Home-Sha256"] = chunk.Hash; context.Response.Headers["X-Home-Fingerprint"] = fingerprint;
            return Results.Bytes(chunk.Data, "application/octet-stream");
        });
        api.MapPost("/offline/exports/{id}/renew", (string id, OfflineCommand input, CurrentAccount actor, MediaStore store) => Results.Ok(store.RenewOfflineExport(id, input.Revision, store.GetPlaybackActor(actor.User.Id, actor.SessionId))));
        api.MapPost("/offline/exports/{id}/retry", (string id, OfflineCommand input, CurrentAccount actor, MediaStore store, EventHub events) =>
        { var result = store.RetryOfflineExport(id, input.Revision, store.GetPlaybackActor(actor.User.Id, actor.SessionId)); events.Publish("offline.changed"); return Results.Ok(result); });
        api.MapPost("/offline/exports/{id}/cancel", (string id, OfflineCommand input, CurrentAccount actor, MediaStore store, OfflineService offline, EventHub events) =>
        { store.CancelOfflineExport(id, input.Revision, store.GetPlaybackActor(actor.User.Id, actor.SessionId)); offline.Interrupt(id); events.Publish("offline.changed"); return Results.NoContent(); });
        api.MapPost("/offline/exports/{id}/progress", (string id, OfflineProgressInput input, CurrentAccount actor, MediaStore store, EventHub events) =>
        { var result = store.SyncOfflineProgress(id, input, store.GetPlaybackActor(actor.User.Id, actor.SessionId)); if (result.Status == "applied") events.Publish("catalog.changed"); return Results.Ok(result); });
        api.MapGet("/users/{id}/offline-policy", (string id, MediaStore store) => { _ = store.GetAccount(id) ?? throw new DomainException("Hesap bulunamadı.", 404); return Results.Ok(store.GetOfflinePolicy(id)); }).RequireAuthorization("admin");
        api.MapPut("/users/{id}/offline-policy", (string id, OfflinePolicy input, MediaStore store, OfflineService offline, EventHub events) =>
        { var result = store.SaveOfflinePolicy(id, input); offline.InterruptUser(id); events.Publish("offline.changed"); return Results.Ok(result); }).RequireAuthorization("admin");
    }
}
