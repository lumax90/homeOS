using System.Collections.Concurrent;
using System.Text;
using HomeServer.Core;
using HomeServer.Infrastructure;
using HomeServer.Playback;

namespace HomeServer.Subtitles;

public sealed class SubtitleSyncService(MediaStore store, MediaAnalysisService analysis, SubtitleService subtitles, SileroVad vad, EventHub events)
{
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _active = new();
    private readonly SemaphoreSlim _apply = new(1, 1);
    public async Task<SubtitleSyncJob> QueueAsync(string mediaId, SubtitleSyncInput input, CancellationToken token)
    {
        if (input.MaximumOffsetSeconds is < 1 or > 120 || input.TrackId is null || input.TrackId.Length > 100) throw new DomainException("Senkron aralığı 1–120 saniye olmalı.");
        var media = await analysis.InspectAsync(mediaId, token);
        if (media.Media.Kind != "video" || media.Probe.Duration is not > 0 or > 14400) throw new DomainException("Senkron için en fazla dört saatlik video seç.", 409);
        var source = subtitles.GetSource(media, input.TrackId, false);
        if (!source.Track.Playable) throw new DomainException("Sesle zaman eşleme metin altyazısı gerektirir.", 409);
        var streams = media.Probe.Streams!.Where(stream => stream.Kind == "audio").ToArray();
        var audio = input.AudioStream is null ? streams.OrderByDescending(stream => stream.IsDefault).FirstOrDefault() : streams.FirstOrDefault(stream => stream.Index == input.AudioStream);
        if (audio is null) throw new DomainException("Konuşmayı içeren ses izi bulunamadı.", 409);
        var capabilities = vad.Capabilities(); if (!capabilities.Available) throw new DomainException(capabilities.Error!, 409);
        var result = store.QueueSubtitleSync(media, source, audio.Index, input); events.Publish("subtitles.changed"); return result;
    }
    public void Cancel(string id)
    {
        store.CancelSubtitleSync(id); if (_active.TryGetValue(id, out var token)) try { token.Cancel(); } catch (ObjectDisposedException) { }
        events.Publish("subtitles.changed");
    }
    private async Task<(AnalyzedMedia Media, SubtitleSourceInfo Source)> SourceAsync(SubtitleSyncJob job, CancellationToken token)
    {
        var media = await analysis.InspectAsync(job.MediaId, token); var source = subtitles.GetSource(media, job.TrackId, false);
        if (media.Fingerprint != job.Fingerprint || media.Media.MatchRevision != job.MatchRevision || source.Fingerprint != job.SubtitleFingerprint) throw new DomainException("Dosya/altyazı senkron işi sırasında değişti; yeni analiz gerekli.", 409);
        return (media, source);
    }
    public async Task<bool> RunOnceAsync(CancellationToken stopping)
    {
        var job = store.ClaimSubtitleSync(); if (job is null) return false;
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(stopping); budget.CancelAfter(TimeSpan.FromMinutes(20)); _active[job.Id] = budget;
        try
        {
            var input = await SourceAsync(job, budget.Token); var text = await subtitles.WebVttAsync(input.Media, job.TrackId, 0, 0, budget.Token); var cues = SubtitleTiming.Read(text);
            if (cues.Length < 3) throw new DomainException("Senkron için en az üç zamanlı altyazı cue'su gerekli.", 409);
            var voice = await vad.AnalyzeAsync(input.Media, job.AudioStream, progress => store.SubtitleSyncProgress(job, progress), budget.Token);
            var result = job.Input.Segmented?SubtitleWarp.Align(voice,cues,job.Input.MaximumOffsetSeconds,job.Input.CorrectFrameRate,budget.Token):SubtitleAligner.Align(voice, cues, job.Input.MaximumOffsetSeconds, job.Input.CorrectFrameRate, budget.Token);
            _ = await SourceAsync(job, budget.Token); budget.Token.ThrowIfCancellationRequested();
            store.ProposeSubtitleSync(job, result);
            if (job.Input.AutoApply && result.CanApply && store.GetSubtitleSync(job.Id)?.Status == "proposed") await ApplyAsync(job.Id, job.Revision, budget.Token);
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
        catch (Exception ex) { store.FailSubtitleSync(job, ex is DomainException ? ex.Message : ex is OperationCanceledException ? "Senkron işi iptal edildi veya süre sınırına ulaştı." : "Konuşma etkinliği analiz edilemedi."); }
        finally { _active.TryRemove(job.Id, out _); events.Publish("subtitles.changed"); }
        return true;
    }
    public async Task<ManagedSubtitle> ApplyAsync(string id, int revision, CancellationToken token)
    {
        await _apply.WaitAsync(token);
        SubtitleSyncJob? job = null;
        try
        {
            job = store.ClaimSubtitleSyncApply(id, revision);
            if (job.Status == "completed") return store.GetManagedSubtitles(job.MediaId, true).FirstOrDefault(subtitle => subtitle.Id == job.SubtitleId) ?? throw new DomainException("Senkron sürümü daha sonra kaldırılmış.", 409);
            var input = await SourceAsync(job, token); var text = await subtitles.WebVttAsync(input.Media, job.TrackId, 0, 0, token);
            var transformed = SubtitleWarp.Transform(SubtitleTiming.Read(text),job.Result!);
            var previous = job.TrackId.StartsWith("managed-", StringComparison.Ordinal) ? job.TrackId[8..] : null;
            var prior = previous is null ? null : store.GetManagedSubtitles(job.MediaId).FirstOrDefault(subtitle => subtitle.Id == previous);
            var publication = new SubtitlePublication("sync", job.Fingerprint, job.MatchRevision, Score: prior?.Score, SupersedesId: previous, JobId: job.Id, JobRevision: job.Revision);
            var result = await subtitles.UploadAsync(job.MediaId, input.Source.Track.Language, "Senkron · " + input.Source.Track.Title[..Math.Min(100, input.Source.Track.Title.Length)], "vtt", Encoding.UTF8.GetBytes(transformed), token,
                job.Id, input.Source.Track.Forced, input.Source.Track.HearingImpaired, expectedMatchRevision: job.MatchRevision, publication: publication);
            store.CompleteSubtitleSync(job, result.Id); events.Publish("subtitles.changed"); return result;
        }
        catch (Exception ex) { if (job is { Status: "applying" }) store.FailSubtitleSync(job, ex is DomainException ? ex.Message : "Senkron sürümü yayımlanamadı."); throw; }
        finally { _apply.Release(); }
    }
}

public sealed class SubtitleSyncWorker(SubtitleSyncService service, MediaStore store, ILogger<SubtitleSyncWorker> logger) : BackgroundService
{
    public override Task StartAsync(CancellationToken token) { store.RecoverSubtitleSync(); return base.StartAsync(token); }
    protected override async Task ExecuteAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try { var worked = await service.RunOnceAsync(token); await Task.Delay(worked ? 500 : 5000, token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogWarning("Altyazı senkron yürütücüsü: {Type}", ex.GetType().Name);
                try { await Task.Delay(15000, token); } catch (OperationCanceledException) { break; }
            }
        }
    }
}
