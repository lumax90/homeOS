using System.Globalization;
using System.Text.Json;
using HomeServer.Core;

namespace HomeServer.Infrastructure;

public interface IMediaProbe
{
    bool FfprobeAvailable { get; }
    Task<ProbeResult?> ProbeAsync(string path, CancellationToken cancellationToken);
}

public sealed class MediaTools(IConfiguration configuration, HomePaths paths, ILogger<MediaTools> logger) : IMediaProbe
{
    private readonly string? _ffmpeg = Find(configuration["Home:FfmpegPath"], "ffmpeg");
    private readonly string? _ffprobe = Find(configuration["Home:FfprobePath"], "ffprobe");
    private readonly SemaphoreSlim _operations = new(2, 2);
    public bool FfmpegAvailable => _ffmpeg is not null;
    public bool FfprobeAvailable => _ffprobe is not null;
    public string FfmpegExecutable => _ffmpeg ?? throw new DomainException("FFmpeg sunucuda yapılandırılmadı.", 409);
    private const string ProbeEntries = "format=duration,format_name,bit_rate,start_time:format_tags:stream=index,codec_type,codec_name,codec_tag_string,width,height,channels,channel_layout,sample_rate,bit_rate,profile,level,avg_frame_rate,pix_fmt,color_transfer,color_primaries,color_space,bits_per_raw_sample,bits_per_sample,extradata:stream_tags=language,title:stream_disposition=default,forced,hearing_impaired,attached_pic:stream_side_data=side_data_type,dv_profile,dv_level:chapter=id,start_time,end_time:chapter_tags=title";

    public async Task<ProbeResult?> ProbeAsync(string path, CancellationToken cancellationToken)
    {
        if (_ffprobe is null) return null;
        await _operations.WaitAsync(cancellationToken);
        try
        {
            var output = await MediaProcess.RunAsync(_ffprobe,
                ["-v", "error", "-protocol_whitelist", "file,pipe", "-format_whitelist", MediaProcess.MediaFormats, "-max_streams", "128",
                  "-show_data", "-show_entries", ProbeEntries,
                 "-of", "json", path], TimeSpan.FromSeconds(30), cancellationToken);
            var result = ParseProbe(output);
            return result.Music is { } music ? result with { Music = Music.MusicTagFile.Read(path, music, cancellationToken) } : result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is IOException or JsonException or OperationCanceledException or InvalidOperationException)
        {
            logger.LogWarning("Medya bilgileri okunamadı: {Path} ({Type})", path, ex.GetType().Name);
            return null;
        }
        finally { _operations.Release(); }
    }

    public async Task<ProbeResult> ProbeLiveRelayAsync(string input, CancellationToken token)
    {
        if (_ffprobe is null) throw new DomainException("Canlı yayın izlerini okumak için ffprobe gerekli.", 409);
        if (!Uri.TryCreate(input, UriKind.Absolute, out var uri) || uri.Scheme != "http" || uri.Host != "127.0.0.1"
            || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || !uri.AbsolutePath.Contains("/internal/live/", StringComparison.Ordinal))
            throw new DomainException("Canlı analiz yalnız özel yayın aracısı üzerinden yapılabilir.", 403);
        await _operations.WaitAsync(token);
        try
        {
            var text = await MediaProcess.RunAsync(_ffprobe,
                ["-v", "error", "-protocol_whitelist", "http,tcp,crypto", "-rw_timeout", "15000000", "-analyzeduration", "3000000", "-probesize", "4000000",
                 "-max_streams", "128", "-show_data", "-show_entries", ProbeEntries, "-of", "json", input], TimeSpan.FromSeconds(25), token);
            return ParseProbe(text);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is IOException or JsonException or OperationCanceledException or InvalidOperationException)
        { throw new DomainException("Canlı kaynağın izleri okunamadı. Bağlantı yeniden denenebilir.", 502); }
        finally { _operations.Release(); }
    }

    public static ProbeResult ParseProbe(string output)
    {
        using var json = JsonDocument.Parse(output);
        var root = json.RootElement;
        var format = Object(root, "format");
        var duration = Number(format, "duration");
        var streams = new List<MediaStreamInfo>();
        if (root.TryGetProperty("streams", out var values) && values.ValueKind == JsonValueKind.Array)
        {
            if (values.GetArrayLength() > 128) throw new DomainException("Dosya desteklenen iz sayısını aşıyor.");
            foreach (var value in values.EnumerateArray())
            {
                var kind = Text(value, "codec_type");
                var disposition = Object(value, "disposition");
                if (kind is not ("video" or "audio" or "subtitle") || Number(disposition, "attached_pic") == 1) continue;
                var tags = Object(value, "tags");
                var index = Int(value, "index");
                if (index is null or < 0) continue;
                streams.Add(new MediaStreamInfo(index.Value, kind, Text(value, "codec_name") ?? "unknown",
                    Sources.ReleaseEvaluator.Language(Text(tags, "language") ?? "und"), Text(tags, "title") ?? "",
                    Number(disposition, "default") == 1, Number(disposition, "forced") == 1, Number(disposition, "hearing_impaired") == 1,
                    Int(value, "width"), Int(value, "height"), Int(value, "channels"), Text(value, "channel_layout"), Int(value, "sample_rate"),
                    Long(value, "bit_rate"), Text(value, "profile"), Text(value, "pix_fmt"), Text(value, "color_transfer"), Text(value, "color_primaries"),
                    value.TryGetProperty("side_data_list", out var sideData) && sideData.EnumerateArray().Any(data => Text(data, "side_data_type")?.Contains("DOVI", StringComparison.OrdinalIgnoreCase) == true), Text(value, "color_space"),
                    Int(value, "level"), CodecIdentifiers.FrameRate(Text(value, "avg_frame_rate")), CodecIdentifiers.BitDepth(Text(value, "pix_fmt"), Int(value, "bits_per_raw_sample")),
                    Text(value, "codec_tag_string"), CodecIdentifiers.FromProbe(value)));
            }
        }
        var chapters = new List<MediaChapter>();
        if (root.TryGetProperty("chapters", out var chapterValues) && chapterValues.ValueKind == JsonValueKind.Array)
        {
            if (chapterValues.GetArrayLength() > 5000) throw new DomainException("Dosya desteklenen chapter sayısını aşıyor.");
            foreach (var chapter in chapterValues.EnumerateArray())
            {
                var start = Number(chapter, "start_time");
                var end = Number(chapter, "end_time");
                if (start is null or < 0 || end is null || end <= start) continue;
                chapters.Add(new MediaChapter(chapters.Count, start.Value, end.Value, Text(Object(chapter, "tags"), "title") ?? $"Chapter {chapters.Count + 1}"));
            }
        }
        var video = streams.FirstOrDefault(s => s.Kind == "video");
        var audio = streams.Where(s => s.Kind == "audio").OrderByDescending(s => s.IsDefault).FirstOrDefault();
        return new ProbeResult(duration is >= 0 ? duration : null, video?.Width, video?.Height, video?.Codec, audio?.Codec,
            streams.Where(s => s.Kind == "audio").Select(s => s.Language).Distinct().ToArray(), streams.ToArray(), chapters.ToArray(), Text(format, "format_name"), Long(format, "bit_rate"), Number(format, "start_time") ?? 0,
            video is null && audio is not null ? Music.MusicTags.Read(root) : null, AnalysisVersion: 1);
    }

    public async Task<double> KeyframeBeforeAsync(AnalyzedMedia media, int videoIndex, double position, CancellationToken token)
    {
        if (position <= 0 || _ffprobe is null) return 0;
        var start = Math.Max(0, position - 60);
        var interval = start.ToString("0.###", CultureInfo.InvariantCulture) + "%+65";
        await _operations.WaitAsync(token);
        try
        {
            var output = await MediaProcess.RunAsync(_ffprobe, ["-v", "error", "-protocol_whitelist", "file,pipe", "-format_whitelist", MediaProcess.MediaFormats,
                "-skip_frame", "nokey", "-select_streams", videoIndex.ToString(CultureInfo.InvariantCulture), "-read_intervals", interval,
                "-show_frames", "-show_entries", "frame=best_effort_timestamp_time", "-of", "json", media.Path], TimeSpan.FromSeconds(25), token);
            using var json = JsonDocument.Parse(output);
            if (!json.RootElement.TryGetProperty("frames", out var frames)) return 0;
            return frames.EnumerateArray().Select(frame => Number(frame, "best_effort_timestamp_time") - media.Probe.StartTime)
                .Where(value => value is >= 0 && value <= position).Select(value => value!.Value).DefaultIfEmpty(0).Max();
        }
        finally { _operations.Release(); }
    }

    public async Task<bool> ThumbnailAsync(string source, string mediaId, double? duration, CancellationToken cancellationToken)
    {
        if (_ffmpeg is null) return false;
        var output = Path.Combine(paths.Thumbnails, mediaId + ".jpg");
        var temporary = Path.Combine(paths.Thumbnails, mediaId + ".tmp.jpg");
        await _operations.WaitAsync(cancellationToken);
        try
        {
            var position = duration is > 1 ? Math.Min(15, duration.Value * 0.15) : 0;
            await MediaProcess.RunAsync(_ffmpeg, ["-hide_banner", "-loglevel", "error", "-nostdin", "-y",
                "-protocol_whitelist", "file,pipe", "-format_whitelist", MediaProcess.MediaFormats,
                "-ss", position.ToString("0.###", CultureInfo.InvariantCulture), "-i", source,
                "-frames:v", "1", "-an", "-vf", "scale=960:-2", "-threads", "1", "-q:v", "3", temporary], TimeSpan.FromSeconds(30), cancellationToken);
            if (!File.Exists(temporary)) return false;
            File.Move(temporary, output, true);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or InvalidOperationException)
        {
            logger.LogWarning("Önizleme oluşturulamadı: {Path} ({Type})", source, ex.GetType().Name);
            return false;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            _operations.Release();
        }
    }
    public async Task<bool> AudioArtworkAsync(string source, string mediaId, CancellationToken token)
    {
        if (_ffmpeg is null) return false;
        var path = Path.Combine(paths.Thumbnails, mediaId + ".tmp.jpg");
        await _operations.WaitAsync(token);
        try
        {
            await MediaProcess.RunAsync(_ffmpeg, ["-hide_banner", "-loglevel", "error", "-nostdin", "-y", "-protocol_whitelist", "file,pipe", "-format_whitelist", MediaProcess.MediaFormats,
                "-i", source, "-map", "0:v:0", "-frames:v", "1", "-an", "-vf", "scale=600:-2", "-threads", "1", path], TimeSpan.FromSeconds(20), token);
            if (!File.Exists(path)) return false;
            File.Move(path, Path.Combine(paths.Thumbnails, mediaId + ".jpg"), true); return true;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or OperationCanceledException) { return false; }
        finally { if (File.Exists(path)) File.Delete(path); _operations.Release(); }
    }

    private static JsonElement Object(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var result) ? result : default;
    private static string? Text(JsonElement value, string name) => Object(value, name) is { ValueKind: JsonValueKind.String } result ? result.GetString() : null;
    private static double? Number(JsonElement value, string name)
    {
        var property = Object(value, name);
        var raw = property.ValueKind == JsonValueKind.Number ? property.GetRawText() : property.ValueKind == JsonValueKind.String ? property.GetString() : null;
        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) && double.IsFinite(result) ? result : null;
    }
    private static int? Int(JsonElement value, string name) => Number(value, name) is { } result && result is >= 0 and <= int.MaxValue ? (int)result : null;
    private static long? Long(JsonElement value, string name) => Number(value, name) is { } result && result is >= 0 and < long.MaxValue ? (long)result : null;

    private static string? Find(string? configured, string name)
    {
        if (!string.IsNullOrWhiteSpace(configured)) return File.Exists(configured) ? Path.GetFullPath(configured) : null;
        var filename = OperatingSystem.IsWindows() ? name + ".exe" : name;
        var bundled = Path.Combine(AppContext.BaseDirectory, filename);
        if (File.Exists(bundled)) return bundled;
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(directory)) continue;
            var path = Path.Combine(directory.Trim('"'), filename);
            if (File.Exists(path)) return path;
        }
        return null;
    }
}
