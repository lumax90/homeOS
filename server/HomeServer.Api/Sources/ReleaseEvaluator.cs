using System.Globalization;
using System.Text.RegularExpressions;
using HomeServer.Core;
using HomeServer.Metadata;

namespace HomeServer.Sources;

public static partial class ReleaseEvaluator
{
    public static ReleaseEvaluation Evaluate(RawRelease release, ProviderContent target, QualityRules rules, int? season = null, int? episode = null, ReleaseEpisodeIdentity? identity = null)
    {
        var parsed = Parse(release);
        var rejected = IdentityRejections(release, target, season, episode, identity).ToList();
        var notes = new List<string>();
        var title = Normalize(release.Title);
        if (parsed.Resolution is null && !rules.AllowUnknownResolution) rejected.Add("Çözünürlük belirlenemedi.");
        if (parsed.Resolution is not null && !rules.Resolutions.Contains(parsed.Resolution.Value)) rejected.Add("Çözünürlük kalite profilinde yok.");
        if (release.SizeBytes is null or <= 0) rejected.Add("Dosya boyutu bilinmiyor.");
        else
        {
            if (release.SizeBytes > rules.MaxSizeGiB * 1024 * 1024 * 1024) rejected.Add("Profilin dosya boyutu sınırı aşılıyor.");
            if (release.SizeBytes < rules.MinSizeGiB * 1024 * 1024 * 1024) rejected.Add("Dosya boyutu profilin alt sınırının altında.");
        }
        if (release.Protocol == "torrent" && rules.MinSeeders > 0 && (release.Seeders is null || release.Seeders < rules.MinSeeders))
            rejected.Add("Yeterli doğrulanmış seeder yok.");
        if (parsed.Source is "cam" or "telesync") rejected.Add("Kamera kaydı kaynak profiline uygun değil.");
        if (rules.Sources is { Length: > 0 } && !rules.Sources.Contains(parsed.Source, StringComparer.OrdinalIgnoreCase))
            rejected.Add("Yayın kaynağı kalite profilinde yok.");
        if (rules.Languages is { Length: > 0 } && !rules.Languages.Any(language => parsed.Languages.Contains(Language(language))))
            rejected.Add("İstenen ses dili doğrulanamadı.");
        if (rules.Protocols is { Length: > 0 } && !rules.Protocols.Contains(release.Protocol)) rejected.Add("Aktarım protokolü profilde yok.");
        if (rules.MaximumAgeDays > 0 && (!DateTimeOffset.TryParse(release.PublishedAt, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var published) || published < DateTimeOffset.UtcNow.AddDays(-rules.MaximumAgeDays)))
            rejected.Add("Sürüm tarihi retention sınırının dışında veya bilinmiyor.");
        foreach (var term in rules.RequiredTerms) if (!ContainsTerm(title, Normalize(term))) rejected.Add($"Gerekli ifade bulunamadı: {term}");
        foreach (var term in rules.ForbiddenTerms) if (ContainsTerm(title, Normalize(term))) rejected.Add($"İstenmeyen ifade bulundu: {term}");
        if (release.Attributes.TryGetValue("password", out var passwords) && passwords.Any(p => p is not ("0" or "false" or "no" or "")))
            rejected.Add("Kaynak parola veya iç arşiv bildiriyor.");
        var custom = CustomScore(release, parsed, rules, rejected, notes);
        var qualityScore = BaseScore(release, parsed, rules) + custom;
        var score = qualityScore + (release.Protocol == rules.PreferredProtocol ? 10 : 0);
        if (release.Seeders is > 0) score += (int)Math.Min(20, Math.Log2((double)release.Seeders.Value + 1));
        notes.Add(parsed.Resolution is null ? "Çözünürlük bilinmiyor." : $"{parsed.Resolution}p · {parsed.Source}");
        if (parsed.Proper) notes.Add("PROPER/REPACK sürümü.");
        if (parsed.MultipleEpisodes) notes.Add("Dosya adı birden fazla bölüm bildiriyor.");
        if (parsed.Languages.Length > 0) notes.Add("Bildirilen dil: " + string.Join(", ", parsed.Languages));
        return new ReleaseEvaluation(rejected.Count == 0, score, rejected.Distinct().ToArray(), notes.ToArray(), parsed, qualityScore, custom);
    }

    public static string[] IdentityRejections(RawRelease release, ProviderContent target, int? season = null, int? episode = null, ReleaseEpisodeIdentity? identity = null)
    {
        var rejected = new List<string>();
        var parsed = Parse(release);
        if (parsed.Ambiguous) rejected.Add("Dosyanın numaralandırması belirsiz veya bölüm sınırını aşıyor.");
        var ids = new (string Key, string? Value)[]
        {
            ("imdb", target.ImdbId), ("imdbid", target.ImdbId),
            ("tmdbid", target.Provider == "tmdb" ? target.ProviderId.ToString(CultureInfo.InvariantCulture) : null),
            ("tvmazeid", target.Provider == "tvmaze" ? target.ProviderId.ToString(CultureInfo.InvariantCulture) : null),
            ("tvdbid", target.TvdbId?.ToString(CultureInfo.InvariantCulture))
        };
        var matchingId = false;
        foreach (var (key, expected) in ids)
        {
            var alternatives = (target.Aliases ?? []).Where(alias => key == alias.Provider + "id").Select(alias => alias.ProviderId.ToString(CultureInfo.InvariantCulture)).ToList();
            if (!string.IsNullOrWhiteSpace(expected)) alternatives.Add(expected);
            if (alternatives.Count == 0 || !release.Attributes.TryGetValue(key, out var values)) continue;
            var supplied = values.Where(v => !string.IsNullOrWhiteSpace(v) && v != "0").Select(Id).ToArray();
            if (supplied.Length == 0) continue;
            if (supplied.Any(value => !alternatives.Any(expectedId => value == Id(expectedId)))) rejected.Add("Kaynağın içerik kimliği istenen yapımla çelişiyor.");
            else matchingId = true;
        }
        if (!matchingId && !TitleMatches(release.Title, target)) rejected.Add("Başlık istenen yapımla eşleşmiyor.");
        var year = target.ReleaseDate?.Length >= 4 && int.TryParse(target.ReleaseDate[..4], out var y) ? y : (int?)null;
        if (target.Kind == "movie")
        {
            if (parsed.Season is not null) rejected.Add("Film yerine dizi sürümü döndü.");
            if (year is not null && parsed.Year is not null && parsed.Year != year) rejected.Add("Yapım yılı farklı.");
            if (year is not null && parsed.Year is null && !matchingId) rejected.Add("Yapım yılı doğrulanamadı.");
        }
        if (identity?.Mode == "daily")
        {
            if (string.IsNullOrEmpty(identity.AirDate) || parsed.AirDate != identity.AirDate) rejected.Add("Günlük bölümün yayın tarihi eşleşmiyor.");
        }
        else if (identity?.Mode == "absolute")
        {
            if (identity.AbsoluteNumber is null || parsed.AbsoluteNumber is null || identity.AbsoluteNumber < parsed.AbsoluteNumber || identity.AbsoluteNumber > (parsed.EndAbsoluteNumber ?? parsed.AbsoluteNumber)) rejected.Add("Mutlak bölüm numarası eşleşmiyor.");
        }
        else
        {
            if (identity?.Mode == "alternative") { season = identity.SourceSeason; episode = identity.SourceEpisode; if (season is null || episode is null) rejected.Add("Alternatif bölüm karşılığı bilinmiyor."); }
            if (season is not null && parsed.Season != season) rejected.Add("Sezon numarası eşleşmiyor veya bilinmiyor.");
            if (episode is not null && !(parsed.EpisodeNumbers ?? (parsed.Episode is { } n ? [n] : [])).Contains(episode.Value))
                rejected.Add(parsed.SeasonPack ? "Tek bölüm isteği için sezon paketi döndü." : "Bölüm kapsamı eşleşmiyor veya bilinmiyor.");
        }
        return rejected.Distinct().ToArray();
    }

    public static bool TitleMatches(string raw, ProviderContent target)
    {
        var title = Normalize(FilenameHints.AnalyzeName(raw).Query);
        var expected = new[] { target.Title, target.OriginalTitle }.Concat((target.Aliases ?? []).SelectMany(alias => new[] { alias.Title ?? "", alias.OriginalTitle ?? "" })).Where(value => !string.IsNullOrWhiteSpace(value)).Select(Normalize);
        return expected.Any(value => title == value || title.StartsWith(value + " ", StringComparison.Ordinal)
            && EditionSuffix().IsMatch(title[(value.Length + 1)..]));
    }

    public static ParsedRelease Parse(RawRelease release)
    {
        var raw = release.Title.Replace('_', '.');
        var hint = FilenameHints.AnalyzeName(release.Title);
        var text = Normalize(raw);
        var quality = Resolution().Match(raw);
        var ep = Episode().Match(raw);
        var marker = ReleaseMarker().Match(raw);
        var identityEnd = new[] { quality.Success ? quality.Index : raw.Length, ep.Success ? ep.Index : raw.Length, marker.Success ? marker.Index : raw.Length }.Min();
        var yearMatch = Year().Matches(raw[..identityEnd]).Cast<Match>().LastOrDefault() ?? Match.Empty;
        int? year = yearMatch.Success ? int.Parse(yearMatch.Value, CultureInfo.InvariantCulture) : null;
        int? season = hint.SeasonNumber ?? AttributeInt(release, "season");
        int? episode = hint.EpisodeNumber ?? AttributeInt(release, "episode");
        var numbers = hint.EpisodeNumbers is { Length: > 0 } ? hint.EpisodeNumbers : episode is { } single ? [single] : Array.Empty<int>();
        var multiple = numbers.Length > 1;
        int? resolution = quality.Success ? int.Parse(quality.Groups[1].Value, CultureInfo.InvariantCulture) : ContainsTerm(text, "4k") || ContainsTerm(text, "uhd") ? 2160 : null;
        if (resolution is null && release.Attributes.TryGetValue("resolution", out var values))
        {
            var match = Resolution().Match(string.Join(' ', values));
            if (match.Success) resolution = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        }
        var tailStart = ep.Success ? ep.Index + ep.Length : yearMatch.Success ? yearMatch.Index + yearMatch.Length : marker.Success ? marker.Index : raw.Length;
        var tail = Normalize(raw[tailStart..]);
        var source = ContainsTerm(tail, "remux") ? "remux" : ContainsTerm(tail, "bluray") || ContainsTerm(tail, "blu ray") ? "bluray"
            : ContainsTerm(tail, "web dl") || ContainsTerm(tail, "webdl") ? "web-dl" : ContainsTerm(tail, "webrip") || ContainsTerm(tail, "web rip") ? "webrip"
            : ContainsTerm(tail, "hdtv") ? "hdtv" : ContainsTerm(tail, "hdcam") || ContainsTerm(tail, "cam") ? "cam"
            : ContainsTerm(tail, "telesync") || ContainsTerm(tail, "hdts") || ContainsTerm(tail, "ts") ? "telesync" : "unknown";
        var codec = Codec().Match(raw);
        var languages = release.Attributes.TryGetValue("language", out var language) ? language.Select(Language).ToList() : [];
        foreach (var term in new[] { "turkish", "tur", "english", "eng", "german", "deu", "german", "french", "fra", "japanese", "jpn" })
            if (ContainsTerm(tail, term)) languages.Add(Language(term));
        var proper = ProperMarker().Match(tail);
        var version = proper.Success ? proper.Groups[1].Success ? int.Parse(proper.Groups[1].Value, CultureInfo.InvariantCulture) : 1 : 0;
        var hdr = ContainsTerm(tail, "dolby vision") || ContainsTerm(tail, "dovi") || ContainsTerm(tail, "dv") ? "dolbyvision"
            : ContainsTerm(tail, "hlg") ? "hlg" : ContainsTerm(tail, "hdr") || ContainsTerm(tail, "hdr10") || ContainsTerm(tail, "hdr10+") ? "hdr10" : null;
        return new ParsedRelease(year, season, episode, season is not null && episode is null, resolution, source,
            codec.Success ? codec.Value.ToLowerInvariant() : null, proper.Success, languages.Distinct().ToArray(), multiple, hdr, version,
            numbers, hint.AirDate, hint.AbsoluteNumber, hint.EndAbsoluteNumber, hint.PartNumber, hint.Ambiguous);
    }

    public static int? ResolutionFor(int? width, int? height) => width >= 3800 || height >= 2100 ? 2160
        : width >= 1900 || height >= 1060 ? 1080 : width >= 1260 || height >= 700 ? 720 : height >= 570 ? 576 : height >= 450 ? 480 : null;
    public static string Language(string value) => value.Trim().ToLowerInvariant() switch
    {
        "tur" or "turkish" => "tr", "eng" or "english" => "en", "ger" or "deu" or "german" => "de",
        "fra" or "fre" or "french" => "fr", "jpn" or "japanese" => "ja", "spa" or "spanish" => "es", var other => other
    };
    private static string Id(string value) => value.Trim().StartsWith("tt", StringComparison.OrdinalIgnoreCase) ? value.Trim()[2..] : value.Trim();
    private static int? AttributeInt(RawRelease r, string key) => r.Attributes.TryGetValue(key, out var values) && int.TryParse(values.FirstOrDefault(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;
    private static string Normalize(string value) => NonWords().Replace(MediaPaths.SearchKey(value), " ").Trim();
    private static bool ContainsTerm(string text, string term) => term.Length > 0 && (" " + text + " ").Contains(" " + term + " ", StringComparison.Ordinal);
    [GeneratedRegex(@"(?<!\d)(?:19|20)\d{2}(?!\d)")] private static partial Regex Year();
    [GeneratedRegex(@"(?i)(?<![a-z0-9])(?:S(?<season>\d{1,3})(?:[ ._-]*E(?<episode>\d{1,4})(?<more>(?:[ ._-]*E|[-])\d{1,4})?)?|(?<season>\d{1,3})x(?<episode>\d{1,4})(?<more>-\d{1,4})?)(?!\d)")] private static partial Regex Episode();
    [GeneratedRegex(@"(?i)(?<!\d)(2160|1080|720|576|480)[pi](?!\d)")] private static partial Regex Resolution();
    [GeneratedRegex(@"(?i)\b(?:x26[45]|h[ .]?26[45]|hevc|av1)\b")] private static partial Regex Codec();
    [GeneratedRegex(@"\b(?:proper|repack|rerip)(?:\s*v?([0-9]{1,2}))?\b", RegexOptions.NonBacktracking)] private static partial Regex ProperMarker();
    [GeneratedRegex(@"[^\p{L}\p{Nd}]+", RegexOptions.NonBacktracking)] private static partial Regex NonWords();
    [GeneratedRegex(@"(?i)(?:^|[ .\[(-])(?:2160p|1080p|720p|576p|480p|web[ .-]?dl|webrip|blu[ .-]?ray|hdtv|remux|hdcam|telesync|x26[45]|h26[45])(?:$|[ .\])-])")] private static partial Regex ReleaseMarker();
    [GeneratedRegex(@"^\[[^\]]{1,80}\][ ._-]*")] private static partial Regex LeadingGroup();
    [GeneratedRegex(@"^(?:(?:extended|edition|directors|director|s|cut|theatrical|unrated|remastered|uncut|anniversary) ?)+$", RegexOptions.NonBacktracking)] private static partial Regex EditionSuffix();
}
