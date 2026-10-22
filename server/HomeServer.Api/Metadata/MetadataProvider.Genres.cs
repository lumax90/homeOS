using System.Globalization;
using System.Text.Json;
using HomeServer.Core;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Caching.Memory;

namespace HomeServer.Metadata;

public sealed partial class MetadataProvider
{
    private readonly SemaphoreSlim _genreRequests = new(1, 1);
    public async Task<IReadOnlyList<DiscoveryGenre>> GenresAsync(string provider, string kind, CancellationToken token, string? language = null)
    {
        Validate(provider, kind);
        if (provider != "tmdb") return [];
        if (string.IsNullOrWhiteSpace(settings.Token)) throw new DomainException("Türlere göre keşif için TMDB bağlantısını ekle.", 409);
        language = DisplayLanguage(language); var key = $"metadata-genres:{kind}:{language}";
        if (cache.TryGetValue<DiscoveryGenre[]>(key, out var stored)) return stored!;
        await _genreRequests.WaitAsync(token);
        try
        {
            if (cache.TryGetValue<DiscoveryGenre[]>(key, out stored)) return stored!;
            var type = kind == "movie" ? "movie" : "tv";
            using var json = await GetAsync(provider, $"genre/{type}/list?language={Uri.EscapeDataString(language)}", token);
            if (!json.RootElement.TryGetProperty("genres", out var values) || values.ValueKind != JsonValueKind.Array || values.GetArrayLength() is < 1 or > 100)
                throw new DomainException("Sağlayıcının tür listesi okunamadı.", 502);
            var genres = new List<DiscoveryGenre>();
            foreach (var row in values.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Object || !row.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.Number || !id.TryGetInt32(out var number) || number < 1
                    || Text(row, "name") is not { } name || string.IsNullOrWhiteSpace(name) || name.Length > 100 || name.Any(char.IsControl))
                    throw new DomainException("Sağlayıcının tür kimliği veya adı geçersiz.", 502);
                genres.Add(new(number, name.Trim()));
            }
            if (genres.Select(genre => genre.Id).Distinct().Count() != genres.Count) throw new DomainException("Sağlayıcının tür listesi tekrarlı.", 502);
            var result = genres.ToArray();
            cache.Set(key, result, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24), Size = 1 });
            return result;
        }
        finally { _genreRequests.Release(); }
    }
    private string DisplayLanguage(string? language) => language is null ? settings.Language : language is "en-US" or "tr-TR" ? language : throw new DomainException("Desteklenen bir arayüz dili seç.");
    private static int[] GenreIds(JsonElement row) => row.TryGetProperty("genre_ids", out var values) && values.ValueKind == JsonValueKind.Array
        ? values.EnumerateArray().Where(value => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out _)).Select(value => value.GetInt32()).ToArray() : [];
    private static string TmdbDiscoveryRoute(string kind, string collection, string query, int page, int? genre, string language)
    {
        var type = kind == "movie" ? "movie" : "tv";
        var parameters = new Dictionary<string, string?> { ["page"] = page.ToString(CultureInfo.InvariantCulture), ["language"] = language, ["include_adult"] = "false" };
        string route;
        if (query.Length > 0) { route = $"search/{type}"; parameters["query"] = query; }
        else if (genre is { } selected && collection != "trending")
        {
            route = $"discover/{type}"; parameters["with_genres"] = selected.ToString(CultureInfo.InvariantCulture);
            parameters["sort_by"] = collection == "top" ? "vote_average.desc" : "popularity.desc";
            if (kind == "movie") parameters["include_video"] = "false";
            if (collection == "top") parameters["vote_count.gte"] = "200";
            if (collection == "upcoming")
            {
                var today = DateOnly.FromDateTime(DateTime.UtcNow);
                parameters[kind == "movie" ? "primary_release_date.gte" : "air_date.gte"] = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                parameters[kind == "movie" ? "primary_release_date.lte" : "air_date.lte"] = today.AddDays(kind == "movie" ? 90 : 7).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                if (kind == "movie") parameters["sort_by"] = "primary_release_date.asc";
            }
        }
        else route = collection switch
        {
            "trending" => $"trending/{type}/week", "popular" => $"{type}/popular", "top" => $"{type}/top_rated",
            "upcoming" when kind == "movie" => "movie/upcoming", "upcoming" => "tv/on_the_air", _ => throw new DomainException("Geçersiz keşif koleksiyonu.")
        };
        return QueryHelpers.AddQueryString(route, parameters);
    }
}
