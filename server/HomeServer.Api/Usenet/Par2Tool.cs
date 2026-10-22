using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using HomeServer.Core;
using HomeServer.Infrastructure;

namespace HomeServer.Usenet;

public sealed record Par2File(string Name, long Length, string Md5);
public sealed record Par2Set(string Id, string Path, Par2File[] Files);

public sealed class Par2Tool(IConfiguration configuration, MediaStore store)
{
    public string? Executable => ExecutablePath.Find(store.GetSetting("usenet.par2-path") ?? configuration["Home:Par2Path"], "par2");
    public async Task<Par2File[]> RepairAsync(string root, long maximumBytes, CancellationToken token)
    {
        var files = Directory.GetFiles(root, "*", SearchOption.TopDirectoryOnly).Where(path => path.EndsWith(".par2", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (files.Length == 0) return [];
        var sets = new Dictionary<string, (string Path, Dictionary<string, Par2File> Files)>();
        foreach (var path in files)
        {
            AppFiles.RejectLink(path);
            foreach (var set in await InspectAsync(path, maximumBytes, token))
            {
                if (!sets.TryGetValue(set.Id, out var entry)) sets[set.Id] = entry = (path, new Dictionary<string, Par2File>(StringComparer.OrdinalIgnoreCase));
                foreach (var file in set.Files)
                {
                    if (entry.Files.TryGetValue(file.Name, out var prior) && prior != file) throw new DomainException("PAR2 paketleri çelişen dosya kimlikleri içeriyor.");
                    entry.Files[file.Name] = file;
                }
            }
        }
        if (sets.Count is 0 or > 16) throw new DomainException("PAR2 recovery set sayısı geçersiz.");
        var descriptions = sets.Values.SelectMany(set => set.Files.Values).Distinct().ToArray();
        if (descriptions.Length > 20000 || descriptions.Sum(file => file.Length) > maximumBytes) throw new DomainException("PAR2 dosya toplamı izin verilen sınırı aşıyor.");
        foreach (var set in sets.Values)
        {
            if (set.Files.Count == 0) throw new DomainException("PAR2 dosya tanımı bulunamadı.");
            if (await ValidateAsync(root, set.Files.Values, token)) continue;
            var executable = Executable ?? throw new DomainException("Eksik/bozuk parçaları onarmak için PAR2 aracı gerekli. Sunucu ayarlarında par2 yolunu bağla.", 409);
            foreach (var file in Directory.EnumerateFiles(root)) AppFiles.RejectLink(file);
            try
            {
                // No purge flag: staging source and recovery files remain available for inspection/retry.
                await MediaProcess.RunAsync(executable, ["repair", "-q", "-m256", "-t2", "-B" + root + Path.DirectorySeparatorChar, "--", set.Path], TimeSpan.FromMinutes(30), token, root);
            }
            catch (InvalidOperationException) { throw new DomainException("PAR2 onarımı tamamlanamadı: recovery blokları eksik veya paket bozuk. Doğrulanmış parçalar korunuyor."); }
            if (!await ValidateAsync(root, set.Files.Values, token)) throw new DomainException("PAR2 onarımından sonra dosya boyutu/hash'i eşleşmedi.");
        }
        return descriptions;
    }
    public static async Task<Par2Set[]> InspectAsync(string path, long maximumBytes, CancellationToken token)
    {
        await using var file = File.OpenRead(path);
        var sets = new Dictionary<string, List<Par2File>>(StringComparer.Ordinal); var header = new byte[64]; var buffer = new byte[128 * 1024]; var packets = 0;
        while (file.Position < file.Length)
        {
            if (++packets > 100000) throw new DomainException("PAR2 paket sayısı sınırı aşıyor.");
            await file.ReadExactlyAsync(header, token);
            if (!header.AsSpan(0, 8).SequenceEqual("PAR2\0PKT"u8)) throw new DomainException("PAR2 paket başlığı geçersiz.");
            var length = BinaryPrimitives.ReadInt64LittleEndian(header.AsSpan(8, 8));
            if (length < 64 || length % 4 != 0 || length - 64 > file.Length - file.Position || length > maximumBytes) throw new DomainException("PAR2 paketi eksik veya boyutu geçersiz.");
            var set = Convert.ToHexString(header.AsSpan(32, 16));
            if (!sets.TryGetValue(set, out var list)) sets[set] = list = [];
            using var digest = IncrementalHash.CreateHash(HashAlgorithmName.MD5); digest.AppendData(header.AsSpan(32));
            var description = header.AsSpan(48, 16).SequenceEqual("PAR 2.0\0FileDesc"u8);
            if (description && length > 4096) throw new DomainException("PAR2 dosya tanımı çok uzun.");
            using var payload = description ? new MemoryStream() : null;
            var remaining = length - 64;
            while (remaining > 0)
            {
                var read = await file.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), token);
                if (read == 0) throw new DomainException("PAR2 paketi kesilmiş.");
                digest.AppendData(buffer, 0, read); payload?.Write(buffer, 0, read); remaining -= read;
            }
            if (!digest.GetHashAndReset().AsSpan().SequenceEqual(header.AsSpan(16, 16))) throw new DomainException("PAR2 paket checksum'ı geçersiz.");
            if (payload is null) continue;
            var data = payload.ToArray();
            if (data.Length < 60) throw new DomainException("PAR2 dosya açıklaması eksik.");
            var nameBytes = data.AsSpan(56); var zero = nameBytes.IndexOf((byte)0);
            if (zero >= 0)
            {
                if (nameBytes[zero..].ContainsAnyExcept((byte)0)) throw new DomainException("PAR2 dosya adı NUL verisi içeriyor.");
                nameBytes = nameBytes[..zero];
            }
            var name = NzbParser.SafeName(new UTF8Encoding(false, true).GetString(nameBytes));
            var size = BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(48, 8));
            if (size < 0 || size > maximumBytes) throw new DomainException("PAR2 dosya boyutu sınırı aşıyor.");
            list.Add(new Par2File(name, size, Convert.ToHexString(data.AsSpan(16, 16)).ToLowerInvariant()));
        }
        return sets.Select(set => new Par2Set(set.Key, path, set.Value.ToArray())).ToArray();
    }
    private static async Task<bool> ValidateAsync(string root, IEnumerable<Par2File> files, CancellationToken token)
    {
        foreach (var file in files)
        {
            var path = Path.Combine(root, file.Name); if (!File.Exists(path)) return false;
            path = MediaPaths.ResolveFile(root, file.Name);
            if (new FileInfo(path).Length != file.Length) return false;
            await using var input = File.OpenRead(path);
            if (Convert.ToHexString(await MD5.HashDataAsync(input, token)).ToLowerInvariant() != file.Md5) return false;
        }
        return true;
    }
}
