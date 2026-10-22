using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HomeServer.Core;
using HomeServer.Infrastructure;
using HomeServer.Metadata;
using Microsoft.AspNetCore.WebUtilities;

namespace HomeServer.Subtitles;

public sealed class OpenSubtitlesProvider(IHttpClientFactory clients, SubtitleSettings settings, MediaStore store)
{
    private readonly SemaphoreSlim _requests = new(1, 1);
    private DateTimeOffset _next;
    public async Task<SubtitleConnection> LoginAsync(SubtitleLogin input, CancellationToken token)
    {
        var connection = settings.Snapshot();
        if (string.IsNullOrWhiteSpace(input.Username) || input.Username.Length > 100 || string.IsNullOrEmpty(input.Password) || input.Password.Length > 256)
            throw new DomainException("Sağlayıcı kullanıcı adı ve parolası gerekli.");
        using var data = await ApiAsync(HttpMethod.Post, "login", input, token, login: true);
        var root = data.RootElement;
        var bearer = Text(root, "token") ?? throw new DomainException("Sağlayıcı oturum token'ı döndürmedi.", 502);
        var host = Text(root, "base_url") ?? "api.opensubtitles.com";
        if (host is not ("api.opensubtitles.com" or "vip-api.opensubtitles.com")) throw new DomainException("Sağlayıcı API adresi onaylı değil.", 502);
        var expiry = DateTimeOffset.UtcNow.AddHours(20);
        try
        {
            using var payload = JsonDocument.Parse(WebEncoders.Base64UrlDecode(bearer.Split('.')[1]));
            if (payload.RootElement.TryGetProperty("exp", out var expires) && expires.TryGetInt64(out var seconds)) expiry = DateTimeOffset.FromUnixTimeSeconds(seconds);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or IndexOutOfRangeException or ArgumentOutOfRangeException) { }
        settings.Store(connection.Secrets with { Token = bearer, Host = host, Username = input.Username, ExpiresAt = expiry.ToString("O") }, connection.Revision);
        if (root.TryGetProperty("user", out var user)) settings.Quota(Integer(user, "allowed_downloads"), null, connection.Revision + 1);
        store.SetSetting("subtitles.error", ""); return settings.Status;
    }
    public async Task<SubtitleSearchPage> SearchAsync(AnalyzedMedia media, ProviderContent? content, SubtitleSearchInput input, CancellationToken token)
    {
        if (input.Language.Length is < 2 or > 3 || input.Language.Any(c => c is < 'a' or > 'z') || input.Page is < 1 or > 20
            || input.HearingImpaired is not ("include" or "exclude" or "only") || input.Forced is not ("include" or "exclude" or "only")) throw new DomainException("Altyazı arama filtresi geçersiz.");
        await using var file = File.OpenRead(media.Path);
        var hash = await MovieHash.ComputeAsync(file, token);
        var query = new SortedDictionary<string, string?>(StringComparer.Ordinal) { ["languages"] = input.Language };
        if (hash is not null) query["moviehash"] = hash;
        if (input.Page > 1) query["page"] = input.Page.ToString();
        if (input.HearingImpaired != "include") query["hearing_impaired"] = input.HearingImpaired;
        if (input.Forced != "include") query["foreign_parts_only"] = input.Forced;
        var series = media.Media.Content?.Kind == "show";
        if (content?.ImdbId is { } imdb) query[series ? "parent_imdb_id" : "imdb_id"] = NormalizeImdb(imdb);
        else if (content?.Provider == "tmdb") query[series ? "parent_tmdb_id" : "tmdb_id"] = content.ProviderId.ToString();
        else query["query"] = Path.GetFileNameWithoutExtension(media.Path).ToLowerInvariant();
        if (series)
        {
            query["season_number"] = media.Media.Content!.SeasonNumber?.ToString();
            query["episode_number"] = media.Media.Content.EpisodeNumber?.ToString();
        }
        var route = QueryHelpers.AddQueryString("subtitles", query);
        using var response = await ApiAsync(HttpMethod.Get, route, null, token);
        var result = new List<SubtitleCandidate>();
        if (!response.RootElement.TryGetProperty("data", out var values) || values.ValueKind != JsonValueKind.Array) throw new DomainException("Altyazı yanıtı okunamadı.", 502);
        foreach (var item in values.EnumerateArray().Take(100))
        {
            if (!item.TryGetProperty("attributes", out var attributes) || !attributes.TryGetProperty("files", out var files)) continue;
            var language = Text(attributes, "language") ?? "und";
            var forced = Boolean(attributes, "foreign_parts_only"); var hi = Boolean(attributes, "hearing_impaired");
            var hashMatches = hash is not null && (Boolean(attributes, "moviehash_match") || Boolean(item, "moviehash_match"));
            var trusted = Boolean(attributes, "from_trusted"); var release = Text(attributes, "release") ?? "";
            var reasons = new List<string>(); var identity = false;
            if (attributes.TryGetProperty("feature_details", out var feature) && content is not null)
            {
                var imdbValue = Integer(feature, series ? "parent_imdb_id" : "imdb_id");
                var tmdbValue = Integer(feature, series ? "parent_tmdb_id" : "tmdb_id");
                if (content.ImdbId is not null && imdbValue is not null)
                {
                    identity = NormalizeImdb(content.ImdbId) == imdbValue.Value.ToString();
                    if (!identity) reasons.Add("IMDb kimliği farklı.");
                }
                if (content.Provider == "tmdb" && tmdbValue is not null)
                {
                    if (tmdbValue != content.ProviderId) reasons.Add("TMDB kimliği farklı."); else identity = true;
                }
                if (series && !(media.Media.Episodes is { Length: > 1 } && hashMatches
                    ? media.Media.Episodes.Any(episode => episode.Season == Integer(feature, "season_number") && episode.Number == Integer(feature, "episode_number"))
                    : Integer(feature, "season_number") == media.Media.Content?.SeasonNumber && Integer(feature, "episode_number") == media.Media.Content?.EpisodeNumber)) reasons.Add("Sezon/bölüm eşleşmiyor.");
            }
            if (!identity && !hashMatches) reasons.Add("Dosya hash'i veya içerik kimliği doğrulanamadı.");
            if ((media.Media.Episodes?.Length > 1 || media.Media.MoviePart is not null) && !hashMatches) reasons.Add("Çok bölümlü/parçalı dosya için dosya hash'i eşleşmeli.");
            if (language != input.Language) reasons.Add("Dil tercihi farklı.");
            if (input.Forced == "only" && !forced || input.Forced == "exclude" && forced) reasons.Add("Forced tercihi farklı.");
            if (input.HearingImpaired == "only" && !hi || input.HearingImpaired == "exclude" && hi) reasons.Add("İşitme engelli altyazısı tercihi farklı.");
            if (Boolean(attributes, "machine_translated")) reasons.Add("Makine çevirisi bildiriliyor.");
            var score = (hashMatches ? 100 : identity ? 60 : 0) + (trusted ? 5 : 0);
            var words = MediaPaths.SearchKey(Path.GetFileNameWithoutExtension(media.Path)).Split('.', ' ', '-', '_').Where(word => word.Length > 2).Distinct().ToArray();
            score += Math.Min(20, words.Count(word => MediaPaths.SearchKey(release).Contains(word, StringComparison.Ordinal)) * 3);
            var url = Text(attributes, "url");
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "www.opensubtitles.com") url = null;
            foreach (var entry in files.EnumerateArray())
            {
                var fileId = Integer(entry, "file_id"); if (fileId is null or <= 0) continue;
                var name = Text(entry, "file_name") ?? release;
                result.Add(new SubtitleCandidate("", media.Media.Id, fileId.Value, language, name[..Math.Min(name.Length, 200)], release[..Math.Min(release.Length, 500)],
                    forced, hi, hashMatches, trusted, reasons.Count == 0, score, reasons.ToArray(), url));
            }
        }
        return new SubtitleSearchPage(result.OrderByDescending(item => item.Eligible).ThenByDescending(item => item.Score).ToArray(), input.Page, Integer(response.RootElement, "total_pages") ?? 1);
    }
    public async Task<byte[]> DownloadAsync(long fileId, CancellationToken token) => await DownloadTicketAsync(await RequestTicketAsync(fileId, token), token);
    public async Task<string> RequestTicketAsync(long fileId, CancellationToken token)
    {
        var connectionRevision = settings.ConnectionRevision;
        if (!settings.Status.Authenticated) throw new DomainException("Altyazı indirmek için sağlayıcı hesabına giriş yap.", 409);
        if (settings.Status.Remaining is 0 && DateTimeOffset.TryParse(settings.Status.ResetAt, out var reset) && reset > DateTimeOffset.UtcNow) throw new DomainException("Altyazı sağlayıcısının günlük kotası doldu.", 429);
        using var response = await ApiAsync(HttpMethod.Post, "download", new { file_id = fileId, sub_format = "srt" }, token);
        if (settings.ConnectionRevision != connectionRevision) throw new DomainException("Altyazı bağlantısı indirme izni alınırken değişti.", 409);
        settings.Quota(Integer(response.RootElement, "remaining"), Text(response.RootElement, "reset_time_utc"), connectionRevision);
        var url = Text(response.RootElement, "link") ?? throw new DomainException("Sağlayıcı indirme bağlantısı vermedi.", 502);
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !AllowedDownload(uri)) throw new DomainException("Altyazı bağlantısı geçersiz veya onaylı sunucuda değil.", 502);
        return url;
    }
    public async Task<byte[]> DownloadTicketAsync(string url, CancellationToken token)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !AllowedDownload(uri)) throw new DomainException("Altyazı indirme bileti geçersiz.", 502);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(25));
        for (var count = 0; count < 4; count++)
        {
            if (!AllowedDownload(uri))
                throw new DomainException("Altyazı bağlantısı onaylı indirme sunucusunda değil.", 502);
            using var file = await clients.CreateClient("subtitle-file").GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if ((int)file.StatusCode is >= 300 and < 400 && file.Headers.Location is { } location) { uri = location.IsAbsoluteUri ? location : new Uri(uri, location); continue; }
            if (!file.IsSuccessStatusCode) throw new DomainException("Altyazı dosyası alınamadı.", 502);
            await using var stream = await file.Content.ReadAsStreamAsync(timeout.Token);
            return await MetadataProvider.ReadLimitedAsync(stream, 2 * 1024 * 1024, timeout.Token);
        }
        throw new DomainException("Altyazı sunucusu çok fazla yönlendirme yaptı.", 502);
    }
    private async Task<JsonDocument> ApiAsync(HttpMethod method, string route, object? body, CancellationToken token, bool login = false)
    {
        var snapshot = settings.Snapshot(); var secrets = snapshot.Secrets;
        if (string.IsNullOrWhiteSpace(secrets.ApiKey)) throw new DomainException("OpenSubtitles API anahtarını önce bağla.", 409);
        await _requests.WaitAsync(token);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(25));
        try
        {
            var wait = _next - DateTimeOffset.UtcNow; if (wait > TimeSpan.Zero) await Task.Delay(wait, timeout.Token);
            _next = DateTimeOffset.UtcNow.AddSeconds(1);
            var host = login ? "api.opensubtitles.com" : secrets.Host;
            var uri = new Uri($"https://{host}/api/v1/{route}");
            for (var count = 0; count < 4; count++)
            {
                if (uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length > 0 || uri.Host is not ("api.opensubtitles.com" or "vip-api.opensubtitles.com")) throw new DomainException("Sağlayıcı API yönlendirmesi geçersiz.", 502);
                using var request = new HttpRequestMessage(method, uri);
                request.Headers.Add("Api-Key", secrets.ApiKey); request.Headers.TryAddWithoutValidation("User-Agent", "HomeMediaServer v" + BuildInfo.Version);
                if (!login && secrets.Token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secrets.Token);
                if (body is not null) request.Content = JsonContent.Create(body);
                using var response = await clients.CreateClient("subtitle-api").SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                if ((int)response.StatusCode is >= 300 and < 400 && method == HttpMethod.Get && response.Headers.Location is { } location) { uri = location.IsAbsoluteUri ? location : new Uri(uri, location); continue; }
                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    if (!login && settings.ConnectionRevision == snapshot.Revision) settings.Store(secrets with { Token = null, ExpiresAt = null, Host = "api.opensubtitles.com" }, snapshot.Revision);
                    throw new DomainException("Altyazı sağlayıcısı kimlik bilgilerini reddetti. Anahtar/hesabı kontrol et.", 409);
                }
                if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.NotAcceptable)
                {
                    _next = DateTimeOffset.UtcNow.AddSeconds(Math.Clamp(response.Headers.RetryAfter?.Delta?.TotalSeconds ?? 60, 1, 3600));
                    throw new DomainException("Altyazı sağlayıcısı kota/bekleme sınırı bildirdi.", 429);
                }
                if (!response.IsSuccessStatusCode) throw new DomainException("Altyazı sağlayıcısına şu anda ulaşılamıyor.", 502);
                await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
                return JsonDocument.Parse(await MetadataProvider.ReadLimitedAsync(stream, 4 * 1024 * 1024, timeout.Token));
            }
            throw new DomainException("Altyazı API yönlendirme sınırı aşıldı.", 502);
        }
        catch (HttpRequestException) { throw new DomainException("Altyazı sağlayıcısı bağlantısı kurulamadı.", 502); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new DomainException("Altyazı sağlayıcısı zaman aşımına uğradı.", 504); }
        catch (JsonException) { throw new DomainException("Altyazı sağlayıcısının JSON yanıtı okunamadı.", 502); }
        finally { _requests.Release(); }
    }
    private static string NormalizeImdb(string value) => value.Replace("tt", "", StringComparison.OrdinalIgnoreCase).TrimStart('0');
    private static bool AllowedDownload(Uri uri) => uri.Scheme == "https" && uri.IsDefaultPort && uri.UserInfo.Length == 0 && uri.Host is "www.opensubtitles.com" or "dl.opensubtitles.org" or "dl.opensubtitles.com";
    private static string? Text(JsonElement value, string name) => value.TryGetProperty(name, out var result) && result.ValueKind == JsonValueKind.String ? result.GetString() : null;
    private static bool Boolean(JsonElement value, string name) => value.TryGetProperty(name, out var result) && result.ValueKind == JsonValueKind.True;
    private static int? Integer(JsonElement value, string name) => value.TryGetProperty(name, out var result) && int.TryParse(result.ToString(), out var number) ? number : null;
}
