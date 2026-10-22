using HomeServer.Core;
using HomeServer.Identity;
using HomeServer.Infrastructure;

namespace HomeServer.Notifications;

public static class WebPushEndpoints
{
    public static void MapWebPush(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/push");
        group.MapGet("/configuration", (WebPushSettings settings) => Results.Ok(settings.Configuration)).RequireAuthorization("admin");
        group.MapPut("/configuration", (PushServerInput input, WebPushSettings settings, MediaStore store, NotificationDispatcher dispatcher, EventHub events) =>
        {
            var before = settings.Configuration; var channels = store.GetNotificationChannels().Where(channel => channel.Kind == "push").Select(channel => channel.Id).ToArray();
            var result = settings.Save(input);
            if (!result.Enabled || before.KeyRevision != result.KeyRevision) foreach (var id in channels) dispatcher.Interrupt(channelId: id);
            events.Publish("notifications.changed"); return Results.Ok(result);
        }).RequireAuthorization("admin");
        group.MapGet("/device", (CurrentAccount actor, MediaStore store, WebPushSettings settings) =>
        {
            var identity = store.GetPlaybackActor(actor.User.Id, actor.SessionId);
            return Results.Ok(new { userId = actor.User.Id, sessionId = actor.SessionId, configuration = settings.Configuration, current = store.GetCurrentBrowserPush(identity), devices = store.GetBrowserPushDevices(identity) });
        });
        group.MapPost("/device", (BrowserPushInput input, CurrentAccount actor, MediaStore store, WebPushSettings settings, NotificationDispatcher dispatcher, EventHub events) =>
        {
            var identity = store.GetPlaybackActor(actor.User.Id, actor.SessionId); var before = store.GetCurrentBrowserPush(identity);
            var result = settings.Subscribe(identity, input);
            if (before?.BindingId != result.BindingId) dispatcher.Interrupt(channelId: result.ChannelId);
            events.Publish("notifications.changed"); return Results.Ok(result);
        });
        group.MapDelete("/devices/{id}", (string id, int revision, CurrentAccount actor, MediaStore store, NotificationDispatcher dispatcher, EventHub events) =>
        { store.RemoveBrowserPush(store.GetPlaybackActor(actor.User.Id, actor.SessionId), id, revision); dispatcher.Interrupt(channelId: id); events.Publish("notifications.changed"); return Results.NoContent(); });
        group.MapPost("/device/test", (NotificationDeliveryCommand input, CurrentAccount actor, MediaStore store, TimeProvider clock, EventHub events) =>
        {
            var registration = store.GetCurrentBrowserPush(store.GetPlaybackActor(actor.User.Id, actor.SessionId)) ?? throw new DomainException("Bu girişte push etkin değil.", 409);
            var job = store.QueueNotificationTest(registration.ChannelId, input.Revision, clock.GetUtcNow()); events.Publish("notifications.changed"); return Results.Ok(job);
        });
    }
}
