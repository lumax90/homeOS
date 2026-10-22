using System.Security.Cryptography;
using System.Text;
using HomeServer.Core;

namespace HomeServer.Metadata;

public sealed class ArtworkCache(HomePaths paths, IHttpClientFactory clients)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<(string Path, string Mime)> GetAsync(string url, CancellationToken token)
    {
        if (!MetadataProvider.AllowedImage(url)) throw new DomainException("Desteklenmeyen görsel adresi.", 400);
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)));
        var directory = Path.Combine(paths.Data, "artwork");
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, key);
        await _gate.WaitAsync(token);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        token = timeout.Token;
        try
        {
            if (File.Exists(file) && File.Exists(file + ".mime")) return (file, await File.ReadAllTextAsync(file + ".mime", token));
            using var response = await clients.CreateClient("artwork").GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
            if (!response.IsSuccessStatusCode) throw new DomainException("Görsel alınamadı.", 502);
            var mime = response.Content.Headers.ContentType?.MediaType;
            if (mime is not ("image/jpeg" or "image/png" or "image/webp")) throw new DomainException("Desteklenmeyen görsel biçimi.", 502);
            await using var body = await response.Content.ReadAsStreamAsync(token);
            var bytes = await MetadataProvider.ReadLimitedAsync(body, 10 * 1024 * 1024, token);
            var temporary = file + ".tmp";
            await File.WriteAllBytesAsync(temporary, bytes, token);
            File.Move(temporary, file, true);
            await File.WriteAllTextAsync(file + ".mime", mime, token);
            return (file, mime);
        }
        finally { _gate.Release(); }
    }
}
