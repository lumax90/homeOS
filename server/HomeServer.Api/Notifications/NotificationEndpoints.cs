using HomeServer.Core;
using HomeServer.Identity;
using HomeServer.Infrastructure;

namespace HomeServer.Notifications;

public static class NotificationEndpoints
{
    public static void MapNotificationDelivery(this RouteGroupBuilder api)
    {
        var channels = api.MapGroup("/notification-channels").RequireAuthorization("admin");
        channels.MapGet("", (MediaStore store) => Results.Ok(store.GetNotificationChannels()));
        channels.MapGet("/capabilities", () => Results.Ok(new { kinds = new[] { "webhook", "smtp" }, events = NotificationSettings.EventKinds, smtp = "MailKit 4.18.1", webhookSignature = "HMAC-SHA256" }));
        channels.MapPost("", (NotificationChannelInput input, NotificationSettings settings, EventHub events) =>
        { var result = settings.Save(null, input); events.Publish("notifications.changed"); return Results.Ok(result); });
        channels.MapPut("/{id}", (string id, NotificationChannelInput input, NotificationSettings settings, NotificationDispatcher dispatcher, EventHub events) =>
        { var result = settings.Save(id, input); dispatcher.Interrupt(channelId: id); events.Publish("notifications.changed"); return Results.Ok(result); });
        channels.MapDelete("/{id}", (string id, int revision, MediaStore store, NotificationDispatcher dispatcher, EventHub events) =>
        { store.DeleteNotificationChannel(id, revision); dispatcher.Interrupt(channelId: id); events.Publish("notifications.changed"); return Results.NoContent(); });
        channels.MapPost("/{id}/test", (string id, NotificationDeliveryCommand input, MediaStore store, TimeProvider clock, EventHub events) =>
        { var result = store.QueueNotificationTest(id, input.Revision, clock.GetUtcNow()); events.Publish("notifications.changed"); return Results.Ok(result); });
        api.MapGet("/notification-deliveries", (bool? all, string? channelId, CurrentAccount actor, MediaStore store) =>
        { if (all == true && !actor.User.IsAdmin) throw new DomainException("Yönetici yetkisi gerekli.", 403); return Results.Ok(store.GetNotificationDeliveries(all == true ? null : actor.User.Id, channelId)); });
        api.MapGet("/notification-deliveries/{id}/attempts", (string id, CurrentAccount actor, MediaStore store) =>
        { Require(id, actor, store); return Results.Ok(store.GetNotificationAttempts(id)); });
        api.MapPost("/notification-deliveries/{id}/cancel", (string id, NotificationDeliveryCommand input, CurrentAccount actor, MediaStore store, NotificationDispatcher dispatcher, EventHub events) =>
        { Require(id, actor, store); store.CancelNotificationDelivery(id, input.Revision); dispatcher.Interrupt(deliveryId: id); events.Publish("notifications.changed"); return Results.NoContent(); });
        api.MapPost("/notification-deliveries/{id}/retry", (string id, NotificationDeliveryCommand input, CurrentAccount actor, MediaStore store, TimeProvider clock, EventHub events) =>
        { Require(id, actor, store); store.RetryNotificationDelivery(id, input.Revision, clock.GetUtcNow()); events.Publish("notifications.changed"); return Results.NoContent(); });
    }
    private static void Require(string id, CurrentAccount actor, MediaStore store)
    {
        var job = store.GetNotificationDelivery(id);
        if (job is null || job.UserId != actor.User.Id && !actor.User.IsAdmin) throw new DomainException("Gönderim bulunamadı.", 404);
    }
}
