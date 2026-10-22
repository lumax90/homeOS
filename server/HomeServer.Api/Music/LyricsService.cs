using System.Globalization;
using System.Text.RegularExpressions;
using HomeServer.Core;
using HomeServer.Infrastructure;
using HomeServer.Playback;

namespace HomeServer.Music;

public sealed record LyricWord(double Seconds, double? EndSeconds, string Text);
public sealed record LyricLine(double? Seconds, string Text, LyricWord[]? Words = null, double? EndSeconds = null);
public sealed partial class LyricsService(MediaStore store)
{
    public async Task<IReadOnlyList<LyricLine>> ReadAsync(MediaItem media, CancellationToken token)
    {
        if (media.Kind != "audio") throw new DomainException("Sözler bir müzik parçası gerektirir.", 404);
        var text = store.GetMusicLyrics(media.Id); var library = store.GetLibrary(media.LibraryId) ?? throw new DomainException("Kütüphane bulunamadı.", 404);
        foreach (var extension in new[] { ".elrc", ".lrc", ".txt" })
        {
            if (extension == ".txt" && !string.IsNullOrEmpty(text)) break;
            var relative = Path.ChangeExtension(media.RelativePath, extension);
            if (!File.Exists(Path.Combine(library.Path, relative))) continue;
            var path = MediaPaths.ResolveFile(library.Path, relative);
            await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 16384, FileOptions.Asynchronous);
            text = SubtitleService.DecodeText(await Metadata.MetadataProvider.ReadLimitedAsync(file, 256 * 1024, token)); break;
        }
        return string.IsNullOrEmpty(text) ? [] : Parse(text);
    }
    public static IReadOnlyList<LyricLine> Parse(string text)
    {
        if (text.Length > 256 * 1024) throw new DomainException("Şarkı sözü 256 KiB sınırını aşıyor.", 413);
        var result = new List<LyricLine>(); var offset = 0d; var wordCount = 0;
        foreach (var raw in text.Replace("\r", "", StringComparison.Ordinal).Split('\n'))
        {
            var line = raw.Trim(); if (line.Length == 0) continue;
            if (Offset().Match(line) is { Success: true } delay && double.TryParse(delay.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) { offset = Math.Clamp(value / 1000, -600, 600); continue; }
            var prefix = Prefix().Match(line); var stamps = Stamp().Matches(prefix.Value).Select(Time).Where(value => value is not null).Select(value => value!.Value).ToArray();
            if (stamps.Length == 0) { if (!line.StartsWith('[')) result.Add(new(null, line)); }
            else
            {
                var content = line[prefix.Length..]; var markers = WordStamp().Matches(content).ToArray(); var words = new List<LyricWord>(); var valid = true;
                if (markers.Length > 0)
                {
                    if (markers[0].Index > 0 && content[..markers[0].Index].Length > 0) words.Add(new(stamps[0], Time(markers[0]), content[..markers[0].Index]));
                    for (var i = 0; i < markers.Length; i++)
                    {
                        var start = Time(markers[i]); var end = i + 1 < markers.Length ? Time(markers[i + 1]) : null;
                        if (start is null || start < stamps[0] || i > 0 && start <= Time(markers[i - 1]) || end is { } stop && stop <= start) { valid = false; break; }
                        var from = markers[i].Index + markers[i].Length; var to = i + 1 < markers.Length ? markers[i + 1].Index : content.Length;
                        if (to > from) words.Add(new(start.Value, end, content[from..to]));
                    }
                }
                var plain = WordStamp().Replace(content, "").Trim();
                foreach (var stamp in stamps)
                {
                    var delta = stamp - stamps[0]; var shifted = valid ? words.Select(word => word with { Seconds = word.Seconds + delta, EndSeconds = word.EndSeconds + delta }).ToArray() : [];
                    result.Add(new(stamp, plain, shifted)); wordCount += shifted.Length;
                }
            }
            if (result.Count > 10000 || wordCount > 40000) throw new DomainException("Zamanlı söz satırı veya kelime sınırı aşıldı.", 413);
        }
        var lines = result.Select(line => line.Seconds is null ? line : line with { Seconds = Math.Max(0, line.Seconds.Value + offset), Words = (line.Words ?? []).Select(word => word with { Seconds = Math.Max(0, word.Seconds + offset), EndSeconds = word.EndSeconds is { } end ? Math.Max(0, end + offset) : null }).ToArray() }).OrderBy(line => line.Seconds ?? -1).ToArray();
        double? next = null;
        for (var index = lines.Length - 1; index >= 0; index--)
        {
            var line = lines[index]; if (line.Seconds is null) continue;
            if (index + 1 < lines.Length && lines[index + 1].Seconds > line.Seconds) next = lines[index + 1].Seconds;
            var words = line.Words ?? [];
            if (next is { } last && words.Any(word => word.Seconds >= last || word.EndSeconds > last)) words = [];
            lines[index] = line with { EndSeconds = next, Words = words.Select(word => word with { EndSeconds = word.EndSeconds ?? next }).ToArray() };
        }
        return lines;
    }
    private static double? Time(Match match)
    {
        var minutes = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture); var seconds = double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        return seconds < 60 && minutes * 60 + seconds <= 86400 ? minutes * 60 + seconds : null;
    }
    [GeneratedRegex(@"^(?:\[[0-9]{1,4}:[0-9]{2}(?:\.[0-9]{1,3})?\]\s*)+", RegexOptions.NonBacktracking)] private static partial Regex Prefix();
    [GeneratedRegex(@"\[([0-9]{1,4}):([0-9]{2}(?:\.[0-9]{1,3})?)\]", RegexOptions.NonBacktracking)] private static partial Regex Stamp();
    [GeneratedRegex(@"<([0-9]{1,4}):([0-9]{2}(?:\.[0-9]{1,3})?)>", RegexOptions.NonBacktracking)] private static partial Regex WordStamp();
    [GeneratedRegex(@"^\[offset:([+-]?[0-9]{1,7})\]$", RegexOptions.IgnoreCase | RegexOptions.NonBacktracking)] private static partial Regex Offset();
}
