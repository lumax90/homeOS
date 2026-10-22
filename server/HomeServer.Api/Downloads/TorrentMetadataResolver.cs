using HomeServer.Core;
using HomeServer.Infrastructure;
using HomeServer.Metadata;
using HomeServer.Sources;
using MonoTorrent;

namespace HomeServer.Downloads;

public sealed class TorrentMetadataResolver(MediaStore store, SourceTransportService network)
{
    public static IReadOnlyList<string> PublicTrackers { get; } = Array.AsReadOnly(new[]
    {
        "udp://tracker.opentrackr.org:1337/announce", "udp://open.stealth.si:80/announce", "udp://tracker.torrent.eu.org:451/announce"
    });

    public MagnetLink WithTrackers(DownloadSource source, MagnetLink magnet)
    {
        if (magnet.AnnounceUrls.Count > 0 || store.DownloadSourceProvider(source.Record.Id)?.Info.ProviderId is not ("yts" or "eztv" or "the-pirate-bay" or "torrents-csv")) return magnet;
        return new MagnetLink(magnet.InfoHashes, magnet.Name, PublicTrackers.ToArray());
    }

    public async Task<byte[]?> TryHttpAsync(DownloadSource source, MagnetLink magnet, CancellationToken token)
    {
        var provider = store.DownloadSourceProvider(source.Record.Id);
        if (provider?.Info.ProviderId != "yts" || new Uri(provider.Info.Endpoint).IdnHost != "movies-api.accel.li" || magnet.InfoHashes.V1 is null) return null;
        // The built-in YTS API publishes this HTTPS metadata endpoint. Never send source
        // credentials to it, follow redirects, or use it for custom/private sources.
        var address = "https://yts.gg/torrent/download/" + magnet.InfoHashes.V1.ToHex();
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(token);
        budget.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            using var response = await network.SendAsync(provider, address, "grab", budget.Token, credentials: false);
            if (!response.IsSuccessStatusCode) return null;
            await using var stream = await response.Content.ReadAsStreamAsync(budget.Token);
            var bytes = await MetadataProvider.ReadLimitedAsync(stream, 4 * 1024 * 1024, budget.Token);
            if (bytes.Length == 0 || bytes[0] != (byte)'d') return null; // A challenge page is not torrent metadata.
            var torrent = TorrentEngine.Parse(bytes);
            if (!TorrentEngine.Hashes(torrent.InfoHashes).Intersect(TorrentEngine.Hashes(magnet.InfoHashes), StringComparer.OrdinalIgnoreCase).Any())
                throw new DomainException("Kaynağın torrent kimliği seçilen sürümle eşleşmedi.", 422);
            token.ThrowIfCancellationRequested();
            return bytes;
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { return null; }
        catch (HttpRequestException) { return null; }
    }
}
