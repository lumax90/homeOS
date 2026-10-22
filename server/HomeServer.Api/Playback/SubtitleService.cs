using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using HomeServer.Core;
using HomeServer.Infrastructure;
using Microsoft.Extensions.Caching.Memory;
using Source = HomeServer.Core.SubtitleSourceInfo;

namespace HomeServer.Playback;

public sealed partial class SubtitleService(HomePaths paths, MediaStore store, MediaTools tools) : IDisposable
{
    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = 16 * 1024 * 1024 });
    private readonly SemaphoreSlim _conversions = new(2, 2);
    private static readonly HashSet<string> TextCodecs = ["subrip", "srt", "webvtt", "ass", "ssa", "mov_text", "text"];
    private static readonly HashSet<string> BitmapCodecs = ["hdmv_pgs_subtitle", "dvd_subtitle", "dvb_subtitle", "xsub"];

    public IReadOnlyList<SubtitleTrack> List(AnalyzedMedia media) => Sources(media).Select(source => source.Track).ToArray();

    private IReadOnlyList<Source> Sources(AnalyzedMedia media, bool includeRetired = false)
    {
        var result = (media.Probe.Streams ?? []).Where(s => s.Kind == "subtitle").Select(s => new Source(
            new SubtitleTrack("embedded-" + s.Index, s.Language, s.Title, s.Codec, "embedded", s.Forced, s.HearingImpaired, TextCodecs.Contains(s.Codec), tools.FfmpegAvailable && (TextCodecs.Contains(s.Codec) || BitmapCodecs.Contains(s.Codec)), !TextCodecs.Contains(s.Codec)),
            media.Path, s.Index, media.Fingerprint + ":" + s.Index)).ToList();
        var parent = Path.GetDirectoryName(media.Path)!;
        var stem = Path.GetFileNameWithoutExtension(media.Path);
        var library = store.GetLibrary(media.Media.LibraryId) ?? throw new DomainException("Kütüphane bulunamadı.", 404);
        foreach (var path in Directory.EnumerateFiles(parent).Where(path =>
            (Path.GetFileNameWithoutExtension(path).Equals(stem, StringComparison.OrdinalIgnoreCase)
             || Path.GetFileNameWithoutExtension(path).StartsWith(stem + ".", StringComparison.OrdinalIgnoreCase)) && Extension(path) is "srt" or "vtt" or "ass" or "ssa").Take(64))
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) continue;
            var relative = Path.GetRelativePath(library.Path, path);
            var safe = MediaPaths.ResolveFile(library.Path, relative);
            var info = new FileInfo(safe);
            if (info.Length > 2 * 1024 * 1024) continue;
            var suffix = Path.GetFileNameWithoutExtension(path)[stem.Length..];
            var parts = suffix.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var language = parts.FirstOrDefault(part => part.Length is 2 or 3 && part is not ("hi" or "sdh")) ?? "und";
            var id = "sidecar-" + Hash(MediaPaths.Key(relative));
            var track = new SubtitleTrack(id, HomeServer.Sources.ReleaseEvaluator.Language(language), Path.GetFileName(path), Extension(path), "sidecar",
                parts.Contains("forced", StringComparer.OrdinalIgnoreCase), parts.Any(p => p.Equals("hi", StringComparison.OrdinalIgnoreCase) || p.Equals("sdh", StringComparison.OrdinalIgnoreCase)), true, tools.FfmpegAvailable);
            result.Add(new Source(track, safe, null, MediaAnalysisService.Fingerprint(info)));
        }
        foreach (var subtitle in store.GetManagedSubtitles(media.Media.Id, includeRetired))
        {
            if (subtitle.Fingerprint is not null && subtitle.Fingerprint != media.Fingerprint) continue;
            var directory = AppFiles.DirectoryIn(paths.State, "subtitles", AppFiles.Id(media.Media.Id));
            var filename = AppFiles.Id(subtitle.Id) + "." + subtitle.Extension;
            if (!File.Exists(Path.Combine(directory, filename))) continue;
            var path = MediaPaths.ResolveFile(directory, filename);
            result.Add(new Source(new SubtitleTrack("managed-" + subtitle.Id, subtitle.Language, subtitle.Title, subtitle.Extension, "managed", subtitle.Forced, subtitle.HearingImpaired, true, tools.FfmpegAvailable, false, subtitle.Score),
                path, null, MediaAnalysisService.Fingerprint(new FileInfo(path))));
        }
        return result;
    }
    public SubtitleSourceInfo GetSource(AnalyzedMedia media, string id, bool includeRetired = true) => Sources(media, includeRetired).FirstOrDefault(source => source.Track.Id == id) ?? throw new DomainException("Altyazı izi bulunamadı veya başka dosya sürümüne ait.", 404);

    public async Task<SubtitleBurnPlan> PrepareBurnAsync(AnalyzedMedia media, string id, string directory, double delay, CancellationToken token)
    {
        var source = GetSource(media, id);
        if (!source.Track.Burnable) throw new DomainException("Bu altyazı biçimi için görüntüye işleme desteklenmiyor.", 409);
        var video = media.Probe.Streams?.FirstOrDefault(stream => stream.Kind == "video");
        if (video?.Width is not > 0 or > 8192 || video.Height is not > 0 or > 8192 || (long)video.Width * video.Height > 34_000_000) throw new DomainException("Altyazı işlemek için geçerli video boyutları gerekli.", 409);
        if (source.Track.Image) return new(id, true, source.Index, delay, video.Width.Value, video.Height.Value);
        var input = source.Path;
        if (source.Index is null)
        {
            await using var file = File.OpenRead(input); var content = DecodeText(await Metadata.MetadataProvider.ReadLimitedAsync(file, 2 * 1024 * 1024, token));
            input = Path.Combine(directory, "caption-input." + source.Track.Format); await File.WriteAllTextAsync(input, content, new UTF8Encoding(false), token);
        }
        var target = Path.Combine(directory, "captions.ass");
        var args = new List<string> { "-hide_banner", "-loglevel", "error", "-nostdin", "-protocol_whitelist", "file,pipe", "-format_whitelist", source.Index is null ? "srt,webvtt,ass" : MediaProcess.MediaFormats,
            "-i", input, "-map", "0:" + (source.Index ?? 0), "-an", "-vn", "-c:s", "ass", "-fs", "4194305", "-f", "ass", target };
        await MediaProcess.RunAsync(tools.FfmpegExecutable, args, TimeSpan.FromSeconds(60), token);
        if (new FileInfo(target).Length is <= 0 or > 4 * 1024 * 1024) throw new DomainException("Altyazı işleme boyut sınırını aşıyor.", 413);
        if (source.Fingerprint != GetSource(media, id).Fingerprint) throw new DomainException("Altyazı hazırlık sırasında değişti.", 409);
        return new(id, false, null, delay, video.Width.Value, video.Height.Value, "captions.ass");
    }

    public async Task<string> WebVttAsync(AnalyzedMedia media, string id, double startSeconds, double delaySeconds, CancellationToken token)
    {
        if (!double.IsFinite(startSeconds) || startSeconds is < 0 or > 31_536_000 || !double.IsFinite(delaySeconds) || Math.Abs(delaySeconds) > 600)
            throw new DomainException("Altyazı zaman ayarı geçersiz.");
        var source = GetSource(media, id);
        if (!source.Track.Playable) throw new DomainException("Bu resim altyazısı tarayıcı metin izi olarak sunulamıyor.", 409);
        var key = media.Media.Id + ":" + id + ":" + source.Fingerprint;
        await _conversions.WaitAsync(token);
        try
        {
            if (!_cache.TryGetValue<string>(key, out var text))
            {
                text = await ConvertAsync(source, token);
                _cache.Set(key, text, new MemoryCacheEntryOptions { Size = text.Length * 2, SlidingExpiration = TimeSpan.FromMinutes(20) });
            }
            return ShiftWebVtt(text!, delaySeconds - startSeconds);
        }
        finally { _conversions.Release(); }
    }

    public async Task<ManagedSubtitle> UploadAsync(string mediaId, string language, string title, string extension, byte[] payload, CancellationToken token,
        string? stableId = null, bool forced = false, bool hearingImpaired = false, int? automationRevision = null, int? expectedMatchRevision = null, SubtitlePublication? publication = null)
    {
        var media = store.GetMedia(mediaId) ?? throw new DomainException("Medya bulunamadı.", 404);
        if (expectedMatchRevision is { } expected && media.MatchRevision != expected) throw new DomainException("Altyazı yayınından önce dosya eşleştirmesi değişti.", 409);
        if (media.Kind != "video") throw new DomainException("Altyazı için bir video dosyası seç.");
        language = HomeServer.Sources.ReleaseEvaluator.Language(language);
        if (!LanguageCode().IsMatch(language) || extension is not ("srt" or "vtt" or "ass" or "ssa")) throw new DomainException("Altyazı dili veya biçimi geçersiz.");
        if (payload.Length is 0 or > 2 * 1024 * 1024) throw new DomainException("Altyazı dosyası en fazla 2 MB olabilir.", 413);
        title = Path.GetFileName(title).Trim();
        if (title.Length is < 1 or > 160) throw new DomainException("Altyazı adı 1–160 karakter olmalı.");
        var existing = store.GetManagedSubtitles(mediaId);
        if (stableId is not null && store.GetManagedSubtitles(mediaId, true).FirstOrDefault(item => item.Id == stableId) is { } prior)
            return !prior.Retired ? prior : throw new DomainException("Bu altyazı geçmişte var; önce geri al.", 409);
        if (existing.Count - (publication?.SupersedesId is not null ? 1 : 0) >= 16) throw new DomainException("Bu medyada 16 etkin altyazı var. Önce gereksiz olanı kaldır.", 409);
        var normalized = DecodeText(payload);
        var directory = AppFiles.DirectoryIn(paths.State, "subtitles", AppFiles.Id(mediaId));
        var id = stableId is null ? Guid.NewGuid().ToString("N") : AppFiles.Id(stableId);
        var path = Path.Combine(directory, id + "." + extension);
        var created = false;
        try
        {
            if (File.Exists(path))
            {
                if (StateFolders.Hash(MediaPaths.ResolveFile(directory, Path.GetFileName(path))) != Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant())
                    throw new DomainException("Yönetilen altyazı hedefi farklı veri içeriyor.", 409);
            }
            else
            {
                await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None); created = true;
                await file.WriteAsync(Encoding.UTF8.GetBytes(normalized), token);
            }
            await _conversions.WaitAsync(token);
            try
            {
                var source = new Source(new SubtitleTrack("managed-" + id, language, title, extension, "managed", false, false, true), path, null, "");
                _ = await ConvertAsync(source, token);
            }
            finally { _conversions.Release(); }
            token.ThrowIfCancellationRequested();
            return store.AddManagedSubtitle(mediaId, id, language, title, extension, forced, hearingImpaired, automationRevision, expectedMatchRevision, publication, StateFolders.Hash(path));
        }
        catch
        {
            if (created && File.Exists(path)) File.Delete(path);
            throw;
        }
    }

    public void Remove(string mediaId, string id)
    {
        _ = store.GetMedia(mediaId) ?? throw new DomainException("Medya bulunamadı.", 404);
        var subtitle = store.GetManagedSubtitles(mediaId, true).FirstOrDefault(s => s.Id == id) ?? throw new DomainException("Yüklenmiş altyazı bulunamadı.", 404);
        var directory = AppFiles.DirectoryIn(paths.State, "subtitles", AppFiles.Id(mediaId));
        var path = Path.Combine(directory, AppFiles.Id(id) + "." + subtitle.Extension);
        if (File.Exists(path)) File.Delete(MediaPaths.ResolveFile(directory, Path.GetFileName(path)));
        store.RemoveManagedSubtitle(mediaId, id);
    }

    private async Task<string> ConvertAsync(Source source, CancellationToken token)
    {
        var directory = AppFiles.DirectoryIn(paths.Data, "subtitle-work", Guid.NewGuid().ToString("N"));
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, ".home-subtitle"), "Home.Subtitle.v1", token);
            var input = source.Path;
            var args = new List<string> { "-hide_banner", "-loglevel", "error", "-nostats", "-nostdin", "-protocol_whitelist", "file,pipe" };
            if (source.Index is null)
            {
                if (new FileInfo(input).Length > 2 * 1024 * 1024) throw new DomainException("Altyazı dosyası çok büyük.", 413);
                await using var stream = File.OpenRead(input);
                var text = DecodeText(await Metadata.MetadataProvider.ReadLimitedAsync(stream, 2 * 1024 * 1024, token));
                input = Path.Combine(directory, "input." + source.Track.Format);
                await File.WriteAllTextAsync(input, text, new UTF8Encoding(false), token);
                args.AddRange(["-format_whitelist", "srt,webvtt,ass", "-sub_charenc", "UTF-8"]);
            }
            else args.AddRange(["-format_whitelist", MediaProcess.MediaFormats]);
            var output = Path.Combine(directory, "captions.vtt");
            args.AddRange(["-i", input, "-map", "0:" + (source.Index ?? 0), "-vn", "-an", "-c:s", "webvtt", "-f", "webvtt", output]);
            await MediaProcess.RunAsync(tools.FfmpegExecutable, args, TimeSpan.FromSeconds(60), token);
            if (new FileInfo(output).Length > 4 * 1024 * 1024) throw new DomainException("Çıkarılan altyazı boyutu sınırı aşıyor.", 413);
            var result = await File.ReadAllTextAsync(output, token);
            if (!result.StartsWith("WEBVTT", StringComparison.Ordinal) || !result.Contains(" --> ", StringComparison.Ordinal))
                throw new DomainException("Altyazıda geçerli zamanlı metin bulunamadı.", 422);
            return result;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is InvalidOperationException or OperationCanceledException or IOException)
        { throw new DomainException("Altyazı WebVTT biçimine çevrilemedi. Dosya biçimini ve karakter kodlamasını kontrol et.", 422); }
        finally { if (Directory.Exists(directory)) AppFiles.DeleteOwnedDirectory(directory); }
    }

    public static string DecodeText(byte[] bytes)
    {
        try
        {
            var text = bytes.AsSpan().StartsWith(new byte[] { 0xff, 0xfe }) ? new UnicodeEncoding(false, true, true).GetString(bytes[2..])
                : bytes.AsSpan().StartsWith(new byte[] { 0xfe, 0xff }) ? new UnicodeEncoding(true, true, true).GetString(bytes[2..])
                : new UTF8Encoding(false, true).GetString(bytes).TrimStart('\ufeff');
            if (text.Contains('\0')) throw new DomainException("Altyazı metin dosyası olmalı.");
            return text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        }
        catch (DecoderFallbackException) { throw new DomainException("Altyazıyı UTF-8 veya UTF-16 olarak kaydedip yeniden ekle."); }
    }

    public static string ShiftWebVtt(string input, double offset)
    {
        var result = new StringBuilder("WEBVTT\n\n");
        foreach (var block in input.Replace("\r\n", "\n", StringComparison.Ordinal).Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            var lines = block.Split('\n');
            var index = Array.FindIndex(lines, line => line.Contains(" --> ", StringComparison.Ordinal));
            if (index < 0) continue;
            var timing = Timing().Match(lines[index]);
            if (!timing.Success) continue;
            var start = Seconds(timing.Groups[1].Value) + offset;
            var end = Seconds(timing.Groups[2].Value) + offset;
            if (!double.IsFinite(start) || !double.IsFinite(end) || end > 31_536_000 || end <= 0 || end <= start) continue;
            result.Append(Stamp(Math.Max(0, start))).Append(" --> ").Append(Stamp(end)).Append(timing.Groups[3].Value).Append('\n');
            foreach (var line in lines[(index + 1)..]) result.AppendLine(line);
            result.Append('\n');
        }
        return result.ToString();
    }
    private static double Seconds(string value)
    {
        var parts = value.Split(':');
        double result = 0;
        foreach (var part in parts)
        {
            if (!double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var component)) return double.NaN;
            result = result * 60 + component;
        }
        return result;
    }
    private static string Stamp(double value)
    {
        var total = (long)Math.Round(value * 1000);
        return $"{total / 3_600_000:00}:{total / 60_000 % 60:00}:{total / 1000 % 60:00}.{total % 1000:000}";
    }
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant()[..32];
    private static string Extension(string path) => Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
    [GeneratedRegex(@"^[a-z]{2,3}$", RegexOptions.NonBacktracking)] private static partial Regex LanguageCode();
    [GeneratedRegex(@"^((?:\d{2,}:)?\d{2}:\d{2}\.\d{3}) --> ((?:\d{2,}:)?\d{2}:\d{2}\.\d{3})(.*)$", RegexOptions.NonBacktracking)] private static partial Regex Timing();
    public void Dispose() { _cache.Dispose(); _conversions.Dispose(); }
}
