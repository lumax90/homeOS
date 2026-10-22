using System.Text.Json;
using HomeServer.Core;

namespace HomeServer.Gaming;

/// <summary>Reviewed catalog metadata only. Payloads come from pinned upstream repositories on an explicit install command.</summary>
public sealed class GameStore
{
    private readonly string _root;
    private readonly Dictionary<string, GameStoreDefinition> _games;
    public GameStore(IWebHostEnvironment environment, IConfiguration configuration)
    {
        _root = Path.GetFullPath(configuration["Home:Games:StorePath"] ?? Path.Combine(environment.ContentRootPath, "Gaming", "Store"));
        var path = Path.Combine(_root, "catalog.json");
        if (!File.Exists(path)) { _games = []; return; }
        path = MediaPaths.ResolveFile(_root, "catalog.json");
        if (new FileInfo(path).Length > 2 * 1024 * 1024) throw new DomainException("Oyun mağazası tanımı çok büyük.");
        var document = JsonSerializer.Deserialize<GameStoreDocument>(File.ReadAllText(path), new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? throw new DomainException("Oyun mağazası tanımı geçersiz.");
        if (document.Format != 1 || document.Games.Length > 32) throw new DomainException("Oyun mağazası tanımı geçersiz.");
        _games = document.Games.ToDictionary(game => game.Id, StringComparer.Ordinal);
        foreach (var game in _games.Values)
        {
            if (game.SourceFiles.Length is < 1 or > 2048 || game.ExpandedBytes > 256 * 1024 * 1024 || game.SourceFiles.Any(file => file.Size < 0 || file.GitBlobSha1.Length != 40)) throw new DomainException("Oyun kaynak sınırları geçersiz.");
            var expected = $"https://codeload.github.com/{game.Repository}/zip/{game.Commit}";
            if (game.ArchiveUrl != expected || game.Commit.Length != 40 || game.Commit.Any(c => !char.IsAsciiHexDigit(c)) || game.ArchiveRoot != game.Repository.Split('/')[1] + "-" + game.Commit + "/") throw new DomainException("Oyun indirme kaynağı geçersiz.");
        }
    }
    public IReadOnlyCollection<GameStoreDefinition> Games => _games.Values;
    public GameStoreDefinition? Find(string id) => _games.GetValueOrDefault(id);
    public GameStoreDefinition Require(string id) => Find(id) ?? throw new DomainException("Oyun mağazada bulunamadı.", 404);
    public string Artwork(GameStoreDefinition game)
    {
        var path = MediaPaths.ResolveFile(_root, "artwork/" + game.Id + ".png");
        if (Infrastructure.StateFolders.Hash(path) != game.ArtworkSha256) throw new DomainException("Oyun görseli doğrulanamadı.", 409);
        return path;
    }
}
