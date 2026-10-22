using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using HomeServer.Core;
using HomeServer.Infrastructure;

namespace HomeServer.Sources;

public sealed class AutomationCoordinator(MediaStore store, IReleaseAcquisition sources, EventHub events, TimeProvider clock)
{
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _active = new();
    public void CancelContent(string id)
    {
        if (_active.TryGetValue(id, out var cancellation)) try { cancellation.Cancel(); } catch (ObjectDisposedException) { }
    }
    public async Task<bool> RunOnceAsync(CancellationToken stopping)
    {
        foreach (var transfer in store.TransferringAutomationJobs())
        {
            var download = store.GetDownload(transfer.DownloadId!);
            if (download?.DesiredState == "cancelled") store.BlockAutomationRelease(transfer, "Kullanıcı iptali: bu hedef kural yeniden etkinleştirilene kadar bekletildi.", true);
            else if (download?.Status == "failed") store.BlockAutomationRelease(transfer, download.Message, false);
            else if (download?.Acquisition?.Status == "review") store.BlockAutomationRelease(transfer, "Katalog bağlantısı kullanıcı incelemesi bekliyor.", true);
            else if (download?.Acquisition?.Status == "linked") store.FinishAutomationJob(transfer, "satisfied", "İstenen sürüm kütüphanede.");
        }
        store.ScheduleAutomation(clock.GetUtcNow());
        var job = store.ClaimAutomationJob(clock.GetUtcNow());
        if (job is null) return false;
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(stopping);
        budget.CancelAfter(TimeSpan.FromMinutes(2));
        _active[job.ContentId] = budget;
        try
        {
            if (!store.AutomationStillWanted(job)) { store.FinishAutomationJob(job, "cancelled", "Hedef artık otomasyon kapsamında değil."); return true; }
            if (job.Settings.AutoGrab) store.CheckDownloadAdmission(store.VideoBudgetActor(job), clock.GetUtcNow());
            if (job.Settings.AutoGrab && store.AutomaticGrabsToday(job.ContentId) >= job.Settings.MaximumDailyGrabs)
            {
                var tomorrow = new DateTimeOffset(clock.GetUtcNow().UtcDateTime.Date.AddDays(1), TimeSpan.Zero);
                store.FinishAutomationJob(job, "waiting", "Günlük edinme sınırı; sonraki gün beklenecek.", tomorrow); return true;
            }
            var target = store.AutomationTargets(job.ContentId, job.Settings).First(t => t.Key == job.TargetKey);
            var result = await sources.SearchAsync(new ReleaseSearchInput(job.ContentId, job.Season, job.Episode, job.Profile.Id), budget.Token, job.Profile);
            var now = clock.GetUtcNow();
            var candidates = result.Items.Where(release => release.Protocol is "torrent" or "usenet" && release.Evaluation.Eligible
                && release.Evaluation.Parsed.Resolution is { } resolution && (!target.Available || resolution > target.BestResolution && target.BestResolution < job.Settings.CutoffResolution
                    || job.Settings.SameQualityUpgrades && resolution == target.BestResolution && target.ExistingQualityScore is { } previous && release.Evaluation.QualityScore > previous)
                && !store.IsReleaseBlocked(job.ContentId, MediaStore.StableFingerprint(release.Fingerprint)))
                .OrderByDescending(release => release.Evaluation.Score).ToArray();
            ReleaseResult? selected = null;
            DateTimeOffset? waitUntil = null;
            foreach (var candidate in candidates)
            {
                var firstSeen = store.ObserveRelease(MediaStore.StableFingerprint(candidate.Fingerprint), now);
                var due = firstSeen.AddMinutes(job.Settings.DelayMinutes);
                if (job.Settings.MinimumAgeHours > 0)
                {
                    if (!DateTimeOffset.TryParse(candidate.PublishedAt, out var published)) continue;
                    if (published.AddHours(job.Settings.MinimumAgeHours) > due) due = published.AddHours(job.Settings.MinimumAgeHours);
                }
                if (due > now) { waitUntil = due; break; }
                selected = candidate; break;
            }
            if (selected is null)
            {
                var message = waitUntil is not null ? "Tercih edilen sürüm gecikme/yayın yaşı aralığında bekliyor."
                    : result.Sources.Count > 0 && result.Sources.All(source => source.Error is not null) ? "Bağlı kaynaklar sonuç veremedi; tekrar denenecek."
                    : "Hedefe ve kalite profiline uygun yeni sürüm bulunamadı.";
                store.FinishAutomationJob(job, "waiting", message, waitUntil ?? now.AddMinutes(job.Settings.SearchIntervalMinutes)); return true;
            }
            budget.Token.ThrowIfCancellationRequested();
            if (!store.AutomationStillWanted(job)) { store.FinishAutomationJob(job, "cancelled", "Hedef veya kural arama sırasında değişti."); return true; }
            store.SelectAutomationRelease(job, selected);
            var fingerprint = MediaStore.StableFingerprint(selected.Fingerprint);
            if (!job.Settings.AutoGrab)
            {
                store.FinishAutomationJob(job, "suggested", "Uygun sürüm bulundu: " + selected.Title, release: selected.Id, fingerprint: fingerprint); return true;
            }
            var download = await sources.GrabAsync(selected.Id, false, budget.Token, new AutomationLease(job.Id, job.RuleRevision));
            if (download.DesiredState != "running") store.FinishAutomationJob(job, "blocked", "Bu sürüm daha önce kullanıcı tarafından durdurulmuş.", release: selected.Id, fingerprint: fingerprint, download: download.Id);
            else store.FinishAutomationJob(job, "grabbed", "Seçilen sürüm doğrulama/edinme kuyruğunda.", release: selected.Id, fingerprint: fingerprint, download: download.Id);
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        { store.FinishAutomationJob(job, "waiting", "Sunucu yeniden açıldığında devam edilecek.", clock.GetUtcNow()); }
        catch (DownloadBudgetException ex) { store.FinishAutomationJob(job, "waiting", ex.Message, ex.RetryAt ?? clock.GetUtcNow().AddMinutes(15), budgetReason: ex.Code); }
        catch (Exception ex)
        {
            var message = ex is DomainException ? ex.Message : ex is OperationCanceledException ? "Otomasyon isteği iptal edildi veya süresi doldu." : "Otomatik kaynak araması tamamlanamadı.";
            store.FinishAutomationJob(job, job.Attempts >= 5 ? "failed" : "waiting", message, clock.GetUtcNow().AddMinutes(Math.Min(60, 5 * Math.Pow(2, Math.Min(job.Attempts - 1, 4)))));
        }
        finally { _active.TryRemove(job.ContentId, out _); events.Publish("automation.changed"); }
        return true;
    }

    public void ObserveRss(RssReleaseBatch batch)
    {
        var now = clock.GetUtcNow();
        foreach (var rule in store.GetAutomationRules().Where(rule => rule.Settings.Enabled))
        {
            var content = store.GetContent(rule.ContentId)!;
            var profile = store.GetQualityProfile(content.QualityProfileId);
            var targets = store.AutomationTargets(rule.ContentId, rule.Settings);
            var relevant = false;
            foreach (var release in batch.Releases)
            {
                foreach (var target in targets)
                {
                    ReleaseEpisodeIdentity? identity;
                    try { identity = store.ReleaseIdentity(content.Id, target.Season, target.Episode); } catch (DomainException) { continue; }
                    var evaluation = ReleaseEvaluator.Evaluate(release, content.Metadata, profile.Rules, target.Season, target.Episode, identity);
                    if (!evaluation.Eligible) continue;
                    var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(release.InfoHash ?? release.Guid))).ToLowerInvariant();
                    if (store.ObserveRelease(fingerprint, now) == now) relevant = true;
                    break;
                }
            }
            if (relevant) store.NudgeAutomation(rule.ContentId, now);
        }
    }
}
