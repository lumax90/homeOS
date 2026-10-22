using System.Security.Cryptography;
using System.Text;
using HomeServer.Core;
using HomeServer.Infrastructure;
using HomeServer.Playback;

namespace HomeServer.Music;

public sealed class MusicPcmCache(HomePaths paths, MediaAnalysisService analysis, MediaTools tools, MediaStore store)
{
    public const int SampleRate = 48000;
    public const int Channels = 2;
    public const long MaximumFrames = 1800L * SampleRate;
    private const long CacheLimit = 4L * 1024 * 1024 * 1024;
    private readonly SemaphoreSlim _encoders = new(2, 2);
    private readonly SemaphoreSlim[] _keys = Enumerable.Range(0, 32).Select(_ => new SemaphoreSlim(1, 1)).ToArray();
    private readonly object _files = new();
    private bool _initialized;
    private string Root => AppFiles.DirectoryIn(paths.Data, "music-cache", "pcm");
    private static string Key(string mediaId, string fingerprint) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("pcm-v1:" + mediaId + ":" + fingerprint)));
    public async Task<MusicPcmInfo> PrepareAsync(string id, CancellationToken token)
    {
        var media = await analysis.InspectAsync(id, token);
        if (media.Media.Kind != "audio" || media.Probe.AudioCodec is null) throw new DomainException("Kesintisiz müzik için bir ses dosyası seç.");
        if (media.Probe.Duration is null or <= 0 or > 1800) throw new DomainException("Kesintisiz müzik önbelleği parça başına 30 dakika destekler. Özgün oynatıcıyı kullan.", 422);
        if (!tools.FfmpegAvailable) throw new DomainException("Kesintisiz ses hazırlığı için FFmpeg gerekli.", 409);
        var key = Key(id, media.Fingerprint); var path = Path.Combine(Root, key + ".pcm");
        var gate = _keys[(uint)key.GetHashCode(StringComparison.Ordinal) % 32]; await gate.WaitAsync(token);
        try
        {
            lock (_files)
            {
                if (!_initialized)
                {
                    foreach (var partial in Directory.EnumerateFiles(Root, "*.partial")) { AppFiles.RejectLink(partial); File.Delete(partial); }
                    _initialized = true;
                }
                if (File.Exists(path)) AppFiles.RejectLink(path);
            }
            if (!File.Exists(path))
            {
                if (!await _encoders.WaitAsync(TimeSpan.FromSeconds(5), token)) throw new DomainException("Ses hazırlama sırası dolu. Biraz sonra yeniden dene.", 429);
                var temporary = Path.Combine(Root, key + "." + Guid.NewGuid().ToString("N") + ".partial");
                try
                {
                    lock (_files) Prune(MaximumFrames * 8);
                    await MediaProcess.RunAsync(tools.FfmpegExecutable, ["-hide_banner", "-loglevel", "error", "-nostdin", "-threads", "2", "-protocol_whitelist", "file,pipe", "-format_whitelist", MediaProcess.MediaFormats,
                        "-i", media.Path, "-map", "0:a:0", "-vn", "-sn", "-dn", "-ac", "2", "-ar", "48000", "-c:a", "pcm_f32le", "-t", "1801", "-fs", (MaximumFrames * 8 + 8).ToString(System.Globalization.CultureInfo.InvariantCulture), "-f", "f32le", temporary], TimeSpan.FromMinutes(3), token);
                    var length = new FileInfo(temporary).Length;
                    if (length is <= 0 || length % 8 != 0 || length > MaximumFrames * 8) throw new DomainException("Hazırlanan ses parça sınırını aşıyor veya eksik.", 422);
                    if (MediaAnalysisService.Fingerprint(new FileInfo(media.Path)) != media.Fingerprint) throw new DomainException("Ses dosyası hazırlık sırasında değişti.", 409);
                    token.ThrowIfCancellationRequested();
                    lock (_files) { Prune(length); File.Move(temporary, path); }
                }
                catch (InvalidOperationException) { throw new DomainException("Ses PCM biçimine çözülemedi.", 422); }
                finally { if (File.Exists(temporary)) File.Delete(temporary); _encoders.Release(); }
            }
            long frames;
            lock (_files) { AppFiles.RejectLink(path); File.SetLastWriteTimeUtc(path, DateTime.UtcNow); frames = new FileInfo(path).Length / 8; }
            var tags = media.Probe.Music; var sonic = store.GetMusicSonic(id, media.Fingerprint);
            return new(id, media.Fingerprint, frames, SampleRate, Channels, tags?.ReplayGainDb, tags?.AlbumGainDb, tags?.TrackPeak ?? (sonic?.TruePeakDb is { } peak ? Math.Pow(10, peak / 20) : null), tags?.AlbumPeak, sonic?.LoudnessLufs,
                media.Media.Title, tags?.Artist ?? "", store.MusicAlbumForMedia(id));
        }
        finally { gate.Release(); }
    }
    private void Prune(long incoming)
    {
        var files = Directory.EnumerateFiles(Root, "*.pcm").Select(path => new FileInfo(path)).OrderBy(file => file.LastWriteTimeUtc).ToList();
        var size = files.Sum(file => file.Length);
        foreach (var file in files)
        {
            if (size + incoming <= CacheLimit) break;
            if (file.LastWriteTimeUtc >= DateTime.UtcNow.AddMinutes(-5)) continue;
            AppFiles.RejectLink(file.FullName);
            try { File.Delete(file.FullName); size -= file.Length; } catch (IOException) { }
        }
        if (size + incoming > CacheLimit) throw new DomainException("Kesintisiz ses önbelleği kullanımda; yer açılması bekleniyor.", 429);
    }
    public FileStream Open(string id, string fingerprint)
    {
        var media = store.GetMedia(id) ?? throw new DomainException("Ses dosyası bulunamadı.", 404);
        var library = store.GetLibrary(media.LibraryId) ?? throw new DomainException("Kütüphane çevrimdışı.", 404);
        var source = MediaPaths.ResolveFile(library.Path, media.RelativePath);
        if (MediaAnalysisService.Fingerprint(new FileInfo(source)) != fingerprint) throw new DomainException("Ses dosyası değişti; yeniden hazırlanmalı.", 409);
        lock (_files)
        {
            var path = Path.Combine(Root, Key(id, fingerprint) + ".pcm");
            if (!File.Exists(path)) throw new DomainException("Ses önbelleği yenilenmeli.", 410);
            AppFiles.RejectLink(path); File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
            return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.Asynchronous | FileOptions.RandomAccess);
        }
    }
    public async Task<byte[]> ReadAsync(string id, string fingerprint, long start, int frames, CancellationToken token)
    {
        if (start < 0 || frames is < 1 or > SampleRate * 10) throw new DomainException("PCM parça aralığı geçersiz.");
        await using var file = Open(id, fingerprint);
        if (start >= file.Length / 8 || frames > file.Length / 8 - start) throw new DomainException("PCM aralığı sesin dışında.", 416);
        var bytes = new byte[frames * 8]; file.Position = start * 8; await file.ReadExactlyAsync(bytes, token); return bytes;
    }
}
