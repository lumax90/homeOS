using HomeServer.Core;
using HomeServer.Infrastructure;

namespace HomeServer.Sources;

public sealed class AutomationWorker(AutomationCoordinator coordinator, MediaStore store, IndexerService sources, ILogger<AutomationWorker> logger) : BackgroundService
{
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        store.RecoverAutomationJobs();
        return base.StartAsync(cancellationToken);
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var worked = await coordinator.RunOnceAsync(stoppingToken);
                if (!worked)
                    foreach (var id in store.DueRssSources(DateTimeOffset.UtcNow))
                    {
                        try
                        {
                            coordinator.ObserveRss(await sources.ReadRssAsync(id, stoppingToken));
                            store.RecordRss(id, null, DateTimeOffset.UtcNow);
                        }
                        catch (DomainException ex) { store.RecordRss(id, ex.Message, DateTimeOffset.UtcNow); }
                    }
                await Task.Delay(worked ? 500 : 3000, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogWarning("Otomasyon yürütücüsü: {Type}", ex.GetType().Name);
                try { await Task.Delay(15000, stoppingToken); } catch (OperationCanceledException) { break; }
            }
        }
    }
}
