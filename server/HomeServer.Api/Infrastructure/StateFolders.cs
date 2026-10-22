using System.Security.Cryptography;
using System.Text.Json;
using HomeServer.Core;

namespace HomeServer.Infrastructure;

public static class StateFolders
{
    public static string? ActiveId(string data)
    {
        var file = Path.Combine(data, "active-state.json");
        if (!File.Exists(file)) return null;
        AppFiles.RejectLink(file);
        return AppFiles.Id(JsonSerializer.Deserialize<RestorePlan>(File.ReadAllText(file))?.Id ?? "");
    }
    public static string Current(string data)
    {
        var id = ActiveId(data);
        if (id is null) return data;
        var path = Path.Combine(data, "states", id);
        if (!Directory.Exists(path) || !File.Exists(Path.Combine(path, "home.db"))) throw new InvalidOperationException("Etkin uygulama state'i bulunamadı.");
        AppFiles.RejectLink(Path.Combine(data, "states")); AppFiles.RejectLink(path);
        return path;
    }
    public static void ActivatePending(string data)
    {
        var file = Path.Combine(data, "pending-restore.json");
        if (!File.Exists(file)) return;
        AppFiles.RejectLink(file);
        var plan = JsonSerializer.Deserialize<RestorePlan>(File.ReadAllText(file)) ?? throw new InvalidOperationException("Geri yükleme planı okunamadı.");
        var current = ActiveId(data);
        if (current == plan.Id) { File.Delete(file); return; }
        if (current != plan.PreviousState) throw new InvalidOperationException("Geri yükleme hazırlanırken etkin state değişti.");
        var root = Path.Combine(data, "states", AppFiles.Id(plan.Id));
        var manifestPath = MediaPaths.ResolveFile(root, ".home-state.json");
        if (Hash(manifestPath) != plan.ManifestHash) throw new InvalidOperationException("Hazırlanan geri yükleme manifesti değişti.");
        var manifest = JsonSerializer.Deserialize<BackupManifest>(File.ReadAllText(manifestPath)) ?? throw new InvalidOperationException("Geri yükleme manifesti geçersiz.");
        foreach (var item in manifest.Files)
        {
            var path = MediaPaths.ResolveFile(root, item.Path.Replace('/', Path.DirectorySeparatorChar));
            if (new FileInfo(path).Length != item.Length || Hash(path) != item.Sha256) throw new InvalidOperationException("Hazırlanan geri yükleme dosyası doğrulanamadı.");
        }
        var temporary = Path.Combine(data, "active-state.new.json");
        File.WriteAllText(temporary, JsonSerializer.Serialize(plan));
        File.Move(temporary, Path.Combine(data, "active-state.json"), true);
        File.Delete(file);
    }
    public static string Hash(string path)
    {
        using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
