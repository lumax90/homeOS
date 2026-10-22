namespace HomeServer.Core;

public sealed record PlaybackLimits(int MaximumStreams = 16, int MaximumStreamsPerDevice = 2, int Revision = 0);
public sealed record UserPlaybackLimits(int MaximumStreams = 3, int MaximumDevices = 3, int Revision = 0);
public sealed record PlaybackDevice(string Id, string Name, int? MaximumStreams, int Revision, string CreatedAt, string LastSeenAt, bool Current = false);
public sealed record PlaybackDeviceInput(string Name, int? MaximumStreams, int Revision);
public sealed record PlaybackActor(string UserId, string LoginId, string DeviceId);
public sealed record PlaybackLease(string Id, string UserId, string LoginId, string DeviceId, string ClientId, long ClientRevision,
    string MediaId, string Fingerprint, int MatchRevision, string Title, string Mode, string Status, string CreatedAt, string LastSeenAt,
    string? PausedAt, double PositionSeconds, string? EndedAt, string? EndReason, PlaybackSession? Session,
    int GlobalRevision, int UserRevision, int DeviceRevision, string? WatchRoomId = null, string? WatchMemberId = null, int? WatchEpoch = null);
public sealed record PlaybackAdmission(PlaybackLease Lease, bool Existing, string[] Replaced);
public sealed record PlaybackActivity(string Id, string UserId, string DisplayName, string DeviceId, string DeviceName, string MediaId, string Title,
    string Mode, string Status, string StartedAt, string LastSeenAt, bool Paused, double PositionSeconds, string? EndedAt, string? EndReason, bool CurrentDevice);
public sealed record PlaybackUsage(PlaybackLimits Server, UserPlaybackLimits User, int DeviceMaximumStreams, IReadOnlyList<PlaybackActivity> Active, IReadOnlyList<PlaybackActivity> History);
public sealed record MusicPlaybackStart(string MediaId, string? ClientId = null, long ClientRevision = 1);
public sealed record PlaybackMediaGrant(string MediaId, string Fingerprint, int MatchRevision, long? Frames);
