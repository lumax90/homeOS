using System.Text.RegularExpressions;
using HomeServer.Core;

namespace HomeServer.Metadata;

public sealed record BoundaryEpisode(string Id, int Season, int Number, string Title, double? RuntimeSeconds);
public sealed record MediaInterval(double Start, double End);
public static partial class EpisodeBoundaryDetector
{
    public static EpisodeBoundaryResult Detect(BoundaryEpisode[] episodes, double duration, IReadOnlyList<MediaChapter> chapters, IReadOnlyList<MediaInterval> black, IReadOnlyList<MediaInterval> silence, bool hasAudio)
    {
        if (episodes.Length is < 2 or > 20 || !double.IsFinite(duration) || duration <= episodes.Length) return new([], "none", [], ["Dosyada en az iki bölüm ve geçerli süre olmalı."], false);
        var starts = new List<double>(); var evidence = new List<string>(); var warnings = new List<string>();
        for (var index = 0; index < episodes.Length; index++)
        {
            var episode = episodes[index]; var matches = chapters.Where(chapter => Matches(chapter.Title, episode, index + 1)).ToArray();
            if (matches.Length != 1) { starts.Clear(); break; }
            starts.Add(matches[0].StartSeconds); evidence.Add($"Sahne işareti: {matches[0].Title} · {matches[0].StartSeconds:0.###} sn");
        }
        if (starts.Count == episodes.Length && starts[0] <= 1 && Ordered(starts, duration))
        { starts[0] = 0; return Result("chapters", starts); }
        evidence.Clear(); starts.Clear(); starts.Add(0);
        var runtime = episodes.All(episode => episode.RuntimeSeconds is > 0) ? episodes.Select(episode => episode.RuntimeSeconds!.Value).ToArray() : episodes.Select(_ => 1d).ToArray();
        var knownRuntime = episodes.All(episode => episode.RuntimeSeconds is > 0);
        warnings.Add(knownRuntime ? "Sağlayıcı süreleri yalnız arama penceresini belirler; önerilen sınırlar dosya sinyalinden gelir." : "Bölüm süreleri bilinmiyor; eşit aralıklar yalnız arama penceresidir, bölüm sınırı sayılmaz.");
        var sum = runtime.Sum(); var expected = 0d;
        for (var index = 1; index < episodes.Length; index++)
        {
            expected += duration * runtime[index - 1] / sum;
            var window = Math.Clamp(duration * Math.Min(runtime[index - 1], runtime[index]) / sum * .2, .75, 300);
            var candidates = black.Where(interval => Valid(interval, duration) && interval.End - interval.Start is >= .2 and <= 12 && Math.Abs(interval.End - expected) <= window)
                .Where(interval => hasAudio ? silence.Any(quiet => Valid(quiet, duration) && Math.Min(quiet.End, interval.End) - Math.Max(quiet.Start, interval.Start) >= .1)
                    : chapters.Any(chapter => Math.Abs(chapter.StartSeconds - interval.End) <= .75))
                .Select(interval => (Time: interval.End, Score: 1 - Math.Abs(interval.End - expected) / window)).OrderByDescending(value => value.Score).ToArray();
            if (candidates.Length == 0 || candidates.Length > 1 && candidates[0].Score - candidates[1].Score < .25)
            {
                warnings.Add($"{index + 1}. bölüm için tekil siyah görüntü/{(hasAudio ? "sessizlik" : "sahne")} sınırı doğrulanamadı. Zamanı elle belirle.");
                return new([], "signals", evidence.ToArray(), warnings.ToArray(), false);
            }
            starts.Add(candidates[0].Time); evidence.Add($"{index + 1}. bölüm: siyah görüntü + {(hasAudio ? "sessizlik" : "sahne işareti")} · {candidates[0].Time:0.###} sn");
        }
        return Ordered(starts, duration) ? Result("signals", starts) : new([], "signals", evidence.ToArray(), [..warnings, "Önerilen aralıklar çakışıyor."], false);
        EpisodeBoundaryResult Result(string method, IReadOnlyList<double> times) => new(episodes.Select((episode, index) => new EpisodeBoundary(episode.Id, times[index], index + 1 < times.Count ? times[index + 1] : duration)).ToArray(), method, evidence.ToArray(), warnings.ToArray(), true);
    }
    private static bool Valid(MediaInterval interval, double duration) => double.IsFinite(interval.Start) && double.IsFinite(interval.End) && interval.Start >= 0 && interval.End > interval.Start && interval.End < duration;
    private static bool Ordered(IReadOnlyList<double> starts, double duration) => starts.All(double.IsFinite) && starts[0] >= 0 && starts[^1] < duration - .1 && starts.Skip(1).Select((value, index) => value - starts[index]).All(length => length > .1);
    private static bool Matches(string title, BoundaryEpisode episode, int ordinal)
    {
        var coordinate = Coordinate().Match(title);
        if (coordinate.Success) return int.TryParse(coordinate.Groups[1].Value, out var season) && int.TryParse(coordinate.Groups[2].Value, out var number) && season == episode.Season && number == episode.Number;
        var numbered = Ordinal().Match(title); return numbered.Success && int.TryParse(numbered.Groups[1].Value, out var index) && index == ordinal;
    }
    [GeneratedRegex(@"\bS(\d{1,3})\s*E(\d{1,5})\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex Coordinate();
    [GeneratedRegex(@"^(?:episode|bölüm|bolum)\s*#?\s*(\d{1,3})(?:\s|[:.\-]|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex Ordinal();
}
