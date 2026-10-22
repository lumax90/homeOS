using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using HomeServer.Core;

namespace HomeServer.Subtitles;

public static partial class SubtitleTiming
{
    public static SubtitleCue[] Read(string vtt)
    {
        var result = new List<SubtitleCue>();
        foreach (var block in vtt.Replace("\r\n", "\n", StringComparison.Ordinal).Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            var lines = block.Split('\n'); var index = Array.FindIndex(lines, line => line.Contains(" --> ", StringComparison.Ordinal)); if (index < 0) continue;
            var match = Times().Match(lines[index]); if (!match.Success) continue;
            var start = Seconds(match.Groups[1].Value); var end = Seconds(match.Groups[2].Value);
            if (!double.IsFinite(start) || !double.IsFinite(end) || start < 0 || end <= start || end > 31_536_000) continue;
            result.Add(new(start, end, lines[(index + 1)..], match.Groups[3].Value));
            if (result.Count > 50000) throw new DomainException("Altyazı 50.000 cue sınırını aşıyor.", 413);
        }
        return result.ToArray();
    }
    public static string Transform(IEnumerable<SubtitleCue> cues, double offset, double scale)
    {
        if (!double.IsFinite(offset) || Math.Abs(offset) > 600 || !double.IsFinite(scale) || scale is < .9 or > 1.1) throw new DomainException("Altyazı zaman dönüşümü geçersiz.");
        var result = new StringBuilder("WEBVTT\n\n");
        foreach (var cue in cues)
        {
            var start = cue.Start * scale + offset; var end = cue.End * scale + offset;
            if (end <= 0 || end <= start || end > 31_536_000) continue;
            result.Append(Stamp(Math.Max(0, start))).Append(" --> ").Append(Stamp(end)).Append(cue.Settings).Append('\n');
            foreach (var line in cue.Lines) result.AppendLine(line); result.Append('\n');
        }
        return result.ToString();
    }
    private static double Seconds(string value)
    {
        double result = 0;
        foreach (var part in value.Split(':')) { if (!double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) return double.NaN; result = result * 60 + number; }
        return result;
    }
    private static string Stamp(double seconds) { var value = (long)Math.Round(seconds * 1000); return $"{value / 3600000:00}:{value / 60000 % 60:00}:{value / 1000 % 60:00}.{value % 1000:000}"; }
    [GeneratedRegex(@"^((?:\d{2,}:)?\d{2}:\d{2}\.\d{3}) --> ((?:\d{2,}:)?\d{2}:\d{2}\.\d{3})(.*)$", RegexOptions.NonBacktracking)] private static partial Regex Times();
}
