using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using HomeServer.Core;
using HomeServer.Downloads;
using HomeServer.Infrastructure;

namespace HomeServer.Operations;

public static class PackageTrust
{
    public const string ManifestName = ".home-package.json";
    public const string SignatureName = ".home-package.sig";
    public const string PublicKeyName = "update-trust.pem";
    public const long MaximumArchive = 1024L * 1024 * 1024;
    public const long MaximumExpanded = 2 * MaximumArchive;
    public static PackageManifest Authenticate(byte[] manifest, byte[] signature, string trustedKey)
    {
        if (manifest.Length > 4 * 1024 * 1024 || signature.Length > 1024) throw new DomainException("Paket manifesti/imzası boyut sınırını aşıyor.", 422);
        try
        {
            using var rsa = RSA.Create(); rsa.ImportFromPem(trustedKey);
            if (rsa.KeySize < 2048 || !rsa.VerifyData(manifest, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pss)) throw new DomainException("Paket imzası bu kurulumun güvenilir anahtarıyla doğrulanamadı.", 422);
            var data = JsonSerializer.Deserialize<PackageManifest>(manifest) ?? throw new DomainException("Paket manifesti okunamadı.", 422);
            if (data.Format != 1 || !Version.TryParse(data.Version, out _) || data.Runtime is not ("win-x64" or "linux-x64" or "linux-arm64") || data.Schema < 1
                || data.Files is null || data.Files.Length is 0 or > 10000 || data.EntryPoint != (data.Runtime == "win-x64" ? "HomeServer.Api.exe" : "HomeServer.Api"))
                throw new DomainException("Paket sürümü/platformu veya manifesti geçersiz.", 422);
            long total = 0; var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in data.Files)
            {
                var path = DownloadFiles.ValidateRelative(file.Path);
                if (file.Path.Contains('\\') || file.Path.StartsWith(".home-", StringComparison.OrdinalIgnoreCase) || !names.Add(path) || file.Length < 0 || file.Length > MaximumExpanded
                    || file.Sha256 is null || file.Sha256.Length != 64 || file.Sha256.Any(c => !char.IsAsciiHexDigit(c)) || total > MaximumExpanded - file.Length)
                    throw new DomainException("Paket dosya listesi geçersiz veya boyut sınırını aşıyor.", 422);
                total += file.Length;
            }
            if (!data.Files.Any(file => file.Path == data.EntryPoint && file.Executable)) throw new DomainException("Paketin uygulama giriş dosyası eksik.", 422);
            return data;
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or ArgumentException) { throw new DomainException("Paket imzası veya manifesti okunamadı.", 422); }
    }
    public static async Task<PackageManifest> VerifyDirectoryAsync(string directory, string trustedKey, CancellationToken token)
    {
        var manifest = await ReadFileAsync(MediaPaths.ResolveFile(directory, ManifestName), 4 * 1024 * 1024, token);
        var signature = await ReadFileAsync(MediaPaths.ResolveFile(directory, SignatureName), 1024, token);
        var data = Authenticate(manifest, signature, trustedKey);
        foreach (var entry in data.Files)
        {
            var path = MediaPaths.ResolveFile(directory, DownloadFiles.ValidateRelative(entry.Path));
            if (new FileInfo(path).Length != entry.Length || await HashAsync(path, token) != entry.Sha256.ToLowerInvariant()) throw new DomainException("Paket dosyası değiştirilmiş veya eksik: " + entry.Path, 422);
        }
        return data;
    }
    public static async Task<PackageManifest> ExtractAsync(string archivePath, string destination, string trustedKey, CancellationToken token)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        if (archive.Entries.Count > 10002 || archive.Entries.Sum(entry => entry.Length) > MaximumExpanded) throw new DomainException("Güncelleme arşivi sınırı aşıyor.", 413);
        var manifestEntry = archive.GetEntry(ManifestName) ?? throw new DomainException("Paket manifesti bulunamadı.", 422);
        var signatureEntry = archive.GetEntry(SignatureName) ?? throw new DomainException("Paket imzası bulunamadı.", 422);
        var manifest = await ReadEntryAsync(manifestEntry, 4 * 1024 * 1024, token); var signature = await ReadEntryAsync(signatureEntry, 1024, token);
        var data = Authenticate(manifest, signature, trustedKey);
        if (archive.Entries.Count != data.Files.Length + 2) throw new DomainException("Paket manifest dışı girdi içeriyor.", 422);
        var allowed = data.Files.ToDictionary(file => file.Path, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            if (!seen.Add(entry.FullName) || ((entry.ExternalAttributes >> 16) & 0xf000) == 0xa000) throw new DomainException("Paket yinelenen yol veya dosya bağlantısı içeriyor.", 422);
            if (entry.FullName is ManifestName or SignatureName) continue;
            if (!allowed.TryGetValue(entry.FullName, out var item) || entry.Length != item.Length) throw new DomainException("Paket manifesti ile arşiv uyuşmuyor.", 422);
            var relative = DownloadFiles.ValidateRelative(item.Path); var parent = Path.GetDirectoryName(relative);
            if (!string.IsNullOrEmpty(parent)) DownloadFiles.EnsureDirectory(destination, parent);
            var path = Path.Combine(destination, relative);
            await using (var source = entry.Open())
            await using (var target = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.Asynchronous)) await CopyAsync(source, target, item.Length, token);
            if (new FileInfo(path).Length != item.Length || await HashAsync(path, token) != item.Sha256.ToLowerInvariant()) throw new DomainException("Paket dosya hash'i eşleşmedi.", 422);
            if (!OperatingSystem.IsWindows() && item.Executable) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
        await File.WriteAllBytesAsync(Path.Combine(destination, ManifestName), manifest, token); await File.WriteAllBytesAsync(Path.Combine(destination, SignatureName), signature, token);
        return data;
    }
    public static async Task<string> HashAsync(string path, CancellationToken token)
    {
        await using var stream = File.OpenRead(path); return Convert.ToHexString(await SHA256.HashDataAsync(stream, token)).ToLowerInvariant();
    }
    public static async Task CopyAsync(Stream source, Stream target, long maximum, CancellationToken token)
    {
        var buffer = new byte[128 * 1024]; long total = 0; int read;
        while ((read = await source.ReadAsync(buffer, token)) > 0)
        {
            total += read; if (total > maximum) throw new DomainException("Paket boyut sınırını aşıyor.", 413);
            await target.WriteAsync(buffer.AsMemory(0, read), token);
        }
        await target.FlushAsync(token);
    }
    private static async Task<byte[]> ReadFileAsync(string path, int maximum, CancellationToken token)
    {
        await using var file = File.OpenRead(path); using var result = new MemoryStream(); await CopyAsync(file, result, maximum, token); return result.ToArray();
    }
    private static async Task<byte[]> ReadEntryAsync(ZipArchiveEntry entry, int maximum, CancellationToken token)
    {
        await using var stream = entry.Open(); using var result = new MemoryStream(); await CopyAsync(stream, result, maximum, token); return result.ToArray();
    }
    public static void AtomicJson<T>(string path, T value)
    {
        if (File.Exists(path)) AppFiles.RejectLink(path);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp"; File.WriteAllText(temporary, JsonSerializer.Serialize(value)); File.Move(temporary, path, true);
    }
}
