using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using HomeServer.Core;
using HomeServer.Metadata;
using Microsoft.Extensions.Caching.Memory;

namespace HomeServer.Music;

public sealed partial class MusicBrainzProvider(IHttpClientFactory clients, IMemoryCache cache)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTimeOffset _next;
    public async Task<MusicPage<MusicArtist>> SearchArtistsAsync(string query, int offset, CancellationToken token, bool autocomplete=false)
    {
        query = query.Trim(); if (query.Length is < 2 or > 120 || offset is < 0 or > 10000) throw new DomainException("Sanatçı araması 2–120 karakter olmalı.");
        var escaped = query.Replace("\\", "\\\\").Replace("\"", "\\\"");
        var term="artist:\""+escaped+"\"";
        if(autocomplete)
        {
            // Operators remain literal. Only the final alphanumeric token gets a trailing wildcard.
            var words=Regex.Matches(query,@"[\p{L}\p{M}\p{N}]+",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100)).Select(match=>match.Value).Take(16).ToArray();
            if(words.Length>0){var prefix=string.Join(" AND ",words.Select((word,index)=>index==words.Length-1?word+"*":"\""+word+"\""));term=$"({term}^4 OR artist:({prefix}) OR alias:({prefix}))";}
        }
        using var data = await GetAsync($"artist?query={Uri.EscapeDataString(term)}&limit=25&offset={offset}", token);
        return new(data.RootElement.GetProperty("artists").EnumerateArray().Select(Artist).ToArray(), data.RootElement.GetProperty("count").GetInt32(), offset, 25);
    }
    public async Task<MusicArtist> ArtistAsync(string id, CancellationToken token)
    {
        id = Id(id); using var data = await GetAsync("artist/" + id, token);
        var result = Artist(data.RootElement); if (result.Id != id) throw new DomainException("Sanatçı kimliği eşleşmedi.", 502); return result;
    }
    public async Task<MusicPage<MusicBrainzAlbum>> AlbumsAsync(string artist, int offset, CancellationToken token, bool allStatuses = false)
    {
        artist = Id(artist); if (offset is < 0 or > 100000) throw new DomainException("Müzik sayfası geçersiz.");
        using var data = await GetAsync($"release-group?artist={artist}&inc=artist-credits&release-group-status={(allStatuses ? "all" : "website-default")}&limit=50&offset={offset}", token);
        var items = data.RootElement.GetProperty("release-groups").EnumerateArray().Select(Album).ToArray();
        return new(items, data.RootElement.GetProperty("release-group-count").GetInt32(), offset, 50);
    }
    public async Task<MusicBrainzAlbum> AlbumAsync(string id, CancellationToken token)
    {
        id = Id(id); using var data = await GetAsync($"release-group/{id}?inc=artist-credits", token);
        var result = Album(data.RootElement); if (result.Id != id) throw new DomainException("Albüm kimliği eşleşmedi.", 502); return result;
    }
    private async Task<JsonDocument> GetAsync(string route, CancellationToken token)
    {
        var key = "musicbrainz:" + route;
        if (cache.TryGetValue<byte[]>(key, out var cached)) return JsonDocument.Parse(cached!);
        await _gate.WaitAsync(token);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            if(cache.TryGetValue<byte[]>(key,out cached))return JsonDocument.Parse(cached!);
            var wait = _next - DateTimeOffset.UtcNow; if (wait > TimeSpan.Zero) await Task.Delay(wait, timeout.Token);
            _next = DateTimeOffset.UtcNow.AddMilliseconds(1100);
            using var response = await clients.CreateClient("musicbrainz").GetAsync(route + (route.Contains('?') ? "&" : "?") + "fmt=json", HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable)
            {
                _next = DateTimeOffset.UtcNow.AddSeconds(Math.Clamp(response.Headers.RetryAfter?.Delta?.TotalSeconds ?? 10, 1, 120));
                throw new DomainException("MusicBrainz kısa bir bekleme istiyor. Biraz sonra dene.", 429);
            }
            if (response.StatusCode == HttpStatusCode.NotFound) throw new DomainException("Müzik bilgisi sağlayıcıda bulunamadı.", 404);
            if (!response.IsSuccessStatusCode) throw new DomainException("MusicBrainz şu anda yanıt veremiyor.", 502);
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            var bytes = await MetadataProvider.ReadLimitedAsync(stream, 4 * 1024 * 1024, timeout.Token);
            var result = JsonDocument.Parse(bytes);
            cache.Set(key, bytes, new MemoryCacheEntryOptions { Size = 1, AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(6) });
            return result;
        }
        catch (HttpRequestException) { throw new DomainException("MusicBrainz bağlantısı kurulamadı.", 502); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new DomainException("Müzik bilgisi isteği zaman aşımına uğradı.", 504); }
        catch (JsonException) { throw new DomainException("Müzik sağlayıcısı yanıtı okunamadı.", 502); }
        finally { _gate.Release(); }
    }
    private static MusicArtist Artist(JsonElement value)
    {
        var id = Id(value.GetProperty("id").GetString()!);
        return new(id, Text(value, "name") ?? "", Text(value, "sort-name"), Text(value, "country"), Text(value, "disambiguation"), "musicbrainz", id);
    }
    private static MusicBrainzAlbum Album(JsonElement value)
    {
        var id = Id(value.GetProperty("id").GetString()!);
        var credits = Credits(value);
        return new(id, Text(value, "title") ?? "", MusicCredits.Display(credits), Text(value, "first-release-date"), Text(value, "primary-type") ?? "", "https://musicbrainz.org/release-group/" + id,
            value.TryGetProperty("secondary-types", out var secondary) && secondary.ValueKind == JsonValueKind.Array ? secondary.EnumerateArray().Select(item => item.GetString() ?? "").ToArray() : [], credits);
    }
    private static string? Text(JsonElement root, string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    public static string Id(string id) => Guid.TryParseExact(id, "D", out var result) ? result.ToString() : throw new DomainException("MusicBrainz kimliği geçersiz.");
}
