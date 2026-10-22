namespace HomeServer.Core;

public sealed record SubtitleConnectionInput(string? ApiKey, bool Clear = false);
public sealed record SubtitleLogin(string Username, string Password);
public sealed record SubtitleConnection(bool Configured, bool Authenticated, string? Username, string? ExpiresAt, int? Remaining, string? ResetAt, string? LastError);
public sealed record SubtitleSecrets(string ApiKey, string? Token, string Host, string? Username, string? ExpiresAt);
public sealed record SubtitleLanguageProfile(string[] Languages, string HearingImpaired = "include", string Forced = "exclude",
    bool Enabled = false, bool AutoDownload = false, int MinimumScore = 80, int MaximumDailyDownloads = 5, bool SkipAudioLanguage = false,
    bool AllowUpgrades = false, int CutoffScore = 100, int MinimumUpgradeDelta = 5, int SearchIntervalHours = 12, string[]? ExcludedAudioLanguages = null);
public sealed record SubtitleSearchInput(string Language = "tr", string HearingImpaired = "include", string Forced = "exclude", int Page = 1);
public sealed record SubtitleCandidate(string Id, string MediaId, long FileId, string Language, string Name, string Release,
    bool Forced, bool HearingImpaired, bool HashMatched, bool Trusted, bool Eligible, int Score, string[] Reasons, string? ProviderUrl);
public sealed record SubtitleSearchPage(IReadOnlyList<SubtitleCandidate> Items, int Page, int TotalPages);
public sealed record StoredSubtitleCandidate(SubtitleCandidate Candidate, string Fingerprint, string? ContentId, string ManagedId, string ExpiresAt, int MatchRevision = 0,
    int ConnectionRevision = 0, string? ProtectedTicket = null, string? TicketExpiresAt = null);
public sealed record SubtitleWanted(string MediaId, string Title, string Language, string Status, string Message, string NextRun);
public sealed record SubtitlePolicyStamp(int GlobalRevision, int OverrideRevision, int ConnectionRevision);
public sealed record EffectiveSubtitlePolicy(SubtitleLanguageProfile Profile, SubtitlePolicyStamp Stamp, bool Inherited, SubtitleLanguageProfile? Override, bool UpgradesPaused = false);
public sealed record SubtitleOverrideInput(SubtitleLanguageProfile? Profile, int ExpectedRevision = 0);
public sealed record SubtitleLease(string Token, string MediaId, string Language, SubtitlePolicyStamp Stamp);
public sealed record SubtitleJob(SubtitleWanted Info, SubtitleLease Lease, SubtitleLanguageProfile Profile, int Attempts);
public sealed record SubtitlePublication(string Origin, string Fingerprint, int MatchRevision, long? ProviderFileId = null, int? Score = null,
    string? SupersedesId = null, SubtitlePolicyStamp? Policy = null, SubtitleLease? Lease = null, int? ConnectionRevision = null, string? JobId = null, int? JobRevision = null);
public sealed record SubtitleDownloadInput(string? ReplaceId = null);
public sealed record SubtitleRestoreInput(string? ReplaceId = null);
public sealed record SubtitleUpgradeDecision(bool Search, string Reason, ManagedSubtitle? Current = null);
public sealed record SubtitleSourceInfo(SubtitleTrack Track, string Path, int? Index, string Fingerprint);
public sealed record SubtitleBurnPlan(string TrackId, bool Image, int? Stream, double DelaySeconds, int Width, int Height, string? TextFile = null);
