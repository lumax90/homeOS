using System.Net;
using HomeServer.Core;
using HomeServer.Metadata;
using Microsoft.Extensions.Caching.Memory;

namespace HomeServer.Music;

public sealed class CoverArtCache(HomePaths paths, IHttpClientFactory clients, IMemoryCache cache)
{
    private readonly SemaphoreSlim _gate = new(2, 2);
    private DateTimeOffset _retryAt;
    public async Task<(string Path, string Mime)?> GetAsync(string type, string id, CancellationToken token,bool allowFetch=true)
    {
        id = MusicBrainzProvider.Id(id); if (type is not ("release" or "release-group")) throw new DomainException("Kapak kimliği geçersiz.");
        var missingKey = "cover-missing:" + type + ":" + id;
        var root = AppFiles.DirectoryIn(paths.Data, "cover-art"); var file = Path.Combine(root, type + "-" + id + ".image");
        if (File.Exists(file) && (!allowFetch||File.GetLastWriteTimeUtc(file) > DateTime.UtcNow.AddDays(-14)))
        { AppFiles.RejectLink(file); return LocalMetadataReader.ImageMime(file) is { } mime ? (file, mime) : null; }
        if (cache.TryGetValue<bool>(missingKey, out _)) return null;
        if(!allowFetch)return null;
        await _gate.WaitAsync(token);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(token); budget.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            if(File.Exists(file)&&File.GetLastWriteTimeUtc(file)>DateTime.UtcNow.AddDays(-14)){AppFiles.RejectLink(file);return LocalMetadataReader.ImageMime(file) is{} cachedMime?(file,cachedMime):null;}
            if(cache.TryGetValue<bool>(missingKey,out _))return null;
            if (_retryAt > DateTimeOffset.UtcNow) throw new DomainException("Kapak sağlayıcısı kısa süre bekleme istiyor.", 429);
            Prune(root);
            var uri = new Uri($"https://coverartarchive.org/{type}/{id}/front-500");
            for (var redirect = 0; redirect < 5; redirect++)
            {
                if (!Allowed(uri)) throw new DomainException("Kapak arşivi yönlendirmesi izinli değil.", 502);
                using var response = await clients.CreateClient("cover-art").GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, budget.Token);
                if (response.StatusCode == HttpStatusCode.NotFound) { cache.Set(missingKey, true, new MemoryCacheEntryOptions { Size = 1, AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15) }); return null; }
                if (response.StatusCode == HttpStatusCode.TooManyRequests) { _retryAt = DateTimeOffset.UtcNow.AddSeconds(Math.Clamp(response.Headers.RetryAfter?.Delta?.TotalSeconds ?? 30, 1, 300)); throw new DomainException("Kapak sağlayıcısı sorgu sınırına ulaştı.", 429); }
                if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } next) { uri = next.IsAbsoluteUri ? next : new Uri(uri, next); continue; }
                if (!response.IsSuccessStatusCode) throw new DomainException("Kapak arşivi şu anda yanıt vermiyor.", 502);
                await using var input = await response.Content.ReadAsStreamAsync(budget.Token);
                var bytes = await MetadataProvider.ReadLimitedAsync(input, 10 * 1024 * 1024, budget.Token);
                var temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    await File.WriteAllBytesAsync(temporary, bytes, budget.Token);
                    var mime = LocalMetadataReader.ImageMime(temporary) ?? throw new DomainException("Kapak yanıtı desteklenen bir resim değil.", 502);
                    if (File.Exists(file)) AppFiles.RejectLink(file);
                    File.Move(temporary, file, true); return (file, mime);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            throw new DomainException("Kapak arşivi yönlendirme sınırını aştı.", 502);
        }
        catch (HttpRequestException) { throw new DomainException("Kapak arşivine bağlanılamadı.", 502); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new DomainException("Kapak arşivi zaman aşımına uğradı.", 504); }
        finally { _gate.Release(); }
    }
    public static bool Allowed(Uri uri) => uri.Scheme == "https" && uri.IsDefaultPort && uri.UserInfo.Length == 0
        && (uri.Host == "coverartarchive.org" || uri.Host == "archive.org" || uri.Host.EndsWith(".archive.org", StringComparison.Ordinal));
    private static void Prune(string root)
    {
        var files = Directory.EnumerateFiles(root, "*.image").Where(file =>
        {
            var name = Path.GetFileNameWithoutExtension(file); var prefix = name.StartsWith("release-group-", StringComparison.Ordinal) ? "release-group-" : "release-";
            return name.StartsWith(prefix, StringComparison.Ordinal) && Guid.TryParseExact(name[prefix.Length..], "D", out _);
        }).Select(file => new FileInfo(file)).OrderBy(file => file.LastWriteTimeUtc).ToList();
        var size = files.Sum(file => file.Length); var count = files.Count;
        foreach (var file in files)
        {
            if (size < 512L * 1024 * 1024 && count < 2000) break;
            AppFiles.RejectLink(file.FullName);
            try { File.Delete(file.FullName); size -= file.Length; count--; } catch (IOException) { }
        }
    }
}
