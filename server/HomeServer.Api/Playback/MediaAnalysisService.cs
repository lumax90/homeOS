using HomeServer.Core;
using HomeServer.Infrastructure;

namespace HomeServer.Playback;

public sealed class MediaAnalysisService(MediaStore store, IMediaProbe tools)
{
    private readonly SemaphoreSlim[] _gates = Enumerable.Range(0, 32).Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    public async Task<AnalyzedMedia> InspectAsync(string mediaId, CancellationToken token, bool allowUnprobed = false)
    {
        var gate = _gates[(uint)mediaId.GetHashCode(StringComparison.Ordinal) % 32];
        await gate.WaitAsync(token);
        try
        {
            var media = store.GetMedia(mediaId) ?? throw new DomainException("Medya bulunamadı.", 404);
            var library = store.GetLibrary(media.LibraryId) ?? throw new DomainException("Kütüphane bağlı değil.", 404);
            var path = MediaPaths.ResolveFile(library.Path, media.RelativePath);
            var fingerprint = Fingerprint(new FileInfo(path));
            var probe = store.GetAnalysis(mediaId, fingerprint);
            if (probe is null)
            {
                if (!tools.FfprobeAvailable)
                {
                    if (allowUnprobed) return new AnalyzedMedia(media, path, fingerprint,
                        new ProbeResult(media.DurationSeconds, media.Width, media.Height, media.VideoCodec, media.AudioCodec));
                    throw new DomainException("Ses/altyazı izlerini okumak için ffprobe yapılandırılmalı.", 409);
                }
                probe = await tools.ProbeAsync(path, token) ?? throw new DomainException("Medya bilgileri okunamadı. Dosyayı ve ffprobe yapılandırmasını kontrol et.", 422);
                if (fingerprint != Fingerprint(new FileInfo(MediaPaths.ResolveFile(library.Path, media.RelativePath))))
                    throw new DomainException("Dosya analiz sırasında değişti. Yeniden dene.", 409);
                store.SaveAnalysis(mediaId, fingerprint, probe);
            }
            return new AnalyzedMedia(media, path, fingerprint, probe);
        }
        catch (IOException) { throw new DomainException("Medya dosyasına erişilemiyor. Kütüphane diskinin bağlı olduğunu kontrol et.", 404); }
        finally { gate.Release(); }
    }

    public static string Fingerprint(FileInfo file) => $"{file.Length:x}-{file.LastWriteTimeUtc.Ticks:x}";
}
