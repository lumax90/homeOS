namespace HomeServer.Core;

public sealed record AutomationInput(bool Enabled = false, bool AutoGrab = false, bool AllowUpgrades = false,
    int CutoffResolution = 1080, int SearchIntervalMinutes = 360, int DelayMinutes = 0, int MinimumAgeHours = 0, int MaximumDailyGrabs = 3, bool SameQualityUpgrades = false);
public sealed record AutomationRule(string ContentId, string Title, AutomationInput Settings, int Revision, string? LastRun, string NextRun, string? LastError, string? BudgetUserId = null, string? BudgetName = null);
public sealed record AutomationTarget(string ContentId, int? Season, int? Episode, int BestResolution, bool Available, int? ExistingQualityScore = null)
{
    public string Key => Season is null ? "movie" : $"s{Season}:e{Episode}";
}
public sealed record AutomationJob(string Id, string ContentId, int? Season, int? Episode, string TargetKey, int RuleRevision,
    string Status, int Attempts, string DueAt, string Message, string? ReleaseId, string? Fingerprint, string? DownloadId,
    AutomationInput Settings, QualityProfile Profile, string CreatedAt);
public sealed record AutomationLease(string JobId, int Revision);
public sealed record RssReleaseBatch(IndexerInfo Source, IReadOnlyList<RawRelease> Releases);
public sealed record BlockedRelease(string ContentId, string Title, string Fingerprint, string Reason, string BlockedAt);
