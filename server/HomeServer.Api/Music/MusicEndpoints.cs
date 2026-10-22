using HomeServer.Core;
using HomeServer.Identity;
using HomeServer.Infrastructure;
using HomeServer.Playback;
using HomeServer.Sources;
using HomeServer.Downloads;

namespace HomeServer.Music;

public static class MusicEndpoints
{
    public static void MapMusic(this RouteGroupBuilder api)
    {
        api.MapMusicRadio();
        api.MapGet("/music/experience/progress",(CurrentAccount actor,MusicEnrichmentWorker worker)=>Results.Ok(worker.Status(actor.User.Id)));
        api.MapGet("/music/experience",(CurrentAccount actor,MusicExperiencePreferences preferences)=>Results.Ok(preferences.Read(actor.User.Id)));
        api.MapPut("/music/experience",(MusicExperienceOptions input,CurrentAccount actor,HttpContext context,MusicExperiencePreferences preferences)=>{MusicAccess.RequireCommand(actor,context);return Results.Ok(preferences.Save(actor.User,actor.SessionId??"",input));}).RequireRateLimiting("watch-command");
        api.MapPost("/music/experience/refresh",(MusicExperienceRefresh input,CurrentAccount actor,HttpContext context,MusicExperiencePreferences preferences)=>{MusicAccess.RequireCommand(actor,context);return Results.Ok(preferences.Refresh(actor.User,actor.SessionId??"",input.Revision));}).RequireRateLimiting("watch-command");
        api.MapGet("/music/albums/{id}/online-info",async(string id,CurrentAccount actor,MediaStore store,MusicOnlineMetadata metadata,CancellationToken token)=>Results.Ok(await metadata.ResolveAsync(store.GetMusicAlbum(id,actor.User)??throw new DomainException("Albüm bulunamadı.",404),actor.User,token)));
        api.MapPost("/music/releases/search", async (MusicSearchInput input, CurrentAccount actor, IndexerService sources, MusicBrainzProvider provider, CancellationToken token) =>
        { actor.RequireUnratedCatalog(); return Results.Ok(await sources.SearchMusicAsync(input, provider, token)); }).RequireAuthorization("acquire");
        api.MapPost("/music/source-results/{id}/download", async (string id, CurrentAccount actor, HttpContext context, MediaStore store, IndexerService sources, CancellationToken token) =>
        { actor.RequireUnratedCatalog(); return Results.Ok(await sources.GrabMusicForAccountAsync(id, DownloadAccess.Actor(actor, context, store), token)); }).RequireAuthorization("acquire");
        api.MapPost("/music/downloads/{id}/link", (string id, CurrentAccount actor, HttpContext context, MediaStore store) => { DownloadAccess.Require(id, actor, context, store); store.RetryMusicLink(id); return Results.Accepted(); }).RequireAuthorization("acquire");
        api.MapPut("/music/downloads/{id}/import-profile", (string id, MusicQualityRules input, CurrentAccount actor, HttpContext context, MediaStore store, DownloadWorker worker) =>
        { DownloadAccess.Require(id, actor, context, store); store.ReviewMusicImport(id, input); worker.Interrupt(id); return Results.Accepted(); }).RequireAuthorization("acquire");
        api.MapGet("/music/discovery/albums", async (string q, string? artist, int? offset, CurrentAccount actor, MusicBrainzProvider provider, CancellationToken token) =>
        { actor.RequireUnratedCatalog(); return Results.Ok(await provider.SearchAlbumsAsync(q, artist, offset ?? 0, token)); });
        api.MapGet("/music/discovery/albums/{id}/releases", async (string id, int? offset, CurrentAccount actor, MusicBrainzProvider provider, CancellationToken token) =>
        { actor.RequireUnratedCatalog(); return Results.Ok(await provider.ReleasesAsync(id, offset ?? 0, token)); });
        api.MapGet("/music/releases/{id}", async (string id, CurrentAccount actor, MusicBrainzProvider provider, CancellationToken token) =>
        { actor.RequireUnratedCatalog(); return Results.Ok(await provider.ReleaseAsync(id, token)); });
        api.MapGet("/music/artwork/{type}/{id}", async (string type, string id, CurrentAccount actor, CoverArtCache cache,MusicExperiencePreferences preferences, CancellationToken token) =>
        {
            actor.RequireUnratedCatalog(); var image = await cache.GetAsync(type, id, token,preferences.Read(actor.User.Id).OnlineCovers);
            return image is null ? Results.NotFound() : Results.File(image.Value.Path, image.Value.Mime);
        });
        api.MapGet("/music/albums/{id}", (string id, CurrentAccount actor, MediaStore store) => store.GetMusicAlbum(id, actor.User) is { } album ? Results.Ok(album) : Results.NotFound());
        api.MapGet("/music/albums/{id}/artwork", async (string id, CurrentAccount actor,HttpContext context, MediaStore store, CoverArtCache cache,MusicExperiencePreferences preferences,MusicOnlineMetadata metadata, CancellationToken token) =>
        {
            var album = store.GetMusicAlbum(id, actor.User) ?? throw new DomainException("Albüm bulunamadı.", 404);
            if(album.ArtworkMediaId is{} embedded)return Results.Redirect(context.Request.PathBase+"/api/media/"+embedded+"/thumbnail");
            var options=preferences.Read(actor.User.Id);var key = album.ReleaseMbid ?? album.ReleaseGroupMbid ?? metadata.Cached(album,actor.User.Id)?.GroupId;
            if(key is null&&options.OnlineCovers&&MusicAccess.Online(actor.User))key=(await metadata.ResolveAsync(album,actor.User,token))?.GroupId;
            if(key is null)return Results.NotFound();var image = await cache.GetAsync(album.ReleaseMbid is not null ? "release" : "release-group", key, token,preferences.Read(actor.User.Id).OnlineCovers&&MusicAccess.Online(actor.User));
            return image is null ? Results.NotFound() : Results.File(image.Value.Path, image.Value.Mime);
        });
        api.MapGet("/music/albums/{id}/release", (string id, CurrentAccount actor, MediaStore store) =>
        { _ = store.GetMusicAlbum(id, actor.User) ?? throw new DomainException("Albüm bulunamadı.", 404); return Results.Ok(new { release = store.MatchedMusicRelease(id) }); });
        api.MapPost("/music/albums/{id}/match-preview", async (string id, MusicAlbumMatchInput input, CurrentAccount actor, MediaStore store, MusicBrainzProvider provider, CancellationToken token) =>
            Results.Ok(store.PreviewMusicMatch(id, await provider.ReleaseAsync(input.ReleaseId, token), actor.User, input.Assignments))).RequireAuthorization("admin");
        api.MapPut("/music/albums/{id}/match", async (string id, MusicAlbumMatchInput input, CurrentAccount actor, MediaStore store, MusicBrainzProvider provider, EventHub events, CancellationToken token) =>
        { var result = store.MatchMusicAlbum(id, await provider.ReleaseAsync(input.ReleaseId, token), actor.User, input.Assignments); events.Publish("music.changed"); return Results.Ok(result); }).RequireAuthorization("admin");
        api.MapDelete("/music/albums/{id}/match", (string id, MediaStore store, EventHub events) => { store.UnmatchMusicAlbum(id); events.Publish("music.changed"); return Results.NoContent(); }).RequireAuthorization("admin");
        api.MapGet("/music/album-follows", (CurrentAccount actor, MediaStore store) => Results.Ok(store.GetFollowedMusicAlbums(actor.User)));
        api.MapPost("/music/discovery/albums/{id}/follow", async (string id, CurrentAccount actor, MediaStore store, MusicBrainzProvider provider, CancellationToken token) =>
        { actor.RequireUnratedCatalog(); var album = await provider.AlbumAsync(id, token); store.FollowMusicAlbum(album, actor.User); return Results.NoContent(); });
        api.MapDelete("/music/discovery/albums/{id}/follow", (string id, CurrentAccount actor, MediaStore store) => { store.UnfollowMusicAlbum(id, actor.User.Id); return Results.NoContent(); });
        api.MapGet("/music/albums", (int? offset, string? q, CurrentAccount actor, MediaStore store) => Results.Ok(store.GetMusicAlbums(actor.User, offset ?? 0, q)));
        api.MapGet("/music/albums/{id}/tracks", (string id, int? offset, CurrentAccount actor, MediaStore store) => Results.Ok(store.GetMusicTracks(id, actor.User, offset ?? 0)));
        api.MapGet("/music/media/{id}/lyrics", async (string id, CurrentAccount actor, LyricsService lyrics, CancellationToken token) =>
            Results.Ok(await lyrics.ReadAsync(actor.RequireMedia(id), token)));
        api.MapGet("/music/discovery/artists", async (string q, int? offset, bool? autocomplete, MusicBrainzProvider provider, CurrentAccount actor, CancellationToken token) => { actor.RequireUnratedCatalog(); return Results.Ok(await provider.SearchArtistsAsync(q, offset ?? 0, token,autocomplete??false)); }).RequireAuthorization("discover");
        api.MapGet("/music/discovery/artists/{id}/albums", async (string id, int? offset, MusicBrainzProvider provider, CurrentAccount actor, CancellationToken token) => { actor.RequireUnratedCatalog(); return Results.Ok(await provider.AlbumsAsync(id, offset ?? 0, token)); }).RequireAuthorization("discover");
        api.MapGet("/music/discovery/albums/{id}", async (string id, MusicBrainzProvider provider, CurrentAccount actor, CancellationToken token) => { actor.RequireUnratedCatalog(); return Results.Ok(await provider.AlbumAsync(id, token)); }).RequireAuthorization("discover");
        api.MapGet("/music/follows", (CurrentAccount actor, MediaStore store) => Results.Ok(!actor.User.IsAdmin && actor.User.MaximumAge is not null && !actor.User.AllowUnrated ? [] : store.FollowedMusicArtists(actor.User.Id)));
        api.MapPost("/music/artists/{id}/follow", async (string id, CurrentAccount actor, MediaStore store, MusicBrainzProvider provider, CancellationToken token) =>
        {
            actor.RequireUnratedCatalog(); var artist = await provider.ArtistAsync(id, token); store.FollowMusicArtist(artist.Id, artist.Name, actor.User.Id); return Results.Ok(artist);
        }).RequireAuthorization("discover");
        api.MapDelete("/music/artists/{id}/follow", (string id, CurrentAccount actor, MediaStore store) => { store.UnfollowMusicArtist(actor.User.Id, id); return Results.NoContent(); });
        api.MapGet("/playlists", (CurrentAccount actor, MediaStore store) => Results.Ok(store.GetPlaylists(actor.User)));
        api.MapGet("/playlists/{id}/items", (string id, CurrentAccount actor, MediaStore store) => Results.Ok(store.GetPlaylistItems(id, actor.User)));
        api.MapPost("/playlists", (PlaylistInput input, CurrentAccount actor, MediaStore store) => Results.Ok(store.SavePlaylist(null, input, actor.User)));
        api.MapPut("/playlists/{id}", (string id, PlaylistInput input, CurrentAccount actor, MediaStore store) => Results.Ok(store.SavePlaylist(id, input, actor.User)));
        api.MapDelete("/playlists/{id}", (string id, CurrentAccount actor, MediaStore store) => { store.DeletePlaylist(id, actor.User.Id); return Results.NoContent(); });
        api.MapGet("/queue", (CurrentAccount actor, MediaStore store) => Results.Ok(store.GetQueue(actor.User)));
        api.MapPut("/queue", (QueueInput input, CurrentAccount actor, MediaStore store) => { store.SaveQueue(input, actor.User); return Results.NoContent(); });
    }
}
public sealed record MusicExperienceRefresh(int Revision);
