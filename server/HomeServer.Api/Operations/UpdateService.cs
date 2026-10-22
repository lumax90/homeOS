using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using HomeServer.Core;
using HomeServer.Infrastructure;

namespace HomeServer.Operations;

public sealed class UpdateService(HomePaths paths, IConfiguration configuration, BackupService backups, RestartCoordinator restart, MediaStore? store = null)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string Root => AppFiles.DirectoryIn(paths.Data, "updates");
    private string Releases => AppFiles.DirectoryIn(Root, "releases");
    public string? TrustPath => ExecutablePathForTrust(configuration);
    public PreparedUpdate? Pending => Read<PreparedUpdate>(Path.Combine(Root, "pending.json"));
    public ReleaseRollback? Rollback => Read<ReleaseRollback>(Path.Combine(Root, "rollback.json"));
    public object Status()
    {
        var key = TrustPath;
        string? fingerprint = null;
        if (key is not null) { using var rsa = RSA.Create(); rsa.ImportFromPem(File.ReadAllText(key)); fingerprint = Convert.ToHexString(SHA256.HashData(rsa.ExportSubjectPublicKeyInfo())).ToLowerInvariant(); }
        return new { currentVersion = typeof(UpdateService).Assembly.GetName().Version?.ToString(3), runtime = RuntimeInformation.RuntimeIdentifier,
            trusted = key is not null, keyFingerprint = fingerprint, canApply = restart.Supported, pending = Pending, rollback = Rollback };
    }
    public async Task<PreparedUpdate> PrepareAsync(Stream source, CancellationToken token)
    {
        await _gate.WaitAsync(token); string? directory = null; string? archive = null; var saved = false;
        try
        {
            if (Pending is not null) throw new DomainException("Hazır bir güncelleme var; önce uygula veya kaldır.", 409);
            var trust = ReadTrust(); var id = Guid.NewGuid().ToString("N"); directory = AppFiles.DirectoryIn(Releases, id); archive = Path.Combine(Root, id + ".zip");
            await using (var file = new FileStream(archive, FileMode.CreateNew, FileAccess.Write, FileShare.None)) await PackageTrust.CopyAsync(source, file, PackageTrust.MaximumArchive, token);
            var manifest = await PackageTrust.ExtractAsync(archive, directory, trust, token);
            ValidateTarget(manifest);
            var result = new PreparedUpdate(id, manifest.Version, manifest.Runtime, DateTimeOffset.UtcNow.ToString("O"));
            PackageTrust.AtomicJson(Path.Combine(Root, "pending.json"), result); saved = true; return result;
        }
        catch (InvalidDataException) { throw new DomainException("Geçerli bir imzalı Home ZIP paketi seç.", 422); }
        finally
        {
            if (archive is not null && File.Exists(archive)) File.Delete(archive);
            if (!saved && directory is not null && Directory.Exists(directory)) AppFiles.DeleteOwnedDirectory(directory);
            _gate.Release();
        }
    }
    public async Task ApplyAsync(CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            if (!restart.Supported) throw new DomainException("Bu kurulumun güncellemesini süreç/container yöneticisinden uygula.", 409);
            RequireNoGames();
            if (backups.PendingRestore() is not null) throw new DomainException("Önce hazırlanmış geri yüklemeyi tamamla veya kaldır.", 409);
            var pending = Pending ?? throw new DomainException("Hazırlanmış güncelleme bulunamadı.", 404);
            var directory = Path.Combine(Releases, AppFiles.Id(pending.Id));
            var manifest = await PackageTrust.VerifyDirectoryAsync(directory, ReadTrust(), token); ValidateTarget(manifest);
            var backup = await backups.CreateAsync(Guid.NewGuid().ToString("N"), token);
            RequireNoGames();
            var previous = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            if (File.Exists(Path.Combine(previous, PackageTrust.ManifestName)))
            {
                var original = await PackageTrust.VerifyDirectoryAsync(previous, ReadTrust(), token);
                PackageTrust.AtomicJson(Path.Combine(Root, "rollback.json"), new ReleaseRollback(previous, original.Version, backup.Id));
            }
            Activate(directory, manifest);
            File.Delete(Path.Combine(Root, "pending.json"));
        }
        finally { _gate.Release(); }
    }
    public async Task RevertAsync(CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            if (!restart.Supported) throw new DomainException("Bu kurulumda uygulama içi geri dönüş kullanılmıyor.", 409);
            RequireNoGames();
            var rollback = Rollback ?? throw new DomainException("Doğrulanmış önceki sürüm kaydı bulunamadı.", 404);
            var manifest = await PackageTrust.VerifyDirectoryAsync(rollback.Directory, ReadTrust(), token);
            if (manifest.Runtime != RuntimeInformation.RuntimeIdentifier) throw new DomainException("Önceki paket platformu eşleşmiyor.", 409);
            await using (var input = File.OpenRead(backups.FilePath(rollback.BackupId))) await backups.PrepareRestoreAsync(input, token);
            try { Activate(rollback.Directory, manifest); }
            catch { backups.CancelRestore(); throw; }
            File.Delete(Path.Combine(Root, "rollback.json"));
        }
        finally { _gate.Release(); }
    }
    public async Task CancelAsync(CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            var pending = Pending; if (pending is null) return;
            File.Delete(Path.Combine(Root, "pending.json"));
            var directory = Path.Combine(Releases, AppFiles.Id(pending.Id)); if (Directory.Exists(directory)) AppFiles.DeleteOwnedDirectory(directory);
        }
        finally { _gate.Release(); }
    }
    private void Activate(string directory, PackageManifest manifest)
    {
        PackageTrust.AtomicJson(Path.Combine(Root, "active.json"), new ActiveRelease(directory, manifest.Version, DateTimeOffset.UtcNow.ToString("O")));
        restart.ReplacementDirectory = directory; restart.ReplacementEntryPoint = manifest.EntryPoint; restart.ReplacementDataPath = paths.Data;
    }
    private static void ValidateTarget(PackageManifest data)
    {
        if (data.Runtime != RuntimeInformation.RuntimeIdentifier) throw new DomainException("Bu paket sunucunun işletim sistemi/mimarisine ait değil.", 409);
        if (data.Schema < MediaStore.LatestSchema) throw new DomainException("Paket mevcut veri şemasını desteklemiyor.", 409);
        if (Version.Parse(data.Version) <= typeof(UpdateService).Assembly.GetName().Version) throw new DomainException("Paket mevcut sürümden yeni değil.", 409);
    }
    private string ReadTrust() => TrustPath is { } path ? File.ReadAllText(path) : throw new DomainException("Bu kurulumda güvenilir güncelleme anahtarı bulunmuyor.", 409);
    private void RequireNoGames()
    { if (store?.GetActiveGameSessions().Count > 0) throw new DomainException("Bir oyun oturumu açık. Güncellemeden önce oyundan çık.", 409); }
    public static string? ExecutablePathForTrust(IConfiguration configuration)
    {
        var path = configuration["Home:Updates:TrustedPublicKeyPath"] ?? Path.Combine(AppContext.BaseDirectory, PackageTrust.PublicKeyName);
        if (!File.Exists(path)) return null; AppFiles.RejectLink(path); return path;
    }
    private static T? Read<T>(string path) where T : class
    {
        if (!File.Exists(path)) return null; AppFiles.RejectLink(path); return JsonSerializer.Deserialize<T>(File.ReadAllText(path));
    }
    public static async Task<bool> RedirectToActiveAsync(IConfiguration configuration)
    {
        if (Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true") return false;
        var data = Path.GetFullPath(configuration["Home:DataPath"] ?? ".data"); var active = Read<ActiveRelease>(Path.Combine(data, "updates", "active.json"));
        if (active is null || string.Equals(Path.GetFullPath(active.Directory).TrimEnd(Path.DirectorySeparatorChar), AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) return false;
        var key = ExecutablePathForTrust(configuration) ?? throw new InvalidOperationException("Etkin sürüme geçiş için güvenilir paket anahtarı bulunamadı.");
        var manifest = await PackageTrust.VerifyDirectoryAsync(active.Directory, File.ReadAllText(key), CancellationToken.None);
        if (manifest.Runtime != RuntimeInformation.RuntimeIdentifier) throw new InvalidOperationException("Etkin paket platformu farklı.");
        RestartCoordinator.StartReplacement(active.Directory, manifest.EntryPoint, data); return true;
    }
}
