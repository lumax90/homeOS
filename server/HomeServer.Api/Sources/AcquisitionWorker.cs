using HomeServer.Core;
using HomeServer.Infrastructure;

namespace HomeServer.Sources;

public sealed class AcquisitionWorker(MediaStore store, EventHub events, ILogger<AcquisitionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            foreach (var id in store.PendingAcquisitionLinks())
            {
                if (token.IsCancellationRequested) return;
                try { store.LinkAcquisition(id); }
                catch (Exception ex)
                {
                    logger.LogWarning("Edinme eşleştirmesi {Id}: {Type}", id, ex.GetType().Name);
                    store.FailAcquisitionLink(id, ex is DomainException ? ex.Message : "İçerik bağlantısı tamamlanamadı. Yeniden deneyebilirsin.");
                }
                events.Publish("catalog.changed");
                events.Publish("downloads.changed");
            }
            foreach (var id in store.PendingMusicLinks())
            {
                if (token.IsCancellationRequested) return;
                try { store.LinkMusicAcquisition(id); }
                catch (Exception ex) { store.MusicLinkReview(id, ex is DomainException ? ex.Message : "Albüm bağlantısı tamamlanamadı."); }
                events.Publish("music.changed"); events.Publish("downloads.changed");
            }
            try { await Task.Delay(2000, token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
        }
    }
}
