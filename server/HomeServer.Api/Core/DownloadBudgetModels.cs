namespace HomeServer.Core;

public sealed record DownloadBudgetPolicy(bool Enabled = true, int? MonthlyAcquisitions = null, long? MonthlyPayloadBytes = null,
    long? MaximumPayloadBytes = null, int MaximumConcurrent = 3, int Revision = 0);
public sealed record DownloadPrincipal(string UserId, int UserVersion, string? LoginId, string Origin);
public sealed record DownloadBudgetRun(string Id, string DownloadId, int DownloadRevision, string? UserId, int PolicyRevision, int Generation);
public sealed record DownloadOwnership(string? UserId, string? DisplayName, string Origin, int Generation, long ChargedBytes, string? WaitReason, string? RetryAt);
public sealed record DownloadBudgetUsage(string UserId, string DisplayName, DownloadBudgetPolicy Policy, string Period, string ResetsAt,
    int Acquisitions, long CommittedBytes, long ReservedBytes, long ReceivedBytes, long UploadedBytes, int ActiveTransfers, int WaitingTransfers);
public sealed record DownloadBudgetEvent(string Id, string? DownloadId, string Title, string Action, long Bytes, string Period, string CreatedAt);
public sealed record DownloadBudgetHistory(IReadOnlyList<DownloadBudgetEvent> Items, int Total, int Offset, int Limit);
public sealed record AssignDownloadOwner(string UserId, int Revision);
public sealed class DownloadBudgetException(string code, string message, DateTimeOffset? retryAt = null) : DomainException(message, 429)
{
    public string Code { get; } = code;
    public DateTimeOffset? RetryAt { get; } = retryAt;
}
