using HomeServer.Core;
using HomeServer.Downloads;
using HomeServer.Infrastructure;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace HomeServer.Usenet;

public sealed class PackageExtractor
{
    public async Task ExtractAsync(string inputDirectory, string outputDirectory, string? password, long maximumBytes, CancellationToken token)
    {
        var all = Directory.GetFiles(inputDirectory).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (var file in all) AppFiles.RejectLink(file);
        var consumed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long total = 0; var entries = 0;
        foreach (var path in all)
        {
            token.ThrowIfCancellationRequested(); if (consumed.Contains(path)) continue;
            var name = Path.GetFileName(path);
            if (name.EndsWith(".par2", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".sfv", StringComparison.OrdinalIgnoreCase)) continue;
            if (!IsArchiveStart(name))
            {
                if (MediaPaths.IsMedia(name) || Path.GetExtension(name).ToLowerInvariant() is ".srt" or ".vtt" or ".ass" or ".ssa" or ".nfo" or ".jpg" or ".png" or ".lrc")
                {
                    var size = new FileInfo(path).Length; Reserve(name, size);
                    await CopyAsync(File.OpenRead(path), Path.Combine(outputDirectory, name), size, token);
                }
                continue;
            }
            var parts = ArchiveFactory.GetFileParts(path).ToArray();
            foreach (var part in parts)
            {
                if (!all.Contains(part, StringComparer.OrdinalIgnoreCase)) throw new DomainException("Arşiv parçası yönetilen paketin dışında.");
                AppFiles.RejectLink(part); consumed.Add(part);
            }
            try
            {
                using var archive = ArchiveFactory.OpenArchive(parts.Select(part => new FileInfo(part)).ToArray(), ReaderOptions.ForFilePath.WithPassword(password));
                if (!archive.IsComplete) throw new DomainException("Arşivin bir veya daha fazla cildi eksik.");
                if (archive.IsSolid || archive.Type == ArchiveType.SevenZip)
                {
                    using var reader = archive.ExtractAllEntries();
                    while (reader.MoveToNextEntry()) await ExtractEntryAsync(reader.Entry, reader.OpenEntryStream);
                }
                else foreach (var entry in archive.Entries) await ExtractEntryAsync(entry, entry.OpenEntryStream);

                async Task ExtractEntryAsync(IEntry entry, Func<Stream> open)
                {
                    token.ThrowIfCancellationRequested();
                    if (++entries > 20000 || entry.LinkTarget is not null || entry.Attrib is { } attributes && (((attributes >> 16) & 0xf000) == 0xa000 || (attributes & 0x400) != 0))
                        throw new DomainException("Arşiv bağlantı veya aşırı sayıda dosya içeriyor.");
                    var relative = DownloadFiles.ValidateRelative((entry.Key ?? "").TrimEnd('/', '\\'));
                    if (relative.Split(Path.DirectorySeparatorChar).Any(segment => segment.StartsWith(".home", StringComparison.OrdinalIgnoreCase))) throw new DomainException("Arşiv korumalı uygulama dosyası içeriyor.");
                    if (entry.IsDirectory) { DownloadFiles.EnsureDirectory(outputDirectory, relative); return; }
                    if (entry.IsEncrypted && string.IsNullOrEmpty(password)) throw new DomainException("Arşiv parolası gerekli. İndirme kartında parolayı kaydedip yeniden dene.", 409);
                    Reserve(relative, entry.Size);
                    var parent = Path.GetDirectoryName(relative); if (!string.IsNullOrEmpty(parent)) DownloadFiles.EnsureDirectory(outputDirectory, parent);
                    var destination = Path.Combine(outputDirectory, relative);
                    var crc = await CopyAsync(open(), destination, entry.Size, token);
                    if (entry.Crc != 0 && unchecked((uint)entry.Crc) != crc) throw new DomainException("Arşiv içindeki dosyanın CRC'si eşleşmedi.");
                }
            }
            catch (Exception ex) when (ex is not (DomainException or OperationCanceledException))
            { throw new DomainException("Arşiv açılamadı: parola yanlış, arşiv parçası eksik veya checksum hatalı. Paket staging alanında korunuyor.", inner: ex); }
        }
        return;
        void Reserve(string relative, long size)
        {
            if (size < 0 || size > maximumBytes || total > maximumBytes - size || !paths.Add(relative)) throw new DomainException("Arşiv boyutu sınırı aşıyor veya aynı hedefe birden fazla dosya yazıyor.");
            total += size;
            if (new DriveInfo(Path.GetPathRoot(outputDirectory)!).AvailableFreeSpace < size + 16 * 1024 * 1024) throw new DomainException("Arşiv açımı için boş alan yetersiz.");
        }
    }
    private static bool IsArchiveStart(string name) => name.EndsWith(".rar", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".7z", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".001", StringComparison.OrdinalIgnoreCase);
    private static async Task<uint> CopyAsync(Stream source, string destination, long expected, CancellationToken token)
    {
        await using (source)
        await using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.Asynchronous))
        {
            var buffer = new byte[128 * 1024]; long size = 0; var crc = new Crc32(); int count;
            while ((count = await source.ReadAsync(buffer, token)) != 0)
            {
                size += count; if (size > expected) throw new DomainException("Arşiv girdisi bildirilen boyutu aşıyor.");
                crc.Append(buffer.AsSpan(0, count)); await output.WriteAsync(buffer.AsMemory(0, count), token);
            }
            if (size != expected) throw new DomainException("Arşiv girdisi kesilmiş.");
            await output.FlushAsync(token); return crc.Value;
        }
    }
}
