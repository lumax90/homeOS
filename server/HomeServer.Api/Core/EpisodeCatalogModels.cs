namespace HomeServer.Core;

public sealed record EpisodeRepairChoice(int ProviderId, string? ExistingEpisodeId);
public sealed record EpisodeRepairInput(string PlanId, int ExpectedRevision, EpisodeRepairChoice[]? Choices = null);
public sealed record EpisodeRefreshChange(ProviderEpisode Incoming, string? ExistingId, int? PreviousSeason, int? PreviousEpisode, string Action, bool NeedsChoice);
public sealed record EpisodeRefreshPreview(string PlanId, string ContentId, int CatalogRevision, string ExpiresAt, EpisodeRefreshChange[] Changes,
    EpisodeCatalogRow[] Retired, string[] Conflicts, string[] Notes, bool CanApply);
public sealed record EpisodeCatalogRow(string Id, ProviderEpisode Metadata, bool Retired = false);
public sealed record EpisodeOrderItem(int Season, int Episode, string[] EpisodeIds, EpisodeCoordinate[]? Coordinates = null);
public sealed record CatalogEpisodeOrder(string Id, string ContentId, string Name, string Kind, string Source, int Revision, EpisodeOrderItem[] Items);
public sealed record EpisodeOrderInput(string Name, string Kind, EpisodeOrderItem[] Items, string? Id = null, int ExpectedRevision = 0, int? ExpectedCatalogRevision = null);
public sealed record EpisodeOrderSelection(string? OrderId, int ExpectedRevision);
public sealed record EpisodeAliasTarget(string EpisodeId, double? StartSeconds = null, double? EndSeconds = null, int? Season = null, int? Episode = null);
public sealed record EpisodeAliasInput(string Provider, int ProviderId, int Season, int Episode, EpisodeAliasTarget[] Targets, int ExpectedRevision);
public sealed record ProviderOrderGroup(string Id, string Name, int Position, ProviderEpisode[] Episodes);
public sealed record ProviderOrderDetails(string Id, string Name, int Type, ProviderOrderGroup[] Groups, string Fingerprint = "");
public sealed record ImportCatalogOrder(string Provider, string OrderId, Dictionary<string, int> Seasons, int ExpectedCatalogRevision, string? Id = null, int ExpectedRevision = 0, string? ExpectedSourceFingerprint = null);
public sealed record EpisodeBoundary(string EpisodeId, double StartSeconds, double EndSeconds);
public sealed record EpisodeBoundaryResult(EpisodeBoundary[] Segments, string Method, string[] Evidence, string[] Warnings, bool CanApply);
public sealed record EpisodeBoundaryJob(string Id, string MediaId, string Fingerprint, int MatchRevision, int Revision, string Status, int Progress, string Message, string CreatedAt, EpisodeBoundaryResult? Result);
public sealed record EpisodeBoundaryCommand(int Revision);
public sealed record EpisodeBoundaryStart(int ExpectedMatchRevision);
