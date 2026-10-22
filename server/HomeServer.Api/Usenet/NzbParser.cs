using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using HomeServer.Core;
using HomeServer.Downloads;

namespace HomeServer.Usenet;

public static partial class NzbParser
{
    public const int MaximumBytes = 8 * 1024 * 1024;
    public static NzbManifest Parse(byte[] bytes, string? displayName = null)
    {
        if (bytes.Length is 0 or > MaximumBytes) throw new DomainException("NZB dosyası boş veya 8 MiB sınırını aşıyor.");
        try
        {
            using var stream = new MemoryStream(bytes);
            using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null, MaxCharactersInDocument = MaximumBytes * 2L });
            var root = XDocument.Load(reader).Root;
            if (root?.Name.LocalName != "nzb") throw new DomainException("Geçerli bir NZB dosyası seç.");
            var ns = root.Name.Namespace;
            var head = root.Element(ns + "head");
            string? Meta(string type) => head?.Elements(ns + "meta").FirstOrDefault(value => (string?)value.Attribute("type") == type)?.Value;
            var files = new List<NzbFile>(); var count = 0;
            foreach (var file in root.Elements(ns + "file"))
            {
                if (files.Count >= 20000) throw new DomainException("NZB dosya sayısı sınırını aşıyor.");
                var subject = (string?)file.Attribute("subject") ?? "";
                var name = Filename().Match(subject) is { Success: true } match ? match.Groups[1].Value : $"article-{files.Count + 1}";
                name = SafeName(name);
                var groups = file.Element(ns + "groups")?.Elements(ns + "group").Select(value => value.Value.Trim()).Take(20).ToArray() ?? [];
                if (groups.Any(group => group.Length is 0 or > 200 || group.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('.' or '-' or '_' or '+')))) throw new DomainException("NZB grup adı geçersiz.");
                var segments = new List<NzbSegment>();
                foreach (var segment in file.Element(ns + "segments")?.Elements(ns + "segment") ?? [])
                {
                    if (++count > 200000) throw new DomainException("NZB 200.000 parça sınırını aşıyor.");
                    if (!int.TryParse((string?)segment.Attribute("number"), NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number is < 1 or > 1000000
                        || !long.TryParse((string?)segment.Attribute("bytes"), NumberStyles.None, CultureInfo.InvariantCulture, out var length) || length is < 1 or > 16 * 1024 * 1024)
                        throw new DomainException("NZB parça numarası veya boyutu geçersiz.");
                    var message = segment.Value.Trim();
                    if (message.StartsWith('<') && message.EndsWith('>')) message = message[1..^1];
                    ValidateMessageId(message);
                    segments.Add(new NzbSegment(number, length, message));
                }
                if (segments.Count == 0 || segments.Select(s => s.Number).Distinct().Count() != segments.Count || segments.Select(s => s.MessageId).Distinct().Count() != segments.Count)
                    throw new DomainException("NZB boş veya yinelenen parça içeriyor.");
                files.Add(new NzbFile(files.Count, name, groups, segments.OrderBy(segment => segment.Number).ToArray()));
            }
            if (files.Count == 0) throw new DomainException("NZB dosya listesi boş.");
            var canonical = string.Join('\n', files.Select(file => string.Join(',', file.Segments.Select(segment => $"{segment.Number}:{segment.MessageId}"))).Order(StringComparer.Ordinal));
            var fingerprint = "nzb:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
            var title = displayName ?? Meta("name") ?? files[0].Name;
            if (string.IsNullOrWhiteSpace(title)) title = "Usenet paketi";
            title = new string(title.Where(c => !char.IsControl(c)).Take(200).ToArray());
            var password = Meta("password"); if (password?.Length > 1024) throw new DomainException("NZB arşiv parolası çok uzun.");
            return new NzbManifest(title, fingerprint, password, files.ToArray());
        }
        catch (XmlException) { throw new DomainException("NZB XML belgesi okunamadı."); }
    }
    public static string SafeName(string name)
    {
        var relative = DownloadFiles.ValidateRelative(name);
        if (relative.Contains(Path.DirectorySeparatorChar) || relative.StartsWith(".home", StringComparison.OrdinalIgnoreCase)) throw new DomainException("Usenet dosya adı güvenli değil.");
        return relative;
    }
    public static void ValidateMessageId(string value)
    {
        if (value.Length is < 3 or > 480 || !value.Contains('@') || value.Any(c => c is <= ' ' or >= '\u007f' or '<' or '>')) throw new DomainException("NZB mesaj kimliği geçersiz.");
    }
    [GeneratedRegex("\"([^\"]+)\"", RegexOptions.NonBacktracking)] private static partial Regex Filename();
}
