using System.Collections.Concurrent;
using System.Diagnostics;
using HomeServer.Core;
using HomeServer.Infrastructure;

namespace HomeServer.Downloads;

public sealed class DownloadWorker(MediaStore store, ITorrentSessions sessions, DownloadImporter importer,
    EventHub events, IConfiguration configuration, ILogger<DownloadWorker> logger) : BackgroundService
{
    private sealed class Run
    {
        private readonly TaskCompletionSource<Task> _work = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationTokenSource Cancellation { get; }
        public Task Task { get; }
        public DownloadBudgetRun? Budget { get; set; }
        public Run(CancellationTokenSource cancellation) { Cancellation = cancellation; Task = _work.Task.Unwrap(); }
        public void Attach(Task task) => _work.TrySetResult(task);
    }
    private readonly ConcurrentDictionary<string, Run> _running = new();
    private readonly SemaphoreSlim _downloads = new(Math.Clamp(configuration.GetValue("Home:Downloads:MaximumConcurrent", 3), 1, 10));
    private readonly SemaphoreSlim _seeders = new(Math.Clamp(configuration.GetValue("Home:Downloads:MaximumSeeders", 3), 1, 10));

    public DownloadRecord Control(string id, string action)
    {
        var intent = action switch { "pause" => "paused", "resume" => "running", "cancel" => "cancelled", _ => throw new DomainException("Geçersiz indirme işlemi.") };
        var result = store.SetDownloadIntent(id, intent);
        if (_running.TryGetValue(id, out var run))
        {
            try { run.Cancellation.Cancel(); } catch (ObjectDisposedException) { }
        }
        events.Publish("downloads.changed");
        return result;
    }
    public void Interrupt(string id)
    {
        if (_running.TryGetValue(id, out var run)) try { run.Cancellation.Cancel(); } catch (ObjectDisposedException) { }
        events.Publish("downloads.changed");
    }
    public void InterruptUser(string userId)
    {
        foreach (var id in _running.Keys) if (store.GetDownload(id)?.Ownership?.UserId == userId) Interrupt(id);
    }
    public async Task<int> StopForMaintenanceAsync(string id, CancellationToken token)
    {
        var paused = Control(id, "pause");
        if (_running.TryGetValue(id, out var run)) await run.Task.WaitAsync(TimeSpan.FromSeconds(30), token);
        return paused.Revision;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        store.RecoverDownloads();
        store.RecoverDownloadBudgets(DateTimeOffset.UtcNow);
        var pruned = DateTimeOffset.UtcNow;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                foreach (var pair in _running.ToArray())
                {
                    if (pair.Value.Task.IsCompleted && _running.TryRemove(pair.Key, out var done)) done.Cancellation.Dispose();
                    else if (pair.Value.Budget is { } active && !store.TouchDownloadBudgetRun(active, DateTimeOffset.UtcNow)) try { pair.Value.Cancellation.Cancel(); } catch (ObjectDisposedException) { }
                }
                if (DateTimeOffset.UtcNow - pruned > TimeSpan.FromMinutes(5)) { store.PruneDownloadBudgets(DateTimeOffset.UtcNow); pruned = DateTimeOffset.UtcNow; }
                foreach (var record in store.GetDownloads().Where(d => d.DesiredState == "running" && d.Status != "failed" && !(d.SourceKind == "nzb" && d.Status == "completed")
                    && (!DateTimeOffset.TryParse(d.Ownership?.RetryAt, out var retry) || retry <= DateTimeOffset.UtcNow)).OrderBy(d => d.Priority).ThenBy(d => d.CreatedAt))
                {
                    if (_downloads.CurrentCount == 0) break;
                    if (_running.ContainsKey(record.Id)) continue;
                    var run = new Run(CancellationTokenSource.CreateLinkedTokenSource(stoppingToken));
                    if (!_running.TryAdd(record.Id, run)) { run.Cancellation.Dispose(); continue; }
                    run.Attach(ProcessAsync(record, run, stoppingToken));
                }
                await Task.Delay(500, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally
        {
            foreach (var run in _running.Values) run.Cancellation.Cancel();
            await Task.WhenAll(_running.Values.Select(r => r.Task));
            foreach (var run in _running.Values) run.Cancellation.Dispose();
            _running.Clear();
        }
    }

    private async Task ProcessAsync(DownloadRecord record, Run running, CancellationToken stopping)
    {
        var token = running.Cancellation.Token;
        var ownsDownload = false;
        var ownsSeeder = false;
        ITorrentSession? session = null;
        var runId = Guid.NewGuid().ToString("N");
        var counterId = runId; long baselineUp = 0, baselineDown = 0; Stopwatch? seedClock = null;
        try
        {
            await _downloads.WaitAsync(token);
            ownsDownload = true;
            var current = store.GetDownload(record.Id)!;
            if (current.Revision != record.Revision || current.DesiredState != "running") return;
            if (record.SourceKind == "nzb" && current.ImportedAt is not null) { Set("completed", "NZB dosyaları kütüphanede hazır.", 100, current.TotalBytes); return; }
            running.Budget = store.BeginDownloadBudgetRun(record.Id, record.Revision, runId, DateTimeOffset.UtcNow);
            Set("metadata", record.SourceKind == "nzb" ? "NZB bilgileri ve kalıcı parçalar denetleniyor." : "Torrent bilgileri alınıyor.", record.Progress, record.TotalBytes);
            session = await sessions.OpenAsync(store.GetDownloadSource(record.Id) with { Budget = running.Budget }, token);
            token.ThrowIfCancellationRequested();
            await session.StartAsync();
            while (!session.Snapshot.Complete)
            {
                token.ThrowIfCancellationRequested();
                var snapshot = session.Snapshot;
                Record(snapshot);
                if (snapshot.Status == "failed") throw new DomainException(snapshot.Message ?? "Aktarım motoru dosya veya bağlantı hatası bildirdi.");
                Set(snapshot.Status, snapshot.Message ?? (snapshot.Status == "verifying" ? "Dosya parçaları doğrulanıyor." : snapshot.Peers == 0 ? "Bağlanılabilecek eşler bekleniyor." : "Dosyalar indiriliyor."),
                    snapshot.Progress, snapshot.Size, snapshot.Down, snapshot.Up, snapshot.Peers, snapshot.Name);
                await Task.Delay(1000, token);
            }
            var completed = session.Snapshot;
            Record(completed);
            await session.StopAsync(); // Flush writes and fast-resume before publishing files.
            if (store.GetDownload(record.Id)?.ImportedAt is null)
            {
                Set("importing", "Doğrulanan dosyalar kütüphaneye alınıyor.", 100, completed.Size, name: completed.Name);
                await importer.ImportAsync(record, session, token);
            }
            store.SetDownloadBudgetSeeding(running.Budget);
            _downloads.Release(); ownsDownload = false;
            if (record.SourceKind == "nzb") { Set("completed", "NZB paketi doğrulandı ve kütüphaneye alındı.", 100, completed.Size); return; }
            Set("completed", "Dosyalar hazır. Paylaşım sırası bekleniyor.", 100, completed.Size);
            await _seeders.WaitAsync(token);
            ownsSeeder = true;
            await session.StartAsync();
            seedClock = Stopwatch.StartNew();
            counterId = Guid.NewGuid().ToString("N"); var seedBaseline = session.Snapshot; baselineUp = seedBaseline.Uploaded; baselineDown = seedBaseline.Received;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                var snapshot = session.Snapshot;
                Record(snapshot);
                var state = store.GetDownload(record.Id)!;
                if (SeedSatisfied(state, store.GetTransferPolicy(record.Id), store.GetSourceSeedRules(record.Id)))
                { await session.StopAsync(); store.FinishSeeding(record.Id, record.Revision); break; }
                if (snapshot.Status == "failed") throw new DomainException("Torrent paylaşımı durdu. Yeniden deneyebilirsin.");
                Set("seeding", "Kütüphaneye alındı · Parçalar paylaşılıyor.", snapshot.Progress, snapshot.Size, 0, snapshot.Up, snapshot.Peers, snapshot.Name);
                await Task.Delay(1500, token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            if (store.GetDownload(record.Id)?.Ownership?.WaitReason is null) Set("queued", stopping.IsCancellationRequested ? "Sunucu açıldığında devam edilecek." : "Yeni komut uygulanacak.",
                store.GetDownload(record.Id)?.Progress ?? 0, store.GetDownload(record.Id)?.TotalBytes ?? 0);
        }
        catch (DownloadBudgetException ex) { store.HoldDownloadBudget(record.Id, record.Revision, ex, DateTimeOffset.UtcNow); }
        catch (OperationCanceledException) { Set("failed", "Kaynak bağlantısı zaman aşımına uğradı; yeniden dene veya başka bir sürüm seç.", store.GetDownload(record.Id)?.Progress ?? 0, store.GetDownload(record.Id)?.TotalBytes ?? 0); }
        catch (Exception ex)
        {
            logger.LogWarning("İndirme {Id} durdu: {ErrorType}", record.Id, ex.GetType().Name);
            var message = ex is DomainException ? ex.Message : "İndirme tamamlanamadı. Bağlantıyı ve boş disk alanını kontrol edip yeniden dene.";
            Set("failed", message, store.GetDownload(record.Id)?.Progress ?? 0, store.GetDownload(record.Id)?.TotalBytes ?? 0);
        }
        finally
        {
            if (session is not null)
            {
                try { await session.StopAsync(); Record(session.Snapshot); }
                catch (Exception ex) { logger.LogWarning("İndirme {Id} son sayaçları okunamadı: {Type}", record.Id, ex.GetType().Name); }
                try { await session.DisposeAsync(); }
                catch (Exception ex) { logger.LogWarning("İndirme {Id} kapatılırken {ErrorType}", record.Id, ex.GetType().Name); }
            }
            if (running.Budget is { } budget) store.CloseDownloadBudgetRun(budget, DateTimeOffset.UtcNow);
            if (ownsDownload) _downloads.Release();
            if (ownsSeeder) _seeders.Release();
            events.Publish("downloads.changed");
        }

        void Record(TorrentSnapshot snapshot) => store.RecordTransferCounters(record.Id, record.Revision, counterId,
            Math.Max(0, snapshot.Uploaded - baselineUp), Math.Max(0, snapshot.Received - baselineDown), (long)(seedClock?.Elapsed.TotalSeconds ?? 0), running.Budget);

        void Set(string status, string message, double progress, long size, long down = 0, long up = 0, int peers = 0, string? name = null)
        {
            store.UpdateDownload(record.Id, record.Revision, status, message, progress, size, down, up, peers, name);
            events.Publish("downloads.changed");
        }
    }
    public static bool SeedSatisfied(DownloadRecord record, TransferPolicy policy, SourceSeedRules source)
    {
        var ratio = record.UploadedBytes / (double)Math.Max(1, record.ReceivedBytes > 0 ? record.ReceivedBytes : record.TotalBytes);
        var requiredRatio = ratio >= source.MinimumRatio; var requiredTime = record.SeedSeconds >= source.MinimumMinutes * 60L;
        var sourceSatisfied = source.SeedType switch
        {
            "ratio" => requiredRatio, "seedtime" => requiredTime, "both" => requiredRatio && requiredTime,
            _ => source.MinimumRatio <= 0 && source.MinimumMinutes <= 0 || source.MinimumRatio > 0 && requiredRatio || source.MinimumMinutes > 0 && requiredTime
        };
        if (!sourceSatisfied) return false;
        var ratioMet = policy.SeedRatio is { } target && ratio >= target;
        var timeMet = policy.SeedMinutes is { } minutes && record.SeedSeconds >= minutes * 60L;
        return policy.SeedStop == "either" ? ratioMet || timeMet : (policy.SeedRatio is not null || policy.SeedMinutes is not null)
            && (policy.SeedRatio is null || ratioMet) && (policy.SeedMinutes is null || timeMet);
    }
}
