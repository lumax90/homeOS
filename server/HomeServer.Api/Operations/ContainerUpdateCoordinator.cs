using System.Text.Json;
using HomeServer.Core;
using HomeServer.Downloads;
using HomeServer.Gaming;
using HomeServer.Infrastructure;

namespace HomeServer.Operations;

public sealed class ContainerUpdateCoordinator(HomePaths paths, MediaStore store, BackupService backups,
    ContainerRequestGate requests, IConfiguration configuration, TimeProvider clock, ILogger<ContainerUpdateCoordinator> logger,
    NativeGameManager? native = null, DownloadWorker? downloads = null, bool? supported = null)
{
    public static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(8);
    private const string PreferenceKey = "operations.container-auto-updates";
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _lease;
    private DateTimeOffset _until;
    private string? _error;
    private string JournalPath => Path.Combine(paths.Data, "updates", "container", "history.json");
    public bool Supported => supported ?? (OperatingSystem.IsLinux() && Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true");
    public bool Configured => Supported && configuration.GetValue<bool>("Home:Updates:Automatic");
    public string? Revision => configuration["HOME_SOURCE_REVISION"] is { Length: 40 } value && value.All(char.IsAsciiHexDigit) ? value : null;
    public bool Enabled => Configured
        && (store.GetSetting(PreferenceKey) is not { } json || JsonSerializer.Deserialize<ContainerUpdatePreferences>(json, Json)?.Enabled == true);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public void ControlUnavailable() => _error = "Local automatic-update control is unavailable.";

    public object Status()
    {
        try
        {
            var history = ReadJournal().Attempts;
            return new { supported = Supported, configured = Configured, enabled = Enabled, preparing = _lease is not null && _until > clock.GetUtcNow(),
                revision = Revision, channel = "stable", backupRetention = 3, last = history.FirstOrDefault(), error = _error };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or DomainException)
        { return new { supported = Supported, configured = Configured, enabled = false, preparing = false, revision = Revision, channel = "stable", backupRetention = 3, error = "Update state requires review." }; }
    }
    public void Configure(ContainerUpdatePreferences input)
    {
        if (!Configured) throw new DomainException("Apply the automatic-update Compose configuration before enabling container updates.", 409);
        store.SetSetting(PreferenceKey, JsonSerializer.Serialize(input, Json));
    }
    public ContainerUpdateActivity Activity()
    {
        var activity = store.ContainerUpdateActivity();
        return activity with { Games = Math.Max(activity.Games, native?.ContainerUpdateBusy == true ? 1 : 0),
            Transfers = Math.Max(activity.Transfers, downloads?.ContainerUpdateBusy == true ? 1 : 0) };
    }
    public async Task<ContainerUpdateReply> PrepareAsync(CancellationToken token)
    {
        if (!await _gate.WaitAsync(0, token)) return Skip("busy", "Another update operation is active.");
        string? owner = null; string? unusedBackup = null;
        try
        {
            ExpireLease();
            if (!Enabled) return Skip("disabled", "Automatic updates are disabled.");
            if (_lease is not null || backups.PendingRestore() is not null) return Skip("busy", "An update or restore is already pending.");
            var history = ReadJournal();
            if (!Activity().Idle) return Skip("busy", "Home is in use; the update will be retried later.");
            owner = Guid.NewGuid().ToString("N");
            if (!requests.Freeze(owner, LeaseDuration)) return Skip("busy", "An API operation is active.");
            if (!store.BeginContainerUpdate(owner, LeaseDuration) || !Activity().Idle)
                return Skip("busy", "A background operation is active.");
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(token);
            budget.CancelAfter(TimeSpan.FromMinutes(2));
            // Only backups marked as automatic may be removed; user-created backups are retained.
            foreach (var previous in history.Attempts.Skip(2))
                if (File.Exists(Path.Combine(paths.Data, "backups", AppFiles.Id(previous.BackupId) + ".zip")))
                    await backups.DeleteContainerUpdateAsync(previous.BackupId, budget.Token);
            var backup = await backups.CreateContainerUpdateAsync(owner, budget.Token);
            unusedBackup = backup.Id;
            if (!Activity().Idle) return Skip("busy", "Activity changed during preparation; Home will remain running.");
            var attempt = new ContainerUpdateAttempt(owner, clock.GetUtcNow().ToString("O"), BuildInfo.Version, Revision, backup.Id, "prepared");
            WriteJournal(new(1, [attempt, .. history.Attempts.Take(2)]));
            unusedBackup = null;
            _lease = owner; _until = clock.GetUtcNow().Add(LeaseDuration); _error = null;
            owner = null;
            logger.LogInformation("Automatic container update prepared with a verified application backup.");
            return new(1, 0, "prepared", "Application backup is ready; container update may proceed.", backup.Id);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _error = "Update preparation failed; the running container was retained.";
            logger.LogWarning("Automatic update preparation deferred: {ErrorType}", ex.GetType().Name);
            return Skip("failed", _error);
        }
        finally
        {
            if (owner is not null) { store.EndContainerUpdate(owner); requests.Release(owner); }
            try
            {
                if (unusedBackup is not null) await backups.DeleteContainerUpdateAsync(unusedBackup, CancellationToken.None);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            { logger.LogWarning("An unused automatic backup was retained after preparation failure."); }
            finally { _gate.Release(); }
        }
    }
    public async Task CompleteStartupAsync(CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            var history = ReadJournal();
            if (history.Attempts.FirstOrDefault() is not { State: "prepared" } before) return;
            var changed = before.FromVersion != BuildInfo.Version || before.FromRevision != Revision;
            var completed = before with { State = changed ? "updated" : "restarted", FinishedAt = clock.GetUtcNow().ToString("O"), ToVersion = BuildInfo.Version, ToRevision = Revision };
            WriteJournal(history with { Attempts = [completed, .. history.Attempts.Skip(1)] });
            logger.LogInformation("Container update startup completed on Home {Version}.", BuildInfo.Version);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or DomainException)
        { _error = "Update history could not be finalized."; logger.LogWarning("Container update history requires review: {ErrorType}", ex.GetType().Name); }
        finally { _gate.Release(); }
    }
    public async Task<ContainerUpdateReply> CancelPreparationAsync(CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            ReleaseLease("deferred");
            return new(1, 0, "ready", "The update cycle has finished.");
        }
        finally { _gate.Release(); }
    }
    public async Task ExpireAsync(CancellationToken token)
    {
        if (!await _gate.WaitAsync(0, token)) return;
        try { ExpireLease(); }
        finally { _gate.Release(); }
    }
    private void ExpireLease() { if (_lease is not null && _until <= clock.GetUtcNow()) ReleaseLease("expired"); }
    private void ReleaseLease(string state)
    {
        if (_lease is not { } owner) return;
        store.EndContainerUpdate(owner); requests.Release(owner); _lease = null;
        try
        {
            var history = ReadJournal();
            if (history.Attempts.FirstOrDefault() is { } current && current.Id == owner && current.State == "prepared")
                WriteJournal(history with { Attempts = [current with { State = state, FinishedAt = clock.GetUtcNow().ToString("O") }, .. history.Attempts.Skip(1)] });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or DomainException)
        { _error = "Update history requires review."; }
    }
    private ContainerUpdateJournal ReadJournal()
    {
        if (!File.Exists(JournalPath)) return new(1, []);
        AppFiles.RejectLink(JournalPath);
        if (new FileInfo(JournalPath).Length > 64 * 1024) throw new DomainException("Update history exceeds its bound.", 409);
        var result = JsonSerializer.Deserialize<ContainerUpdateJournal>(File.ReadAllText(JournalPath), Json) ?? throw new DomainException("Update history is invalid.", 409);
        if (result.Format != 1 || result.Attempts is null || result.Attempts.Length > 3 || result.Attempts.Any(row => row is null || !Guid.TryParseExact(row.Id, "N", out _) || row.BackupId != row.Id))
            throw new DomainException("Update history is invalid.", 409);
        return result;
    }
    private void WriteJournal(ContainerUpdateJournal journal)
    {
        _ = AppFiles.DirectoryIn(paths.Data, "updates", "container");
        if (File.Exists(JournalPath)) AppFiles.RejectLink(JournalPath);
        PackageTrust.AtomicJson(JournalPath, journal);
    }
    private static ContainerUpdateReply Skip(string state, string message) => new(1, 75, state, message);
}
