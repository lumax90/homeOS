using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using HomeServer.Core;
using MonoTorrent;

namespace HomeServer.Downloads;

public sealed partial class DownloadFiles(HomePaths paths)
{
    public string Root => EnsureDirectory(paths.Data, "downloads");
    public string LibraryRoot => EnsureDirectory(Root, "library");
    public string MusicLibraryRoot => EnsureDirectory(Root, "music-library");
    public string Payload(string id) => EnsureDirectory(EnsureDirectory(Root, "staging"), ValidId(id));
    public string ImportArea => EnsureDirectory(Root, "imports");
    public string EngineCache => EnsureDirectory(Root, "engine");

    public static string ValidateRelative(string value)
    {
        var portable = value.Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(portable) || portable.Length > 2000 || Path.IsPathRooted(value) || portable.StartsWith('/'))
            throw new DomainException("Torrent dosya yolu geçerli değil.");
        var segments = portable.Split('/');
        foreach (var segment in segments)
        {
            if (segment.Length is 0 or > 240 || segment is "." or ".." || segment.EndsWith(' ') || segment.EndsWith('.')
                || segment.Any(c => c < 32 || "<>:\"|?*".Contains(c)) || DeviceName().IsMatch(segment))
                throw new DomainException("Torrent güvenli olmayan veya desteklenmeyen bir dosya yolu içeriyor.");
        }
        return Path.Combine(segments);
    }

    public static void ValidateTorrent(Torrent torrent)
    {
        if (torrent.Files.Count is 0 or > 20000 || torrent.Size <= 0) throw new DomainException("Torrent dosya listesi boş veya çok büyük.");
        _ = ValidateRelative(torrent.Name);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in torrent.Files)
        {
            var name = ValidateRelative(file.Path);
            if (!names.Add(name)) throw new DomainException("Torrent aynı hedefe yazan birden fazla dosya içeriyor.");
        }
        if (!torrent.Files.Any(f => MediaPaths.IsMedia(f.Path))) throw new DomainException("Torrentte desteklenen bir video veya ses dosyası bulunamadı.");
    }

    public static string EnsureDirectory(string root, string relative)
    {
        _ = ValidateRelative(relative);
        Directory.CreateDirectory(root);
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) throw new DomainException("İndirme alanı bir dosya sistemi bağlantısı olamaz.");
        var current = root;
        foreach (var segment in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            current = Path.Combine(current, segment);
            Directory.CreateDirectory(current);
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new DomainException("İndirme alanında dosya sistemi bağlantısı bulundu.");
        }
        return Path.GetFullPath(current);
    }

    public static string ValidId(string id) => Guid.TryParseExact(id, "N", out _) ? id : throw new DomainException("Geçersiz indirme kimliği.");

    public static bool TryHardLink(string source, string destination)
    {
        if (OperatingSystem.IsWindows()) return CreateHardLink(destination, source, IntPtr.Zero);
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()) return Link(source, destination) == 0;
        return false;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateHardLinkW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLink(string newFile, string existingFile, IntPtr attributes);
    [DllImport("libc", EntryPoint = "link", SetLastError = true)]
    private static extern int Link(string existingFile, string newFile);
    [GeneratedRegex(@"(?i)^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)")]
    private static partial Regex DeviceName();
}
