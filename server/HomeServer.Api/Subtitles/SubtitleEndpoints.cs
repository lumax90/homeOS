using HomeServer.Core;
using HomeServer.Identity;
using HomeServer.Infrastructure;
using HomeServer.Playback;

namespace HomeServer.Subtitles;

public static class SubtitleEndpoints
{
    public static void MapSubtitleProviders(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/subtitles").RequireAuthorization("admin");
        group.MapGet("/connection", (SubtitleSettings settings) => Results.Ok(settings.Status));
        group.MapPut("/connection", (SubtitleConnectionInput input, SubtitleSettings settings, SubtitleCoordinator coordinator, EventHub events) => { settings.Save(input); coordinator.Interrupt(); events.Publish("subtitles.changed"); return Results.Ok(settings.Status); });
        group.MapPost("/login", async (SubtitleLogin input, OpenSubtitlesProvider provider, CancellationToken token) => Results.Ok(await provider.LoginAsync(input, token))).RequireRateLimiting("credentials");
        group.MapGet("/profile", (SubtitleSettings settings) => Results.Ok(settings.Profile));
        group.MapPut("/profile", (SubtitleLanguageProfile input, SubtitleSettings settings, SubtitleCoordinator coordinator, EventHub events) => { settings.SaveProfile(input); coordinator.Interrupt(); events.Publish("subtitles.changed"); return Results.Ok(settings.Profile); });
        group.MapGet("/wanted", (MediaStore store) => Results.Ok(store.GetSubtitleWanted()));
        group.MapPost("/media/{id}/search", async (string id, SubtitleSearchInput input, CurrentAccount actor, SubtitleAcquisition acquisition, CancellationToken token) =>
        { actor.RequireMedia(id); return Results.Ok(await acquisition.SearchAsync(id, input, token)); });
        group.MapPost("/media/{id}/download/{candidate}", async (string id, string candidate, SubtitleDownloadInput? input, CurrentAccount actor, SubtitleAcquisition acquisition, CancellationToken token) =>
        { actor.RequireMedia(id); return Results.Ok(await acquisition.AcquireAsync(id, candidate, token, replaceId: input?.ReplaceId)); });
        group.MapGet("/media/{id}/policy", (string id, CurrentAccount actor, MediaStore store) => { actor.RequireMedia(id); return Results.Ok(store.GetEffectiveSubtitlePolicy(id)); });
        group.MapPut("/media/{id}/policy", (string id, SubtitleOverrideInput input, CurrentAccount actor, MediaStore store, SubtitleCoordinator coordinator, EventHub events) =>
        { actor.RequireMedia(id); var result = store.SaveSubtitleOverride(id, input); coordinator.Interrupt(id); events.Publish("subtitles.changed"); return Results.Ok(result); });
        group.MapPost("/media/{id}/refresh", (string id, CurrentAccount actor, MediaStore store) => { actor.RequireMedia(id); store.WakeSubtitle(id); return Results.Accepted(); });
        group.MapGet("/media/{id}/versions", (string id, CurrentAccount actor, MediaStore store) => { actor.RequireMedia(id); return Results.Ok(store.GetManagedSubtitles(id, true)); });
        group.MapPost("/media/{id}/versions/{version}/restore", (string id, string version, SubtitleRestoreInput? input, CurrentAccount actor, MediaStore store, SubtitleCoordinator coordinator, EventHub events) =>
        { actor.RequireMedia(id); var result = store.RestoreSubtitleVersion(id, version, input?.ReplaceId); coordinator.Interrupt(id); events.Publish("subtitles.changed"); return Results.Ok(result); });
        group.MapDelete("/media/{id}/versions/{version}", (string id, string version, CurrentAccount actor, SubtitleService subtitles, EventHub events) =>
        { actor.RequireMedia(id); subtitles.Remove(id, version); events.Publish("subtitles.changed"); return Results.NoContent(); });
        group.MapGet("/sync/capabilities", (SileroVad vad) => Results.Ok(vad.Capabilities()));
        group.MapGet("/sync/jobs", (string? mediaId, MediaStore store) => Results.Ok(store.GetSubtitleSyncJobs(mediaId)));
        group.MapPost("/media/{id}/sync", async (string id, SubtitleSyncInput input, CurrentAccount actor, SubtitleSyncService sync, CancellationToken token) =>
        { actor.RequireMedia(id); return Results.Ok(await sync.QueueAsync(id, input, token)); });
        group.MapPost("/sync/jobs/{id}/cancel", (string id, SubtitleSyncService sync) => { sync.Cancel(id); return Results.NoContent(); });
        group.MapPost("/sync/jobs/{id}/apply", async (string id, SubtitleSyncCommand input, SubtitleSyncService sync, CancellationToken token) => Results.Ok(await sync.ApplyAsync(id, input.Revision, token)));
    }
}
