using HomeServer.Core;
using HomeServer.Identity;
using HomeServer.Infrastructure;

namespace HomeServer.Sources;

public static class AutomationEndpoints
{
    public static void MapAutomation(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/automation").RequireAuthorization("admin");
        group.MapGet("", (MediaStore store) => Results.Ok(store.GetAutomationRules()));
        group.MapGet("/jobs", (string? contentId, MediaStore store) => Results.Ok(store.GetAutomationJobs(contentId)));
        group.MapGet("/blocked", (MediaStore store) => Results.Ok(store.GetBlockedReleases()));
        group.MapDelete("/{id}/blocked/{fingerprint}", (string id, string fingerprint, MediaStore store) => { store.UnblockRelease(id, fingerprint); return Results.NoContent(); });
        group.MapGet("/{id}", (string id, MediaStore store) => Results.Ok(store.GetAutomationRule(id)));
        group.MapPut("/{id}", (string id, AutomationInput input, CurrentAccount actor, MediaStore store, AutomationCoordinator coordinator, EventHub events) =>
        {
            var result = store.SaveAutomationRule(id, input, actor.User.Id); coordinator.CancelContent(id); events.Publish("automation.changed"); return Results.Ok(result);
        });
        group.MapPost("/{id}/search", (string id, MediaStore store, EventHub events) =>
        {
            store.WakeAutomation(id); events.Publish("automation.changed"); return Results.Accepted(null, new { message = "Otomasyon araması sıraya alındı." });
        });
    }
}
