using System.Text.Json;
using HomeServer.Core;
using HomeServer.Identity;
using HomeServer.Infrastructure;
using HomeServer.Playback;
using HomeServer.Sources;
using Microsoft.AspNetCore.Http.Features;

namespace HomeServer.Metadata;

public static class CatalogIdentityEndpoints
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static void MapCatalogIdentity(this RouteGroupBuilder api)
    {
        api.MapGet("/content/{id}/identity", (string id, CurrentAccount actor, MediaStore store) =>
        { var content = actor.RequireContent(id); return Results.Ok(new { content, aliases = store.GetCatalogAliases(content.Id), names = store.GetCatalogNames(content.Id), localNames = store.GetCatalogNames(content.Id, true) }); });
        api.MapPost("/content/{id}/identity/refresh", async (string id, CurrentAccount actor, MediaStore store, MetadataProvider provider, EventHub events, CancellationToken token) =>
        {
            var content = actor.RequireContent(id); using var budget = CancellationTokenSource.CreateLinkedTokenSource(token); budget.CancelAfter(TimeSpan.FromMinutes(3));
            var metadata = await provider.DetailsAsync(content.Metadata.Provider, content.Metadata.Kind, content.Metadata.ProviderId, budget.Token);
            var episodes = metadata.Kind == "show" ? await provider.EpisodesAsync(metadata.Provider, metadata.ProviderId, budget.Token) : [];
            var names = await provider.AlternativeNamesAsync(metadata, budget.Token);
            store.RefreshCatalogIdentity(content.Id, content.CatalogRevision, metadata, episodes); store.SaveCatalogNames(content.Id, names, metadata.Provider); events.Publish("catalog.changed");
            return Results.Ok(store.GetContent(content.Id));
        }).RequireAuthorization("admin");
        api.MapPut("/content/{id}/identity/names", (string id, CatalogNamesInput input, CurrentAccount actor, MediaStore store, EventHub events) =>
        { store.SaveCatalogNames(actor.RequireContent(id).Id, input.Names); events.Publish("catalog.changed"); return Results.NoContent(); }).RequireAuthorization("admin");
        api.MapPost("/content/{id}/identity/preview", async (string id, HttpContext context, MediaStore store, MetadataProvider provider, CancellationToken token) =>
        {
            var input = await Read<CatalogMergeInput>(context, token); var metadata = await provider.DetailsAsync(input.Provider, input.Kind, input.ProviderId, token);
            return Results.Ok(store.PreviewCatalogMerge(id, metadata, input.Episodes));
        }).RequireAuthorization("admin");
        api.MapPost("/content/{id}/identity/merge", async (string id, HttpContext context, CurrentAccount actor, MediaStore store, MetadataProvider provider, AutomationCoordinator automation, EventHub events, CancellationToken token) =>
        {
            var input = await Read<CatalogMergeInput>(context, token); var metadata = await provider.DetailsAsync(input.Provider, input.Kind, input.ProviderId, token);
            var previous = store.FindContent(input.Provider, input.Kind, input.ProviderId)?.Id;
            var result = store.MergeCatalog(id, metadata, input, actor.User); automation.CancelContent(id); if (previous is not null) automation.CancelContent(previous); events.Publish("catalog.changed"); events.Publish("tracking.changed");
            return Results.Ok(result);
        }).RequireAuthorization("admin");
        api.MapGet("/content/{id}/numbering", (string id, CurrentAccount actor, MediaStore store) => Results.Ok(store.GetEpisodeNumbering(actor.RequireContent(id).Id)));
        api.MapPut("/content/{id}/numbering", async (string id, HttpContext context, CurrentAccount actor, MediaStore store, AutomationCoordinator automation, EventHub events, CancellationToken token) =>
        { var input = await Read<NumberingInput>(context, token); var content = actor.RequireContent(id); var result = store.SaveEpisodeNumbering(content.Id, input); automation.CancelContent(content.Id); events.Publish("catalog.changed"); return Results.Ok(result); }).RequireAuthorization("admin");
        api.MapGet("/content/{id}/numbering/groups", async (string id, CurrentAccount actor, MetadataProvider provider, CancellationToken token) =>
        {
            var content = actor.RequireContent(id); if (content.Metadata.Provider != "tmdb" || content.Metadata.Kind != "show") throw new DomainException("Otomatik grup aktarımı TMDB kanonik dizi kaydı gerektirir.", 409);
            return Results.Ok(await provider.EpisodeOrdersAsync(content.Metadata.ProviderId, token));
        }).RequireAuthorization("admin");
        api.MapPost("/content/{id}/numbering/import", async (string id, EpisodeOrderImport input, CurrentAccount actor, MediaStore store, MetadataProvider provider, EventHub events, CancellationToken token) =>
        {
            var content = actor.RequireContent(id); if (content.Metadata.Provider != "tmdb" || content.Metadata.Kind != "show") throw new DomainException("TMDB kanonik dizi kaydı gerekli.", 409);
            var group = await provider.AbsoluteOrderAsync(content.Metadata.ProviderId, input.OrderId, token);
            var result = store.SaveEpisodeNumbering(content.Id, new NumberingInput("absolute", "tmdb:" + group.Id, group.Entries, input.ExpectedRevision), providerVerified: true); events.Publish("catalog.changed"); return Results.Ok(result);
        }).RequireAuthorization("admin");
        api.MapPost("/metadata/episode-selection", async (EpisodeSelectionInput input, MetadataProvider provider, MediaStore store, CancellationToken token) =>
        {
            var metadata = await provider.DetailsAsync(input.Provider, "show", input.ProviderId, token); var content = store.FindContent(input.Provider, "show", input.ProviderId);
            var locator = input.Locator;
            if (locator.Mode is "absolute" or "alternative") return Results.Ok(store.LocateEpisodes(content?.Id ?? throw new DomainException("Önce diziyi takip listesine ekleyip bölüm sırasını kaydet.", 409), locator));
            if (locator.Mode == "daily") return Results.Ok(new LocatedEpisodes(content?.Id ?? "", metadata.Provider, await provider.DailyEpisodesAsync(metadata.Provider, metadata.ProviderId, locator.AirDate ?? "", token), metadata.Provider, metadata.ProviderId));
            if (locator.Mode != "seasonal" || locator.Season is null or < 0 or > 1000 || locator.Episode is null or < 1 or > 10000 || locator.EndEpisode < locator.Episode || locator.EndEpisode - locator.Episode >= 20) throw new DomainException("Bölüm aralığı geçersiz.");
            var episodes = new List<ProviderEpisode>();
            var numbers = locator.Numbers ?? Enumerable.Range(locator.Episode.Value, (locator.EndEpisode ?? locator.Episode.Value) - locator.Episode.Value + 1).ToArray();
            if (numbers.Length is < 1 or > 20 || numbers.Any(number => number is < 1 or > 10000) || numbers.Distinct().Count() != numbers.Length || !numbers.SequenceEqual(numbers.Order())) throw new DomainException("Bölüm listesi 1–20 farklı, sıralı sayı içermeli.");
            foreach (var number in numbers) episodes.Add(await provider.EpisodeAsync(metadata.Provider, metadata.ProviderId, locator.Season.Value, number, token));
            return Results.Ok(new LocatedEpisodes(content?.Id ?? "", metadata.Provider, episodes.ToArray(), metadata.Provider, metadata.ProviderId));
        }).RequireAuthorization("admin");
        api.MapPut("/media/{id}/episodes", async (string id, EpisodeMatchInput input, CurrentAccount actor, MetadataProvider provider, MediaAnalysisService analysis, MediaStore store, EventHub events, CancellationToken token) =>
        {
            var media = actor.RequireMedia(id);
            if (input.Episodes is null || input.Episodes.Length is < 1 or > 20) throw new DomainException("1–20 bölüm seç.");
            if (media.DurationSeconds is null && input.Episodes.Any(episode => episode.StartSeconds is not null)) { var inspected = await analysis.InspectAsync(id, token); store.SaveProbe(id, inspected.Probe, media.HasThumbnail); }
            var metadata = await provider.DetailsAsync(input.Provider, "show", input.ProviderId, token); var segments = new List<(ProviderEpisode, double?, double?)>();
            foreach (var episode in input.Episodes) segments.Add((await provider.EpisodeAsync(input.Provider, input.ProviderId, episode.Season, episode.Episode, token), episode.StartSeconds, episode.EndSeconds));
            var result = store.MatchMediaEpisodes(id, metadata, segments, input.Edition ?? "", input.ExpectedRevision); events.Publish("catalog.changed"); return Results.Ok(result);
        }).RequireAuthorization("admin");
        api.MapGet("/content/{id}/movie-editions", (string id, CurrentAccount actor, MediaStore store) => Results.Ok(store.GetMovieEditions(actor.RequireContent(id).Id, actor.User)));
        api.MapPut("/content/{id}/movie-editions", (string id, MoviePartsInput input, CurrentAccount actor, MediaStore store, EventHub events) =>
        { var result = store.SetMovieParts(actor.RequireContent(id).Id, input); events.Publish("catalog.changed"); return Results.Ok(result); }).RequireAuthorization("admin");
        api.MapDelete("/movie-editions/{id}", (string id, int revision, MediaStore store, EventHub events) => { store.RemoveMovieEdition(id, revision); events.Publish("catalog.changed"); return Results.NoContent(); }).RequireAuthorization("admin");
        api.MapGet("/media/{id}/sequence", (string id, string? episode, bool? part, CurrentAccount actor, MediaStore store) => Results.Ok(store.GetVideoSequence(id, actor.User, episode, part ?? false)));
    }
    private static async Task<T> Read<T>(HttpContext context, CancellationToken token)
    {
        const int max = 4 * 1024 * 1024;
        if (context.Request.ContentLength > max) throw new DomainException("Katalog komutu 4 MB sınırını aşıyor.", 413);
        if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } feature) feature.MaxRequestBodySize = max;
        try { return JsonSerializer.Deserialize<T>(await MetadataProvider.ReadLimitedAsync(context.Request.Body, max, token), Json) ?? throw new DomainException("Katalog komutu boş."); }
        catch (JsonException) { throw new DomainException("Katalog komutu okunamadı."); }
    }
}
