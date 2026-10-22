namespace HomeServer.Core;

public sealed record OfflinePolicy(bool Enabled = true, int MaximumItems = 50, long MaximumBytes = 68719476736, int LeaseDays = 7, int Revision = 0);
public sealed record OfflineInput(string ClientId, string Profile = "original", int? AudioStream = null, string? SubtitleId = null, bool BurnSubtitle = false, double SubtitleDelaySeconds = 0, int? ExpectedMatchRevision = null, string? SubtitleFingerprint = null, string? ExpectedUserId = null, string? ExpectedSessionId = null);
public sealed record OfflineAsset(string Id, string ContentType, long Bytes, string Fingerprint, int ChunkBytes, int Chunks, string? Language = null, string? Title = null);
public sealed record OfflineManifest(string GrantId, string MediaId, string Title, string Kind, string LibraryName, double DurationSeconds,
    string SourceFingerprint, int MatchRevision, string ExpiresAt, OfflineAsset[] Assets, string ProgressStamp, double PositionSeconds, bool IsWatched);
public sealed record OfflineExport(string Id, string UserId, string DeviceId, string MediaId, string SourceFingerprint, long SourceCatalogBytes, string SourceCatalogModified,
    int MatchRevision, int UserVersion, int PolicyRevision, OfflineInput Input, string Status, int Revision,
    [property: System.Text.Json.Serialization.JsonIgnore] string? Lease, int Progress, string Message, string CreatedAt, string ExpiresAt, long ReservedBytes, OfflineManifest? Manifest, long LastSequence, bool RequiresPreparation = false);
public sealed record OfflineCommand(int Revision);
public sealed record OfflineProgressInput(string EventId, long Sequence, double PositionSeconds, string ExpectedStamp, string ExpectedUserId);
public sealed record OfflineProgressResult(string Status, string Stamp, double PositionSeconds, double DurationSeconds, bool IsWatched, long Sequence);
public sealed record OfflineDeviceStatus(string ServerId, string UserId, string DeviceId, string SessionId, string DisplayName, OfflinePolicy Policy, IReadOnlyList<OfflineExport> Exports);
