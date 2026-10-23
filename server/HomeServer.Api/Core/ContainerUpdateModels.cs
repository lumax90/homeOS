namespace HomeServer.Core;

public sealed record ContainerUpdateActivity(int Games, int Playback, int Transfers, int Recordings, int Jobs)
{
    public bool Idle => Games == 0 && Playback == 0 && Transfers == 0 && Recordings == 0 && Jobs == 0;
}
public sealed record ContainerUpdatePreferences(bool Enabled = true);
public sealed record ContainerUpdateAttempt(string Id, string PreparedAt, string FromVersion, string? FromRevision,
    string BackupId, string State, string? FinishedAt = null, string? ToVersion = null, string? ToRevision = null);
public sealed record ContainerUpdateJournal(int Format, ContainerUpdateAttempt[] Attempts);
public sealed record ContainerUpdateReply(int Protocol, int ExitCode, string State, string Message, string? BackupId = null);
