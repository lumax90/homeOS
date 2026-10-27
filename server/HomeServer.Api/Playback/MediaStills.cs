using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using HomeServer.Core;
using HomeServer.Infrastructure;

namespace HomeServer.Playback;

public sealed record MediaStill(int Index, double PositionSeconds, string Url);
public sealed record MediaStillGallery(string MediaId, string Fingerprint, IReadOnlyList<MediaStill> Images, string? Reason);

// Source media is read-only. Only bounded, independently cached JPEGs are written.
public sealed partial class MediaStills(HomePaths paths, MediaStore store, MediaAnalysisService analysis, MediaTools tools, EncodingSupport encoding)
{
    private const long MaximumImage = 2 * 1024 * 1024;
    private const long CacheBudget = 256 * 1024 * 1024;
    private readonly SemaphoreSlim _work = new(1, 1);
    private string CacheRoot()
    {
        var path = Path.Combine(paths.Thumbnails, "scene-stills-v1"); var existed = Directory.Exists(path);
        var root = AppFiles.DirectoryIn(paths.Thumbnails, "scene-stills-v1"); var marker = Path.Combine(root, ".home-scene-stills");
        if (!existed) File.WriteAllText(marker, "Home.SceneStills.v1");
        if (!File.Exists(marker))
            throw new DomainException("Sahne önbelleği sahipliği doğrulanamadı. Mevcut dosyalar korunuyor.", 409);
        AppFiles.RejectLink(marker);
        if (new FileInfo(marker).Length > 64 || File.ReadAllText(marker) != "Home.SceneStills.v1") throw new DomainException("Sahne önbelleği sahipliği doğrulanamadı. Mevcut dosyalar korunuyor.", 409);
        return root;
    }
    public static double[] Positions(double? duration) => duration is > 1 and <= 86400 && double.IsFinite(duration.Value)
        ? new[] { .1, .22, .34, .46, .58, .70 }.Select(fraction => Math.Round(Math.Min(duration.Value - .25, duration.Value * fraction), 3)).Distinct().ToArray() : [];
    public static bool NeedsToneMap(MediaStreamInfo video) => video.ColorTransfer is "smpte2084" or "arib-std-b67";
    public static bool SupportedVideo(MediaStreamInfo? video) => video is not null
        && (!(video.DolbyVision || video.DolbyVisionConfig is not null) || DolbyVisionBaseLayer.CanUseHdr10(video))
        && (!NeedsToneMap(video) || video.ColorPrimaries == "bt2020");
    private void RequireSource(string id, Account viewer)
    {
        var current = store.GetAccount(viewer.Id);
        if (current is null || !current.Enabled || store.GetMedia(id, current) is null) throw new DomainException("Medya bulunamadı veya bu kütüphaneye erişimin yok.", 404);
    }
    public async Task<MediaStillGallery> DescribeAsync(string id, Account viewer, CancellationToken token)
    {
        RequireSource(id, viewer);
        var media = await analysis.InspectAsync(id, token); var video = media.Probe.Streams?.FirstOrDefault(stream => stream.Kind == "video");
        if (media.Media.Kind != "video" || !SupportedVideo(video))
            return new(id, media.Fingerprint, [], "Bu kaynaktan doğru renklerle sahne fotoğrafı hazırlama doğrulanmadı.");
        if (!tools.FfmpegAvailable || NeedsToneMap(video!) && !(await encoding.InspectAsync(token)).ToneMapping)
            return new(id, media.Fingerprint, [], "Sahne fotoğrafları için gerekli FFmpeg filtreleri sunucuda kullanılamıyor.");
        return new(id, media.Fingerprint, Positions(media.Probe.Duration).Select((position, index) =>
            new MediaStill(index, position, $"/api/media/{id}/stills/{index}.jpg?fingerprint={Uri.EscapeDataString(media.Fingerprint)}")).ToArray(), null);
    }
    private static string Key(string id, string fingerprint) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(id + ":" + fingerprint)));
    public static string[] Arguments(AnalyzedMedia media, int index, string destination)
    {
        var positions = Positions(media.Probe.Duration);
        if (index < 0 || index >= positions.Length) throw new DomainException("Sahne fotoğrafı bulunamadı.", 404);
        var video = media.Probe.Streams?.FirstOrDefault(stream => stream.Kind == "video");
        if (!SupportedVideo(video)) throw new DomainException("Bu kaynaktan doğru renklerle sahne fotoğrafı hazırlama doğrulanmadı.", 409);
        var filter = NeedsToneMap(video!)
            ? EncodingSupport.ToneMapFilter + ",scale=960:-2"
            : "scale=960:-2";
        return ["-hide_banner", "-loglevel", "error", "-nostdin", "-protocol_whitelist", "file,pipe", "-format_whitelist", MediaProcess.MediaFormats,
            "-ss", positions[index].ToString("0.###", CultureInfo.InvariantCulture), "-i", media.Path, "-map", "0:" + video!.Index,
            "-frames:v", "1", "-an", "-sn", "-dn", "-vf", filter, "-threads", "1", "-q:v", "3", destination];
    }
    public async Task<string> GetAsync(string id, int index, string fingerprint, Account viewer, CancellationToken token)
    {
        RequireSource(id, viewer);
        if (index is < 0 or > 5 || fingerprint is not { Length: > 0 and <= 80 }) throw new DomainException("Sahne fotoğrafı bulunamadı.", 404);
        var media = await analysis.InspectAsync(id, token);
        if (fingerprint != media.Fingerprint) throw new DomainException("Film dosyası değişti. Sahne fotoğraflarını yenile.", 409);
        var gallery = await DescribeAsync(id, viewer, token);
        if (gallery.Fingerprint != fingerprint || index < 0 || index >= gallery.Images.Count)
            throw new DomainException(gallery.Reason ?? "Sahne fotoğrafı bulunamadı.", 409);
        if (!await _work.WaitAsync(TimeSpan.FromSeconds(30), token)) throw new DomainException("Sahne fotoğrafı hazırlığı meşgul. Yeniden deneyebilirsin.", 429);
        string? temporary = null;
        try
        {
            RequireSource(id, viewer);
            var admitted = await analysis.InspectAsync(id, token);
            if (admitted.Fingerprint != fingerprint) throw new DomainException("Film dosyası değişti. Sahne fotoğraflarını yenile.", 409);
            var root = CacheRoot(); var output = Path.Combine(root, Key(id, fingerprint) + "-" + index + ".jpg");
            if (File.Exists(output)) { AppFiles.RejectLink(output); return output; }
            Prune(root);
            if (new DriveInfo(Path.GetPathRoot(root)!).AvailableFreeSpace < MaximumImage + 64 * 1024 * 1024)
                throw new DomainException("Sahne fotoğrafı önbelleği için yeterli disk alanı yok.", 409);
            temporary = Path.Combine(root, Guid.NewGuid().ToString("N") + ".tmp.jpg");
            await MediaProcess.RunAsync(tools.FfmpegExecutable, Arguments(media, index, temporary), TimeSpan.FromSeconds(25), token);
            var file = new FileInfo(temporary);
            if (!file.Exists || file.Length is < 4 or > MaximumImage) throw new DomainException("Sahne fotoğrafı oluşturulamadı.", 422);
            await using (var image = File.OpenRead(temporary))
            {
                var header = new byte[3]; if (await image.ReadAsync(header, token) != 3 || header[0] != 0xff || header[1] != 0xd8 || header[2] != 0xff)
                    throw new DomainException("Sahne fotoğrafı biçimi geçersiz.", 422);
            }
            var latest = await analysis.InspectAsync(id, token);
            if (latest.Fingerprint != fingerprint) throw new DomainException("Film dosyası değişti. Sahne fotoğraflarını yenile.", 409);
            RequireSource(id, viewer);
            File.Move(temporary, output, overwrite: false); temporary = null; return output;
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new DomainException("Sahne fotoğrafı hazırlığı zaman aşımına uğradı.", 504); }
        catch (InvalidOperationException) { throw new DomainException("Sahne fotoğrafı oluşturulamadı. Yeniden deneyebilirsin.", 422); }
        finally { if (temporary is not null && File.Exists(temporary)) File.Delete(temporary); _work.Release(); }
    }
    private static void Prune(string root)
    {
        foreach (var temporary in Directory.EnumerateFiles(root, "*.tmp.jpg").Where(path => Guid.TryParseExact(Path.GetFileName(path).Replace(".tmp.jpg", ""), "N", out _)))
        { AppFiles.RejectLink(temporary); File.Delete(temporary); } // Prior process's abandoned work; only one writer owns this marked cache.
        var files = Directory.EnumerateFiles(root, "*.jpg").Where(path => OwnedImage().IsMatch(Path.GetFileName(path))).Select(path => new FileInfo(path)).OrderBy(file => file.LastWriteTimeUtc).ToArray();
        foreach (var file in files) AppFiles.RejectLink(file.FullName);
        var total = files.Sum(file => file.Length);
        foreach (var file in files)
        {
            if (total <= CacheBudget - MaximumImage) break;
            AppFiles.RejectLink(file.FullName); total -= file.Length; file.Delete();
        }
    }
    [GeneratedRegex(@"^[a-f0-9]{64}-[0-5]\.jpg$")] private static partial Regex OwnedImage();
}
