using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HomeServer.Core;
using HomeServer.Infrastructure;
using HomeServer.Playback;

namespace HomeServer.Offline;

public sealed class OfflineService(HomePaths paths, MediaStore store, MediaAnalysisService analysis, MediaTools tools, SubtitleService subtitles, EncodingSupport encoding, EventHub events, ILogger<OfflineService> logger) : BackgroundService
{
    public const int ChunkBytes = 2 * 1024 * 1024;
    public const long MaximumFileBytes = 32L * 1024 * 1024 * 1024;
    private const long CacheBudget = 16L * 1024 * 1024 * 1024;
    private sealed record ProcessMarker(int Id, long StartedAt, string Executable);
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _active = new();
    private readonly ConcurrentDictionary<string, int> _readers = new();
    private readonly SemaphoreSlim _transfers = new(6, 6);
    private string Root => AppFiles.DirectoryIn(paths.Data, "offline-exports");

    public async Task<OfflineExport> QueueAsync(string mediaId, PlaybackActor actor, OfflineInput input, CancellationToken token)
    {
        if (input.Profile is not ("original" or "compatible-720" or "compatible-1080") || !double.IsFinite(input.SubtitleDelaySeconds) || Math.Abs(input.SubtitleDelaySeconds) > 600) throw new DomainException("Çevrimdışı kopya tercihi geçersiz.");
        var media = await analysis.InspectAsync(mediaId, token);
        if (media.Probe.Duration is not > 0 or > 86400 || media.Probe.Streams?.Any(stream => stream.Kind is "video" or "audio") != true) throw new DomainException("Çevrimdışı kopya için süresi ve izleri okunmuş medya gerekli.", 409);
        if (input.Profile != "original" && media.Probe.Duration > 14400) throw new DomainException("Uyumlu çevrimdışı hazırlık en fazla dört saat destekler.", 409);
        if (input.AudioStream is { } audio && media.Probe.Streams?.Any(stream => stream.Kind == "audio" && stream.Index == audio) != true) throw new DomainException("Ses izi bu dosyada yok.");
        var source = input.SubtitleId is null ? null : subtitles.GetSource(media, input.SubtitleId, false);
        if (input.Profile == "original" && (input.AudioStream is not null || input.BurnSubtitle || source?.Track.Image == true)) throw new DomainException("Ses izi seçimi veya altyazı işleme için uyumlu kopya seç.", 409);
        input = input with { SubtitleFingerprint = source?.Fingerprint, ExpectedUserId = null, ExpectedSessionId = null };
        var originalBytes = new FileInfo(media.Path).Length;
        var bitrate = media.Media.Kind == "audio" ? 192000 : input.Profile == "compatible-1080" ? 6000000 : 3000000;
        var reservation = input.Profile == "original" ? originalBytes + 4L * 1024 * 1024 : (long)(media.Probe.Duration.Value * bitrate / 8 * 1.3) + 16L * 1024 * 1024;
        if (originalBytes > MaximumFileBytes || reservation > MaximumFileBytes) throw new DomainException("Çevrimdışı kopya dosya başına 32 GiB sınırını aşıyor.", 413);
        var result = store.QueueOfflineExport(actor, media, input, reservation, DateTimeOffset.UtcNow); events.Publish("offline.changed"); return result;
    }
    public void Interrupt(string? id = null)
    {
        foreach (var (key, cancellation) in _active) if (id is null || id == key) try { cancellation.Cancel(); } catch (ObjectDisposedException) { }
    }
    public void InterruptUser(string userId)
    {
        foreach (var id in _active.Keys) if (store.GetOfflineExport(id)?.UserId == userId) Interrupt(id);
    }
    public OfflineManifest Manifest(string id, PlaybackActor actor)
    {
        var job = store.RequireOfflineExport(id, actor);
        if (job.Status != "ready" || job.Manifest is null) throw new DomainException("Cihaz kopyası henüz hazır değil.", 409);
        var media = store.GetMedia(job.MediaId, store.GetAccount(actor.UserId))!;
        return job.Manifest with { ExpiresAt = job.ExpiresAt, ProgressStamp = store.ProgressStamp(job.MediaId, actor.UserId), PositionSeconds = media.PositionSeconds, IsWatched = media.IsWatched };
    }
    public OfflineExport Describe(string id, PlaybackActor actor)
    {
        var job = store.RequireOfflineExport(id, actor, true);
        if (job.Status != "ready" || job.Manifest is null) return job;
        var missing = job.Manifest.Assets.Any(asset => (asset.Id != "media" || job.Input.Profile != "original") && !File.Exists(Path.Combine(Root, job.Id, asset.Id == "media" ? "media.mp4" : "captions.vtt")));
        return job with { RequiresPreparation = missing };
    }
    public async Task<(byte[] Data, string Hash)> ReadChunkAsync(string id, string assetId, int index, string fingerprint, PlaybackActor actor, CancellationToken token)
    {
        if (index < 0 || assetId is not ("media" or "subtitle")) throw new DomainException("Çevrimdışı parça adresi geçersiz.");
        if (!await _transfers.WaitAsync(0, token)) throw new DomainException("Cihaza aktarım sırası dolu.", 429);
        var entered = false;
        try
        {
            var count = _readers.AddOrUpdate(actor.DeviceId, 1, (_, current) => current + 1); entered = true;
            if (count > 2) throw new DomainException("Bu cihazda çok fazla aktarım isteği var.", 429);
            var job = store.RequireOfflineExport(id, actor); var manifest = Manifest(id, actor);
            var asset = manifest.Assets.FirstOrDefault(item => item.Id == assetId) ?? throw new DomainException("Çevrimdışı dosya izi bulunamadı.", 404);
            if (asset.Fingerprint != fingerprint || index >= asset.Chunks) throw new DomainException("Çevrimdışı parça sürümü değişti.", 409);
            var path = assetId == "media" && job.Input.Profile == "original" ? store.PlaybackSource(job.MediaId, store.GetAccount(actor.UserId)!).Path
                : MediaPaths.ResolveFile(Root, Path.Combine(AppFiles.Id(id), assetId == "media" ? "media.mp4" : "captions.vtt"));
            if (MediaAnalysisService.Fingerprint(new FileInfo(path)) != asset.Fingerprint) throw new DomainException("Cihaza aktarılacak dosya değişti; yeniden hazırla.", 409);
            await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.RandomAccess);
            var offset = (long)index * ChunkBytes; var bytes = new byte[(int)Math.Min(ChunkBytes, asset.Bytes - offset)]; file.Position = offset; await file.ReadExactlyAsync(bytes, token);
            if (MediaAnalysisService.Fingerprint(new FileInfo(path)) != asset.Fingerprint) throw new DomainException("Aktarım sırasında dosya değişti.", 409);
            store.RequireOfflineExport(id, actor); token.ThrowIfCancellationRequested();
            return (bytes, Convert.ToHexStringLower(SHA256.HashData(bytes)));
        }
        catch (FileNotFoundException) { throw new DomainException("Sunucu kopyası yeniden hazırlanmalı veya kaynak disk bağlanmalı.", 410); }
        finally { if (entered) _readers.AddOrUpdate(actor.DeviceId, 0, (_, current) => Math.Max(0, current - 1)); _transfers.Release(); }
    }
    public async Task<bool> RunOnceAsync(CancellationToken stopping)
    {
        var job = store.ClaimOfflineExport(); if (job is null) return false;
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(stopping); budget.CancelAfter(TimeSpan.FromHours(1)); _active[job.Id] = budget;
        string? directory = null;
        try
        {
            var media = await SourceAsync(job, budget.Token); directory = Path.Combine(Root, AppFiles.Id(job.Id));
            if (Directory.Exists(directory)) DeleteWork(directory);
            directory = AppFiles.DirectoryIn(Root, job.Id); await File.WriteAllTextAsync(Path.Combine(directory, ".home-offline"), "Home.Offline.v1", budget.Token);
            var assets = new List<OfflineAsset>(); var input = job.Input; var source = input.SubtitleId is null ? null : subtitles.GetSource(media, input.SubtitleId, false);
            var burn = source is not null && (source.Track.Image || input.BurnSubtitle);
            var mediaPath = media.Path; var mime = media.Media.MimeType;
            if (input.Profile != "original")
            {
                var cacheBytes = CacheBytes();
                if (cacheBytes + job.ReservedBytes > CacheBudget || new DriveInfo(Path.GetPathRoot(Root)!).AvailableFreeSpace < job.ReservedBytes + 256L * 1024 * 1024) throw new DomainException("Çevrimdışı hazırlık cache'i veya disk alanı dolu; eski hazırlıkları kaldır.", 409);
                var hdr = media.Probe.Streams?.Any(stream => stream.Kind == "video" && stream.ColorTransfer is "smpte2084" or "arib-std-b67") == true;
                var tone = hdr && (await encoding.InspectAsync(budget.Token)).ToneMapping;
                var request = new PlaybackStart(media.Media.Id, new(["mp4", "m4a"], ["h264"], ["aac"], true), AudioStream: input.AudioStream,
                    MaxHeight: input.Profile == "compatible-1080" ? 1080 : 720, MaxBitrate: input.Profile == "compatible-1080" ? 6000000 : 3000000,
                    ForceCompatible: true, SubtitleId: burn ? input.SubtitleId : null, BurnSubtitle: burn, SubtitleDelaySeconds: input.SubtitleDelaySeconds);
                var plan = PlaybackPlanner.Decide(media, request, tools.FfmpegAvailable, job.ReservedBytes * 2, tone, burn ? source?.Track : null, completeFile: true);
                if (burn) plan = plan with { Subtitle = await subtitles.PrepareBurnAsync(media, input.SubtitleId!, directory, input.SubtitleDelaySeconds, budget.Token) };
                var encoder = plan.VideoStream is null ? "libx264" : await encoding.EncoderAsync(budget.Token);
                var temporary = Path.Combine(directory, "media.partial.mp4");
                for (var attempt = 0; ; attempt++)
                {
                    try
                    {
                        var arguments = PlaybackManager.EncodingArguments(media.Path, 0, plan, encoder);
                        arguments.AddRange(["-progress", "pipe:1", "-t", media.Probe.Duration!.Value.ToString("0.######", CultureInfo.InvariantCulture), "-fs", (job.ReservedBytes - 4 * 1024 * 1024).ToString(CultureInfo.InvariantCulture), "-movflags", "+faststart", "-f", "mp4", temporary]);
                        await EncodeAsync(job, directory, arguments, media.Probe.Duration.Value, budget.Token); break;
                    }
                    catch (Exception ex) when (encoder == "h264_nvenc" && attempt == 0 && !budget.IsCancellationRequested && ex is InvalidOperationException or DomainException)
                    { encoding.HardwareFailed(); encoder = "libx264"; if (File.Exists(temporary)) File.Delete(temporary); }
                }
                var probe = await tools.ProbeAsync(temporary, budget.Token);
                if (probe?.Duration is not > 0 || Math.Abs(probe.Duration.Value - media.Probe.Duration!.Value) > Math.Max(1, media.Probe.Duration.Value * .01)
                    || media.Media.Kind == "video" && probe.VideoCodec != "h264" || media.Probe.AudioCodec is not null && probe.AudioCodec != "aac") throw new DomainException("Çevrimdışı çıktı süre/codec doğrulamasını geçmedi.", 422);
                mime = media.Media.Kind == "audio" ? "audio/mp4" : "video/mp4"; mediaPath = temporary;
            }
            assets.Add(Asset("media", mime, mediaPath));
            if (source is not null && !burn)
            {
                var text = await subtitles.WebVttAsync(media, source.Track.Id, 0, input.SubtitleDelaySeconds, budget.Token);
                var captionPath = Path.Combine(directory, "captions.vtt"); await File.WriteAllTextAsync(captionPath, text, new UTF8Encoding(false), budget.Token);
                assets.Add(Asset("subtitle", "text/vtt; charset=utf-8", captionPath) with { Language = source.Track.Language, Title = source.Track.Title });
            }
            _ = await SourceAsync(job, budget.Token); budget.Token.ThrowIfCancellationRequested();
            var item = store.GetMedia(job.MediaId, store.GetAccount(job.UserId))!;
            var manifest = new OfflineManifest(job.Id, job.MediaId, item.Title, item.Kind, item.LibraryName, media.Probe.Duration!.Value, job.SourceFingerprint, job.MatchRevision, job.ExpiresAt,
                assets.ToArray(), store.ProgressStamp(job.MediaId, job.UserId), item.PositionSeconds, item.IsWatched);
            store.CompleteOfflineExport(job, manifest, () => { if (job.Input.Profile != "original") File.Move(mediaPath, Path.Combine(directory, "media.mp4")); });
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
        catch (Exception ex)
        {
            store.FailOfflineExport(job, ex is DomainException ? ex.Message : ex is OperationCanceledException ? "Çevrimdışı hazırlık iptal edildi veya süre sınırına ulaştı." : "Cihaz kopyası hazırlanamadı.");
            if (directory is not null && Directory.Exists(directory)) try { DeleteWork(directory); } catch (Exception cleanup) when (cleanup is IOException or DomainException) { }
        }
        finally { _active.TryRemove(job.Id, out _); events.Publish("offline.changed"); }
        return true;
    }
    private async Task<AnalyzedMedia> SourceAsync(OfflineExport job, CancellationToken token)
    {
        if (!store.OfflineWorkCurrent(job)) throw new OperationCanceledException(token);
        var media = await analysis.InspectAsync(job.MediaId, token);
        if (media.Fingerprint != job.SourceFingerprint || media.Media.MatchRevision != job.MatchRevision || job.Input.SubtitleId is { } id && subtitles.GetSource(media, id, false).Fingerprint != job.Input.SubtitleFingerprint) throw new DomainException("Kaynak dosya veya altyazı hazırlık sırasında değişti.", 409);
        return media;
    }
    private static OfflineAsset Asset(string id, string mime, string path)
    {
        var info = new FileInfo(path); return new(id, mime, info.Length, MediaAnalysisService.Fingerprint(info), ChunkBytes, (int)((info.Length + ChunkBytes - 1) / ChunkBytes));
    }
    private async Task EncodeAsync(OfflineExport job, string directory, List<string> arguments, double duration, CancellationToken token)
    {
        using var process = MediaProcess.Start(tools.FfmpegExecutable, arguments, workingDirectory: directory);
        var errors = MediaProcess.ReadErrorTailAsync(process.StandardError);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        var progress = ReadProgress();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, ".home-process.json"), JsonSerializer.Serialize(new ProcessMarker(process.Id, process.StartTime.ToUniversalTime().Ticks, tools.FfmpegExecutable)), token);
            var completed = process.WaitForExitAsync(cancellation.Token);
            while (!completed.IsCompleted)
            {
                await Task.WhenAny(completed, Task.Delay(500, cancellation.Token)); cancellation.Token.ThrowIfCancellationRequested();
                if (!store.OfflineWorkCurrent(job)) { cancellation.Cancel(); throw new OperationCanceledException(cancellation.Token); }
            }
            await completed; await progress; await errors;
            if (process.ExitCode != 0) throw new DomainException("FFmpeg çevrimdışı kopyayı tamamlayamadı.", 422);
        }
        finally
        {
            MediaProcess.Kill(process); await process.WaitForExitAsync(CancellationToken.None); cancellation.Cancel();
            try { await progress; } catch (OperationCanceledException) { } await errors;
        }
        async Task ReadProgress()
        {
            var last = -1;
            while (await process.StandardOutput.ReadLineAsync(cancellation.Token) is { } line)
            {
                if (!line.StartsWith("out_time_us=", StringComparison.Ordinal) || !long.TryParse(line[12..], out var microseconds)) continue;
                var percent = Math.Clamp((int)(microseconds / 1_000_000d / duration * 95), 0, 95);
                if (percent > last) { store.OfflineProgress(job, percent); last = percent; }
            }
        }
    }
    private long CacheBytes() => Directory.EnumerateDirectories(Root).Where(path => Guid.TryParseExact(Path.GetFileName(path), "N", out _)).Sum(path =>
    {
        AppFiles.RejectLink(path); return Directory.EnumerateFiles(path).Sum(file => { AppFiles.RejectLink(file); return new FileInfo(file).Length; });
    });
    private static void DeleteWork(string directory)
    {
        AppFiles.RejectLink(directory); var marker = Path.Combine(directory, ".home-offline");
        if (!File.Exists(marker)) throw new DomainException("Çevrimdışı çalışma alanı Home'a ait değil.", 409);
        AppFiles.RejectLink(marker); if (File.ReadAllText(marker) != "Home.Offline.v1") throw new DomainException("Çevrimdışı çalışma işareti farklı.", 409);
        AppFiles.DeleteOwnedDirectory(directory);
    }
    private async Task RecoverWorkAsync(CancellationToken token)
    {
        foreach (var directory in Directory.EnumerateDirectories(Root))
        {
            if (!Guid.TryParseExact(Path.GetFileName(directory), "N", out _) || !File.Exists(Path.Combine(directory, ".home-offline"))) continue;
            AppFiles.RejectLink(directory); var marker = Path.Combine(directory, ".home-process.json");
            if (File.Exists(marker))
            {
                AppFiles.RejectLink(marker); ProcessMarker? previous = null;
                try { previous = JsonSerializer.Deserialize<ProcessMarker>(await File.ReadAllTextAsync(marker, token)); } catch (JsonException) { }
                if (previous is not null) try
                {
                    using var process = Process.GetProcessById(previous.Id);
                    if (process.StartTime.ToUniversalTime().Ticks == previous.StartedAt && process.MainModule?.FileName == previous.Executable) { MediaProcess.Kill(process); await process.WaitForExitAsync(token); }
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
            }
            if (store.GetOfflineExport(Path.GetFileName(directory))?.Status is not "ready") DeleteWork(directory);
        }
    }
    public override async Task StartAsync(CancellationToken token) { await RecoverWorkAsync(token); store.RecoverOfflineExports(); await base.StartAsync(token); }
    protected override async Task ExecuteAsync(CancellationToken token)
    {
        var lastCleanup = DateTimeOffset.UtcNow;
        while (!token.IsCancellationRequested)
        {
            try
            {
                var worked = await RunOnceAsync(token);
                if (DateTimeOffset.UtcNow - lastCleanup > TimeSpan.FromMinutes(1))
                {
                    store.ReconcileOfflineExports();
                    foreach (var job in store.OfflineCleanupCandidates()) { var directory = Path.Combine(Root, job.Id); if (Directory.Exists(directory)) DeleteWork(directory); }
                    store.PruneOfflineExports(); lastCleanup = DateTimeOffset.UtcNow;
                }
                await Task.Delay(worked ? 250 : 3000, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogWarning("Çevrimdışı hazırlık: {Type}", ex.GetType().Name); try { await Task.Delay(5000, token); } catch (OperationCanceledException) { break; } }
        }
    }
}
