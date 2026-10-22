using System.Globalization;
using System.Text.RegularExpressions;
using HomeServer.Core;
using Microsoft.Extensions.Caching.Memory;

namespace HomeServer.Sources;

public static partial class ReleaseEvaluator
{
    private static readonly MemoryCache Patterns = new(new MemoryCacheOptions { SizeLimit = 128 });
    public static void ValidatePreferences(QualityRules rules)
    {
        if (rules.CustomFormats?.Length > 20 || rules.MinimumCustomScore is < -20000 or > 20000 || rules.MaximumAgeDays is < 0 or > 100000
            || rules.PreferredProtocol is not ("any" or "torrent" or "usenet") || rules.Protocols?.Length > 2 || rules.Protocols?.Any(value => value is not ("torrent" or "usenet")) == true)
            throw new DomainException("Gelişmiş kalite/protokol değerleri geçersiz.");
        foreach (var format in rules.CustomFormats ?? [])
        {
            if (string.IsNullOrWhiteSpace(format.Name) || format.Name.Length > 80 || format.Score is < -1000 or > 1000 || format.Pattern?.Length > 400
                || format.Codecs?.Any(codec => codec is not ("h264" or "hevc" or "av1" or "vp9")) == true || format.Codecs?.Length > 4
                || format.Sources?.Length > 8 || format.Sources?.Any(source => source is not ("remux" or "bluray" or "web-dl" or "webrip" or "hdtv" or "unknown")) == true
                || format.Protocols?.Length > 2 || format.Protocols?.Any(protocol => protocol is not ("torrent" or "usenet")) == true || format.Hdr is not (null or "" or "sdr" or "hdr10" or "hlg" or "dolbyvision"))
                throw new DomainException("Custom-format koşulları geçersiz.");
            if (!string.IsNullOrEmpty(format.Pattern)) _ = Pattern(format.Pattern);
        }
    }
    public static string CodecName(string? codec) => codec?.Replace(".", "", StringComparison.Ordinal).Replace(" ", "", StringComparison.Ordinal).ToLowerInvariant() switch
    { "x264" or "h264" => "h264", "x265" or "h265" or "hevc" => "hevc", "av1" => "av1", "vp9" => "vp9", _ => "unknown" };
    private static Regex Pattern(string pattern)
    {
        if (Patterns.TryGetValue<Regex>(pattern, out var stored)) return stored!;
        try
        {
            var result = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(25));
            Patterns.Set(pattern, result, new MemoryCacheEntryOptions { Size = 1, SlidingExpiration = TimeSpan.FromHours(1) }); return result;
        }
        catch (ArgumentException) { throw new DomainException("Custom-format regex ifadesi geçerli değil."); }
    }
    private static bool FormatMatches(CustomFormat format, RawRelease release, ParsedRelease parsed)
    {
        var matches = (string.IsNullOrEmpty(format.Pattern) || Pattern(format.Pattern).IsMatch(release.Title))
            && (format.Codecs is not { Length: > 0 } || format.Codecs.Contains(CodecName(parsed.Codec), StringComparer.OrdinalIgnoreCase))
            && (format.Sources is not { Length: > 0 } || format.Sources.Contains(parsed.Source, StringComparer.OrdinalIgnoreCase))
            && (format.Protocols is not { Length: > 0 } || format.Protocols.Contains(release.Protocol, StringComparer.Ordinal))
            && (string.IsNullOrEmpty(format.Hdr) || format.Hdr == (parsed.Hdr ?? "sdr"));
        return format.Negate ? !matches : matches;
    }
    private static int CustomScore(RawRelease release, ParsedRelease parsed, QualityRules rules, List<string> rejected, List<string>? notes = null)
    {
        var score = 0;
        foreach (var format in rules.CustomFormats ?? [])
        {
            try
            {
                if (FormatMatches(format, release, parsed)) { score += format.Score; notes?.Add($"{format.Name}: {format.Score:+#;-#;0}"); }
                else if (format.Required) rejected.Add("Gerekli custom-format eşleşmedi: " + format.Name);
            }
            catch (RegexMatchTimeoutException) { rejected.Add("Custom-format regex süre sınırını aştı: " + format.Name); }
        }
        if (score < rules.MinimumCustomScore) rejected.Add($"Custom-format puanı düşük: {score} / {rules.MinimumCustomScore}.");
        return score;
    }
    public static int PreferenceScore(RawRelease release, QualityRules rules, int? actualResolution = null, string? actualCodec = null)
    {
        var parsed = Parse(release);
        if (actualResolution is not null) parsed = parsed with { Resolution = actualResolution };
        if (actualCodec is not null) parsed = parsed with { Codec = actualCodec };
        var rejected = new List<string>(); var custom = CustomScore(release, parsed, rules, rejected);
        var title = Normalize(release.Title);
        var fits = rejected.Count == 0 && rules.RequiredTerms.All(term => ContainsTerm(title, Normalize(term)))
            && !rules.ForbiddenTerms.Any(term => ContainsTerm(title, Normalize(term)));
        return BaseScore(release, parsed, rules) + custom - (fits ? 0 : 100000);
    }
    private static int BaseScore(RawRelease release, ParsedRelease parsed, QualityRules rules)
    {
        var rank = parsed.Resolution is null ? -1 : Array.IndexOf(rules.Resolutions, parsed.Resolution.Value);
        var score = rank < 0 ? 0 : 1000 - rank * 100;
        score += parsed.Source switch { "remux" => 45, "bluray" => 40, "web-dl" => 35, "webrip" => 25, "hdtv" => 15, _ => 0 };
        foreach (var term in rules.PreferredTerms) if (ContainsTerm(Normalize(release.Title), Normalize(term))) score += 10;
        return score + Math.Max(parsed.ProperVersion, parsed.Proper ? 1 : 0) * 5;
    }
    public static string[] ActualPreferenceRejections(RawRelease evidence, QualityRules rules, ProbeResult probe)
    {
        var video = probe.Streams?.FirstOrDefault(stream => stream.Kind == "video");
        var unknownColor = video is null || video.ColorTransfer is null && (video.ColorPrimaries == "bt2020" || video.PixelFormat?.Contains("10", StringComparison.Ordinal) == true);
        var parsed = Parse(evidence) with { Codec = probe.VideoCodec, Hdr = video?.DolbyVision == true ? "dolbyvision" : video?.ColorTransfer == "smpte2084" ? "hdr10" : video?.ColorTransfer == "arib-std-b67" ? "hlg" : unknownColor ? "unknown" : null };
        var rejected = new List<string>(); _ = CustomScore(evidence, parsed, rules, rejected); return rejected.ToArray();
    }
    public static SourceSeedRules SeedRules(RawRelease release)
    {
        double Number(string key, double fallback) => release.Attributes.TryGetValue(key, out var values) && double.TryParse(values.FirstOrDefault(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number) ? number : fallback;
        var type = release.Attributes.TryGetValue("seedtype", out var types) ? types.FirstOrDefault()?.ToLowerInvariant() : null;
        return new(Math.Clamp(Number("minimumratio", 0), 0, 100), (int)Math.Clamp(Math.Ceiling(Number("minimumseedtime", 0) / 60), 0, 525600),
            Math.Clamp(Number("downloadvolumefactor", 1), 0, 10), Math.Clamp(Number("uploadvolumefactor", 1), 0, 10), type is "ratio" or "seedtime" or "both" ? type : "either");
    }
}
