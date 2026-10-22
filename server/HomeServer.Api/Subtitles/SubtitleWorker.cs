using HomeServer.Infrastructure;

namespace HomeServer.Subtitles;

public sealed class SubtitleWorker(SubtitleCoordinator coordinator, MediaStore store, ILogger<SubtitleWorker> logger) : BackgroundService
{
    public override Task StartAsync(CancellationToken token) { store.RecoverSubtitleJobs(); return base.StartAsync(token); }
    protected override async Task ExecuteAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try { var worked = await coordinator.RunOnceAsync(token); await Task.Delay(worked ? 1500 : 5000, token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogWarning("Altyazı yürütücüsü: {Type}", ex.GetType().Name);
                try { await Task.Delay(15000, token); } catch (OperationCanceledException) { break; }
            }
        }
    }
}
