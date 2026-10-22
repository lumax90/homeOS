using HomeServer.Core;
using HomeServer.Identity;
using HomeServer.Infrastructure;

namespace HomeServer.Music;

public static class MusicCatalogEndpoints
{
    public static void MapMusicCatalog(this RouteGroupBuilder api)
    {
        api.MapGet("/music/library/tracks", (int? offset, string? q, string? view, CurrentAccount actor, MediaStore store) => Results.Ok(store.GetMusicLibraryTracks(actor.User, offset ?? 0, q, view ?? "all")));
        api.MapGet("/music/library/artists", (int? offset, string? q, CurrentAccount actor, MediaStore store) => Results.Ok(store.GetLocalMusicArtists(actor.User, offset ?? 0, q)));
        api.MapGet("/music/library/artists/{id}/albums", (string id, int? offset, CurrentAccount actor, MediaStore store) => Results.Ok(store.GetMusicAlbums(actor.User, offset ?? 0, artistId: id)));
        api.MapGet("/music/discovery/albums/{id}/relations", async (string id, CurrentAccount actor, MusicBrainzProvider provider, CancellationToken token) => { actor.RequireUnratedCatalog(); return Results.Ok(await provider.AlbumRelationsAsync(id, token)); });
        api.MapGet("/music/recordings/{id}", async (string id, CurrentAccount actor, MusicBrainzProvider provider, CancellationToken token) => { actor.RequireUnratedCatalog(); return Results.Ok(await provider.RecordingAsync(id, token)); });
        api.MapGet("/music/media/{id}/videos", (string id, CurrentAccount actor, MediaStore store) => Results.Ok(new { recordingMbid = store.MusicRecordingForMedia(id, actor.User), items = store.GetMusicVideos(id, actor.User, actor.User.IsAdmin) }));
        api.MapPost("/music/media/{id}/videos", async (string id, MusicVideoInput input, CurrentAccount actor, MediaStore store, MusicBrainzProvider provider, EventHub events, CancellationToken token) =>
        {
            if (input.VideoRecordingMbid is { } recording && !(await provider.RecordingAsync(recording, token)).Video) throw new DomainException("Seçilen MusicBrainz kaydı bir video recording değil.", 409);
            var result = store.LinkMusicVideo(id, input, actor.User); events.Publish("music.changed"); return Results.Ok(result);
        }).RequireAuthorization("admin");
        api.MapDelete("/music/video-links/{id}", (string id, int revision, CurrentAccount actor, MediaStore store, EventHub events) => { store.UnlinkMusicVideo(id, revision, actor.User); events.Publish("music.changed"); return Results.NoContent(); }).RequireAuthorization("admin");
        var lists = api.MapGroup("/music/list-imports").RequireAuthorization("discover");
        lists.MapGet("", (CurrentAccount actor, MediaStore store) => Results.Ok(store.GetMusicListJobs(actor.User)));
        lists.MapGet("/{id}", (string id, CurrentAccount actor, MediaStore store) => Results.Ok(store.GetMusicListJob(id, actor.User)));
        lists.MapPost("", (MusicListInput input, CurrentAccount actor, MediaStore store, EventHub events) => { var job = store.QueueMusicList(input, actor.User); events.Publish("music.lists.changed"); return Results.Ok(job); });
        lists.MapPost("/{id}/cancel", (string id, MusicListApply input, CurrentAccount actor, MediaStore store, EventHub events) => { store.CancelMusicList(id, input.Revision, actor.User); events.Publish("music.lists.changed"); return Results.NoContent(); });
        lists.MapPost("/{id}/apply", (string id, MusicListApply input, CurrentAccount actor, MediaStore store, EventHub events) => { var result = store.ApplyMusicList(id, input, actor.User); events.Publish("music.lists.changed"); events.Publish("music.automation.changed"); return Results.Ok(result); });
    }
}
