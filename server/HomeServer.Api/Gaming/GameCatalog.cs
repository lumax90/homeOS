using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using HomeServer.Core;
using HomeServer.Infrastructure;
using HomeServer.Downloads;

namespace HomeServer.Gaming;

/// <summary>Only reviewed, locally shipped builds. Not an arbitrary HTML/EXE uploader or internet downloader.</summary>
public sealed partial class GameCatalog
{
    private readonly string _root;
    private readonly Dictionary<string, GameManifest> _games = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _locations = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private readonly HomePaths _paths;
    private readonly GameStore _store;
    private readonly NativeCoreRegistry? _cores;
    public GameCatalog(IWebHostEnvironment environment,IConfiguration configuration,HomePaths paths,GameStore store,NativeCoreRegistry? cores=null)
    {
        _paths=paths;_store=store;_cores=cores;
        _root = Path.GetFullPath(configuration["Home:Games:PackagePath"] ?? Path.Combine(environment.ContentRootPath, "GamePacks"));
        if (Directory.Exists(_root))
        {
            AppFiles.RejectLink(_root);
            foreach (var directory in Directory.EnumerateDirectories(_root).Take(33)) Register(directory, Path.GetFileName(directory));
        }
        foreach (var game in _store.Games) { var directory = InstalledDirectory(game); if (File.Exists(Path.Combine(directory, "manifest.json"))) Register(directory, game.Id); }
    }
    public string InstalledDirectory(GameStoreDefinition game) => Path.Combine(_paths.State, "games", "library", game.Id, game.Commit);
    public void Register(string directory, string id)
    {
        var path = MediaPaths.ResolveFile(directory, "manifest.json");
        if (new FileInfo(path).Length > 512 * 1024) throw new DomainException("Oyun paketi tanımı çok büyük.");
        var manifest = JsonSerializer.Deserialize<GameManifest>(File.ReadAllText(path), new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? throw new DomainException("Oyun paketi tanımı geçersiz.");
        Validate(manifest, id);
        foreach (var asset in manifest.Files) { using var file = OpenAt(directory, manifest, asset.Path); }
        lock (_gate) { if (_games.Count >= 32 && !_games.ContainsKey(id)) throw new DomainException("Oyun paketi sınırı aşıldı."); _games[id] = manifest; _locations[id] = directory; }
    }
    public bool Installed(string id) { lock (_gate) return _games.ContainsKey(id); }
    public IReadOnlyList<GameEntry> List(MediaStore store, Account actor)
    {
        if (!Allowed(actor)) return [];
        var states = store.GetGameStates(actor.Id).ToDictionary(state => state.GameId, StringComparer.Ordinal);
        var entries = _store.Games.Select(game => StoreEntry(game, states.GetValueOrDefault(game.Id) ?? EmptyState(game.Id), store.GetProductJobs(actor, "game-install", game.Id).FirstOrDefault())).ToList();
        lock (_gate) entries.AddRange(_games.Values.Where(game => _store.Find(game.Id) is null).Select(game => Entry(game, states.GetValueOrDefault(game.Id) ?? EmptyState(game.Id) with { InLibrary = true })));
        NativeRuntimeProfile? profile=null;try{profile=NativeRuntimeProfile.Read(_paths);}catch(DomainException){}
        entries.AddRange(store.GetGameRoms(actor).Select(rom=>
        {
            var coreId=NativeSystems.Require(rom.System).Core;var reason=_cores?.BlockReason(rom,profile)??(profile?.Enabled!=true?"Oynamak için sunucu emülasyonunu etkinleştir.":!profile.InputReady?NativeRuntimeProfile.InputBlockReason:null);
            var version=profile?.Version??rom.System+"-"+rom.Sha256;
            if(_cores is not null)try{coreId=_cores.Selection(rom).CoreId;if(coreId!="mgba"&&profile is not null&&_cores.Installed(coreId) is{} installed)version=_cores.CurrentVersion(profile,coreId,installed.Binary.Sha256);}catch(Exception error)when(error is DomainException or IOException or JsonException){reason="Çekirdek dosyaları kullanılamıyor; ROM ve kayıtlar korundu.";}
            var presentation=RomArtwork.ReadPresentation(_paths,rom);
            RomMetadataDocument? metadata=null;try{metadata=RomMetadata.Read(_paths,rom);}catch(Exception error)when(error is DomainException or IOException or UnauthorizedAccessException){}var title=presentation.TitleEdited||presentation.Title!=rom.Title?presentation.Title:metadata?.Game.Name??presentation.Title;
            return new GameEntry(rom.Id,title,metadata?.Game.Publisher??"","","","",version,"RetroArch / "+coreId,"user-supplied","native","16/9",[],rom.SizeBytes,states.GetValueOrDefault(rom.Id)??EmptyState(rom.Id) with{InLibrary=true},Runtime:"native",Playable:actor.IsAdmin&&reason is null,System:rom.System,BlockReason:reason,RomSha256:rom.Sha256,HasArtwork:RomArtwork.CoverExists(_paths,rom),Category:presentation.Category,ImportedAt:rom.ImportedAt,ArtworkStatus:store.GetProductJobs(actor,"rom-artwork",rom.Id).FirstOrDefault()?.Status,CoreId:coreId,CoreName:coreId=="mgba"?"mGBA 0.10.5":_cores?.Name(coreId)??coreId,SystemLabel:NativeSystems.Require(rom.System).Name,Metadata:metadata);
        }));
        return entries;
    }
    public GameEntry RequireEntry(string id, MediaStore store, Account actor) => List(store, actor).FirstOrDefault(game => game.Id == id) ?? throw new DomainException("Oyun bulunamadı veya profil kapsamı dışında.", 404);
    private GameEntry StoreEntry(GameStoreDefinition game, GameUserState state, ProductJob? job)
    {
        var installed = Installed(game.Id);
        return new(game.Id, game.Title, game.Publisher, game.SourceUrl, "https://github.com/" + game.Repository, game.Commit, game.Version, "HTML5 / JavaScript", game.License, "web", "16/9", game.Genres, game.ExpandedBytes, state, installed, true, "html", job?.Id, installed ? "installed" : job?.Status, job?.Progress ?? 0, job?.Message);
    }
    public GameManifest Require(string id, Account actor)
    {
        if (id.StartsWith("rom-", StringComparison.Ordinal)) throw new DomainException("Güvenli sunucu yayını henüz hazır değil; ROM çalıştırılmadı.", 409);
        lock (_gate) { if (!Allowed(actor) || !_games.TryGetValue(id, out var game)) throw new DomainException("Oyun önce kurulmalı.", 409); return game; }
    }
    public static bool Allowed(Account actor) => actor.Enabled && (actor.IsAdmin || actor.MaximumAge is null || actor.AllowUnrated);
    public static GameUserState EmptyState(string id) => new(id, false, false, null, 0, null, 0, false);
    public static GameEntry Entry(GameManifest game, GameUserState state) => new(game.Id, game.Title, game.Publisher,
        game.SourceUrl, game.SourceRepository, game.SourceCommit, game.Version, game.Engine, game.License, game.Kind,
        game.Aspect, game.Genres, game.Files.Sum(asset => asset.Size), state, Runtime: game.Runtime);
    public FileStream OpenVerified(GameManifest game, string assetPath)
    {
        string directory; lock (_gate) directory = _locations[game.Id];
        return OpenAt(directory, game, assetPath);
    }
    private static FileStream OpenAt(string directory, GameManifest game, string assetPath)
    {
        var asset = game.Files.FirstOrDefault(file => file.Path == assetPath) ?? throw new DomainException("Oyun dosyası bulunamadı.", 404);
        var path = MediaPaths.ResolveFile(directory, asset.Path);
        var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.SequentialScan);
        try
        {
            if (file.Length != asset.Size || !Convert.ToHexString(SHA256.HashData(file)).Equals(asset.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new DomainException("Oyun paketi bütünlük kontrolünden geçemedi.", 409);
            file.Position = 0;
            return file;
        }
        catch { file.Dispose(); throw; }
    }
    public static string ContentType(string path) => Path.GetExtension(path) switch
    { ".html" => "text/html; charset=utf-8", ".js" => "text/javascript; charset=utf-8", ".css" => "text/css; charset=utf-8",
      ".wasm" => "application/wasm", ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", ".svg" => "image/svg+xml", ".json" => "application/json", ".ogg" => "audio/ogg", ".mp3" => "audio/mpeg", ".wav" => "audio/wav", ".txt" or ".md" => "text/plain; charset=utf-8", _ => "application/octet-stream" };
    private static void Validate(GameManifest game, string folder)
    {
        if (game.Id is null || !Slug().IsMatch(game.Id) || folder != game.Id || game.Title is not { Length: > 0 and <= 120 }
            || game.Version is not { Length: > 0 and <= 80 } || game.Kind != "web" || game.Aspect is not ("1/1" or "16/9")
            || game.Files is not { Length: > 0 and <= 2048 } || game.SavePaths is not { Length: > 0 and <= 8 }
            || game.Genres is not { Length: > 0 and <= 8 } || !Uri.TryCreate(game.SourceUrl, UriKind.Absolute, out var source) || source.Scheme != "https")
            throw new DomainException("Oyun paketi tanımı geçersiz.");
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var asset in game.Files)
            if (!IsSafeAssetPath(asset.Path) || asset.Size is < 0 or > 128 * 1024 * 1024 || !Hash().IsMatch(asset.Sha256 ?? "") || !paths.Add(asset.Path!))
                throw new DomainException("Oyun paketi dosya sınırları geçersiz.");
        var required = game.Runtime == "godot" ? new[] { "index.html", "index.js", "index.wasm", "index.pck", "home-game.js", "cover.png", "backdrop.png", "licenses/Game.txt" } : new[] { "index.html", "home-web.js", "home-config.json", "cover.png", "backdrop.png", "licenses/Game.txt" };
        if (game.Runtime is not ("godot" or "html") || game.Files.Sum(file => file.Size) > 256 * 1024 * 1024 || !required.All(paths.Contains))
            throw new DomainException("Oyun paketi eksik veya çok büyük.");
        if (game.SavePaths.Distinct(StringComparer.Ordinal).Count() != game.SavePaths.Length || game.SavePaths.Any(path => !SavePath().IsMatch(path)))
            throw new DomainException("Oyun kayıt yolları geçersiz.");
    }
    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,59}$")] private static partial Regex Slug();
    public static bool IsSafeAssetPath(string? path)
    {
        if (path is null || !AssetPath().IsMatch(path)) return false;
        try { _ = DownloadFiles.ValidateRelative(path); return true; }
        catch (DomainException) { return false; }
    }
    [GeneratedRegex("^(?:[A-Za-z0-9_-][A-Za-z0-9_. -]{0,99}/){0,20}(?:[A-Za-z0-9_-][A-Za-z0-9_. -]{0,99}\\.(?:html|js|css|wasm|pck|png|jpe?g|svg|webp|ico|json|wav|ogg|mp3|txt|md)|LICENSE|README)$")] private static partial Regex AssetPath();
    [GeneratedRegex("^[a-fA-F0-9]{64}$")] private static partial Regex Hash();
    [GeneratedRegex("^(?:home-progress\\.json|web-profile\\.json|save/[0-5]/box\\.save)$")] private static partial Regex SavePath();
}
