using HomeServer.Core;
using HomeServer.Infrastructure;
using HomeServer.Identity;
using HomeServer.Metadata;
using Microsoft.AspNetCore.Http.Features;

namespace HomeServer.Playback;

public static class PlaybackEndpoints
{
    public static void MapPlayback(this RouteGroupBuilder api)
    {
        api.MapGet("/media/{id}/tracks", async (string id, CurrentAccount actor, MediaAnalysisService analysis, SubtitleService subtitles, CancellationToken token) =>
        {
            actor.RequireMedia(id);
            var media = await analysis.InspectAsync(id, token);
            return Results.Ok(new MediaTracks(id, media.Fingerprint, media.Probe.Duration, media.Probe.Container, media.Probe.BitRate,
                media.Probe.Streams ?? [], media.Probe.Chapters ?? [], subtitles.List(media)));
        });
        api.MapGet("/media/{id}/subtitles/{track}", async (string id, string track, double? start, double? delay,
            CurrentAccount actor, MediaAnalysisService analysis, SubtitleService subtitles, CancellationToken token) =>
        {
            actor.RequireMedia(id);
            var media = await analysis.InspectAsync(id, token);
            return Results.Text(await subtitles.WebVttAsync(media, track, start ?? 0, delay ?? 0, token), "text/vtt; charset=utf-8");
        });
        api.MapPost("/media/{id}/subtitles", async (string id, string language, string title, string extension, HttpContext context,
            SubtitleService subtitles, EventHub events, CancellationToken token) =>
        {
            const int max = 2 * 1024 * 1024;
            if (context.Request.ContentLength > max) throw new DomainException("Altyazı dosyası en fazla 2 MB olabilir.", 413);
            var limit = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (limit is { IsReadOnly: false }) limit.MaxRequestBodySize = max;
            var bytes = await MetadataProvider.ReadLimitedAsync(context.Request.Body, max, token);
            var result = await subtitles.UploadAsync(id, language, title, extension, bytes, token);
            events.Publish("tracks.changed");
            return Results.Ok(result);
        }).RequireAuthorization("admin");
        api.MapDelete("/media/{id}/subtitles/managed/{subtitleId}", (string id, string subtitleId, SubtitleService subtitles, EventHub events) =>
        {
            subtitles.Remove(id, subtitleId);
            events.Publish("tracks.changed");
            return Results.NoContent();
        }).RequireAuthorization("admin");
        api.MapPost("/playback", async (PlaybackStart input, CurrentAccount actor, MediaStore store, PlaybackSessions playback, CancellationToken token) =>
        {
            if (input.ExpectedUserId is not null && input.ExpectedUserId != actor.User.Id || input.ExpectedSessionId is not null && input.ExpectedSessionId != actor.SessionId)
                throw new DomainException("Oynatma komutu sırasında profil veya giriş değişti.", 409);
            actor.RequireMedia(input.MediaId); return Results.Ok(await playback.StartPlaybackAsync(store.GetPlaybackActor(actor.User.Id, actor.SessionId), input, token));
        });
        api.MapPost("/playback/{id}/heartbeat", (string id, PlaybackHeartbeat heartbeat, CurrentAccount actor, MediaStore store, PlaybackSessions playback) =>
        {
            return Results.Ok(playback.Touch(id, store.GetPlaybackActor(actor.User.Id, actor.SessionId), heartbeat));
        });
        api.MapGet("/playback/{id}", (string id, CurrentAccount actor, MediaStore store, PlaybackSessions playback) =>
        {
            return Results.Ok(playback.Touch(id, store.GetPlaybackActor(actor.User.Id, actor.SessionId)));
        });
        api.MapDelete("/playback/{id}", async (string id, CurrentAccount actor, MediaStore store, PlaybackSessions playback) =>
        {
            await playback.CloseAsync(id, store.GetPlaybackActor(actor.User.Id, actor.SessionId));
            return Results.NoContent();
        });
        api.MapPost("/playback/clients/{clientId}/close", async (string clientId, PlaybackClientClose? input, CurrentAccount actor, MediaStore store, PlaybackSessions playback) =>
        {
            if (input?.Revision is not { } revision) throw new DomainException("Kapatılacak oynatma revizyonu gerekli.");
            await playback.CloseClientAsync(clientId, store.GetPlaybackActor(actor.User.Id, actor.SessionId), revision);
            return Results.NoContent();
        });
        api.MapMethods("/playback/{id}/{file}", ["GET", "HEAD"], (string id, string file, CurrentAccount actor, MediaStore store, PlaybackSessions playback) =>
        {
            return playback.Artifact(id, file, store.GetPlaybackActor(actor.User.Id, actor.SessionId));
        });
    }
}
