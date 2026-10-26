using System.Globalization;
using System.Text.Json;
using HomeServer.Core;

namespace HomeServer.Infrastructure;

/// <summary>RFC 6381 codec identifiers from ffprobe's configuration record, without decoding media.</summary>
public static class CodecIdentifiers
{
    public static double? FrameRate(string? fraction)
    {
        var parts = fraction?.Split('/');
        if (parts is not { Length: 2 } || !double.TryParse(parts[0], CultureInfo.InvariantCulture, out var top)
            || !double.TryParse(parts[1], CultureInfo.InvariantCulture, out var bottom) || bottom <= 0) return null;
        var value = top / bottom; return double.IsFinite(value) && value is > 0 and <= 1000 ? value : null;
    }
    public static int? BitDepth(string? pixel, int? depth)
    {
        if (depth is > 0 and <= 32) return depth;
        if (pixel is null) return null;
        foreach (var bits in new[] { 10, 12, 16 })
            if (pixel.EndsWith(bits + "le", StringComparison.Ordinal) || pixel.EndsWith(bits + "be", StringComparison.Ordinal)) return bits;
        return pixel is "yuv420p" or "yuvj420p" or "yuv422p" or "yuv444p" or "nv12" ? 8 : null;
    }
    public static DolbyVisionInfo? DolbyVisionFromProbe(JsonElement stream)
    {
        if(!stream.TryGetProperty("side_data_list",out var list)||list.ValueKind!=JsonValueKind.Array)return null;
        foreach(var data in list.EnumerateArray())
        {
            int? Number(string name)=>data.TryGetProperty(name,out var value)&&int.TryParse(value.ToString(),CultureInfo.InvariantCulture,out var number)?number:null;
            bool? Flag(string name)=>Number(name) switch{0=>false,1=>true,_=>null};
            if(Number("dv_profile") is not{}profile||profile is <0 or >127||Number("dv_level") is not{}level||level is <0 or >63)continue;
            var compatibility=Number("dv_bl_signal_compatibility_id");
            return new(profile,level,compatibility is >=0 and <=15?compatibility:null,Flag("bl_present_flag"),Flag("el_present_flag"),Flag("rpu_present_flag"));
        }
        return null;
    }
    public static string? FromProbe(JsonElement stream,bool allowDolbyVision=true)
    {
        string? Text(string name) => stream.TryGetProperty(name, out var item) && item.ValueKind == JsonValueKind.String ? item.GetString() : null;
        int? Number(string name) => stream.TryGetProperty(name, out var item) && int.TryParse(item.ToString(), CultureInfo.InvariantCulture, out var number) ? number : null;
        var codec = Text("codec_name"); var bytes = ExtraData(Text("extradata"));
        if (allowDolbyVision && DolbyVisionFromProbe(stream) is{}dolbyVision)
        {
            var prefix=codec switch
            {
                "hevc"=>Text("codec_tag_string")=="dvhe"?"dvhe":"dvh1",
                "h264"=>Text("codec_tag_string")=="dvav"?"dvav":"dva1",
                "av1"=>"dav1",
                _=>null
            };
            return prefix is null?null:$"{prefix}.{dolbyVision.Profile:D2}.{dolbyVision.Level:D2}";
        }
        if (codec == "h264")
        {
            if (bytes.Length >= 4 && bytes[0] == 1) return "avc1." + Convert.ToHexString(bytes.AsSpan(1, 3));
            var profile = Text("profile") switch { "Constrained Baseline" => "42E0", "Baseline" => "4200", "Main" => "4D00", "High" => "6400", "High 10" => "6E00", _ => null };
            return profile is not null && Number("level") is > 0 and <= 255 ? "avc1." + profile + Number("level")!.Value.ToString("X2") : null;
        }
        if (codec == "hevc" && bytes.Length >= 13 && bytes[0] == 1)
        {
            var profileSpace = (bytes[1] >> 6) switch { 1 => "A", 2 => "B", 3 => "C", _ => "" };
            uint compatibility = 0;
            for (var bit = 0; bit < 32; bit++) if ((bytes[2 + bit / 8] & (1 << (7 - bit % 8))) != 0) compatibility |= 1u << bit;
            var constraints = bytes.AsSpan(6, 6).ToArray().ToList(); while (constraints.Count > 0 && constraints[^1] == 0) constraints.RemoveAt(constraints.Count - 1);
            return $"hvc1.{profileSpace}{bytes[1] & 31}.{compatibility:X}.{((bytes[1] & 32) != 0 ? "H" : "L")}{bytes[12]}"
                + string.Concat(constraints.Select(value => "." + value.ToString("X2")));
        }
        // MPEG-TS exposes Annex-B parameter sets instead of an hvcC record. ffprobe has already read their profile/level.
        if (codec == "hevc" && Number("level") is > 0 and <= 255)
            return Text("profile") switch { "Main" => $"hvc1.1.6.L{Number("level")}.B0", "Main 10" => $"hvc1.2.4.L{Number("level")}.B0", _ => null };
        if (codec == "av1" && bytes.Length >= 3 && (bytes[0] & 128) != 0)
            return $"av01.{bytes[1] >> 5}.{bytes[1] & 31:D2}{((bytes[2] & 128) != 0 ? "H" : "M")}.{((bytes[2] & 64) == 0 ? 8 : (bytes[2] & 32) != 0 ? 12 : 10):D2}";
        return codec switch
        {
            "aac" => Text("profile") switch { "HE-AAC" => "mp4a.40.5", "HE-AACv2" => "mp4a.40.29", "LC" => "mp4a.40.2", _ => null },
            "mp3" => "mp4a.6B", "ac3" => "ac-3", "eac3" => "ec-3", "opus" => "opus", "flac" => "flac",
            "vp9" => $"vp09.{(Text("profile") == "Profile 2" ? 2 : 0):D2}.51.{(BitDepth(Text("pix_fmt"), Number("bits_per_raw_sample")) ?? 8):D2}",
            _ => null
        };
    }
    private static byte[] ExtraData(string? dump)
    {
        if (dump is null || dump.Length > 256 * 1024) return [];
        var bytes = new List<byte>();
        foreach (var line in dump.Split('\n'))
        {
            var colon = line.IndexOf(':'); if (colon < 0) continue;
            var hex = line[(colon + 1)..].TrimStart(); var end = hex.IndexOf("  ", StringComparison.Ordinal); if (end >= 0) hex = hex[..end];
            hex = hex.Replace(" ", "", StringComparison.Ordinal);
            if (hex.Length % 2 != 0 || hex.Any(c => !char.IsAsciiHexDigit(c))) return [];
            bytes.AddRange(Convert.FromHexString(hex));
            if (bytes.Count >= 64) break; // Only the configuration header is needed for a codec identifier.
        }
        return bytes.ToArray();
    }
}
