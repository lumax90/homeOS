using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using HomeServer.Core;

namespace HomeServer.Metadata;

public static partial class LocalMetadataReader
{
    public static async Task<LocalMediaMetadata?> ReadAsync(Library library, SourceFile media, CancellationToken token)
    {
        if (media.Kind != "video") return null;
        var relative = Path.ChangeExtension(media.RelativePath, ".nfo");
        var directory = Path.GetDirectoryName(media.RelativePath) ?? "";
        if (!File.Exists(Path.Combine(library.Path, relative)))
        {
            var movie = Path.Combine(directory, "movie.nfo");
            relative = library.Kind == "movies" && File.Exists(Path.Combine(library.Path, movie)) ? movie : relative;
        }
        LocalMediaMetadata? metadata = null;
        if (File.Exists(Path.Combine(library.Path, relative)))
        {
            var path = MediaPaths.ResolveFile(library.Path, relative);
            await using var source = File.OpenRead(path);
            var bytes = await MetadataProvider.ReadLimitedAsync(source, 1024 * 1024, token);
            metadata = Parse(bytes, relative);
        }
        var poster = LocalImage(library.Path, directory, metadata?.Poster, [Path.GetFileNameWithoutExtension(media.RelativePath) + "-poster.jpg", "poster.jpg", "folder.jpg", "poster.png"]);
        var backdrop = LocalImage(library.Path, directory, metadata?.Backdrop, ["fanart.jpg", "backdrop.jpg", "fanart.png"]);
        if (metadata is null && poster is null && backdrop is null) return null;
        return (metadata ?? new LocalMediaMetadata(null, null, null, null, null, null, null, null, null, null, [], "yerel görsel")) with { Poster = poster, Backdrop = backdrop };
    }
    public static LocalMediaMetadata Parse(byte[] bytes, string sourceName)
    {
        try
        {
            using var stream = new MemoryStream(bytes); using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1024 * 1024 });
            var root = XDocument.Load(reader).Root ?? throw new DomainException("NFO içeriği boş.");
            if (root.Name.LocalName is not ("movie" or "episodedetails" or "tvshow")) throw new DomainException("NFO türü desteklenmiyor.");
            string? Text(string key, int max = 1000) => root.Element(key)?.Value.Trim() is { Length: > 0 } text ? text[..Math.Min(text.Length, max)] : null;
            int? Number(string key) => int.TryParse(Text(key), out var value) && value >= 0 ? value : null;
            var ids = root.Elements("uniqueid").Where(value => (string?)value.Attribute("type") == "tmdb").Select(value => value.Value.Trim()).Distinct().ToArray();
            if (ids.Length > 1) throw new DomainException("NFO çelişen sağlayıcı kimliği içeriyor.");
            var tmdb = root.Name.LocalName == "episodedetails" ? null : Number("tmdbid") ?? (int.TryParse(ids.FirstOrDefault(), out var parsed) && parsed > 0 ? parsed : (int?)null);
            var imdb = Text("imdbid") ?? root.Elements("uniqueid").FirstOrDefault(value => (string?)value.Attribute("type") == "imdb")?.Value.Trim();
            if (imdb is not null && !Imdb().IsMatch(imdb)) imdb = null;
            var poster = root.Elements("thumb").FirstOrDefault(value => (string?)value.Attribute("aspect") is null or "poster")?.Value.Trim() ?? root.Element("art")?.Element("poster")?.Value.Trim();
            var backdrop = root.Element("fanart")?.Element("thumb")?.Value.Trim() ?? root.Element("art")?.Element("fanart")?.Value.Trim();
            var year = Number("year"); if (year is < 1800 or > 2200) year = null;
            var season = Number("season"); var episode = Number("episode");
            if (season > 1000 || episode > 10000 || episode == 0) throw new DomainException("NFO bölüm numarası geçersiz.");
            return new(Text("title", 500) ?? Text("name", 500), Text("plot", 20000), year, Text("showtitle", 500), season, episode,
                tmdb, imdb, poster, backdrop, root.Elements("tag").Select(value => value.Value.Trim()).Where(value => value.Length is > 0 and <= 40).Take(20).ToArray(), sourceName, root.Name.LocalName == "movie" ? "movie" : "show");
        }
        catch (XmlException) { throw new DomainException("NFO XML belgesi okunamadı."); }
    }
    private static string? LocalImage(string root, string directory, string? candidate, string[] fallback)
    {
        foreach (var relativeName in new[] { candidate }.Concat(fallback))
        {
            if (string.IsNullOrWhiteSpace(relativeName) || relativeName.Contains("://", StringComparison.Ordinal) || Path.IsPathRooted(relativeName)) continue;
            var relative = Path.Combine(directory, relativeName.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(Path.Combine(root, relative))) continue;
            var path = MediaPaths.ResolveFile(root, relative);
            if (ImageMime(path) is not null) return Path.GetRelativePath(root, path);
        }
        return null;
    }
    public static string? ImageMime(string path)
    {
        if (new FileInfo(path).Length > 10 * 1024 * 1024) return null;
        using var stream = File.OpenRead(path); Span<byte> header = stackalloc byte[12]; var count = stream.Read(header);
        if (count >= 3 && header[0] == 255 && header[1] == 216 && header[2] == 255) return "image/jpeg";
        if (count >= 8 && header[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return "image/png";
        if (count == 12 && header[..4].SequenceEqual("RIFF"u8) && header[8..].SequenceEqual("WEBP"u8)) return "image/webp";
        return null;
    }
    [GeneratedRegex(@"^tt[0-9]{5,12}$", RegexOptions.NonBacktracking)] private static partial Regex Imdb();
}
