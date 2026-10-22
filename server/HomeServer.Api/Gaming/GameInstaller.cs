using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HomeServer.Core;
using HomeServer.Downloads;
using HomeServer.Infrastructure;

namespace HomeServer.Gaming;

public sealed partial class GameInstaller(GameStore store, GameCatalog catalog, MediaStore database, HomePaths paths,
    IWebHostEnvironment environment, IGamePackageSource source, EventHub events) : IProductWorkHandler
{
    public string[] Kinds => ["game-install"];
    public ProductJob Queue(string id, Account actor)
    {
        var game = store.Require(id); RequireInstallAccount(actor);
        return database.QueueProductJob("game-install", id, new { game.Version, game.Commit }, actor);
    }
    public async Task RunAsync(ProductJob job, CancellationToken token)
    {
        var actor = database.RequireProductJob(job); RequireInstallAccount(actor);
        var game = store.Require(job.ScopeId ?? "");
        if (job.Input.GetProperty("commit").GetString() != game.Commit || job.Input.GetProperty("version").GetString() != game.Version) throw new DomainException("Oyun mağazası sürümü değişti; kurulumu yeniden başlat.", 409);
        if (catalog.Installed(game.Id)) { Complete(); return; }
        if (new DriveInfo(Path.GetPathRoot(paths.State)!).AvailableFreeSpace < game.ExpandedBytes + 128 * 1024 * 1024) throw new DomainException("Oyun kurulumu için yeterli boş alan yok.", 409);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(token); budget.CancelAfter(TimeSpan.FromMinutes(10)); token = budget.Token;
        var stage = AppFiles.DirectoryIn(paths.State, "games", "staging", AppFiles.Id(job.Id), "attempt-" + job.Attempts);
        var archivePath = Path.Combine(stage, "source.zip");
        database.ProductProgress(job, 2, "Oyun geliştiricinin kaynağından indiriliyor."); events.Publish("games.changed");
        await using (var response = await source.OpenAsync(game, token))
        await using (var output = new FileStream(archivePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous))
        {
            var buffer = new byte[64 * 1024]; long total = 0; int count; var lastProgress = 2;
            while ((count = await response.Stream.ReadAsync(buffer, token)) != 0)
            {
                total += count; if (total > 128 * 1024 * 1024) throw new DomainException("Oyun arşivi boyut sınırını aşıyor.", 413);
                await output.WriteAsync(buffer.AsMemory(0, count), token);
                var progress = response.Length > 0 ? 2 + (int)Math.Min(45, total * 45 / response.Length.Value) : 20;
                if (progress > lastProgress) { lastProgress = progress; database.ProductProgress(job, progress, "Oyun geliştiricinin kaynağından indiriliyor."); }
            }
            if (response.Length is { } expected && expected != total) throw new DomainException("Oyun indirmesi tamamlanmadı.", 502);
        }
        var build = AppFiles.DirectoryIn(stage, "build");
        await ExtractReviewedAsync(archivePath, build, game, token);
        database.ProductProgress(job, 65, "Oyun kaynakları doğrulanıyor ve hazırlanıyor.");
        AdaptStaticWeb(build, game);
        var runtime = Path.Combine(environment.ContentRootPath, "Gaming", "Runtime");
        foreach (var name in new[] { "home-web.js", "home-web.css" }) File.Copy(MediaPaths.ResolveFile(runtime, name), Path.Combine(build, name), false);
        await File.WriteAllTextAsync(Path.Combine(build, "home-config.json"), JsonSerializer.Serialize(new { id = game.Id, adapter = game.Adapter, input = game.Input, scripts = game.Scripts }, new JsonSerializerOptions(JsonSerializerDefaults.Web)), token);
        var image = store.Artwork(game); File.Copy(image, Path.Combine(build, "cover.png"), false); File.Copy(image, Path.Combine(build, "backdrop.png"), false);
        var licenseDirectory = AppFiles.DirectoryIn(build, "licenses"); File.Copy(MediaPaths.ResolveFile(build, game.LicensePath), Path.Combine(licenseDirectory, "Game.txt"), false);
        await File.WriteAllTextAsync(Path.Combine(licenseDirectory, "Home-adaptation.txt"), $"Home static web adapter. Original source: https://github.com/{game.Repository}/tree/{game.Commit}\nOriginal license: {game.License}. Source files and notices are retained. External analytics/advertisements are not loaded. Original font binaries are omitted in favor of system fonts.\n", token);
        var assets = new List<GameAsset>();
        foreach (var file in Directory.EnumerateFiles(build, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        { token.ThrowIfCancellationRequested(); assets.Add(new(Path.GetRelativePath(build, file).Replace('\\', '/'), new FileInfo(file).Length, await VerifiedFilePublication.HashAsync(file, token))); }
        var manifest = new GameManifest(game.Id, game.Title, game.Publisher, game.SourceUrl, "https://github.com/" + game.Repository, game.Commit, game.Version, "HTML5 / JavaScript", game.License, "web", "16/9", game.Genres, ["web-profile.json"], assets.ToArray(), "html");
        await File.WriteAllTextAsync(Path.Combine(build, "manifest.json"), JsonSerializer.Serialize(manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web)), token);
        database.RequireProductJob(job); RequireInstallAccount(database.GetAccount(actor.Id)!);
        var parent = AppFiles.DirectoryIn(paths.State, "games", "library", game.Id); var target = Path.Combine(parent, game.Commit);
        if (Directory.Exists(target))
        {
            var old = MediaPaths.ResolveFile(target, "manifest.json");
            if (!File.ReadAllBytes(old).AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(build, "manifest.json")))) throw new DomainException("Oyun kurulum hedefi farklı veri içeriyor; mevcut dosyalar korundu.", 409);
        }
        else Directory.Move(build, target); // Same managed volume, no conflicting target overwrite.
        catalog.Register(target, game.Id); Complete();
        void Complete()
        {
            var current = database.GetGameStates(actor.Id).FirstOrDefault(state => state.GameId == game.Id);
            database.SetGamePreference(game.Id, new(true, current?.Favorite ?? false), actor);
            database.FinishProductJob(job, new { game.Id, game.Version, game.Commit }, message: "Oyun kuruldu."); events.Publish("games.changed");
        }
    }
    public static void RequireInstallAccount(Account actor)
    { if (!GameCatalog.Allowed(actor) || !actor.IsAdmin && !actor.CanAcquire) throw new DomainException("Bu hesap oyun kuramaz.", 403); }
    public static async Task ExtractReviewedAsync(string archivePath, string target, GameStoreDefinition game, CancellationToken token)
    {
        var expected = game.SourceFiles.ToDictionary(file => file.Path, StringComparer.Ordinal); var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var count = 0;
        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var entry in archive.Entries)
        {
            token.ThrowIfCancellationRequested(); if (++count > 4096) throw new DomainException("Oyun arşivinde çok fazla dosya var.");
            if (!entry.FullName.StartsWith(game.ArchiveRoot, StringComparison.Ordinal)) throw new DomainException("Oyun arşivi sürüm kökü eşleşmiyor.");
            var relative = entry.FullName[game.ArchiveRoot.Length..].TrimEnd('/'); if (relative.Length == 0) continue;
            _ = DownloadFiles.ValidateRelative(relative);
            if ((entry.ExternalAttributes >> 16 & 0xf000) == 0xa000 || (entry.ExternalAttributes & 0x400) != 0) throw new DomainException("Oyun arşivi dosya sistemi bağlantısı içeriyor.");
            if (entry.FullName.EndsWith('/')) continue;
            if (!seen.Add(relative)) throw new DomainException("Oyun arşivi yinelenen dosya içeriyor.");
            if (!expected.TryGetValue(relative, out var file)) continue; // Reviewed runtime/source files only, never EXE/install scripts.
            if (entry.Length != file.Size) throw new DomainException("Oyun dosyası boyutu kaynak kaydıyla eşleşmiyor.");
            var parent = Path.GetDirectoryName(relative.Replace('/', Path.DirectorySeparatorChar)); if (parent is not null && parent.Length > 0) DownloadFiles.EnsureDirectory(target, parent);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1); hash.AppendData(Encoding.UTF8.GetBytes($"blob {file.Size}\0"));
            await using var input = entry.Open(); await using var output = new FileStream(Path.Combine(target, relative.Replace('/', Path.DirectorySeparatorChar)), FileMode.CreateNew, FileAccess.Write, FileShare.None);
            var buffer = new byte[64 * 1024]; long total = 0; int bytes;
            while ((bytes = await input.ReadAsync(buffer, token)) > 0) { total += bytes; if (total > file.Size) throw new DomainException("Oyun dosyası bildirilen boyutu aşıyor."); hash.AppendData(buffer.AsSpan(0, bytes)); await output.WriteAsync(buffer.AsMemory(0, bytes), token); }
            if (total != file.Size || !Convert.ToHexStringLower(hash.GetHashAndReset()).Equals(file.GitBlobSha1, StringComparison.OrdinalIgnoreCase)) throw new DomainException("Oyun dosyası sabitlenmiş kaynak sürümüyle eşleşmiyor.", 422);
        }
        if (expected.Keys.Any(path => !seen.Contains(path))) throw new DomainException("Oyun arşivinde gerekli kaynak dosyaları eksik.");
    }
    private static void AdaptStaticWeb(string target, GameStoreDefinition game)
    {
        var index = MediaPaths.ResolveFile(target, "index.html"); var html = File.ReadAllText(index);
        html = Scripts().Replace(html, "");
        html = ExternalLinks().Replace(html, "");
        html = InlineHandlers().Replace(html, "");
        html = html.Replace("This site is the official version of 2048.", "This is the open-source web edition of 2048, adapted for Home.", StringComparison.Ordinal);
        html = html.Replace("</head>", "<link rel=\"stylesheet\" href=\"home-web.css\"><script src=\"home-web.js\"></script></head>", StringComparison.OrdinalIgnoreCase);
        File.WriteAllText(index, html);
        foreach (var css in Directory.EnumerateFiles(target, "*.css", SearchOption.AllDirectories)) File.WriteAllText(css, FontFaces().Replace(File.ReadAllText(css), ""));
    }
    [GeneratedRegex("<script\\b[^>]*>[\\s\\S]*?</script>", RegexOptions.IgnoreCase)] private static partial Regex Scripts();
    [GeneratedRegex("<link\\b[^>]*(?:href=[\"'](?:https?:)?//|rel=[\"']manifest)[^>]*>", RegexOptions.IgnoreCase)] private static partial Regex ExternalLinks();
    [GeneratedRegex("\\s+on[a-z]+\\s*=\\s*(?:\"[^\"]*\"|'[^']*')", RegexOptions.IgnoreCase)] private static partial Regex InlineHandlers();
    [GeneratedRegex("@font-face\\s*\\{[^}]*\\}", RegexOptions.IgnoreCase)] private static partial Regex FontFaces();
}
