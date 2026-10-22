using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using HomeServer.Core;
using Microsoft.Extensions.Caching.Memory;

namespace HomeServer.Metadata;

public sealed partial class MetadataProvider(IHttpClientFactory clients, MetadataSettings settings, IMemoryCache cache)
{
    private readonly SemaphoreSlim _requests = new(1, 1);
    private DateTimeOffset _nextRequest;

    public async Task<IReadOnlyList<ProviderContent>> SearchAsync(string provider, string kind, string query, CancellationToken token)
    {
        Validate(provider, kind);
        if (provider == "tmdb" && string.IsNullOrWhiteSpace(settings.Token)) throw new DomainException("TMDB okuma token'ını ayarlardan ekle.", 409);
        query = query.Trim();
        if (query.Length is < 2 or > 120) throw new DomainException("Arama 2–120 karakter olmalı.");
        var key = $"metadata:{provider}:{kind}:{settings.Language}:{query}";
        if (cache.TryGetValue<IReadOnlyList<ProviderContent>>(key, out var stored)) return stored!;
        var route = provider == "tvmaze" ? $"search/shows?q={Uri.EscapeDataString(query)}"
            : $"search/{(kind == "movie" ? "movie" : "tv")}?query={Uri.EscapeDataString(query)}&language={settings.Language}";
        using var json = await GetAsync(provider, route, token);
        var results = provider == "tvmaze" ? json.RootElement.EnumerateArray().Select(r => r.GetProperty("show"))
            : json.RootElement.GetProperty("results").EnumerateArray().AsEnumerable();
        var content = results.Take(20).Select(r => ParseContent(provider, kind, r)).ToArray();
        cache.Set(key, (IReadOnlyList<ProviderContent>)content, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10), Size = 1 });
        return content;
    }

    public async Task<(ProviderContent Content, ProviderEpisode? Episode)> ResolveAsync(MatchRequest request, CancellationToken token)
    {
        Validate(request.Provider, request.Kind);
        if (request.ProviderId <= 0) throw new DomainException("Geçersiz sağlayıcı kimliği.");
        if (request.Kind == "show" && (request.SeasonNumber is null or < 0 or > 1000 || request.EpisodeNumber is null or < 1 or > 10000))
            throw new DomainException("Dizi için sezon ve bölüm numarasını belirt.");
        var content = await DetailsAsync(request.Provider, request.Kind, request.ProviderId, token);
        if (content.ProviderId != request.ProviderId) throw new DomainException("Sağlayıcı farklı bir içerik döndürdü.", 502);
        ProviderEpisode? episode = null;
        if (request.Kind == "show")
            episode = await EpisodeAsync(request.Provider, request.ProviderId, request.SeasonNumber!.Value, request.EpisodeNumber!.Value, token);
        return (content, episode);
    }

    public async Task<ProviderEpisode> EpisodeAsync(string provider, int id, int seasonNumber, int episodeNumber, CancellationToken token)
    {
        Validate(provider, "show");
        if (id < 1 || seasonNumber is < 0 or > 1000 || episodeNumber is < 1 or > 10000) throw new DomainException("Geçersiz bölüm kimliği.");
        var route = provider == "tvmaze" ? $"shows/{id}/episodebynumber?season={seasonNumber}&number={episodeNumber}"
            : $"tv/{id}/season/{seasonNumber}/episode/{episodeNumber}?language={settings.Language}";
        using var details = await GetAsync(provider, route, token);
        var r = details.RootElement;
        var season = r.GetProperty(provider == "tvmaze" ? "season" : "season_number").GetInt32();
        var number = r.GetProperty(provider == "tvmaze" ? "number" : "episode_number").GetInt32();
        if (season != seasonNumber || number != episodeNumber) throw new DomainException("Sağlayıcının bölüm numarası eşleşmedi.", 502);
        return new ProviderEpisode(r.GetProperty("id").GetInt32(), season, number, Text(r, "name") ?? "",
            PlainText(Text(r, provider == "tvmaze" ? "summary" : "overview") ?? ""),
            Text(r, provider == "tvmaze" ? "airdate" : "air_date"),
            provider == "tvmaze" ? TvmazeImage(r, "original") : TmdbImage(Text(r, "still_path"), "w780"), EpisodeRuntime(r));
    }

    public async Task TestTmdbAsync(CancellationToken token)
    {
        using var response = await GetAsync("tmdb", "authentication", token);
        if (!response.RootElement.TryGetProperty("success", out var success) || !success.GetBoolean())
            throw new DomainException("TMDB bağlantısı doğrulanamadı.", 502);
    }

    private async Task<JsonDocument> GetAsync(string provider, string route, CancellationToken token)
    {
        var secret = provider == "tmdb" ? settings.Token : null;
        if (provider == "tmdb" && string.IsNullOrWhiteSpace(secret)) throw new DomainException("Film bilgileri için ayarlardan TMDB okuma token'ını ekle.", 409);
        await _requests.WaitAsync(token);
        var callerToken = token;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        token = timeout.Token;
        try
        {
            var delay = _nextRequest - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero) await Task.Delay(delay, token);
            _nextRequest = DateTimeOffset.UtcNow.AddMilliseconds(600);
            using var request = new HttpRequestMessage(HttpMethod.Get, route);
            if (secret is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);
            using var response = await clients.CreateClient(provider).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                _nextRequest = DateTimeOffset.UtcNow.AddSeconds(Math.Clamp(response.Headers.RetryAfter?.Delta?.TotalSeconds ?? 10, 1, 60));
                throw new DomainException("Metadata sağlayıcısı kısa bir bekleme istiyor. Birazdan tekrar dene.", 429);
            }
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                throw new DomainException("Metadata bağlantısı reddedildi. Ayarlardaki token'ı kontrol et.", 409);
            if (response.StatusCode == HttpStatusCode.NotFound) throw new DomainException("Bu içerik veya bölüm sağlayıcıda bulunamadı.", 404);
            if (!response.IsSuccessStatusCode) throw new DomainException("Metadata sağlayıcısına şu anda ulaşılamıyor.", 502);
            await using var body = await response.Content.ReadAsStreamAsync(token);
            var bytes = await ReadLimitedAsync(body, 16 * 1024 * 1024, token);
            return JsonDocument.Parse(bytes);
        }
        catch (HttpRequestException) { throw new DomainException("Metadata sağlayıcısıyla bağlantı kurulamadı.", 502); }
        catch (OperationCanceledException) when (!callerToken.IsCancellationRequested) { throw new DomainException("Metadata isteği zaman aşımına uğradı.", 504); }
        catch (JsonException) { throw new DomainException("Metadata sağlayıcısının yanıtı okunamadı.", 502); }
        finally { _requests.Release(); }
    }

    private static ProviderContent ParseContent(string provider, string kind, JsonElement r)
    {
        var id = r.GetProperty("id").GetInt32();
        var title = Text(r, provider == "tvmaze" || kind == "show" ? "name" : "title") ?? "";
        if (string.IsNullOrWhiteSpace(title)) throw new DomainException("Sağlayıcı içerik adını döndürmedi.", 502);
        var genres = r.TryGetProperty("genres", out var values) && values.ValueKind == JsonValueKind.Array
            ? values.EnumerateArray().Select(g => g.ValueKind == JsonValueKind.String ? g.GetString()! : Text(g, "name") ?? "").Where(g => g.Length > 0).ToArray() : [];
        var external = r.TryGetProperty(provider == "tvmaze" ? "externals" : "external_ids", out var ids) ? ids : default;
        var imdb = Text(r, "imdb_id") ?? (external.ValueKind == JsonValueKind.Object ? Text(external, provider == "tvmaze" ? "imdb" : "imdb_id") : null);
        int? tvdb = external.ValueKind == JsonValueKind.Object && external.TryGetProperty(provider == "tvmaze" ? "thetvdb" : "tvdb_id", out var tv) && tv.ValueKind == JsonValueKind.Number && tv.TryGetInt32(out var tvId) ? tvId : null;
        double? rating = provider == "tvmaze" && r.TryGetProperty("rating", out var ratingObject) && ratingObject.ValueKind == JsonValueKind.Object
            && ratingObject.TryGetProperty("average", out var avg) && avg.ValueKind == JsonValueKind.Number && avg.TryGetDouble(out var a) ? a
            : r.TryGetProperty("vote_average", out var vote) && vote.ValueKind == JsonValueKind.Number && vote.TryGetDouble(out var v) ? v : null;
        int? runtime = r.TryGetProperty("runtime", out var runtimeValue) && runtimeValue.ValueKind == JsonValueKind.Number && runtimeValue.TryGetInt32(out var minutes) && minutes > 0 ? minutes : null;
        var classification = provider == "tmdb" ? AgeRatings.Read(r) : ((int?)null, (string?)null);
        return new ProviderContent(provider, id, kind, title,
            Text(r, kind == "movie" ? "original_title" : "original_name") ?? title,
            PlainText(Text(r, provider == "tvmaze" ? "summary" : "overview") ?? ""),
            Text(r, provider == "tvmaze" ? "premiered" : kind == "movie" ? "release_date" : "first_air_date"),
            provider == "tvmaze" ? TvmazeImage(r, "original") : TmdbImage(Text(r, "poster_path"), "w500"),
            provider == "tvmaze" ? null : TmdbImage(Text(r, "backdrop_path"), "w1280"),
            provider == "tvmaze" ? $"https://www.tvmaze.com/shows/{id}" : $"https://www.themoviedb.org/{(kind == "movie" ? "movie" : "tv")}/{id}", genres,imdb,tvdb,rating,runtime,classification.Item1,classification.Item2);
    }

    public static bool AllowedImage(string? input) => Uri.TryCreate(input, UriKind.Absolute, out var uri)
        && uri.Scheme == "https" && uri.IsDefaultPort && uri.UserInfo.Length == 0
        && uri.Host is "image.tmdb.org" or "static.tvmaze.com";
    private static string? TmdbImage(string? path, string size) => path is not null && ImagePath().IsMatch(path) ? $"https://image.tmdb.org/t/p/{size}{path}" : null;
    private static string? TvmazeImage(JsonElement r, string size)
    {
        var url = r.TryGetProperty("image", out var image) && image.ValueKind == JsonValueKind.Object ? Text(image, size) : null;
        return AllowedImage(url) ? url : null;
    }
    private static string? Text(JsonElement r, string name) => r.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static int? EpisodeRuntime(JsonElement value) => value.TryGetProperty("runtime", out var runtime) && runtime.ValueKind == JsonValueKind.Number && runtime.TryGetInt32(out var minutes) && minutes is > 0 and <= 1440 ? minutes : null;
    private static string PlainText(string text) => WebUtility.HtmlDecode(Tags().Replace(text, " ")).Trim();
    private static void Validate(string provider, string kind)
    {
        if (provider is not ("tvmaze" or "tmdb") || kind is not ("movie" or "show")) throw new DomainException("Geçersiz metadata sağlayıcısı veya içerik türü.");
        if (provider == "tvmaze" && kind != "show") throw new DomainException("TVmaze dizi bilgileri sağlar. Filmler için TMDB seç.");
    }
    public static async Task<byte[]> ReadLimitedAsync(Stream source, int max, CancellationToken token)
    {
        using var result = new MemoryStream();
        var buffer = new byte[16 * 1024];
        int count;
        while ((count = await source.ReadAsync(buffer, token)) > 0)
        {
            if (result.Length + count > max) throw new DomainException("Yanıt izin verilen boyutu aşıyor.", 413);
            await result.WriteAsync(buffer.AsMemory(0, count), token);
        }
        return result.ToArray();
    }
    [GeneratedRegex(@"<[^>]*>", RegexOptions.Singleline)] private static partial Regex Tags();
    [GeneratedRegex(@"^/[a-zA-Z0-9_-]+\.(jpg|png|webp)$")] private static partial Regex ImagePath();
}
