using System.Collections.Concurrent;
using HomeServer.Core;
using HomeServer.Infrastructure;

namespace HomeServer.Scanning;

public sealed class ScanWorker(MediaStore store, LibraryScanner scanner, EventHub events, ILogger<ScanWorker> logger)
    : BackgroundService
{
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _running = new();

    public void Cancel(string id)
    {
        store.RequestCancellation(id);
        if (_running.TryGetValue(id, out var cancellation))
        {
            try { cancellation.Cancel(); }
            catch (ObjectDisposedException) { /* The worker completed concurrently. */ }
        }
        events.Publish("jobs.changed");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        store.RecoverInterruptedJobs();
        while (!stoppingToken.IsCancellationRequested)
        {
            var job = store.TryClaimJob();
            if (job is null)
            {
                try { await Task.Delay(500, stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                continue;
            }
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            _running[job.Id] = cancellation;
            if (store.GetJob(job.Id)?.CancelRequested == true) cancellation.Cancel();
            events.Publish("jobs.changed");
            try { await scanner.ScanAsync(job, cancellation.Token); }
            catch (OperationCanceledException)
            {
                if (stoppingToken.IsCancellationRequested && store.GetJob(job.Id)?.CancelRequested != true)
                    store.EndJob(job.Id, "queued", "Sunucu yeniden başladığında tarama sürdürülecek.");
                else store.EndJob(job.Id, "cancelled", "Tarama iptal edildi. Önceki içerik bilgileri korundu.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Kütüphane taraması başarısız: {LibraryId}", job.LibraryId);
                var message = ex is DomainException ? ex.Message : "Klasör okunamadı. Bağlantıyı ve dosya izinlerini kontrol edip yeniden dene.";
                store.EndJob(job.Id, "failed", message);
            }
            finally
            {
                _running.TryRemove(job.Id, out _);
                events.Publish("catalog.changed");
            }
        }
    }
}
