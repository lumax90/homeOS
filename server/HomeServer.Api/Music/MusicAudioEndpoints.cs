using HomeServer.Core;
using HomeServer.Identity;
using HomeServer.Infrastructure;
using HomeServer.Playback;

namespace HomeServer.Music;

public static class MusicAudioEndpoints
{
    public static void MapMusicAudio(this RouteGroupBuilder api)
    {
        api.MapGet("/music/playback-preferences", (CurrentAccount actor, MediaStore store) => Results.Ok(store.GetMusicPlaybackPreferences(actor.User.Id)));
        api.MapPut("/music/playback-preferences", (MusicPlaybackPreferences input, CurrentAccount actor, MediaStore store) => { store.SaveMusicPlaybackPreferences(actor.User.Id, input); return Results.NoContent(); });
        api.MapPost("/music/playback", async (MusicPlaybackStart input, CurrentAccount actor, MediaStore store, PlaybackSessions playback, CancellationToken token) =>
        {
            actor.RequireMedia(input.MediaId); return Results.Ok(await playback.StartMusicAsync(store.GetPlaybackActor(actor.User.Id, actor.SessionId), input, token));
        });
        api.MapPost("/music/media/{id}/prepare", (string id, string? playbackId, CurrentAccount actor, MediaStore store, PlaybackSessions playback) =>
        { actor.RequireMedia(id); return playback.PrepareMusic(id, playbackId, store.GetPlaybackActor(actor.User.Id, actor.SessionId)); });
        api.MapGet("/music/media/{id}/pcm", (string id, string? playbackId, string fingerprint, long start, int frames, CurrentAccount actor, MediaStore store, PlaybackSessions playback) =>
        { actor.RequireMedia(id); return playback.MusicBytes(id, playbackId, fingerprint, start, frames, store.GetPlaybackActor(actor.User.Id, actor.SessionId)); });
        api.MapGet("/music/media/{id}/favorite", (string id, CurrentAccount actor, MediaStore store) => { actor.RequireMedia(id); return Results.Ok(new { favorite = store.IsMusicFavorite(id, actor.User.Id) }); });
        api.MapPut("/music/media/{id}/favorite", (string id, MusicFavoriteInput input, CurrentAccount actor, MediaStore store) => { store.SetMusicFavorite(id, actor.User, input.Favorite); return Results.NoContent(); });
        api.MapGet("/music/recommendations", (CurrentAccount actor, MediaStore store) => Results.Ok(store.MusicSuggestions(actor.User)));
        api.MapGet("/music/media/{id}/similar", (string id, CurrentAccount actor, MediaStore store) => Results.Ok(store.MusicSuggestions(actor.User, id)));
        api.MapGet("/music/media/{id}/analysis", async (string id, CurrentAccount actor, MediaStore store, MediaAnalysisService analysis, CancellationToken token) =>
        { actor.RequireMedia(id); var media = await analysis.InspectAsync(id, token); return Results.Ok(new { analysis = store.GetMusicSonic(id, media.Fingerprint) }); });
        api.MapPost("/music/media/{id}/analyze", (string id, MediaStore store) => Results.Ok(store.QueueMusicTask(id, "sonic"))).RequireAuthorization("admin");
        api.MapPost("/music/albums/{id}/analyze", (string id, CurrentAccount actor, MediaStore store) =>
        {
            var page = store.GetMusicTracks(id, actor.User); var total = 0;
            while (page.Items.Count > 0)
            {
                foreach (var track in page.Items.Where(track => track.Media.Available)) { store.QueueMusicTask(track.MediaId, "sonic"); total++; }
                var offset = page.Offset + page.Items.Count; if (offset >= page.Total) break; page = store.GetMusicTracks(id, actor.User, offset);
            }
            return Results.Ok(new { queued = total });
        }).RequireAuthorization("admin");
        api.MapGet("/music/tasks", (MediaStore store) => Results.Ok(store.GetMusicTasks())).RequireAuthorization("admin");
        api.MapPost("/music/tasks/{id}/cancel", (string id, MusicTaskWorker worker) => { worker.Cancel(id); return Results.NoContent(); }).RequireAuthorization("admin");
        api.MapGet("/music/media/{id}/tags/preview", async (string id, bool? rename, MusicTagService tags, CancellationToken token) => Results.Ok(await tags.PreviewAsync(id, rename ?? true, token))).RequireAuthorization("admin");
        api.MapPost("/music/media/{id}/tags", async (string id, MusicTagInput input, MusicTagService tags, CancellationToken token) => Results.Ok(await tags.QueueAsync(id, input, token))).RequireAuthorization("admin");
        api.MapPost("/music/tasks/{id}/restore-tags", async (string id, MusicTagService tags, CancellationToken token) => Results.Ok(await tags.RestoreAsync(id, token))).RequireAuthorization("admin");
        api.MapDelete("/music/tasks/{id}/tag-history", (string id, MusicTagService tags) => { tags.PruneHistory(id); return Results.NoContent(); }).RequireAuthorization("admin");
    }
}
