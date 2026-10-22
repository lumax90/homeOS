using HomeServer.Core;
using HomeServer.Infrastructure;

namespace HomeServer.Metadata;

public sealed class TrackingWorker(MediaStore store, MetadataProvider provider, EventHub events, ILogger<TrackingWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken token)
    {
        store.RecoverTrackingSync();
        while (!token.IsCancellationRequested)
        {
            var work = store.ClaimTrackingSync();
            if (work is null)
            {
                try { await Task.Delay(2000, token); } catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
                continue;
            }
            var (entry, revision) = work.Value;
            events.Publish("tracking.changed");
            try
            {
                using var budget = CancellationTokenSource.CreateLinkedTokenSource(token);
                budget.CancelAfter(TimeSpan.FromMinutes(10));
                var metadata = await provider.DetailsAsync(entry.Metadata.Provider, entry.Metadata.Kind, entry.Metadata.ProviderId, budget.Token);
                var episodes = metadata.Kind == "show" ? await provider.EpisodesAsync(metadata.Provider, metadata.ProviderId, budget.Token) : Array.Empty<ProviderEpisode>();
                store.CompleteTrackingSync(entry.Id, revision, metadata, episodes);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogWarning("İçerik yenileme başarısız {Id}: {Type}",entry.Id,ex.GetType().Name);
                store.FailTrackingSync(entry.Id,revision,ex is DomainException ? ex.Message : "İçerik bilgileri yenilenemedi. Daha sonra tekrar denenecek.");
            }
            finally { events.Publish("tracking.changed"); }
        }
    }
}
