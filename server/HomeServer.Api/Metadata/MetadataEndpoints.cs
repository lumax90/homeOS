using HomeServer.Core;
using HomeServer.Infrastructure;
using HomeServer.Identity;
using HomeServer.Scanning;

namespace HomeServer.Metadata;

public static class MetadataEndpoints
{
    public static void MapMetadata(this RouteGroupBuilder api)
    {
        api.MapGet("/metadata/settings", (MetadataSettings settings) => Results.Ok(settings.Status));
        api.MapPut("/metadata/settings", (MetadataPreferences input, MetadataSettings settings) => Results.Ok(settings.Save(input))).RequireAuthorization("admin");
        api.MapPost("/metadata/test", async (MetadataProvider provider, CancellationToken token) =>
        {
            await provider.TestTmdbAsync(token);
            return Results.Ok(new { message = "TMDB bağlantısı doğrulandı." });
        }).RequireAuthorization("admin");
        api.MapGet("/metadata/search", async (string provider, string kind, string q, MetadataProvider metadata, MediaStore store, CurrentAccount actor, CancellationToken token) =>
            Results.Ok(await CatalogEndpoints.FilterMetadataAsync(await metadata.SearchAsync(provider, kind, q, token), actor.User, metadata, store, token))).RequireAuthorization("discover");
        api.MapGet("/media/{id}/match-hint", (string id, CurrentAccount actor) => Results.Ok(FilenameHints.Parse(actor.RequireMedia(id).RelativePath))).RequireAuthorization("admin");
        api.MapPut("/media/{id}/match", async (string id, MatchRequest input, MetadataProvider provider, MediaStore store, EventHub events, CancellationToken token) =>
        {
            var item = store.GetMedia(id) ?? throw new DomainException("İçerik bulunamadı.", 404);
            if (item.Kind != "video") throw new DomainException("Bir video dosyası seç.");
            var resolved = await provider.ResolveAsync(input, token);
            var result = store.MatchMedia(id, resolved.Content, resolved.Episode, input.Edition ?? "", item.MatchRevision);
            events.Publish("catalog.changed");
            return Results.Ok(result);
        }).RequireAuthorization("admin");
        api.MapDelete("/media/{id}/match", (string id, MediaStore store, EventHub events) =>
        {
            var result = store.UnmatchMedia(id);
            events.Publish("catalog.changed");
            return Results.Ok(result);
        }).RequireAuthorization("admin");
        api.MapGet("/media/{id}/versions", (string id, MediaStore store, CurrentAccount actor) => Results.Ok(store.GetVersions(id, actor.User)));
        api.MapGet("/media/{id}/artwork/{type}", async (string id, string type, HttpContext context, CurrentAccount actor, MediaStore store, ArtworkCache cache, CancellationToken token) =>
        {
            var content = actor.RequireMedia(id).Content;
            if (LibraryEndpoints.LocalArtwork(id, type, store) is { } local) return local;
            var url = type switch { "poster" => content?.PosterUrl, "backdrop" => content?.BackdropUrl ?? content?.StillUrl, _ => null };
            if (url is null) return Results.NotFound();
            var image = await cache.GetAsync(url, token);
            context.Response.Headers.CacheControl = "private, max-age=86400";
            return Results.File(image.Path, image.Mime);
        });
    }
}
