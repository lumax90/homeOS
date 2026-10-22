using System.Text.RegularExpressions;
using System.Globalization;
using HomeServer.Core;

namespace HomeServer.Metadata;

public static partial class FilenameHints
{
    public static FilenameHint Parse(string filename) => AnalyzeName(Path.GetFileNameWithoutExtension(filename));
    public static FilenameHint AnalyzeName(string input)
    {
        var name = LeadingGroup().Replace(input.Length <= 2400 ? input : input[..2400], "");
        var episodeMatches = Episode().Matches(name); var episode = episodeMatches.Count > 0 ? episodeMatches[0] : Match.Empty;
        var year = Year().Matches(name).Cast<Match>().LastOrDefault() ?? Match.Empty;
        var daily = Daily().Match(name); string? airDate = null; var ambiguous = input.Length > 2400 || episodeMatches.Count > 1;
        if (daily.Success)
        {
            var date = $"{daily.Groups[1].Value}-{daily.Groups[2].Value}-{daily.Groups[3].Value}";
            if (DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) airDate = date; else ambiguous = true;
        }
        var absolute = !episode.Success && !daily.Success ? Absolute().Match(name) : Match.Empty;
        int? absoluteNumber = absolute.Success ? int.Parse(absolute.Groups[1].Value, CultureInfo.InvariantCulture) : null;
        int? absoluteEnd = absolute.Groups.Count > 2 && absolute.Groups[2].Success ? int.Parse(absolute.Groups[2].Value, CultureInfo.InvariantCulture) : absoluteNumber;
        if (absoluteNumber is 0 || absoluteEnd < absoluteNumber || absoluteEnd - absoluteNumber >= 20) ambiguous = true;
        int? season = episode.Success ? int.Parse(episode.Groups["season"].Value, CultureInfo.InvariantCulture) : null;
        int? number = episode.Success && episode.Groups["episode"].Success ? int.Parse(episode.Groups["episode"].Value, CultureInfo.InvariantCulture) : null;
        var numbers = new List<int>(); if (number is { } first) numbers.Add(first);
        foreach (Match extra in ExtraEpisodes().Matches(episode.Groups["tail"].Value))
        {
            var last = int.Parse(extra.Groups[2].Value, CultureInfo.InvariantCulture);
            if (last < 1 || last > 10000 || numbers.Count == 0) { ambiguous = true; break; }
            if (extra.Groups[1].Value == "-")
            {
                if (last <= numbers[^1] || last - numbers[^1] + numbers.Count > 20) { ambiguous = true; break; }
                numbers.AddRange(Enumerable.Range(numbers[^1] + 1, last - numbers[^1]));
            }
            else numbers.Add(last);
        }
        if (numbers.Count > 20 || numbers.Distinct().Count() != numbers.Count || numbers.Any(value => value < 1) || !numbers.SequenceEqual(numbers.Order())) ambiguous = true;
        if (episode.Success && UnparsedEpisodeTail().IsMatch(name[(episode.Index + episode.Length)..])) ambiguous = true;
        var part = Part().Matches(name).Cast<Match>().LastOrDefault(match => !year.Success || match.Index > year.Index) ?? Match.Empty;
        int? partNumber = part.Success ? int.Parse(part.Groups[1].Value, CultureInfo.InvariantCulture) : null;
        if (partNumber is < 1 or > 20) ambiguous = true;
        var end = new[] { episode.Success ? episode.Index : name.Length, year.Success ? year.Index : name.Length, daily.Success ? daily.Index : name.Length,
            absolute.Success ? absolute.Index : name.Length, part.Success ? part.Index : name.Length, ReleaseInfo().Match(name) is { Success: true } info ? info.Index : name.Length }.Min();
        var query = Separators().Replace(name[..end], " ").Trim(' ', '-', '[', ']', '(', ')');
        if (query.Length < 2) query = Separators().Replace(name, " ").Trim();
        return new FilenameHint(query, year.Success ? int.Parse(year.Groups[1].Value, CultureInfo.InvariantCulture) : null, season, number,
            numbers.ToArray(), airDate, absoluteNumber, absoluteEnd, partNumber, ambiguous);
    }
    [GeneratedRegex(@"(?i)(?<![a-z0-9])(?:S(?<season>\d{1,3})(?:[ ._-]*E(?<episode>\d{1,4})(?<tail>(?:[ ._-]*E\d{1,4}|-\d{1,4})*))?|(?<season>\d{1,3})x(?<episode>\d{1,4})(?<tail>(?:-\d{1,4})*))(?![a-z0-9])", RegexOptions.None, 100)]
    private static partial Regex Episode();
    [GeneratedRegex(@"(?i)(E|-)(\d{1,4})", RegexOptions.NonBacktracking)] private static partial Regex ExtraEpisodes();
    [GeneratedRegex(@"^(?:[ ._-]*[ES]\d|-\d+(?:$|[ ._-]))", RegexOptions.IgnoreCase, 100)] private static partial Regex UnparsedEpisodeTail();
    [GeneratedRegex(@"(?<!\d)((?:19|20)\d{2})[._-](\d{2})[._-](\d{2})(?!\d)", RegexOptions.None, 100)] private static partial Regex Daily();
    [GeneratedRegex(@"\s-\s?(\d{1,4})(?:-(\d{1,4}))?(?:v[1-9])?(?=$|[ ._\[])" , RegexOptions.IgnoreCase, 100)] private static partial Regex Absolute();
    [GeneratedRegex(@"(?i)(?:^|[ ._-])(?:cd|disc|disk|part|pt)[ ._-]?(\d{1,2})(?=$|[ ._-])", RegexOptions.None, 100)] private static partial Regex Part();
    [GeneratedRegex(@"^\[[^\]]{1,80}\][ ._-]*", RegexOptions.NonBacktracking)] private static partial Regex LeadingGroup();
    [GeneratedRegex(@"(?<![a-z0-9])((?:19|20)\d{2})(?![a-z0-9])", RegexOptions.IgnoreCase, 100)]
    private static partial Regex Year();
    [GeneratedRegex(@"(?i)(?:^|[ ._-])(?:2160p|1080p|720p|480p|web[ ._-]?dl|bluray|brrip|hdtv|x26[45]|h26[45])(?:$|[ ._-])")]
    private static partial Regex ReleaseInfo();
    [GeneratedRegex(@"[._]+")]
    private static partial Regex Separators();
}
