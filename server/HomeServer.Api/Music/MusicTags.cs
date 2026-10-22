using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using HomeServer.Core;

namespace HomeServer.Music;

public static partial class MusicTags
{
    public static AudioTags Read(JsonElement root)
    {
        var tags = root.TryGetProperty("format", out var format) && format.TryGetProperty("tags", out var value) && value.ValueKind == JsonValueKind.Object
            ? value.EnumerateObject().Where(property => property.Value.ValueKind == JsonValueKind.String).GroupBy(property => Key(property.Name))
                .ToDictionary(group => group.Key, group => group.First().Value.GetString()!) : [];
        string Get(params string[] keys) => keys.Select(key => tags.GetValueOrDefault(key)).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";
        var title = Get("title"); var artist = Get("artist"); var albumArtist = Get("albumartist"); var album = Get("album");
        var date = Get("date", "year");
        int Number(string value, int fallback) => int.TryParse(value.Split('/')[0], out var parsed) && parsed >= 0 ? parsed : fallback;
        var gainText = Get("replaygaintrackgain").Replace("dB", "", StringComparison.OrdinalIgnoreCase).Trim();
        double? gain = double.TryParse(gainText, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number) ? Math.Clamp(number, -30, 30) : null;
        double? Gain(string key) => double.TryParse(Get(key).Replace("dB", "", StringComparison.OrdinalIgnoreCase).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) ? Math.Clamp(value, -30, 30) : null;
        double? Peak(string key) => double.TryParse(Get(key), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) && value is > 0 and <= 100 ? value : null;
        var lyrics = Get("lyrics", "unsyncedlyrics", "unsynchronizedlyrics");
        int? depth = null;
        var artwork = false;
        if (root.TryGetProperty("streams", out var streams))
            foreach (var stream in streams.EnumerateArray())
            {
                if (stream.TryGetProperty("codec_type", out var type) && type.GetString() == "audio")
                {
                    if (stream.TryGetProperty("bits_per_raw_sample", out var raw) && int.TryParse(raw.ToString(), out var bits) && bits > 0) depth ??= bits;
                    if (stream.TryGetProperty("bits_per_sample", out var sample) && sample.TryGetInt32(out var sampleBits) && sampleBits > 0) depth ??= sampleBits;
                }
                if (stream.TryGetProperty("disposition", out var disposition) && disposition.TryGetProperty("attached_pic", out var attached) && attached.GetInt32() == 1) artwork = true;
            }
        return new AudioTags(title[..Math.Min(title.Length, 500)], artist[..Math.Min(artist.Length, 500)], albumArtist[..Math.Min(albumArtist.Length, 500)], album[..Math.Min(album.Length, 500)],
            date.Length == 0 ? null : date[..Math.Min(date.Length, 20)], Number(Get("disc", "discnumber"), 1), Number(Get("track", "tracknumber"), 0),
            Mbid(Get("musicbrainzartistid")), Mbid(Get("musicbrainzreleasegroupid")), Mbid(Get("musicbrainzalbumid", "musicbrainzreleaseid")), Mbid(Get("musicbrainztrackid", "musicbrainzrecordingid")),
            lyrics.Length == 0 ? null : lyrics[..Math.Min(lyrics.Length, 256 * 1024)], gain, depth, artwork, Gain("replaygainalbumgain"), Peak("replaygaintrackpeak"), Peak("replaygainalbumpeak"), Mbid(Get("musicbrainzalbumartistid")));
    }
    private static string? Mbid(string value) => MusicCredits.ValidId(value.Trim());
    private static string Key(string value) => NonLetters().Replace(value.ToLowerInvariant(), "");
    [GeneratedRegex("[^a-z0-9]", RegexOptions.NonBacktracking)] private static partial Regex NonLetters();
}
