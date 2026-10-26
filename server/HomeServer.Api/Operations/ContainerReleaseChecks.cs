using HomeServer.Core;

namespace HomeServer.Operations;

public sealed class ContainerReleaseChecks(ContainerUpdateCoordinator updates, ContainerReleaseSource source,
    IHostApplicationLifetime lifetime, ILogger<ContainerReleaseChecks> logger) : BackgroundService
{
    private readonly SemaphoreSlim _checking = new(1, 1);
    public async Task CheckAsync(CancellationToken token)
    {
        if (!updates.Configured || !updates.Enabled) throw new DomainException("Managed update checks are not enabled.", 409);
        if (!await _checking.WaitAsync(0, token)) return;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(45));
            var candidate = await source.CheckAsync(BuildInfo.Version, updates.Revision, updates.Availability().InstalledDigest, timeout.Token);
            await updates.ReportCandidateAsync(candidate, null, token);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException && !token.IsCancellationRequested)
        {
            logger.LogWarning("Container release metadata check deferred: {Type}", ex.GetType().Name);
            await updates.ReportCandidateAsync(null, "Release information could not be checked. The running version is unchanged.", token);
        }
        finally { _checking.Release(); }
    }
    protected override async Task ExecuteAsync(CancellationToken token)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = lifetime.ApplicationStarted.Register(() => started.TrySetResult());
        try
        {
            await started.Task.WaitAsync(token);
            while (!token.IsCancellationRequested)
            {
                try { if (updates.Configured && updates.Enabled) await CheckAsync(token); }
                catch (Exception ex) when (ex is not OutOfMemoryException && !token.IsCancellationRequested)
                { logger.LogWarning("Container update settings require review: {Type}", ex.GetType().Name); }
                await Task.Delay(TimeSpan.FromMinutes(15), token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }
}
