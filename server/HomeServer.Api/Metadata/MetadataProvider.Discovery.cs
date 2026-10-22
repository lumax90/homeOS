using System.Text.Json;
using HomeServer.Core;
using Microsoft.Extensions.Caching.Memory;

namespace HomeServer.Metadata;

public sealed partial class MetadataProvider
{
    public async Task ValidateScopeAsync(ProviderContent content, int? season, int? episode, CancellationToken token)
    {
        if (season is null && episode is null) return;
        if (content.Kind != "show" || season is null or < 0 or > 1000 || episode is < 1 or > 10000) throw new DomainException("İstek bölüm kapsamı geçersiz.");
        if (episode is not null) { _ = await EpisodeAsync(content.Provider, content.ProviderId, season.Value, episode.Value, token); return; }
        using var response = await GetAsync(content.Provider, content.Provider == "tvmaze" ? $"shows/{content.ProviderId}/seasons" : $"tv/{content.ProviderId}", token);
        var seasons = content.Provider == "tvmaze" ? response.RootElement : response.RootElement.GetProperty("seasons");
        if (!seasons.EnumerateArray().Any(value => value.TryGetProperty(content.Provider == "tvmaze" ? "number" : "season_number", out var number)
            && number.ValueKind == JsonValueKind.Number && number.GetInt32() == season)) throw new DomainException("Bu sezon sağlayıcıda bulunamadı.", 404);
    }
    public async Task<DiscoveryPage> DiscoverAsync(string provider, string kind, string collection, string? query, int page, CancellationToken token, int? genre = null, string? genreLanguage = null)
    {
        Validate(provider, kind);
        if (page is < 1 or > 500) throw new DomainException("Geçersiz katalog sayfası.");
        if (genre is < 1) throw new DomainException("Geçersiz tür kimliği.");
        if (genre is not null && provider != "tmdb") throw new DomainException("Türlere göre film ve dizi keşfi için TMDB seç.");
        if (collection is not ("trending" or "popular" or "upcoming" or "top")) throw new DomainException("Geçersiz keşif koleksiyonu.");
        query = query?.Trim() ?? "";
        if (query.Length == 1 || query.Length > 120) throw new DomainException("Arama 2–120 karakter olmalı.");
        if (provider == "tmdb" && string.IsNullOrWhiteSpace(settings.Token)) throw new DomainException("Gerçek film kataloğu için TMDB token'ını bağla.", 409);
        if (query.Length > 0 && provider == "tvmaze")
            return new DiscoveryPage(await SearchAsync(provider, kind, query, token), provider, 1, 1, false);
        var language = settings.Language;
        var labelLanguage = DisplayLanguage(genreLanguage);
        var key = $"discovery-v3:{provider}:{kind}:{collection}:{query}:{genre}:{page}:{language}:{labelLanguage}";
        if (cache.TryGetValue<DiscoveryPage>(key, out var cached)) return cached!;
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(token); budget.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            IReadOnlyList<DiscoveryGenre> genres = [];
            if (genre is { } selected)
            {
                genres = await GenresAsync(provider, kind, budget.Token, labelLanguage);
                if (!genres.Any(value => value.Id == selected)) throw new DomainException("Bu tür seçilen film/dizi kataloğunda bulunmuyor.");
            }
            else if (provider == "tmdb" && cache.TryGetValue<DiscoveryGenre[]>($"metadata-genres:{kind}:{labelLanguage}", out var knownGenres)) genres = knownGenres!;
            var labels = genres.ToDictionary(value => value.Id, value => value.Name);
            var items = new List<ProviderContent>(); int? pages = null; var current = page; var more = false;
            // TMDB text-search and weekly-trending endpoints have no genre parameter. Consume a bounded
            // batch of complete provider pages and return its cursor, retaining matches after sparse pages.
            var maximumPages = genre is not null && (query.Length > 0 || collection == "trending") ? 4 : 1;
            for (var read = 0; read < maximumPages; read++, current++)
            {
                var route = provider == "tvmaze" ? $"shows?page={current - 1}" : TmdbDiscoveryRoute(kind, collection, query, current, genre, language);
                JsonDocument json;
                try { json = await GetAsync(provider, route, budget.Token); }
                catch (DomainException ex) when (provider == "tvmaze" && ex.StatusCode == 404 && current > 1) { more = false; break; }
                using (json)
                {
                    var array = provider == "tvmaze" ? json.RootElement : json.RootElement.GetProperty("results");
                    if (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() > 250) throw new DomainException("Sağlayıcının katalog sayfası geçersiz.", 502);
                    foreach (var row in array.EnumerateArray())
                    {
                        if (provider == "tmdb" && row.TryGetProperty("adult", out var adult) && adult.ValueKind == JsonValueKind.True) continue;
                        var ids = provider == "tmdb" ? GenreIds(row) : [];
                        if (genre is { } chosen && !ids.Contains(chosen)) continue;
                        var content = ParseContent(provider, kind, row);
                        if (ids.Length > 0 && labels.Count > 0) content = content with { Genres = ids.Where(labels.ContainsKey).Select(id => labels[id]).Distinct().ToArray() };
                        if (!items.Any(item => item.ProviderId == content.ProviderId)) items.Add(content);
                    }
                    pages = provider == "tvmaze" ? null : Math.Clamp(json.RootElement.GetProperty("total_pages").GetInt32(), 0, 500);
                    more = current < 500 && (pages is null ? array.GetArrayLength() > 0 : current < pages);
                }
                if (!more || items.Count >= 20 || read + 1 == maximumPages) break;
            }
            var result = new DiscoveryPage(items.ToArray(), provider, page, pages, more, more ? current + 1 : null);
            cache.Set(key, result, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15), Size = 1 });
            return result;
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new DomainException("Tür kataloğu isteği zaman aşımına uğradı; yeniden dene.", 504); }
    }

    public async Task<ProviderContent> DetailsAsync(string provider, string kind, int id, CancellationToken token)
    {
        Validate(provider, kind);
        if (id <= 0) throw new DomainException("Geçersiz içerik kimliği.");
        if (provider == "tmdb" && string.IsNullOrWhiteSpace(settings.Token)) throw new DomainException("Film bilgileri için TMDB bağlantısını ekle.", 409);
        var key = $"details:{provider}:{kind}:{id}:{settings.Language}";
        if (cache.TryGetValue<ProviderContent>(key, out var cached)) return cached!;
        var route = provider == "tvmaze" ? $"shows/{id}" : $"{(kind == "movie" ? "movie" : "tv")}/{id}?language={settings.Language}&append_to_response=external_ids,{(kind == "movie" ? "release_dates" : "content_ratings")}";
        using var json = await GetAsync(provider, route, token);
        var content = ParseContent(provider, kind, json.RootElement);
        if (content.ProviderId != id) throw new DomainException("Sağlayıcı kimliği eşleşmedi.", 502);
        cache.Set(key, content, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15), Size = 1 });
        return content;
    }

    public async Task<IReadOnlyList<ProviderEpisode>> EpisodesAsync(string provider, int id, CancellationToken token)
    {
        Validate(provider, "show");
        var result = new List<ProviderEpisode>();
        if (provider == "tvmaze")
        {
            using var json = await GetAsync(provider, $"shows/{id}/episodes?specials=1", token);
            foreach (var r in json.RootElement.EnumerateArray())
            {
                if (!r.TryGetProperty("number", out var number) || number.ValueKind != JsonValueKind.Number || !number.TryGetInt32(out var n) || n < 1) continue;
                result.Add(new ProviderEpisode(r.GetProperty("id").GetInt32(), r.GetProperty("season").GetInt32(), n,
                    Text(r, "name") ?? "", PlainText(Text(r, "summary") ?? ""), Text(r, "airdate"), TvmazeImage(r, "original"), EpisodeRuntime(r)));
            }
        }
        else
        {
            using var show = await GetAsync(provider, $"tv/{id}?language={settings.Language}", token);
            if (!show.RootElement.TryGetProperty("seasons", out var seasons) || seasons.ValueKind != JsonValueKind.Array) throw new DomainException("Sağlayıcının sezon listesi eksik.", 502);
            if (show.RootElement.TryGetProperty("id", out var showId) && showId.GetInt32() != id) throw new DomainException("Bölüm listesi başka bir diziye ait.", 502);
            var seasonRows = seasons.EnumerateArray().ToArray();
            if (seasonRows.Length > 1001 || seasonRows.Select(season => season.GetProperty("season_number").GetInt32()).Distinct().Count() != seasonRows.Length) throw new DomainException("Sağlayıcının sezon listesi tekrarlı veya sınırı aşıyor.", 502);
            foreach (var season in seasonRows)
            {
                token.ThrowIfCancellationRequested();
                var s = season.GetProperty("season_number").GetInt32();
                if (s is < 0 or > 1000) throw new DomainException("Sağlayıcı sezon numarası geçersiz.", 502);
                using var json = await GetAsync(provider, $"tv/{id}/season/{s}?language={settings.Language}", token);
                if (json.RootElement.TryGetProperty("season_number", out var actualSeason) && actualSeason.GetInt32() != s) throw new DomainException("Sağlayıcı farklı sezon döndürdü.", 502);
                var episodeRows = json.RootElement.GetProperty("episodes").EnumerateArray().ToArray();
                if (season.TryGetProperty("episode_count", out var expectedCount) && expectedCount.ValueKind == JsonValueKind.Number && expectedCount.GetInt32() != episodeRows.Length) throw new DomainException("Sağlayıcının sezon bölüm listesi eksik veya arada değişti; yeniden dene.", 502);
                foreach (var r in episodeRows)
                {
                    var n = r.GetProperty("episode_number").GetInt32();
                    if (n < 1) continue;
                    result.Add(new ProviderEpisode(r.GetProperty("id").GetInt32(), s, n, Text(r, "name") ?? "",
                        PlainText(Text(r, "overview") ?? ""), Text(r, "air_date"), TmdbImage(Text(r, "still_path"), "w780"), EpisodeRuntime(r)));
                }
                if (result.Count > 20000 || result.Sum(episode => (long)episode.Title.Length + episode.Overview.Length) > 8 * 1024 * 1024) throw new DomainException("Dizi bölüm listesi desteklenen sınırı aşıyor.");
            }
        }
        if (result.Count > 20000 || result.Select(episode => episode.ProviderId).Distinct().Count() != result.Count || result.Select(episode => (episode.SeasonNumber, episode.EpisodeNumber)).Distinct().Count() != result.Count)
            throw new DomainException("Sağlayıcının bölüm listesi tekrarlı veya sınırı aşıyor.", 502);
        return result;
    }
}
