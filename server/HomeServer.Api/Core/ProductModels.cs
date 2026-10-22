namespace HomeServer.Core;

public sealed record DiscoveryPage(IReadOnlyList<ProviderContent> Items, string Provider, int Page, int? TotalPages, bool HasMore, int? NextPage = null);
public sealed record DiscoveryGenre(int Id, string Name);
public sealed record DiscoveryPersonalPage(IReadOnlyList<ContentEntry> Items, int Total, int Offset, int Limit);
public sealed record ContentEntry(string Id, ProviderContent Metadata, string MonitorMode, string QualityProfileId,
    string? RequestedAt, string? LastSyncAt, string? LastError, int FileCount, int EpisodeCount, int MissingCount, bool Requested, string SyncState, string? RequestStatus = null, int CatalogRevision = 1);
public sealed record ContentRequestInput(string Provider, string Kind, int ProviderId, string MonitorMode = "all", string QualityProfileId = "balanced", int? Season = null, int? Episode = null);
public sealed record TrackingUpdate(string MonitorMode, string QualityProfileId);
public sealed record EpisodeEntry(string Id, string ContentId, string ContentTitle, int SeasonNumber, int EpisodeNumber, string Title,
    string? AirDate, string State, int FileCount);
public sealed record EpisodePage(IReadOnlyList<EpisodeEntry> Items, int Total, int Offset, int Limit, IReadOnlyList<int> Seasons);
public sealed record QualityRules(int[] Resolutions, double MaxSizeGiB, int MinSeeders,
    string[] RequiredTerms, string[] ForbiddenTerms, string[] PreferredTerms, bool AllowUnknownResolution,
    double MinSizeGiB = 0, string[]? Languages = null, string[]? Sources = null, CustomFormat[]? CustomFormats = null,
    int MinimumCustomScore = 0, string[]? Protocols = null, string PreferredProtocol = "any", int MaximumAgeDays = 0);
public sealed record QualityProfile(string Id, string Name, QualityRules Rules);
public sealed record QualityProfileInput(string Name, QualityRules Rules);
public sealed record ParsedRelease(int? Year, int? Season, int? Episode, bool SeasonPack, int? Resolution,
    string Source, string? Codec, bool Proper, string[] Languages, bool MultipleEpisodes = false, string? Hdr = null, int ProperVersion = 0,
    int[]? EpisodeNumbers = null, string? AirDate = null, int? AbsoluteNumber = null, int? EndAbsoluteNumber = null, int? PartNumber = null, bool Ambiguous = false);
public sealed record ReleaseEvaluation(bool Eligible, int Score, string[] Rejections, string[] Notes, ParsedRelease Parsed, int QualityScore = 0, int CustomScore = 0);

public sealed record IndexerInput(string Name, string Endpoint, string Protocol, string? ApiKey,
    int Priority = 25, string Categories = "", string AllowedHosts = "", bool Enabled = true);
public sealed record IndexerCapabilities(int MaxPageSize, Dictionary<string, string[]> Searches, Dictionary<int,string> Categories);
public sealed record IndexerInfo(string Id, string Name, string Endpoint, string Protocol, bool Configured,
    bool Enabled, int Priority, string Categories, string AllowedHosts, IndexerCapabilities? Capabilities,
    string? LastCheckedAt, string? LastSuccessAt, string? LastError, string? RetryAfter, int Revision = 1, string? ProviderId = null);
public sealed record IndexerSecret(IndexerInfo Info, string Secret);
public sealed record RawRelease(string Guid, string Title, string Protocol, long? SizeBytes, int? Seeders,
    string? PublishedAt, string Url, string? InfoHash, Dictionary<string,string[]> Attributes);
public sealed record ReleaseResult(string Id, string IndexerId, string IndexerName, string Title, string Protocol,
    long? SizeBytes, int? Seeders, string? PublishedAt, ReleaseEvaluation Evaluation, string? DownloadId, string Fingerprint = "");
public sealed record SearchSourceOutcome(string IndexerId, string Name, int Results, string? Error, bool MoreAvailable = false);
public sealed record ReleaseSearchResult(IReadOnlyList<ReleaseResult> Items, IReadOnlyList<SearchSourceOutcome> Sources);
public sealed record ReleaseSearchInput(string ContentId, int? Season = null, int? Episode = null, string? ProfileId = null, int Page = 0);
public sealed record GrabRelease(bool OverrideRejections = false);
public sealed record AcquisitionContext(string ContentId, int? Season, int? Episode, string ReleaseId, AutomationLease? Automation = null);
public sealed record StoredRelease(ReleaseResult Result, string ContentId, int? Season, int? Episode, string ProtectedUrl, string ProfileJson, string ExpiresAt, string? InfoHash = null);
public sealed record AcquisitionWork(string DownloadId, string ContentId, int? Season, int? Episode, string? ProfileJson, string? LibraryId, string? ScanJobId, EpisodeNumbering? Numbering = null);
public sealed record VerifiedAcquisitionFile(string RelativePath, long Length, ProviderEpisode? Episode, ProviderEpisode[]? Episodes = null, int? PartNumber = null);
public sealed record AcquisitionStatus(string ContentId, string Title, string Status, string Message);
public sealed record ImportProfileReview(string QualityProfileId);
