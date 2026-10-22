namespace HomeServer.Core;

public sealed record WatchIdentity(string UserId, string SessionId);
public sealed record WatchPlaybackBinding(string RoomId, string MemberId, long ClientRevision, int Epoch);
public sealed record WatchRoomCreate(string ClientId, long ClientRevision, string PlaybackId, string Mode, double PositionSeconds,
    bool Playing = false, double Rate = 1, double Volume = 1, bool Muted = false, WatchIdentity? Identity = null);
public sealed record WatchRoomJoin(string ClientId, long ClientRevision, string Role = "player", WatchIdentity? Identity = null);
public sealed record WatchRoomLeave(long ClientRevision, WatchIdentity? Identity = null);
public sealed record WatchRoomHeartbeat(long ClientRevision, long Sequence, int Epoch, int SyncRevision, string? PlaybackId,
    double PositionSeconds, bool Ready, bool Playing, string State = "preparing", int Revision = 0, WatchIdentity? Identity = null);
public sealed record WatchRoomCommand(string MemberId, long ClientRevision, string CommandId, int ExpectedRevision, string Action,
    double? PositionSeconds = null, double? Rate = null, string? MediaId = null, string? Name = null, string? UserId = null,
    bool? Enabled = null, double? Volume = null, bool? Muted = null, WatchIdentity? Identity = null);
public sealed record WatchRoomParticipant(string Id, string UserId, string Name, string Device, string Role, bool Connected, bool Ready,
    string State, double PositionSeconds, bool Playing, bool Owner);
public sealed record WatchRoomInvite(string UserId, string Name, bool Removed);
public sealed record WatchRoomEvent(string Action, string Actor, int Revision, string CreatedAt);
public sealed record WatchRoomSnapshot(string Id, string Mode, string OwnerId, string OwnerName, string MediaId, string Title, string Kind,
    string Fingerprint, int MatchRevision, string State, double PositionSeconds, double DurationSeconds, long AnchorUnixMs, long ServerUnixMs,
    double Rate, double Volume, bool Muted, int Revision, int SyncRevision, int Epoch, bool GuestControl, bool CanControl, bool CanManage,
    string Message, WatchRoomParticipant[] Participants, WatchRoomInvite[] Invitations);
public sealed record WatchRoomConnection(WatchRoomSnapshot Room, string MemberId, string ClientId, long ClientRevision, string Role);
public sealed record WatchRoomCommandResult(int AppliedRevision, bool Duplicate, WatchRoomSnapshot Room);
public sealed record WatchRoomSummary(string Id, string Mode, string MediaId, string Title, string Kind, string OwnerName, string Device,
    string State, int Players, bool Owned, bool Joined);
