using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using HomeServer.Core;
using HomeServer.Infrastructure;
using Microsoft.Data.Sqlite;

namespace HomeServer.Operations;

public sealed partial class BackupService(HomePaths paths, MediaStore store)
{
    public const long MaximumArchiveBytes = 1024L * 1024 * 1024;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string Root => AppFiles.DirectoryIn(paths.Data, "backups");

    public async Task<BackupInfo> CreateAsync(string id, CancellationToken token)
        => await CreateCoreAsync(id, null, token);
    public async Task<BackupInfo> CreateContainerUpdateAsync(string id, CancellationToken token)
        => await CreateCoreAsync(id, "container-update", token);
    private async Task<BackupInfo> CreateCoreAsync(string id, string? purpose, CancellationToken token)
    {
        _ = AppFiles.Id(id);
        await _gate.WaitAsync(token);
        var temporary = ""; var pendingZip = "";
        try
        {
            temporary = AppFiles.DirectoryIn(paths.Data, "backup-work", Guid.NewGuid().ToString("N"));
            pendingZip = Path.Combine(Root, id + ".tmp");
            var target = Path.Combine(Root, id + ".zip");
            if (File.Exists(target)) return ReadInfo(id);
            var schema = store.SnapshotState(temporary, token);
            var files = SnapshotFiles(temporary);
            if (files.Sum(file => file.Length) > MaximumArchiveBytes) throw new DomainException("Uygulama yedeği 1 GiB sınırını aşıyor.");
            var manifest = new BackupManifest(1, schema, typeof(BackupService).Assembly.GetName().Version?.ToString() ?? "", DateTimeOffset.UtcNow.ToString("O"), paths.Data, files, purpose);
            await File.WriteAllTextAsync(Path.Combine(temporary, "manifest.json"), JsonSerializer.Serialize(manifest), token);
            if (File.Exists(pendingZip)) File.Delete(pendingZip);
            using (var zip = ZipFile.Open(pendingZip, ZipArchiveMode.Create))
                foreach (var file in Directory.EnumerateFiles(temporary, "*", SearchOption.AllDirectories))
                {
                    token.ThrowIfCancellationRequested();
                    zip.CreateEntryFromFile(file, Path.GetRelativePath(temporary, file).Replace('\\', '/'), CompressionLevel.Fastest);
                }
            File.Move(pendingZip, target);
            return new BackupInfo(id, manifest.CreatedAt, new FileInfo(target).Length, schema);
        }
        finally
        {
            if (Directory.Exists(temporary)) AppFiles.DeleteOwnedDirectory(temporary);
            if (File.Exists(pendingZip)) File.Delete(pendingZip);
            _gate.Release();
        }
    }
    public IReadOnlyList<BackupInfo> List() => Directory.EnumerateFiles(Root, "*.zip").Where(file => Guid.TryParseExact(Path.GetFileNameWithoutExtension(file), "N", out _))
        .Select(file => ReadInfo(Path.GetFileNameWithoutExtension(file))).OrderByDescending(info => info.CreatedAt).ToArray();
    private BackupInfo ReadInfo(string id)
    {
        var path = FilePath(id);
        using var archive = ZipFile.OpenRead(path);
        var entry = archive.GetEntry("manifest.json") ?? throw new DomainException("Yedek manifesti yok.", 422);
        if (entry.Length > 8 * 1024 * 1024) throw new DomainException("Yedek manifesti çok büyük.", 422);
        using var stream = entry.Open();
        var manifest = JsonSerializer.Deserialize<BackupManifest>(stream) ?? throw new DomainException("Yedek manifesti okunamadı.", 422);
        return new BackupInfo(id, manifest.CreatedAt, new FileInfo(path).Length, manifest.Schema);
    }
    public string FilePath(string id) => MediaPaths.ResolveFile(Root, AppFiles.Id(id) + ".zip");

    public async Task<bool> DeleteContainerUpdateAsync(string id, CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            var path = FilePath(id);
            using (var archive = ZipFile.OpenRead(path))
            {
                var entry = archive.GetEntry("manifest.json") ?? throw new DomainException("Yedek manifesti yok.", 422);
                if (entry.Length > 8 * 1024 * 1024) throw new DomainException("Yedek manifesti çok büyük.", 422);
                using var stream = entry.Open();
                if (JsonSerializer.Deserialize<BackupManifest>(stream)?.Purpose != "container-update") return false;
            }
            File.Delete(path); return true;
        }
        finally { _gate.Release(); }
    }

    public async Task<RestorePlan> PrepareRestoreAsync(Stream input, CancellationToken token)
    {
        await _gate.WaitAsync(token);
        var id = Guid.NewGuid().ToString("N");
        var state = "";
        var scheduled = false;
        try
        {
            state = AppFiles.DirectoryIn(paths.Data, "states", id);
            var upload = Path.Combine(state, "upload.zip");
            if (File.Exists(Path.Combine(paths.Data, "pending-restore.json"))) throw new DomainException("Bir geri yükleme zaten yeniden başlatmayı bekliyor.", 409);
            await using (var output = new FileStream(upload, FileMode.CreateNew, FileAccess.Write, FileShare.None)) await CopyLimitedAsync(input, output, MaximumArchiveBytes, token);
            BackupManifest manifest;
            using (var archive = ZipFile.OpenRead(upload))
            {
                if (archive.Entries.Count > 50000 || archive.Entries.Sum(entry => entry.Length) > MaximumArchiveBytes) throw new DomainException("Yedek içerik sınırını aşıyor.", 413);
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in archive.Entries)
                {
                    if (!AllowedPath(entry.FullName) || !names.Add(entry.FullName) || ((entry.ExternalAttributes >> 16) & 0xf000) == 0xa000)
                        throw new DomainException("Yedek güvenli olmayan veya tekrarlanan bir dosya yolu içeriyor.", 422);
                }
                var manifestEntry = archive.GetEntry("manifest.json") ?? throw new DomainException("Home yedek manifesti bulunamadı.", 422);
                if (manifestEntry.Length > 8 * 1024 * 1024) throw new DomainException("Manifest boyutu geçersiz.", 413);
                using (var stream = manifestEntry.Open()) manifest = JsonSerializer.Deserialize<BackupManifest>(stream) ?? throw new DomainException("Manifest okunamadı.", 422);
                if (manifest.Format != 1 || manifest.Schema > MediaStore.LatestSchema || manifest.Schema < 1 || manifest.Files is null || manifest.Files.Length != archive.Entries.Count - 1)
                    throw new DomainException("Yedek biçimi veya veritabanı sürümü bu Home ile uyumlu değil.", 409);
                if (!manifest.Files.Any(file => file.Path == "home.db")) throw new DomainException("Yedekte uygulama veritabanı yok.", 422);
                var declared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var file in manifest.Files)
                {
                    token.ThrowIfCancellationRequested();
                    if (file.Path == "manifest.json" || !AllowedPath(file.Path) || !declared.Add(file.Path) || file.Length < 0 || file.Length > MaximumArchiveBytes)
                        throw new DomainException("Manifest dosya listesi geçersiz.", 422);
                    var entry = archive.GetEntry(file.Path) ?? throw new DomainException("Yedek dosyası eksik.", 422);
                    if (entry.Length != file.Length) throw new DomainException("Yedek dosya boyutu eşleşmiyor.", 422);
                    var directory = Path.GetDirectoryName(file.Path.Replace('/', Path.DirectorySeparatorChar));
                    if (directory is not null) Directory.CreateDirectory(Path.Combine(state, directory));
                    var destination = Path.Combine(state, file.Path.Replace('/', Path.DirectorySeparatorChar));
                    await using (var source = entry.Open())
                    await using (var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        await CopyLimitedAsync(source, target, file.Length, token);
                    if (new FileInfo(destination).Length != file.Length || StateFolders.Hash(destination) != file.Sha256) throw new DomainException("Yedek dosya bütünlüğü doğrulanamadı.", 422);
                }
            }
            File.Delete(upload);
            SanitizeRestoredDatabase(Path.Combine(state, "home.db"), manifest);
            var prepared = manifest with { Files = SnapshotFiles(state) };
            var preparedPath = Path.Combine(state, ".home-state.json");
            await File.WriteAllTextAsync(preparedPath, JsonSerializer.Serialize(prepared), token);
            var plan = new RestorePlan(id, StateFolders.ActiveId(paths.Data), StateFolders.Hash(preparedPath), DateTimeOffset.UtcNow.ToString("O"));
            var planFile = Path.Combine(state, "pending.json");
            await using (var file = new FileStream(planFile, FileMode.CreateNew, FileAccess.Write, FileShare.None)) await JsonSerializer.SerializeAsync(file, plan, cancellationToken: token);
            File.Move(planFile, Path.Combine(paths.Data, "pending-restore.json"), false);
            scheduled = true;
            return plan;
        }
        catch (InvalidDataException) { throw new DomainException("Geçerli bir Home ZIP yedeği seç.", 422); }
        catch (JsonException) { throw new DomainException("Yedek manifesti okunamadı.", 422); }
        finally
        {
            if (!scheduled && Directory.Exists(state)) AppFiles.DeleteOwnedDirectory(state);
            _gate.Release();
        }
    }
    private void SanitizeRestoredDatabase(string path, BackupManifest manifest)
    {
        using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()); db.Open();
        using var check = db.CreateCommand(); check.CommandText = "PRAGMA quick_check";
        if (check.ExecuteScalar() as string != "ok") throw new DomainException("Yedek veritabanı bütünlük kontrolünü geçemedi.", 422);
        check.CommandText = "PRAGMA user_version";
        if (Convert.ToInt32(check.ExecuteScalar()) != manifest.Schema) throw new DomainException("Yedek şeması manifest ile eşleşmiyor.", 422);
        bool Table(string name) { using var query = db.CreateCommand(); query.CommandText = "SELECT name FROM sqlite_schema WHERE type='table' AND name=$name"; query.Parameters.AddWithValue("$name", name); return query.ExecuteScalar() is string; }
        void Execute(string sql) { using var command = db.CreateCommand(); command.CommandText = sql; command.ExecuteNonQuery(); }
        if (!Table("owner") || !Table("media") || !Table("libraries")) throw new DomainException("Bu bir Home veritabanı değil.", 422);
        if (Table("user_sessions")) Execute("UPDATE user_sessions SET revoked_at=datetime('now')");
        if (Table("household_devices")) Execute("DELETE FROM household_devices");
        if (Table("downloads")) Execute("UPDATE downloads SET desired_state='paused',status='paused',revision=revision+1,download_speed=0,upload_speed=0,message='Geri yüklemeden sonra kullanıcı devam komutu bekleniyor.'");
        if (Table("automation_rules")) Execute("UPDATE automation_rules SET enabled=0,auto_grab=0,revision=revision+1,settings_json=json_set(settings_json,'$.enabled',json('false'),'$.autoGrab',json('false')); UPDATE automation_jobs SET status='cancelled' WHERE status IN ('queued','waiting','running')");
        if (Table("system_jobs")) Execute("UPDATE system_jobs SET status='failed',message='Yedekten gelen eski işlem yürütülmedi.' WHERE status IN ('queued','running')");
        if (Table("settings")) Execute("UPDATE settings SET value=json_set(value,'$.Enabled',json('false'),'$.AutoDownload',json('false')) WHERE key='subtitles.profile'");
        Execute("UPDATE jobs SET status='cancelled',cancel_requested=1 WHERE status IN ('queued','running'); UPDATE media SET has_thumbnail=0;");
        foreach (var name in new[] { "library", "music-library" })
        {
            using var updateRoot = db.CreateCommand(); updateRoot.CommandText = "UPDATE libraries SET path=$new,path_key=$key WHERE path=$old";
            var managed = Path.Combine(paths.Data, "downloads", name);
            updateRoot.Parameters.AddWithValue("$new", managed); updateRoot.Parameters.AddWithValue("$key", MediaPaths.Key(managed));
            updateRoot.Parameters.AddWithValue("$old", Path.Combine(manifest.OriginalDataPath, "downloads", name)); updateRoot.ExecuteNonQuery();
        }
        Execute("PRAGMA wal_checkpoint(TRUNCATE); PRAGMA journal_mode=DELETE;");
    }
    public RestorePlan? PendingRestore()
    {
        var file = Path.Combine(paths.Data, "pending-restore.json");
        return File.Exists(file) ? JsonSerializer.Deserialize<RestorePlan>(File.ReadAllText(file)) : null;
    }
    public void CancelRestore()
    {
        var file = Path.Combine(paths.Data, "pending-restore.json");
        if (File.Exists(file)) File.Delete(file);
    }
    private static BackupFile[] SnapshotFiles(string root) => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Select(path =>
        new BackupFile(Path.GetRelativePath(root, path).Replace('\\', '/'), new FileInfo(path).Length, StateFolders.Hash(path))).ToArray();
    private static bool AllowedPath(string path) => path is "home.db" or "manifest.json" or "https-certificate.protected" or "games/server-emulation.json" || KeyPath().IsMatch(path) || SubtitlePath().IsMatch(path) || MusicPreferencePath().IsMatch(path);
    [GeneratedRegex(@"^keys/[A-Za-z0-9-]+\.xml$", RegexOptions.NonBacktracking)] private static partial Regex KeyPath();
    [GeneratedRegex(@"^subtitles/[a-f0-9]{32}/[a-f0-9]{32}\.(srt|vtt|ass|ssa)$", RegexOptions.NonBacktracking)] private static partial Regex SubtitlePath();
    [GeneratedRegex(@"^music/(preferences|radio)/[A-Za-z0-9_-]{1,80}\.json$",RegexOptions.NonBacktracking)] private static partial Regex MusicPreferencePath();
    private static async Task CopyLimitedAsync(Stream source, Stream target, long maximum, CancellationToken token)
    {
        var buffer = new byte[128 * 1024]; long total = 0; int count;
        while ((count = await source.ReadAsync(buffer, token)) > 0)
        {
            total += count; if (total > maximum) throw new DomainException("Yedek boyut sınırını aşıyor.", 413);
            await target.WriteAsync(buffer.AsMemory(0, count), token);
        }
        await target.FlushAsync(token);
    }
}
