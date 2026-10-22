namespace HomeServer.Core;

public sealed record MusicMonitorSettings(bool Enabled = false, string Mode = "future", string[]? PrimaryTypes = null,
    string[]? ExcludedSecondaryTypes = null, string[]? ReleaseStatuses = null, string[]? Countries = null, string[]? Formats = null,
    bool Search = false, bool AutoGrab = false, bool AllowUpgrades = false, string Cutoff = "lossless", int CutoffBitrateKbps = 320,
    int RefreshHours = 12, int SearchIntervalMinutes = 360, int DelayMinutes = 0, int MaximumDailyGrabs = 2, MusicQualityRules? Quality = null);
public sealed record MusicMonitorInput(MusicMonitorSettings Settings, int ExpectedRevision = 0);
public sealed record MusicMonitor(string Id, string UserId, int UserVersion, string Kind, string Mbid, string Name,
    MusicMonitorSettings Settings, int Revision, int Cursor, string CreatedAt, string NextRun, string? LastRun, string? LastError);
public sealed record MonitoredMusicAlbum(string MonitorId, MusicBrainzAlbum Metadata, bool Wanted, bool? Override, string FirstSeen);
public sealed record MusicAlbumOverride(bool? Wanted, int ExpectedRevision);
public sealed record MusicAudioQuality(string Codec, int Tier, int BitrateKbps = 0, int BitDepth = 0, int SampleRate = 0);
public sealed record MusicTargetState(bool Available, bool Pending, bool Offline, bool UnknownQuality, MusicAudioQuality? Quality, MusicAudioQuality[]? Qualities = null);
public sealed record MusicAutomationLease(string JobId, int Revision);
public sealed record MusicAutomationJob(string Id, string MonitorId, string GroupId, string Title, int Revision, string Status,
    int Attempts, string DueAt, string Message, string? ResultId, string? Fingerprint, string? DownloadId, MusicRelease? Release);
public sealed record MusicBlockedRelease(string GroupId, string Fingerprint, string Reason, string BlockedAt);
