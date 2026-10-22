using System.Collections.Concurrent;
using HomeServer.Core;
using HomeServer.Infrastructure;
using HomeServer.Playback;

namespace HomeServer.Subtitles;

public sealed class SubtitleCoordinator(SubtitleSettings settings, MediaStore store, MediaAnalysisService analysis, SubtitleService subtitles, SubtitleAcquisition acquisition, EventHub events, TimeProvider clock)
{
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _active = new();
    private string _cursor = "";
    public void Interrupt(string? mediaId = null)
    {
        foreach (var (id, cancellation) in _active.Where(pair => mediaId is null || pair.Key == mediaId)) try { cancellation.Cancel(); } catch (ObjectDisposedException) { }
    }
    public static bool Compatible(SubtitleTrack track, string language, SubtitleLanguageProfile profile) => track.Language == language && (track.Playable || track.Burnable)
        && (profile.Forced == "include" || track.Forced == (profile.Forced == "only")) && (profile.HearingImpaired == "include" || track.HearingImpaired == (profile.HearingImpaired == "only"));
    public static SubtitleUpgradeDecision Decide(AnalyzedMedia media, string language, SubtitleLanguageProfile profile, IReadOnlyList<SubtitleTrack> tracks, IReadOnlyList<ManagedSubtitle> versions)
    {
        var audio = media.Probe.AudioLanguages ?? [];
        if (profile.SkipAudioLanguage && audio.Contains(language) || (profile.ExcludedAudioLanguages ?? []).Intersect(audio).Any()) return new(false, "Ses dili kuralı bu altyazıyı kapsam dışında tutuyor.");
        var present = tracks.Where(track => Compatible(track, language, profile)).ToArray();
        if (present.Length == 0) return new(true, "İstenen dil/forced/HI altyazısı eksik.");
        if (present.Any(track => !track.Id.StartsWith("managed-", StringComparison.Ordinal) || !versions.Any(version => "managed-" + version.Id == track.Id && version.Origin == "provider")))
            return new(false, "Kaynak veya kullanıcı altyazısı mevcut.");
        var current = versions.Where(version => present.Any(track => track.Id == "managed-" + version.Id)).OrderByDescending(version => version.Score ?? int.MaxValue).First();
        if (!profile.AllowUpgrades || current.Score is null || current.Score >= profile.CutoffScore) return new(false, "Altyazı kalite hedefi karşılanıyor veya yükseltme kapalı.", current);
        return new(true, "Daha iyi eşleşen altyazı aranıyor.", current);
    }
    public async Task<bool> RunOnceAsync(CancellationToken stopping)
    {
        if (!settings.Profile.Enabled || !settings.Status.Configured) return false;
        var batch = store.SubtitleMediaBatch(_cursor);
        foreach (var id in batch) { store.ScheduleSubtitles(id, store.GetEffectiveSubtitlePolicy(id)); _cursor = id; }
        if (batch.Count < 20) _cursor = "";
        var job = store.ClaimSubtitleJob(clock.GetUtcNow()); if (job is null) return false;
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(stopping); budget.CancelAfter(TimeSpan.FromMinutes(3)); _active[job.Lease.MediaId] = budget;
        try
        {
            store.RequireSubtitlePolicy(job.Lease.MediaId, job.Lease.Stamp, job.Lease.Language, false, job.Lease);
            var media = await analysis.InspectAsync(job.Lease.MediaId, budget.Token);
            var decision = Decide(media, job.Lease.Language, job.Profile, subtitles.List(media), store.GetManagedSubtitles(job.Lease.MediaId));
            var next = clock.GetUtcNow().AddHours(job.Profile.SearchIntervalHours);
            if (!decision.Search) { store.FinishSubtitleJob(job, "ready", decision.Reason, next); return true; }
            var found = await acquisition.SearchAsync(job.Lease.MediaId, new(job.Lease.Language, job.Profile.HearingImpaired, job.Profile.Forced), budget.Token);
            var best = found.Items.FirstOrDefault(candidate => candidate.Eligible && candidate.Score >= job.Profile.MinimumScore
                && (decision.Current is null || candidate.FileId != decision.Current.ProviderFileId && candidate.Score >= decision.Current.Score + job.Profile.MinimumUpgradeDelta));
            if (best is null) { store.FinishSubtitleJob(job, "waiting", "Profil ve yükseltme eşiğine uygun altyazı bulunamadı.", next); return true; }
            store.RequireSubtitlePolicy(job.Lease.MediaId, job.Lease.Stamp, job.Lease.Language, false, job.Lease);
            if (!job.Profile.AutoDownload)
            {
                store.FinishSubtitleJob(job, "suggested", decision.Current is null ? "Uygun altyazı bulundu." : "Daha iyi altyazı bulundu.", next);
                store.NotifySubtitle(job.Lease.MediaId, job.Lease.Language + " için uygun altyazı bulundu.", $"subtitle:{job.Lease.MediaId}:{best.FileId}"); return true;
            }
            await acquisition.AcquireAsync(job.Lease.MediaId, best.Id, budget.Token, job.Lease.Stamp.GlobalRevision, job.Lease, decision.Current?.Id);
            store.FinishSubtitleJob(job, "ready", decision.Current is null ? "Altyazı eklendi." : "Altyazı yükseltildi; önceki sürüm geçmişte korunuyor.", next);
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { store.FinishSubtitleJob(job, "waiting", "Yeniden açılışta devam edilecek.", clock.GetUtcNow()); }
        catch (Exception ex)
        {
            var message = ex is DomainException ? ex.Message : ex is OperationCanceledException ? "Altyazı işi iptal edildi veya süresi doldu." : "Altyazı değerlendirmesi tamamlanamadı.";
            store.FinishSubtitleJob(job, "failed", message, ex is DomainException { StatusCode: 502 or 504 } && job.Attempts < 3 ? clock.GetUtcNow().AddMinutes(2) : clock.GetUtcNow().AddHours(1));
        }
        finally { _active.TryRemove(job.Lease.MediaId, out _); events.Publish("subtitles.changed"); }
        return true;
    }
}
