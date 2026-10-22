using HomeServer.Core;
using HomeServer.Identity;
using HomeServer.Infrastructure;
using HomeServer.Sources;
using Microsoft.AspNetCore.Http.Features;

namespace HomeServer.Metadata;

public static class EpisodeCatalogEndpoints
{
    public static void MapEpisodeCatalog(this RouteGroupBuilder api)
    {
        api.MapGet("/content/{id}/episode-catalog", (string id, bool? retired, int? offset, int? limit, CurrentAccount actor, MediaStore store) =>
        { var episodes = store.GetEpisodeCatalog(actor.RequireContent(id).Id, retired == true && actor.User.IsAdmin); var start = Math.Clamp(offset ?? 0, 0, 20000); var size = Math.Clamp(limit ?? 200, 1, 200); return Results.Ok(new { items = episodes.Skip(start).Take(size), total = episodes.Count, offset = start, limit = size }); });
        var admin = api.MapGroup("").RequireAuthorization("admin");
        admin.MapPost("/content/{id}/episode-refresh", async (string id, CurrentAccount actor, MediaStore store, MetadataProvider provider, CancellationToken token) =>
        {
            var content = actor.RequireContent(id); using var budget = CancellationTokenSource.CreateLinkedTokenSource(token); budget.CancelAfter(TimeSpan.FromMinutes(3));
            var metadata = await provider.DetailsAsync(content.Metadata.Provider, "show", content.Metadata.ProviderId, budget.Token);
            var episodes = await provider.EpisodesAsync(metadata.Provider, metadata.ProviderId, budget.Token);
            return Results.Ok(store.CreateEpisodeRefreshPlan(content.Id, content.CatalogRevision, metadata, episodes, actor.User));
        });
        admin.MapPost("/episode-refresh/preview", async (HttpContext context, CurrentAccount actor, MediaStore store, CancellationToken token) =>
        { var input = await ReadLarge<EpisodeRepairInput>(context, token); return Results.Ok(store.PreviewEpisodeRefresh(input.PlanId, actor.User, input.Choices)); });
        admin.MapPost("/episode-refresh/apply", async (HttpContext context, CurrentAccount actor, MediaStore store, AutomationCoordinator automation, EventHub events, CancellationToken token) =>
        { var input = await ReadLarge<EpisodeRepairInput>(context, token); var result = store.ApplyEpisodeRefresh(input, actor.User); automation.CancelContent(result.Id); events.Publish("catalog.changed"); events.Publish("tracking.changed"); return Results.Ok(result); });
        admin.MapPut("/content/{id}/episode-map", async (string id, EpisodeAliasInput input, CurrentAccount actor, MediaStore store, MetadataProvider provider, EventHub events, CancellationToken token) =>
        {
            var content = actor.RequireContent(id);
            if (!store.GetCatalogAliases(content.Id).Any(alias => alias.Provider == input.Provider && alias.ProviderId == input.ProviderId)) throw new DomainException("Önce bu sağlayıcı yapım kimliğini kataloğa bağla.", 409);
            var episode = await provider.EpisodeAsync(input.Provider, input.ProviderId, input.Season, input.Episode, token);
            store.SaveProviderEpisodeMap(content.Id, input, episode, actor.User); events.Publish("catalog.changed"); return Results.NoContent();
        });
        api.MapGet("/content/{id}/episode-orders", (string id, CurrentAccount actor, MediaStore store) => Results.Ok(store.GetCatalogEpisodeOrders(actor.RequireContent(id).Id)));
        admin.MapPut("/content/{id}/episode-orders", async (string id, HttpContext context, CurrentAccount actor, MediaStore store, EventHub events, CancellationToken token) =>
        { var input = await ReadLarge<EpisodeOrderInput>(context, token); var order = store.SaveCatalogEpisodeOrder(actor.RequireContent(id).Id, input, actor.User); events.Publish("catalog.changed"); return Results.Ok(order); });
        admin.MapPut("/content/{id}/episode-orders/selection", (string id, EpisodeOrderSelection input, CurrentAccount actor, MediaStore store, AutomationCoordinator automation, EventHub events) =>
        { var content = actor.RequireContent(id); var result = store.SelectCatalogEpisodeOrder(content.Id, input, actor.User); automation.CancelContent(content.Id); events.Publish("catalog.changed"); return Results.Ok(result); });
        admin.MapDelete("/episode-orders/{id}", (string id, int revision, CurrentAccount actor, MediaStore store, EventHub events) =>
        { store.RemoveCatalogEpisodeOrder(id, revision, actor.User); events.Publish("catalog.changed"); return Results.NoContent(); });
        admin.MapGet("/content/{id}/episode-orders/tmdb", async (string id, CurrentAccount actor, MediaStore store, MetadataProvider provider, CancellationToken token) =>
            Results.Ok(await provider.EpisodeOrdersAsync(TmdbId(actor.RequireContent(id).Id, store), token)));
        admin.MapGet("/content/{id}/episode-orders/tmdb/{order}", async (string id, string order, CurrentAccount actor, MediaStore store, MetadataProvider provider, CancellationToken token) =>
            Results.Ok(await provider.EpisodeOrderDetailsAsync(TmdbId(actor.RequireContent(id).Id, store), order, token)));
        admin.MapPost("/content/{id}/episode-orders/import", async (string id, ImportCatalogOrder input, CurrentAccount actor, MediaStore store, MetadataProvider provider, EventHub events, CancellationToken token) =>
        {
            var content = actor.RequireContent(id); var source = await provider.EpisodeOrderDetailsAsync(TmdbId(content.Id, store), input.OrderId, token);
            var order = store.ImportCatalogEpisodeOrder(content.Id, source, input, actor.User); events.Publish("catalog.changed"); return Results.Ok(order);
        });
        admin.MapGet("/media/{id}/episode-boundaries", (string id, CurrentAccount actor, MediaStore store) => { actor.RequireMedia(id); return Results.Ok(store.GetEpisodeBoundaryJobs(id)); });
        admin.MapPost("/media/{id}/episode-boundaries", async (string id, EpisodeBoundaryStart input, CurrentAccount actor, EpisodeBoundaryService service, CancellationToken token) =>
        { actor.RequireMedia(id); return Results.Ok(await service.QueueAsync(id, input.ExpectedMatchRevision, actor.User, token)); });
        admin.MapPost("/episode-boundaries/{id}/cancel", (string id, EpisodeBoundaryCommand input, CurrentAccount actor, MediaStore store, EpisodeBoundaryService service, EventHub events) =>
        { store.CancelEpisodeBoundary(id, input.Revision, actor.User); service.Interrupt(id); events.Publish("catalog.changed"); return Results.NoContent(); });
        admin.MapPost("/episode-boundaries/{id}/apply", (string id, EpisodeBoundaryCommand input, CurrentAccount actor, MediaStore store, EventHub events) =>
        { var result = store.ApplyEpisodeBoundary(id, input.Revision, actor.User); events.Publish("catalog.changed"); return Results.Ok(result); });
    }
    private static int TmdbId(string id, MediaStore store) => store.GetCatalogAliases(id).FirstOrDefault(alias => alias.Provider == "tmdb" && alias.Kind == "show")?.ProviderId ?? throw new DomainException("Sağlayıcı sırası için TMDB yapım kimliği bağlı olmalı.", 409);
    private static async Task<T> ReadLarge<T>(HttpContext context, CancellationToken token)
    {
        if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } feature) feature.MaxRequestBodySize = 4 * 1024 * 1024;
        return await context.Request.ReadFromJsonAsync<T>(token) ?? throw new DomainException("Katalog komutu gerekli.");
    }
}
