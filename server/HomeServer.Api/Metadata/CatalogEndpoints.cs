using HomeServer.Core;
using HomeServer.Infrastructure;
using HomeServer.Identity;

namespace HomeServer.Metadata;

public static class CatalogEndpoints
{
    public static void MapCatalog(this RouteGroupBuilder api)
    {
        api.MapGet("/discovery/genres", async (string provider, string kind, string? language, MetadataProvider metadata, CancellationToken token) =>
            Results.Ok(await metadata.GenresAsync(provider, kind, token, language))).RequireAuthorization("discover");
        api.MapGet("/discovery/personal", (string kind, string shelf, int? offset, int? limit, CurrentAccount actor, MediaStore store) =>
            Results.Ok(store.GetDiscoveryPersonal(kind, shelf, offset ?? 0, limit ?? 30, actor.User))).RequireAuthorization("discover");
        api.MapGet("/discovery", async (string provider, string kind, string? collection, string? q, int? page, int? genre, string? genreLanguage, MetadataProvider metadata, CurrentAccount actor, MediaStore store, CancellationToken token) =>
        {
            var data = await metadata.DiscoverAsync(provider, kind, collection ?? "trending", q, page ?? 1, token, genre, genreLanguage);
            return Results.Ok(data with { Items = await FilterMetadataAsync(data.Items, actor.User, metadata, store, token) });
        }).RequireAuthorization("discover");
        api.MapGet("/discovery/{provider}/{kind}/{id:int}", async (string provider, string kind, int id, MetadataProvider metadata, MediaStore store, CurrentAccount actor, CancellationToken token) =>
        {
            if (!actor.User.IsAdmin && !actor.User.CanDiscover)
            {
                var existing = store.FindContent(provider, kind, id, actor.User) ?? throw new DomainException("İçerik bulunamadı.", 404);
                actor.RequireContent(existing.Id); return Results.Ok(new { metadata = existing.Metadata, saved = existing });
            }
            var data = await metadata.DetailsAsync(provider, kind, id, token); actor.RequireMetadata(data);
            return Results.Ok(new { metadata = data, saved = store.FindContent(provider, kind, id, actor.User) });
        });
        api.MapGet("/content", (MediaStore store, CurrentAccount actor) => Results.Ok(store.GetTracked(actor.User)));
        api.MapGet("/content/{id}", (string id, CurrentAccount actor) => Results.Ok(actor.RequireContent(id)));
        api.MapGet("/content/{id}/files", (string id, MediaStore store, CurrentAccount actor) =>
        {
            actor.RequireContent(id); return Results.Ok(store.GetContentFiles(id, actor.User));
        });
        api.MapGet("/content/{id}/episodes", (string id, int? season, string? state, int? offset, int? limit, MediaStore store, CurrentAccount actor) =>
        {
            actor.RequireContent(id); return Results.Ok(store.GetEpisodePage(id, null, null, season, state, offset ?? 0, Math.Clamp(limit ?? 100, 1, 200), actor.User));
        });
        api.MapPost("/content/requests", async (ContentRequestInput input, MetadataProvider provider, MediaStore store, CurrentAccount actor, EventHub events, CancellationToken token) =>
        {
            if (!actor.User.CanRequest) throw new DomainException("Bu hesap içerik isteyemez.", 403);
            var metadata = await provider.DetailsAsync(input.Provider, input.Kind, input.ProviderId, token);
            actor.RequireMetadata(metadata);
            await provider.ValidateScopeAsync(metadata, input.Season, input.Episode, token);
            var result = store.RequestContent(metadata, input.MonitorMode, input.QualityProfileId, actor.User, input.Season, input.Episode);
            events.Publish("tracking.changed"); events.Publish("requests.changed"); return Results.Ok(result);
        }).RequireAuthorization("discover");
        api.MapPut("/content/{id}/tracking", (string id, TrackingUpdate input, MediaStore store, EventHub events) =>
        {
            var result = store.UpdateTracking(id, input); events.Publish("tracking.changed"); return Results.Ok(result);
        }).RequireAuthorization("admin");
        api.MapDelete("/content/{id}/request", (string id, MediaStore store, CurrentAccount actor, EventHub events) =>
        {
            store.CancelContentRequest(id, actor.User); events.Publish("tracking.changed"); events.Publish("requests.changed"); return Results.NoContent();
        });
        api.MapGet("/requests", (string? status, MediaStore store, CurrentAccount actor) => Results.Ok(store.GetRequests(actor.User, status)));
        api.MapGet("/requests/{id}/history", (string id, MediaStore store, CurrentAccount actor) => Results.Ok(store.GetRequestEvents(id, actor.User)));
        api.MapPost("/requests/{id}/decision", (string id, RequestDecision input, MediaStore store, CurrentAccount actor, EventHub events) =>
        {
            store.DecideRequest(id, input.Status, input.Note ?? "", actor.User); events.Publish("requests.changed"); events.Publish("tracking.changed");
            return Results.NoContent();
        });
        api.MapGet("/notifications", (MediaStore store, CurrentAccount actor) => Results.Ok(store.GetNotifications(actor.User.Id)));
        api.MapPost("/notifications/read", (MediaStore store, CurrentAccount actor) => { store.ReadNotification(actor.User.Id, null); return Results.NoContent(); });
        api.MapPost("/notifications/{id}/read", (string id, MediaStore store, CurrentAccount actor) => { store.ReadNotification(actor.User.Id, id); return Results.NoContent(); });
        api.MapGet("/calendar", (string? from, string? to, int? offset, MediaStore store, CurrentAccount actor) =>
        {
            var start = ParseDate(from, DateOnly.FromDateTime(DateTime.UtcNow)); var end = ParseDate(to, start.AddDays(30));
            if (end < start || end.DayNumber - start.DayNumber > 93) throw new DomainException("Takvim aralığı en fazla 93 gün olabilir.");
            return Results.Ok(store.GetEpisodePage(null, start.ToString("yyyy-MM-dd"), end.ToString("yyyy-MM-dd"), null, null, offset ?? 0, 100, actor.User));
        });
        api.MapGet("/quality-profiles", (MediaStore store) => Results.Ok(store.GetQualityProfiles()));
        api.MapPost("/quality-profiles", (QualityProfileInput input, MediaStore store) => Results.Ok(store.SaveQualityProfile(null, input))).RequireAuthorization("admin");
        api.MapPut("/quality-profiles/{id}", (string id, QualityProfileInput input, MediaStore store) => Results.Ok(store.SaveQualityProfile(id, input))).RequireAuthorization("admin");
    }
    public static async Task<IReadOnlyList<ProviderContent>> FilterMetadataAsync(IReadOnlyList<ProviderContent> items, Account account, MetadataProvider provider, MediaStore store, CancellationToken token)
    {
        if (account.IsAdmin) return items;
        var result = new List<ProviderContent>();
        foreach (var original in items)
        {
            var item = original;
            if (account.MaximumAge is not null && !account.AllowUnrated && item.MinimumAge is null && item.Provider == "tmdb")
            {
                try { item = await provider.DetailsAsync(item.Provider, item.Kind, item.ProviderId, token); }
                catch (DomainException) { continue; }
            }
            if (store.CanSeeProviderContent(item, account)) result.Add(item);
        }
        return result;
    }
    private static DateOnly ParseDate(string? value, DateOnly fallback) => string.IsNullOrEmpty(value) ? fallback
        : DateOnly.TryParseExact(value, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var result)
            ? result : throw new DomainException("Tarih YYYY-AA-GG biçiminde olmalı.");
}
