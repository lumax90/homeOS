using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HomeServer.Core;
using HomeServer.Infrastructure;
using HomeServer.Operations;

namespace HomeServer.Music;

public sealed record MusicAlbumEnrichment(string Fingerprint, string? GroupId, MusicBrainzAlbum? Album, string CheckedAt, string Status = "matched", string? RetryAt = null, int Generation = 0, string? Message = null);

/// <summary>Profile-private, conservative enrichment. It never rewrites tags or changes library identities.</summary>
public sealed class MusicOnlineMetadata(HomePaths paths, MusicExperiencePreferences preferences, MusicBrainzProvider provider, MediaStore store)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static string Key(MusicAlbum album) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(album.Id+"\0"+album.Title+"\0"+album.Artist+"\0"+album.ReleaseGroupMbid+"\0"+album.ReleaseMbid)));
    private string PathFor(MusicAlbum album, string user)
    {
        if (!Gaming.NativeSystems.SafeSegment(user) || !Gaming.NativeSystems.SafeSegment(album.Id)) throw new DomainException("Müzik bilgisi kapsamı geçersiz.");
        return Path.Combine(paths.State, "music", "online-metadata", user, album.Id+".json");
    }
    public MusicAlbumEnrichment? Cached(MusicAlbum album, string user)
    {
        var path = PathFor(album, user); if (!File.Exists(path)) return null;
        path = MediaPaths.ResolveFile(paths.State, Path.GetRelativePath(paths.State, path)); if (new FileInfo(path).Length > 64*1024) return null;
        try { var value = JsonSerializer.Deserialize<MusicAlbumEnrichment>(File.ReadAllText(path), Json); return value?.Fingerprint == Key(album) ? value : null; }
        catch (JsonException) { return null; }
    }
    private static bool Fresh(MusicAlbumEnrichment? value, MusicExperienceOptions options) => value is not null && value.Generation == options.MetadataGeneration && DateTimeOffset.TryParse(value.RetryAt, out var next) && next > DateTimeOffset.UtcNow;
    public async Task<MusicAlbumEnrichment?> ResolveAsync(MusicAlbum album, Account actor, CancellationToken token)
    {
        var options = preferences.Read(actor.Id); var cached = Cached(album, actor.Id);
        if (!MusicAccess.Online(store.GetAccount(actor.Id) ?? actor) || !options.OnlineMetadata && !options.OnlineCovers || Fresh(cached, options)) return cached;
        await _gate.WaitAsync(token);
        try
        {
            options = preferences.Read(actor.Id); cached = Cached(album, actor.Id);
            if (!options.OnlineMetadata && !options.OnlineCovers || Fresh(cached, options)) return cached;
            MusicAlbumEnrichment value;
            try
            {
                MusicBrainzAlbum? candidate = null;
                if (album.ReleaseGroupMbid is { } id) candidate = await provider.AlbumAsync(id, token);
                else if (album.ReleaseMbid is { } release) candidate = await provider.AlbumAsync((await provider.ReleaseAsync(release, token)).Metadata.GroupId, token);
                else if (album.Title.Length is >=2 and <=120 && album.Artist.Length is >=2 and <=120 && album.Artist != "Sanatçı etiketi yok" && album.Title != "Albüm etiketi yok")
                {
                    var results = await provider.SearchAlbumsAsync(album.Title, album.Artist, 0, token);
                    // A truncated search is not evidence that only one exact identity exists.
                    var exact = results.Items.Where(item=>Same(item.Title,album.Title) && Same(item.Artist,album.Artist)).DistinctBy(item=>item.Id).ToArray();
                    if (results.Total <= results.Items.Count && exact.Length == 1) candidate = exact[0];
                }
                if(candidate is not null&&JsonSerializer.SerializeToUtf8Bytes(candidate,Json).Length>48*1024)throw new DomainException("Müzik sağlayıcısı yanıtı okunamadı.",502);
                value = new(Key(album), candidate?.Id, candidate, DateTimeOffset.UtcNow.ToString("O"), candidate is null ? "unmatched" : "matched", DateTimeOffset.UtcNow.AddDays(candidate is null ? 7 : 30).ToString("O"), options.MetadataGeneration);
            }
            catch (DomainException error) when (error.StatusCode is 404 or 429 or 502 or 504)
            { value = new(Key(album), cached?.GroupId, cached?.Album, DateTimeOffset.UtcNow.ToString("O"), "retry", DateTimeOffset.UtcNow.AddMinutes(error.StatusCode == 404 ? 1440 : 30).ToString("O"), options.MetadataGeneration, error.Message); }
            var current = store.GetAccount(actor.Id); var settings = preferences.Read(actor.Id);
            if (current is null || !MusicAccess.Online(current) || !settings.OnlineMetadata && !settings.OnlineCovers || settings.MetadataGeneration != options.MetadataGeneration || store.GetMusicAlbum(album.Id, current) is not { } latest || Key(latest) != Key(album)) return cached;
            token.ThrowIfCancellationRequested(); var root = AppFiles.DirectoryIn(paths.State, "music", "online-metadata", actor.Id); PackageTrust.AtomicJson(Path.Combine(root, album.Id+".json"), value); return value;
        }
        finally { _gate.Release(); }
    }
    public static bool Same(string a, string b) => string.Equals(a.Trim().Normalize(NormalizationForm.FormC), b.Trim().Normalize(NormalizationForm.FormC), StringComparison.OrdinalIgnoreCase);
}
