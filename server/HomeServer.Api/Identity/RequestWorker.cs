using HomeServer.Infrastructure;

namespace HomeServer.Identity;

public sealed class RequestWorker(MediaStore store, ILogger<RequestWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var cursor = "";
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var content = store.RequestContentBatch(cursor);
                foreach (var id in content)
                {
                    stoppingToken.ThrowIfCancellationRequested();
                    store.ReconcileRequests(id); cursor = id;
                }
                if (content.Count < 25) cursor = "";
                await Task.Delay(content.Count == 25 ? 1000 : 15000, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogWarning("İstek durumu yenilenemedi: {Type}", ex.GetType().Name);
                try { await Task.Delay(15000, stoppingToken); } catch (OperationCanceledException) { break; }
            }
        }
    }
}
