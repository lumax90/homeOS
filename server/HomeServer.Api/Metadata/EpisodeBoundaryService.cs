using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using HomeServer.Core;
using HomeServer.Infrastructure;
using HomeServer.Playback;

namespace HomeServer.Metadata;

public sealed partial class EpisodeBoundaryService(HomePaths paths, MediaStore store, MediaAnalysisService analysis, MediaTools tools, EventHub events, ILogger<EpisodeBoundaryService> logger) : BackgroundService
{
    private sealed record ProcessStamp(int Id, long StartedAt, string Executable);
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _active = new();
    private string Root => AppFiles.DirectoryIn(paths.Data, "episode-boundaries");
    public void Interrupt(string id) { if (_active.TryGetValue(id, out var cancellation)) try { cancellation.Cancel(); } catch (ObjectDisposedException) { } }
    public async Task<EpisodeBoundaryJob> QueueAsync(string id, int revision, Account actor, CancellationToken token)
    { var inspected = await analysis.InspectAsync(id, token); var result = store.QueueEpisodeBoundary(inspected, revision, actor); events.Publish("catalog.changed"); return result; }
    public async Task<bool> RunOnceAsync(CancellationToken stopping)
    {
        var work = store.ClaimEpisodeBoundary(); if (work is null) return false;
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(stopping); budget.CancelAfter(TimeSpan.FromMinutes(3)); _active[work.Job.Id] = budget;
        string? directory = null;
        try
        {
            RequireSource(work); var inspected = await analysis.InspectAsync(work.Job.MediaId, budget.Token);
            var duration = inspected.Probe.Duration ?? 0; var chapters = inspected.Probe.Chapters ?? []; var hasAudio = inspected.Probe.Streams?.Any(stream => stream.Kind == "audio") == true;
            var result = EpisodeBoundaryDetector.Detect(work.Episodes, duration, chapters, [], [], hasAudio);
            if (!result.CanApply)
            {
                if (!tools.FfmpegAvailable) throw new DomainException("Sahne işaretleri bölüm sınırını vermedi; görüntü/ses taraması için FFmpeg gerekli.", 409);
                directory = AppFiles.DirectoryIn(Root, work.Job.Id); await File.WriteAllTextAsync(Path.Combine(directory, ".home-boundary"), "Home.EpisodeBoundary.v1", budget.Token);
                var signals = await ScanAsync(work, inspected, directory, budget.Token);
                result = EpisodeBoundaryDetector.Detect(work.Episodes, duration, chapters, signals.Black, signals.Silence, hasAudio);
            }
            budget.Token.ThrowIfCancellationRequested(); RequireSource(work); store.FinishEpisodeBoundary(work, result);
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
        catch (Exception ex) { store.FinishEpisodeBoundary(work, null, ex is DomainException ? ex.Message : ex is OperationCanceledException ? "Bölüm analizi iptal edildi veya üç dakikalık işlem sınırına ulaştı." : "Bölüm sınırı dosyadan okunamadı."); }
        finally
        {
            _active.TryRemove(work.Job.Id, out _);
            if (directory is not null) try { DeleteWork(directory); } catch (Exception ex) when (ex is IOException or DomainException) { logger.LogWarning("Bölüm analiz alanı temizlenemedi: {Type}", ex.GetType().Name); }
            events.Publish("catalog.changed");
        }
        return true;
    }
    private void RequireSource(EpisodeBoundaryWork work)
    {
        if (!store.EpisodeBoundaryCurrent(work)) throw new OperationCanceledException();
        if (store.PlaybackSource(work.Job.MediaId, store.GetAccount(work.UserId)!).Fingerprint != work.Job.Fingerprint) throw new DomainException("Dosya analiz sırasında değişti; yeni analiz gerekli.", 409);
    }
    private async Task<(List<MediaInterval> Black, List<MediaInterval> Silence)> ScanAsync(EpisodeBoundaryWork work, AnalyzedMedia media, string directory, CancellationToken token)
    {
        var video = media.Probe.Streams?.FirstOrDefault(stream => stream.Kind == "video") ?? throw new DomainException("Video izi bulunamadı.", 409);
        var audio = media.Probe.Streams?.Where(stream => stream.Kind == "audio").OrderByDescending(stream => stream.IsDefault).FirstOrDefault();
        var filter = $"[0:{video.Index}]scale=320:-2,fps=4,blackdetect=d=0.25:pic_th=0.98:pix_th=0.10[v]";
        if (audio is not null) filter += $";[0:{audio.Index}]aformat=channel_layouts=mono,silencedetect=n=-45dB:d=0.25[a]";
        var args = new List<string> { "-hide_banner", "-loglevel", "info", "-nostdin", "-nostats", "-threads", "2", "-protocol_whitelist", "file,pipe", "-format_whitelist", MediaProcess.MediaFormats,
            "-i", media.Path, "-filter_complex_threads", "1", "-filter_complex", filter, "-map", "[v]" };
        if (audio is not null) args.AddRange(["-map", "[a]"]); args.AddRange(["-progress", "pipe:1", "-f", "null", "-"]);
        using var process = MediaProcess.Start(tools.FfmpegExecutable, args); var black = new List<MediaInterval>(); var silence = new List<MediaInterval>();
        var logs = ReadLogs(); var progress = ReadProgress();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, ".home-process.json"), JsonSerializer.Serialize(new ProcessStamp(process.Id, process.StartTime.ToUniversalTime().Ticks, tools.FfmpegExecutable)), token);
            var exit = process.WaitForExitAsync(token);
            while (!exit.IsCompleted)
            {
                await Task.WhenAny(exit, Task.Delay(300, token)); token.ThrowIfCancellationRequested();
                if (logs.IsFaulted) await logs; if (progress.IsFaulted) await progress;
                RequireSource(work);
            }
            await exit; await logs; await progress;
            if (process.ExitCode != 0) throw new DomainException("FFmpeg bölüm sınırı taramasını tamamlayamadı.", 422);
            return (black, silence);
        }
        finally
        {
            MediaProcess.Kill(process); await process.WaitForExitAsync(CancellationToken.None);
            try { await logs; } catch (OperationCanceledException) { } catch (DomainException) { }
            try { await progress; } catch (OperationCanceledException) { }
        }
        async Task ReadLogs()
        {
            double? quiet = null;
            while (await process.StandardError.ReadLineAsync(token) is { } line)
            {
                if (line.Length > 32768 || black.Count + silence.Count >= 20000) throw new DomainException("Dosya çok fazla sahne/ses sınırı bildiriyor.");
                var match = Black().Match(line);
                if (match.Success && Number(match.Groups[1].Value) is { } start && Number(match.Groups[2].Value) is { } end) black.Add(new(start, end));
                var silent = Silence().Match(line);
                if (silent.Success && Number(silent.Groups[2].Value) is { } point)
                { if (silent.Groups[1].Value == "start") quiet = point; else if (quiet is { } first) { silence.Add(new(first, point)); quiet = null; } }
            }
        }
        async Task ReadProgress()
        {
            var last = 0;
            while (await process.StandardOutput.ReadLineAsync(token) is { } line)
                if (line.StartsWith("out_time_us=", StringComparison.Ordinal) && long.TryParse(line[12..], out var value) && media.Probe.Duration is > 0)
                { var percent = Math.Clamp((int)(value / 1_000_000d / media.Probe.Duration.Value * 90), 5, 95); if (percent >= last + 5) { store.UpdateEpisodeBoundaryProgress(work, percent); last = percent; } }
        }
    }
    private static double? Number(string value) => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) && double.IsFinite(result) ? result : null;
    [GeneratedRegex(@"black_start:\s*([\d.eE+\-]+)\s+black_end:\s*([\d.eE+\-]+)", RegexOptions.CultureInvariant)] private static partial Regex Black();
    [GeneratedRegex(@"silence_(start|end):\s*([\d.eE+\-]+)", RegexOptions.CultureInvariant)] private static partial Regex Silence();
    private static void DeleteWork(string directory)
    {
        AppFiles.RejectLink(directory); var marker = MediaPaths.ResolveFile(directory, ".home-boundary");
        if (File.ReadAllText(marker) != "Home.EpisodeBoundary.v1") throw new DomainException("Bölüm analiz alanı Home'a ait değil.", 409); AppFiles.DeleteOwnedDirectory(directory);
    }
    public override async Task StartAsync(CancellationToken token)
    {
        foreach (var directory in Directory.EnumerateDirectories(Root))
        {
            if (!Guid.TryParseExact(Path.GetFileName(directory), "N", out _) || !File.Exists(Path.Combine(directory, ".home-boundary"))) continue;
            AppFiles.RejectLink(directory); var path = Path.Combine(directory, ".home-process.json");
            if (File.Exists(path))
            {
                AppFiles.RejectLink(path); ProcessStamp? previous = null;
                try { previous = JsonSerializer.Deserialize<ProcessStamp>(await File.ReadAllTextAsync(path, token)); } catch (JsonException) { }
                if (previous is not null) try
                {
                    using var process = Process.GetProcessById(previous.Id);
                    if (process.StartTime.ToUniversalTime().Ticks == previous.StartedAt && process.MainModule?.FileName == previous.Executable) { MediaProcess.Kill(process); await process.WaitForExitAsync(token); }
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
            }
            DeleteWork(directory);
        }
        store.RecoverEpisodeBoundaries(); await base.StartAsync(token);
    }
    protected override async Task ExecuteAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try { await RunOnceAsync(token); await Task.Delay(2000, token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogWarning("Bölüm sınırı işi: {Type}", ex.GetType().Name); try { await Task.Delay(5000, token); } catch (OperationCanceledException) { break; } }
        }
    }
}
