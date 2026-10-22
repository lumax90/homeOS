using HomeServer.Core;
using HomeServer.Infrastructure;

namespace HomeServer.Operations;

public sealed class SystemWorker(MediaStore store, BackupService backups, EventHub events, ILogger<SystemWorker> logger) : BackgroundService
{
    public override Task StartAsync(CancellationToken cancellationToken) { store.RecoverSystemJobs(); return base.StartAsync(cancellationToken); }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var job = store.ClaimSystemJob();
            if (job is null) { try { await Task.Delay(2000, stoppingToken); } catch (OperationCanceledException) { break; } continue; }
            try { await backups.CreateAsync(job.Id, stoppingToken); store.EndSystemJob(job.Id, "completed", "Uygulama yedeği doğrulandı ve hazır."); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { store.EndSystemJob(job.Id, "queued", "Yedekleme yeniden açılışta devam edecek."); break; }
            catch (Exception ex) { logger.LogWarning("Yedekleme {Id}: {Type}", job.Id, ex.GetType().Name); store.EndSystemJob(job.Id, "failed", ex is DomainException ? ex.Message : "Yedekleme tamamlanamadı. Boş alanı ve dosya erişimini kontrol et."); }
            events.Publish("system.changed");
        }
    }
}
