using HomeServer.Core;
using HomeServer.Identity;
using HomeServer.Infrastructure;

namespace HomeServer.Watching;

public static class WatchRoomEndpoints
{
    private static PlaybackActor Actor(CurrentAccount account, MediaStore store, WatchIdentity? expected = null, bool command = false)
    {
        if (command && (expected?.UserId != account.User.Id || expected?.SessionId != account.SessionId)) throw new DomainException("Oda komutu sırasında profil veya giriş değişti.", 409);
        return store.GetPlaybackActor(account.User.Id, account.SessionId);
    }
    public static void MapWatchRooms(this RouteGroupBuilder api)
    {
        var routes = api.MapGroup("/watch");
        routes.MapGet("/rooms", (CurrentAccount account, MediaStore store, TimeProvider clock) => Results.Ok(store.GetWatchRooms(Actor(account, store), clock.GetUtcNow()))).RequireRateLimiting("watch-live");
        routes.MapGet("/rooms/{id}", (string id, string? member, CurrentAccount account, MediaStore store, TimeProvider clock) => Results.Ok(store.GetWatchRoom(id, Actor(account, store), clock.GetUtcNow(), member))).RequireRateLimiting("watch-live");
        routes.MapGet("/rooms/{id}/events", (string id, CurrentAccount account, MediaStore store, TimeProvider clock) => Results.Ok(store.GetWatchRoomEvents(id, Actor(account, store), clock.GetUtcNow()))).RequireRateLimiting("watch-live");
        routes.MapPost("/rooms", async (WatchRoomCreate input, CurrentAccount account, MediaStore store, WatchRoomService rooms) => Results.Ok(await rooms.CreateAsync(Actor(account, store, input.Identity, true), input))).RequireRateLimiting("watch-command");
        routes.MapPost("/rooms/{id}/join", async (string id, WatchRoomJoin input, CurrentAccount account, MediaStore store, WatchRoomService rooms) => Results.Ok(await rooms.JoinAsync(id, Actor(account, store, input.Identity, true), input))).RequireRateLimiting("watch-command");
        routes.MapPost("/clients/{id}/leave", async (string id, WatchRoomLeave input, CurrentAccount account, MediaStore store, WatchRoomService rooms) =>
        { await rooms.LeaveAsync(Actor(account, store, input.Identity, true), id, input); return Results.NoContent(); }).RequireRateLimiting("watch-command");
        routes.MapPost("/rooms/{id}/members/{member}/heartbeat", (string id, string member, WatchRoomHeartbeat input, CurrentAccount account, MediaStore store, WatchRoomService rooms) => Results.Ok(rooms.Pulse(id, member, Actor(account, store, input.Identity, true), input))).RequireRateLimiting("watch-live");
        routes.MapPost("/rooms/{id}/commands", async (string id, WatchRoomCommand input, CurrentAccount account, MediaStore store, WatchRoomService rooms, CancellationToken token) => Results.Ok(await rooms.CommandAsync(id, Actor(account, store, input.Identity, true), input, token))).RequireRateLimiting("watch-command");
    }
}
