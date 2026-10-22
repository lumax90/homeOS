namespace HomeServer.Watching;

// A bounded aggregate is committed with its command receipt and client intent in one SQLite transaction.
// Only the explicitly projected Core DTOs cross the HTTP boundary.
internal sealed class WatchRoomDocument
{
    public string Id { get; set; } = "";
    public string OwnerId { get; set; } = "";
    public string OwnerLoginId { get; set; } = "";
    public string HostMemberId { get; set; } = "";
    public string Mode { get; set; } = "together";
    public string MediaId { get; set; } = "";
    public string Fingerprint { get; set; } = "";
    public int MatchRevision { get; set; }
    public string Title { get; set; } = "";
    public string Kind { get; set; } = "video";
    public double DurationSeconds { get; set; }
    public string State { get; set; } = "paused";
    public double PositionSeconds { get; set; }
    public long AnchorUnixMs { get; set; }
    public double Rate { get; set; } = 1;
    public double Volume { get; set; } = 1;
    public bool Muted { get; set; }
    public int Revision { get; set; } = 1;
    public int SyncRevision { get; set; } = 1;
    public int Epoch { get; set; } = 1;
    public bool GuestControl { get; set; }
    public string Message { get; set; } = "Oda hazır. Başlamak için oynat.";
    public long CreatedUnixMs { get; set; }
    public long ActiveUnixMs { get; set; }
    public List<WatchMember> Members { get; set; } = [];
    public List<WatchInvite> Invitations { get; set; } = [];
}
internal sealed class WatchMember
{
    public string Id { get; set; } = "";
    public string UserId { get; set; } = "";
    public string LoginId { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public string ClientId { get; set; } = "";
    public long ClientRevision { get; set; }
    public string Role { get; set; } = "player";
    public bool Active { get; set; } = true;
    public long SeenUnixMs { get; set; }
    public long Sequence { get; set; }
    public int SyncRevision { get; set; }
    public string? PlaybackId { get; set; }
    public bool Ready { get; set; }
    public bool Playing { get; set; }
    public double PositionSeconds { get; set; }
    public string State { get; set; } = "preparing";
}
internal sealed class WatchInvite
{
    public string UserId { get; set; } = "";
    public bool Removed { get; set; }
}
