using System.Collections.Concurrent;
using HomeServer.Core;
using HomeServer.Infrastructure;

namespace HomeServer.Notifications;

public sealed class NotificationDispatcher(MediaStore store, NotificationSettings settings, INotificationTransport transport, TimeProvider clock, EventHub events)
{
    private readonly ConcurrentDictionary<string, (NotificationDelivery Job, CancellationTokenSource Token)> _active = new();
    public void Interrupt(string? channelId = null, string? deliveryId = null)
    {
        foreach (var (_, value) in _active)
            if ((channelId is null || value.Job.ChannelId == channelId) && (deliveryId is null || value.Job.Id == deliveryId))
                try { value.Token.Cancel(); } catch (ObjectDisposedException) { }
    }
    public async Task<bool> RunOnceAsync(CancellationToken stopping)
    {
        var job = store.ClaimNotificationDelivery(clock.GetUtcNow()); if (job is null) return false;
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(stopping); budget.CancelAfter(TimeSpan.FromSeconds(30)); _active[job.Id] = (job, budget);
        events.Publish("notifications.changed");
        try
        {
            if (!store.NotificationLeaseCurrent(job)) throw new OperationCanceledException(budget.Token);
            var connection = store.GetNotificationConnection(job.ChannelId)!;
            var secrets = settings.Secrets(connection);
            if (connection.Channel.Revision != job.ChannelRevision || !store.NotificationLeaseCurrent(job)) { CancelCurrent(job); return true; }
            var work = transport.SendAsync(job, connection.Channel, secrets, budget.Token);
            try
            {
                while (!work.IsCompleted)
                {
                    await Task.WhenAny(work, Task.Delay(250, budget.Token)); budget.Token.ThrowIfCancellationRequested();
                    if (!store.NotificationLeaseCurrent(job)) { budget.Cancel(); throw new OperationCanceledException(budget.Token); }
                }
                var result = await work; budget.Token.ThrowIfCancellationRequested();
                if (store.NotificationLeaseCurrent(job))
                {
                    store.FinishNotificationDelivery(job, result, clock.GetUtcNow());
                    if (result.SubscriptionExpired) store.RetireBrowserPush(job.ChannelId, "Push servisi aboneliği kaldırdı.", job.ChannelRevision);
                }
                else CancelCurrent(job);
            }
            finally { if (!work.IsCompleted) budget.Cancel(); try { await work; } catch { /* The failure is recorded below without remote text. */ } }
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
        catch (OperationCanceledException)
        {
            if (store.NotificationLeaseCurrent(job)) store.FinishNotificationDelivery(job, new(false, true, "Bildirim gönderimi zaman aşımına uğradı."), clock.GetUtcNow());
            else CancelCurrent(job);
        }
        catch (Exception ex)
        {
            store.FinishNotificationDelivery(job, new(false, false, ex is DomainException ? "Bildirim bağlantısı yeniden yapılandırılmalı." : "Bildirim gönderilemedi; bağlantı ayarlarını kontrol et."), clock.GetUtcNow());
            if (!store.NotificationLeaseCurrent(job) && store.GetNotificationDelivery(job.Id)?.Status == "sending") CancelCurrent(job);
        }
        finally { _active.TryRemove(job.Id, out _); events.Publish("notifications.changed"); }
        return true;
    }
    private void CancelCurrent(NotificationDelivery job)
    {
        var current = store.GetNotificationDelivery(job.Id);
        if (current is { Status: "sending" }) store.CancelNotificationDelivery(current.Id, current.Revision, "Kanal veya alıcı erişimi değişti; gönderim iptal edildi.");
    }
}
public sealed class NotificationWorker(NotificationDispatcher dispatcher, MediaStore store, TimeProvider clock, ILogger<NotificationWorker> logger) : BackgroundService
{
    public override Task StartAsync(CancellationToken token) { store.RecoverNotificationDeliveries(clock.GetUtcNow()); return base.StartAsync(token); }
    protected override async Task ExecuteAsync(CancellationToken token)
    {
        var lastPrune = clock.GetUtcNow();
        while (!token.IsCancellationRequested)
        {
            try
            {
                var worked = await dispatcher.RunOnceAsync(token);
                if (clock.GetUtcNow() - lastPrune >= TimeSpan.FromMinutes(1)) { store.ReconcileBrowserPush(); store.PruneNotificationDeliveries(clock.GetUtcNow()); lastPrune = clock.GetUtcNow(); }
                await Task.Delay(worked ? 200 : 3000, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogWarning("Bildirim kuyruğu: {Type}", ex.GetType().Name);
                try { await Task.Delay(5000, token); } catch (OperationCanceledException) { break; }
            }
        }
    }
}
