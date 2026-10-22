using System.Collections.Concurrent;
using HomeServer.Core;
using HomeServer.Infrastructure;

namespace HomeServer.Music;

public sealed class MusicTaskWorker(MediaStore store, MusicSonicAnalyzer sonic, MusicTagService tags, EventHub events, ILogger<MusicTaskWorker> logger) : BackgroundService
{
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _active = new();
    public void Cancel(string id)
    {
        store.CancelMusicTask(id);
        if (_active.TryGetValue(id, out var cancellation)) try { cancellation.Cancel(); } catch (ObjectDisposedException) { }
    }
    public override Task StartAsync(CancellationToken cancellationToken) { store.RecoverMusicTasks(); return base.StartAsync(cancellationToken); }
    public async Task<bool> RunOnceAsync(CancellationToken stopping)
    {
        var task = store.ClaimMusicTask(); if (task is null) return false;
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(stopping); budget.CancelAfter(TimeSpan.FromMinutes(10)); _active[task.Id] = budget;
        try
        {
            if (task.Kind == "sonic")
            { var result = await sonic.AnalyzeAsync(task.MediaId, budget.Token); store.SaveMusicSonic(task, result); store.FinishMusicTask(task, "completed", "Loudness ve yerel ses benzerliği analizi hazır."); }
            else await tags.ExecuteAsync(task, budget.Token);
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
        catch (Exception ex)
        {
            var message = ex is DomainException ? ex.Message : ex is OperationCanceledException ? "Müzik işi iptal edildi veya süre sınırına ulaştı." : "Müzik dosyası işlenemedi.";
            if (store.GetMusicTask(task.Id)?.Status == "publishing")
            { store.MusicPublishingError(task.Id, message); try { await Task.Delay(15000, stopping); } catch (OperationCanceledException) { } }
            else store.FinishMusicTask(task, "failed", message);
        }
        finally { _active.TryRemove(task.Id, out _); events.Publish("music.changed"); }
        return true;
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { var worked = await RunOnceAsync(stoppingToken); await Task.Delay(worked ? 300 : 3000, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogWarning("Müzik iş yürütücüsü: {Type}", ex.GetType().Name);
                try { await Task.Delay(15000, stoppingToken); } catch (OperationCanceledException) { break; }
            }
        }
    }
}
