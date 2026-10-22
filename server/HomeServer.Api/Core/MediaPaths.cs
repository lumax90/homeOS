using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace HomeServer.Core;

public static partial class MediaPaths
{
    private static readonly Dictionary<string, (string Kind, string Mime)> Formats = new(StringComparer.OrdinalIgnoreCase)
    {
        [".mp4"] = ("video", "video/mp4"), [".m4v"] = ("video", "video/mp4"),
        [".webm"] = ("video", "video/webm"), [".mkv"] = ("video", "video/x-matroska"),
        [".mov"] = ("video", "video/quicktime"), [".avi"] = ("video", "video/x-msvideo"),
        [".ts"] = ("video", "video/mp2t"),
        [".mp3"] = ("audio", "audio/mpeg"), [".m4a"] = ("audio", "audio/mp4"),
        [".m4b"] = ("audio", "audio/mp4"),
        [".flac"] = ("audio", "audio/flac"), [".ogg"] = ("audio", "audio/ogg"),
        [".opus"] = ("audio", "audio/ogg"), [".wav"] = ("audio", "audio/wav"), [".oga"] = ("audio", "audio/ogg"),
        [".aif"] = ("audio", "audio/aiff"), [".aiff"] = ("audio", "audio/aiff")
    };

    public static string NormalizeRoot(string input)
    {
        if (string.IsNullOrWhiteSpace(input) || !Path.IsPathFullyQualified(input))
            throw new DomainException("Sunucuda bulunan klasörün tam yolunu gir.");
        string root;
        try { root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(input.Trim())); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        { throw new DomainException("Bu klasör yolu geçerli değil."); }
        if (!Directory.Exists(root)) throw new DomainException("Klasöre erişilemiyor. Yolun sunucuda bulunduğunu kontrol et.");
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
            throw new DomainException("Kısayol yerine klasörün gerçek yolunu seç.");
        return root;
    }

    public static string Key(string path) => OperatingSystem.IsWindows() ? path.ToUpperInvariant() : path;
    public static bool IsMedia(string path) => Formats.ContainsKey(Path.GetExtension(path));
    public static bool IsVideo(string path) => Formats.TryGetValue(Path.GetExtension(path), out var format) && format.Kind == "video";

    public static string ResolveFile(string root, string relative)
    {
        if (Path.IsPathRooted(relative)) throw new DomainException("Dosya yolu kütüphane sınırları dışında.", 403);
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var full = Path.GetFullPath(Path.Combine(normalizedRoot, relative));
        var prefix = Path.EndsInDirectorySeparator(normalizedRoot) ? normalizedRoot : normalizedRoot + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!full.StartsWith(prefix, comparison)) throw new DomainException("Dosya yolu kütüphane sınırları dışında.", 403);

        var current = full;
        while (true)
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new DomainException("Bağlantı üzerinden medya dosyasına erişilemiyor.", 403);
            if (string.Equals(current, normalizedRoot, comparison)) break;
            current = Path.GetDirectoryName(current) ?? throw new DomainException("Geçersiz dosya yolu.", 403);
        }
        return full;
    }

    public static SourceFile? Inspect(string root, string path)
    {
        if (!Formats.TryGetValue(Path.GetExtension(path), out var format)) return null;
        var relative = Path.GetRelativePath(root, path);
        var safePath = ResolveFile(root, relative);
        var file = new FileInfo(safePath);
        var title = Separator().Replace(Path.GetFileNameWithoutExtension(path), " ").Trim();
        return new SourceFile(relative, title, format.Kind, format.Mime, file.Length,
            new DateTimeOffset(file.LastWriteTimeUtc).ToString("O", CultureInfo.InvariantCulture));
    }

    public static string SearchKey(string text)
    {
        var chars = text.Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray();
        return new string(chars).Normalize(NormalizationForm.FormC).ToLowerInvariant().Replace('ı', 'i');
    }

    [GeneratedRegex(@"[._]+")]
    private static partial Regex Separator();
}
