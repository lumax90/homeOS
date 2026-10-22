using HomeServer.Core;
using HomeServer.Identity;
using HomeServer.Infrastructure;

namespace HomeServer.Music;

public static class MusicAutomationEndpoints
{
    public static void MapMusicAutomation(this RouteGroupBuilder api)
    {
        api.MapGet("/music/monitors", (CurrentAccount actor, MediaStore store) => { actor.RequireUnratedCatalog(); return Results.Ok(store.GetMusicMonitors(actor.User.Id)); }).RequireAuthorization("discover");
        api.MapPut("/music/monitors/{kind}/{mbid}", async (string kind, string mbid, MusicMonitorInput input, CurrentAccount actor, MediaStore store, MusicBrainzProvider metadata, MusicAutomationCoordinator coordinator, CancellationToken token) =>
        {
            actor.RequireUnratedCatalog(); _ = MusicMonitorPolicy.Validate(input.Settings);
            var name = kind switch { "artist" => (await metadata.ArtistAsync(mbid, token)).Name, "album" => (await metadata.AlbumAsync(mbid, token)).Title, _ => throw new DomainException("Takip türü geçersiz.") };
            var result = store.SaveMusicMonitor(kind, mbid, name, input, actor.User); coordinator.Cancel(result.Id); return Results.Ok(result);
        }).RequireAuthorization("discover");
        api.MapGet("/music/monitors/{id}/albums", (string id, int? offset, CurrentAccount actor, MediaStore store) =>
        { Own(id, actor, store); return Results.Ok(store.GetMonitoredMusicAlbums(id, offset ?? 0)); }).RequireAuthorization("discover");
        api.MapPut("/music/monitors/{id}/albums/{group}", (string id, string group, MusicAlbumOverride input, CurrentAccount actor, MediaStore store, MusicAutomationCoordinator coordinator) =>
        { Own(id, actor, store); store.OverrideMonitoredMusicAlbum(id, group, input, actor.User.Id); coordinator.Cancel(id); return Results.NoContent(); }).RequireAuthorization("discover");
        api.MapPost("/music/monitors/{id}/refresh", (string id, MusicAlbumOverride input, CurrentAccount actor, MediaStore store) =>
        { Own(id, actor, store); store.WakeMusicMonitor(id, input.ExpectedRevision, actor.User.Id); return Results.Accepted(); }).RequireAuthorization("discover");
        api.MapGet("/music/automation/jobs", (CurrentAccount actor, MediaStore store) => { actor.RequireUnratedCatalog(); return Results.Ok(store.GetMusicAutomationJobs(actor.User.Id)); }).RequireAuthorization("discover");
        api.MapGet("/music/automation/blocked", (MediaStore store) => Results.Ok(store.GetMusicBlockedReleases())).RequireAuthorization("admin");
        api.MapDelete("/music/automation/blocked/{group}/{fingerprint}", (string group, string fingerprint, MediaStore store) => { store.UnblockMusicRelease(group, fingerprint); return Results.NoContent(); }).RequireAuthorization("admin");
    }
    private static void Own(string id, CurrentAccount actor, MediaStore store)
    {
        actor.RequireUnratedCatalog(); if (store.GetMusicMonitor(id)?.UserId != actor.User.Id) throw new DomainException("Müzik takibi bulunamadı.", 404);
    }
}
