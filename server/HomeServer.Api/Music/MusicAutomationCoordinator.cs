using System.Collections.Concurrent;
using HomeServer.Core;
using HomeServer.Infrastructure;

namespace HomeServer.Music;

public sealed class MusicAutomationCoordinator(MediaStore store, MusicBrainzProvider metadata, IMusicAcquisition sources, EventHub events, TimeProvider clock)
{
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _active = new();
    public void Cancel(string monitor)
    {
        if (_active.TryGetValue(monitor, out var cancellation)) try { cancellation.Cancel(); } catch (ObjectDisposedException) { }
    }
    public static bool NeedsSearch(MusicTargetState target, MusicMonitorSettings settings) => !target.Pending && !target.Offline && !target.UnknownQuality
        && (!target.Available || settings.AllowUpgrades && !(target.Qualities ?? (target.Quality is { } quality ? [quality] : [])).All(quality => MusicReleaseEvaluator.AtCutoff(quality, settings)));
    public static bool AcceptCandidate(MusicSourceResult result, MusicTargetState target, MusicMonitorSettings settings)
    {
        if (!result.Evaluation.Eligible || result.Evaluation.Quality is not { Tier: > 0 } quality) return false;
        if (target.Quality is null) return !target.Available;
        if (!MusicReleaseEvaluator.AtLeast(quality, target.Quality)) return false;
        return !target.Available || settings.AllowUpgrades && (target.Qualities ?? [target.Quality]).Any(previous => MusicReleaseEvaluator.Better(quality, previous));
    }
    public async Task<bool> RunOnceAsync(CancellationToken stopping)
    {
        foreach (var job in store.MusicTransfers())
        {
            var download = store.GetDownload(job.DownloadId!);
            if (download?.DesiredState == "cancelled") store.FinishMusicAutomation(job, "blocked", "Kullanıcı iptali: tekrar edinmek için takip tercihini yeniden kaydet.");
            else if (download?.Status == "failed") store.BlockMusicRelease(job, download.Message);
            else if (download?.MusicAcquisition?.Status == "review") store.FinishMusicAutomation(job, "blocked", "Albüm bağlantısı kullanıcı incelemesi bekliyor.");
            else if (download?.MusicAcquisition?.Status == "linked") store.FinishMusicAutomation(job, "satisfied", "Albüm doğrulandı ve kütüphanede hazır.");
        }
        var worked = false;
        if (store.DueMusicMonitor(clock.GetUtcNow()) is { } monitor)
        {
            worked = true;
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(stopping); budget.CancelAfter(TimeSpan.FromSeconds(60)); _active[monitor.Id] = budget;
            try
            {
                if (!store.MusicMonitorCurrent(monitor)) throw new DomainException("Profil yetkisi değişti; müzik takip tercihini yeniden kaydet.", 409);
                var page = monitor.Kind == "artist" ? await metadata.AlbumsAsync(monitor.Mbid, monitor.Cursor, budget.Token, monitor.Settings.ReleaseStatuses!.Any(status => status != "Official"))
                    : new MusicPage<MusicBrainzAlbum>([await metadata.AlbumAsync(monitor.Mbid, budget.Token)], 1, 0, 1);
                budget.Token.ThrowIfCancellationRequested(); store.RecordMusicMonitorPage(monitor, page, clock.GetUtcNow());
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested) { return worked; }
            catch (Exception ex) { store.FailMusicMonitor(monitor, ex is DomainException ? ex.Message : "Müzik metadata yenilemesi tamamlanamadı; önceki katalog korunuyor.", clock.GetUtcNow()); }
            finally { _active.TryRemove(monitor.Id, out _); events.Publish("music.automation.changed"); }
        }
        store.ScheduleDueMusicAutomation(clock.GetUtcNow());
        var claimed = store.ClaimMusicAutomationJob(clock.GetUtcNow());
        if (claimed is null) return worked;
        await ExecuteJobAsync(claimed, stopping); return true;
    }
    private async Task ExecuteJobAsync(MusicAutomationJob job, CancellationToken stopping)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(stopping); budget.CancelAfter(TimeSpan.FromMinutes(2)); _active[job.MonitorId] = budget;
        try
        {
            if (!store.MusicJobWanted(job)) { store.FinishMusicAutomation(job, "cancelled", "Müzik takip kapsamı veya hesap yetkisi değişti."); return; }
            var monitor = store.GetMusicMonitor(job.MonitorId)!; var now = clock.GetUtcNow();
            if (monitor.Settings.AutoGrab) store.CheckDownloadAdmission(store.DownloadActor(monitor.UserId, null, "music-automation"), now);
            if (monitor.Settings.AutoGrab && store.MusicGrabsToday(monitor.UserId, now) >= monitor.Settings.MaximumDailyGrabs)
            { store.FinishMusicAutomation(job, "waiting", "Günlük müzik edinme sınırı; yarın yeniden değerlendirilecek.", new DateTimeOffset(now.UtcDateTime.Date.AddDays(1), TimeSpan.Zero)); return; }
            var release = job.Release;
            if (release is null)
            {
                var cursor = store.MusicReleaseCursor(job.Id); var page = await metadata.ReleasesAsync(job.GroupId, cursor, budget.Token);
                if (page.Total > 100000 || page.Items.Count == 0 && cursor < page.Total) throw new DomainException("Albüm baskı sayfası eksik veya çok büyük.", 502);
                var selected = page.Items.Where(item => MusicMonitorPolicy.AcceptRelease(item, monitor.Settings, now)).OrderBy(item => item.Date, StringComparer.Ordinal).ThenBy(item => item.Id, StringComparer.Ordinal).FirstOrDefault();
                if (selected is not null)
                {
                    release = await metadata.ReleaseAsync(selected.Id, budget.Token);
                    if (release.Metadata.GroupId != job.GroupId || !MusicMonitorPolicy.AcceptRelease(release.Metadata, monitor.Settings, now)) throw new DomainException("Seçilen baskının kimliği veya metadata profili değişti.", 409);
                    store.SaveMusicJobRelease(job, release); job = job with { Release = release };
                }
                else
                {
                    var more = cursor + page.Items.Count < page.Total;
                    store.SaveMusicJobRelease(job, null, more ? cursor + page.Items.Count : 0);
                    store.FinishMusicAutomation(job, "waiting", more ? "Metadata profiline uyan baskı sonraki sayfalarda aranıyor." : "Yayımlanmış ve metadata profiline uygun baskı bekleniyor.",
                        more ? now.AddSeconds(2) : now.AddMinutes(monitor.Settings.SearchIntervalMinutes)); return;
                }
            }
            var target = store.MusicTarget(release);
            if (!NeedsSearch(target, monitor.Settings))
            {
                var wait = target.Pending || target.Offline || target.UnknownQuality;
                store.FinishMusicAutomation(job, wait ? "waiting" : "satisfied", target.Pending ? "Albüm için mevcut transfer/import bekleniyor." : target.Offline ? "Albüm diski çevrimdışı; yeni edinme bekletildi." : target.UnknownQuality ? "Yerel albüm kalite analizi bekleniyor." : "Albüm kütüphanede; kalite hedefi karşılanıyor.", now.AddMinutes(monitor.Settings.SearchIntervalMinutes)); return;
            }
            var result = await sources.SearchMusicAsync(new MusicSearchInput(release.Metadata.Id, monitor.Settings.Quality!), metadata, budget.Token);
            var candidates = result.Items.Where(item => AcceptCandidate(item, target, monitor.Settings) && !store.MusicReleaseBlocked(job.GroupId, item.Fingerprint))
                .OrderByDescending(item => item.Evaluation.Score).ThenBy(item => item.Id, StringComparer.Ordinal).ToArray();
            var candidate = candidates.FirstOrDefault();
            if (candidate is null)
            { store.FinishMusicAutomation(job, "waiting", result.Sources.All(source => source.Error is not null) ? "Müzik kaynaklarına erişim bekleniyor." : "Profil ve yükseltme koşullarına uygun albüm bulunamadı.", now.AddMinutes(monitor.Settings.SearchIntervalMinutes)); return; }
            var due = store.ObserveRelease("music:" + candidate.Fingerprint, now).AddMinutes(monitor.Settings.DelayMinutes);
            if (due > now) { store.FinishMusicAutomation(job, "waiting", "Tercih edilen albüm sürümü gecikme aralığında bekliyor.", due); return; }
            budget.Token.ThrowIfCancellationRequested();
            if (!store.MusicJobWanted(job)) { store.FinishMusicAutomation(job, "cancelled", "Takip arama sırasında değişti."); return; }
            store.SelectMusicAutomationResult(job, candidate);
            if (!monitor.Settings.AutoGrab) { store.FinishMusicAutomation(job, "suggested", "Uygun albüm sürümü bulundu: " + candidate.Title); return; }
            var download = await sources.GrabMusicAsync(candidate.Id, budget.Token, new MusicAutomationLease(job.Id, job.Revision));
            if (download.DesiredState != "running") store.FinishMusicAutomation(job, "blocked", "Bu albüm sürümü daha önce kullanıcı tarafından durdurulmuş.");
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        { store.FinishMusicAutomation(job, "waiting", "Sunucu açıldığında müzik işi devam edecek.", clock.GetUtcNow()); }
        catch (DownloadBudgetException ex) { store.FinishMusicAutomation(job, "waiting", ex.Message, ex.RetryAt ?? clock.GetUtcNow().AddMinutes(15), budgetReason: ex.Code); }
        catch (Exception ex)
        {
            var message = ex is DomainException ? ex.Message : ex is OperationCanceledException ? "Müzik işi iptal edildi veya süresi doldu." : "Müzik edinme değerlendirmesi tamamlanamadı.";
            store.FinishMusicAutomation(job, store.MusicJobWanted(job) ? "waiting" : "cancelled", message, clock.GetUtcNow().AddMinutes(Math.Min(360, 5 * Math.Pow(2, Math.Min(job.Attempts, 6)))));
        }
        finally { _active.TryRemove(job.MonitorId, out _); events.Publish("music.automation.changed"); }
    }
}

public sealed class MusicAutomationWorker(MusicAutomationCoordinator coordinator, MediaStore store, ILogger<MusicAutomationWorker> logger) : BackgroundService
{
    public override Task StartAsync(CancellationToken cancellationToken) { store.RecoverMusicAutomation(); return base.StartAsync(cancellationToken); }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { var worked = await coordinator.RunOnceAsync(stoppingToken); await Task.Delay(worked ? 1000 : 5000, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogWarning("Müzik takip yürütücüsü: {Type}", ex.GetType().Name);
                try { await Task.Delay(15000, stoppingToken); } catch (OperationCanceledException) { break; }
            }
        }
    }
}
