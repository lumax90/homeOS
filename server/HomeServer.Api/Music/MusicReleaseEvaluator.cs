using System.Text.RegularExpressions;
using System.Globalization;
using HomeServer.Core;

namespace HomeServer.Music;

public static partial class MusicReleaseEvaluator
{
    public static void Validate(MusicQualityRules rules)
    {
        if (rules is null) throw new DomainException("Müzik kalite profili gerekli.");
        if (!double.IsFinite(rules.MaximumSizeGiB) || rules.MaximumSizeGiB is <= 0 or > 1000 || rules.MinimumBitrateKbps is < 0 or > 10000 || rules.MinimumSeeders is < 0 or > 10000
            || rules.MinimumBitDepth is not (0 or 16 or 24 or 32) || rules.MinimumSampleRate is < 0 or > 384000
            || rules.Codecs?.Length > 10 || rules.Codecs?.Any(codec => codec is not ("flac" or "alac" or "pcm" or "mp3" or "aac" or "opus" or "vorbis")) == true)
            throw new DomainException("Müzik kalite profili geçersiz.");
    }
    public static MusicReleaseEvaluation Evaluate(RawRelease release, MusicRelease target, MusicQualityRules rules)
    {
        Validate(rules); var errors = new List<string>(); var title = Normalize(release.Title);
        var identity = false;
        foreach (var (key, expected) in new[] { ("musicbrainzreleaseid", target.Metadata.Id), ("musicbrainzreleasegroupid", target.Metadata.GroupId) })
            if (release.Attributes.TryGetValue(key, out var ids) && ids.Length > 0)
            {
                if (ids.Any(id => !id.Equals(expected, StringComparison.OrdinalIgnoreCase))) errors.Add("Sağlayıcı müzik kimliği farklı."); else identity = true;
            }
        if (!identity && (!Contains(title, Normalize(target.Metadata.Title)) || !Contains(title, Normalize(target.Metadata.Artist)))) errors.Add("Sanatçı ve albüm kimliği başlıkta doğrulanamadı.");
        var codec = AudioCodec().Match(release.Title).Value.ToLowerInvariant(); codec = Codec(codec);
        var lossless = IsLossless(codec);
        if (codec == "unknown") errors.Add("Ses codec'i doğrulanamadı.");
        if (rules.LosslessOnly && !lossless) errors.Add("Profil kayıpsız ses istiyor.");
        if (rules.Codecs is { Length: > 0 } && !rules.Codecs.Contains(codec)) errors.Add("Codec müzik profilinde yok.");
        if (release.SizeBytes is null or <= 0 || release.SizeBytes > rules.MaximumSizeGiB * 1024 * 1024 * 1024) errors.Add("Paket boyutu bilinmiyor veya sınırı aşıyor.");
        if (release.Protocol == "torrent" && (release.Seeders ?? 0) < rules.MinimumSeeders) errors.Add("Yeterli seeder yok.");
        var bitrate = Bitrate().Match(release.Title); var rate = bitrate.Success ? int.Parse(bitrate.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
        if (!lossless && rules.MinimumBitrateKbps > 0 && rate < rules.MinimumBitrateKbps) errors.Add("İstenen bitrate başlıkta doğrulanamadı.");
        var bits = Depth().Match(release.Title); var sample = SampleRate().Match(release.Title); var pair = DepthRate().Match(release.Title);
        var depth = bits.Success ? int.Parse(bits.Groups[1].Value, CultureInfo.InvariantCulture) : pair.Success ? int.Parse(pair.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
        var sampleRate = sample.Success ? (int)(double.Parse(sample.Groups[1].Value, CultureInfo.InvariantCulture) * 1000) : pair.Success ? (int)(double.Parse(pair.Groups[2].Value, CultureInfo.InvariantCulture) * 1000) : 0;
        if (depth < rules.MinimumBitDepth) errors.Add("İstenen bit derinliği başlıkta doğrulanamadı.");
        if (sampleRate < rules.MinimumSampleRate) errors.Add("İstenen örnekleme hızı başlıkta doğrulanamadı.");
        var quality = Quality(codec, rate, depth, sampleRate);
        return new MusicReleaseEvaluation(errors.Count == 0, quality.Tier * 1000 + Math.Min(500, rate) + depth, errors.ToArray(), codec == "unknown" ? null : codec, lossless, quality);
    }
    public static MusicAudioQuality Quality(string? codec, int bitrate, int depth, int sampleRate)
    {
        var normalized = Codec(codec);
        return new(normalized, normalized == "unknown" ? 0 : IsLossless(normalized) ? depth > 16 || sampleRate > 48000 ? 3 : 2 : 1, bitrate, depth, sampleRate);
    }
    public static MusicAudioQuality Quality(ProbeResult info) => Quality(info.AudioCodec,
        (int)Math.Min(10000, (info.Streams?.FirstOrDefault(stream => stream.Kind == "audio")?.BitRate ?? info.BitRate ?? 0) / 1000),
        info.Music?.BitDepth ?? 0, info.Streams?.FirstOrDefault(stream => stream.Kind == "audio")?.SampleRate ?? 0);
    public static bool Better(MusicAudioQuality candidate, MusicAudioQuality previous)
    {
        if (candidate.Tier == 0 || previous.Tier == 0) return false;
        if (candidate.Tier >= 2 && previous.Tier >= 2)
            return previous.BitDepth > 0 && previous.SampleRate > 0 && candidate.BitDepth >= previous.BitDepth && candidate.SampleRate >= previous.SampleRate
                && (candidate.BitDepth > previous.BitDepth || candidate.SampleRate > previous.SampleRate);
        if (candidate.Tier != previous.Tier) return candidate.Tier > previous.Tier;
        if (candidate.Tier == 1) return candidate.Codec == previous.Codec && previous.BitrateKbps > 0 && candidate.BitrateKbps > previous.BitrateKbps;
        return previous.BitDepth > 0 && previous.SampleRate > 0 && candidate.BitDepth >= previous.BitDepth && candidate.SampleRate >= previous.SampleRate
            && (candidate.BitDepth > previous.BitDepth || candidate.SampleRate > previous.SampleRate);
    }
    public static bool AtLeast(MusicAudioQuality candidate, MusicAudioQuality previous) => candidate.Tier >= 2 && previous.Tier >= 2
        ? candidate.BitDepth >= previous.BitDepth && candidate.SampleRate >= previous.SampleRate
        : candidate.Tier > previous.Tier || candidate.Tier == 1 && previous.Tier == 1 && candidate.Codec == previous.Codec && candidate.BitrateKbps >= previous.BitrateKbps;
    public static bool AtCutoff(MusicAudioQuality quality, MusicMonitorSettings settings) => settings.Cutoff switch
    {
        "lossy" => quality.Tier > 1 || quality.Tier == 1 && quality.BitrateKbps >= settings.CutoffBitrateKbps,
        "lossless" => quality.Tier >= 2,
        "hires" => quality.Tier >= 3,
        _ => false
    };
    public static string Codec(string? value) => value?.ToLowerInvariant() switch { "flac" => "flac", "alac" => "alac", "pcm" or "wav" or "aiff" => "pcm", "m4a" => "aac", "aac" => "aac", "mp3" => "mp3", "opus" => "opus", "ogg" or "vorbis" => "vorbis", var text when text?.StartsWith("pcm_", StringComparison.Ordinal) == true => "pcm", _ => "unknown" };
    public static bool IsLossless(string? value) => value is "flac" or "alac" or "pcm";
    public static string Normalize(string value) => Spaces().Replace(MediaPaths.SearchKey(value), " ").Trim();
    private static bool Contains(string text, string term) => term.Length > 0 && (" " + text + " ").Contains(" " + term + " ", StringComparison.Ordinal);
    [GeneratedRegex(@"\b(?:flac|alac|wav|aiff|mp3|aac|m4a|opus|ogg|vorbis)\b", RegexOptions.IgnoreCase | RegexOptions.NonBacktracking)] private static partial Regex AudioCodec();
    [GeneratedRegex(@"\b(64|96|128|160|192|224|256|320)\s*(?:kbps|k)?\b", RegexOptions.IgnoreCase | RegexOptions.NonBacktracking)] private static partial Regex Bitrate();
    [GeneratedRegex(@"\b(16|24|32)[ -]?bit\b", RegexOptions.IgnoreCase | RegexOptions.NonBacktracking)] private static partial Regex Depth();
    [GeneratedRegex(@"\b(44\.1|48|88\.2|96|176\.4|192|352\.8|384)\s*k(?:hz)?\b", RegexOptions.IgnoreCase | RegexOptions.NonBacktracking)] private static partial Regex SampleRate();
    [GeneratedRegex(@"\b(16|24|32)[/-](44\.1|48|88\.2|96|176\.4|192|352\.8|384)\b", RegexOptions.NonBacktracking)] private static partial Regex DepthRate();
    [GeneratedRegex(@"[^\p{L}\p{Nd}]+", RegexOptions.NonBacktracking)] private static partial Regex Spaces();
}
