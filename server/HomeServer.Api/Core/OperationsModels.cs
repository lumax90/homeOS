namespace HomeServer.Core;

public sealed record BackupFile(string Path, long Length, string Sha256);
public sealed record BackupManifest(int Format, int Schema, string Version, string CreatedAt, string OriginalDataPath, BackupFile[] Files);
public sealed record BackupInfo(string Id, string CreatedAt, long SizeBytes, int Schema);
public sealed record SystemJob(string Id, string Kind, string Status, string Message, string CreatedAt, string? FinishedAt);
public sealed record RestorePlan(string Id, string? PreviousState, string ManifestHash, string PreparedAt);
