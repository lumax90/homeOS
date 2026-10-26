namespace HomeServer.Core;

public sealed record ContainerUpdateActivity(int Games, int Playback, int Transfers, int Recordings, int Jobs)
{
    public bool Idle => Games == 0 && Playback == 0 && Transfers == 0 && Recordings == 0 && Jobs == 0;
}
public sealed record ContainerUpdatePreferences(bool Enabled = true, bool AutomaticInstall = false);
public sealed record ContainerUpdateAttempt(string Id, string PreparedAt, string FromVersion, string? FromRevision,
    string BackupId, string State, string? FinishedAt = null, string? ToVersion = null, string? ToRevision = null,
    string? TargetDigest = null, string? TargetRevision = null);
public sealed record ContainerUpdateJournal(int Format, ContainerUpdateAttempt[] Attempts);
public sealed record ContainerUpdateReply(int Protocol, int ExitCode, string State, string Message, string? BackupId = null);
public sealed record ContainerUpdateCandidate(string Version, string Revision, string Digest, long TotalBytes, long? EstimatedDownloadBytes);
public sealed record ContainerUpdateAvailability(int Format = 1, ContainerUpdateCandidate? Candidate = null,
    string? ApprovedDigest = null, string Phase = "idle", string? CheckedAt = null, string? HelperSeenAt = null,
    string? InstalledDigest = null, string? Error = null, string? DismissedDigest = null);
public sealed record ContainerUpdateApproval(string Digest);
public sealed record ContainerUpdateAgentCommand(int Protocol, string Action, string? Digest = null);
