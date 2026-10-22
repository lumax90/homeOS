namespace HomeServer.Core;

public sealed record CatalogAlias(string Provider, string Kind, int ProviderId, string? Title, string? OriginalTitle);
public sealed record CatalogEpisodeMap(string SourceEpisodeId, string TargetEpisodeId, double? StartSeconds = null, double? EndSeconds = null);
public sealed record CatalogMergeInput(string Provider, string Kind, int ProviderId, int TargetRevision = 0, int? SourceRevision = null, CatalogEpisodeMap[]? Episodes = null);
public sealed record CatalogMergeEpisode(string Id, int Season, int Number, string Title, string? AirDate, string? TargetId, bool Automatic, CatalogEpisodeMap[]? Targets = null);
public sealed record CatalogMergePreview(string TargetId, string? SourceId, int TargetRevision, int? SourceRevision, ProviderContent Target, ProviderContent Source,
    string[] Evidence, string[] Conflicts, CatalogMergeEpisode[] Episodes, int Files, int Requests, bool CanApply);
public sealed record MediaEpisodeSegment(string EpisodeId, int Season, int Number, string Title, int Position, double? StartSeconds, double? EndSeconds);
public sealed record EpisodeSegmentInput(int Season, int Episode, double? StartSeconds = null, double? EndSeconds = null);
public sealed record EpisodeMatchInput(string Provider, int ProviderId, string Edition, EpisodeSegmentInput[] Episodes, int ExpectedRevision = 0);
public sealed record EpisodeLocator(string Mode = "seasonal", int? Season = null, int? Episode = null, int? EndEpisode = null, string? AirDate = null, int? AbsoluteNumber = null, int[]? Numbers = null);
public sealed record LocatedEpisodes(string ContentId, string NumberingSource, ProviderEpisode[] Episodes, string? Provider = null, int? ProviderId = null);
public sealed record NumberingEntry(int Number, int Season, int Episode, int? ProviderId = null, int? SourceSeason = null, int? SourceEpisode = null, string? EpisodeId = null);
public sealed record EpisodeSelectionInput(string Provider, int ProviderId, EpisodeLocator Locator);
public sealed record EpisodeOrderImport(string OrderId, int ExpectedRevision);
public sealed record CatalogNamesInput(string[] Names);
public sealed record NumberingInput(string Mode, string Source, NumberingEntry[]? Entries = null, int ExpectedRevision = 0);
public sealed record EpisodeNumbering(string Mode, string Source, int Revision, NumberingEntry[] Entries, string? OrderId = null, bool Valid = true);
public sealed record ProviderEpisodeOrder(string Id, string Name, string Description, int Type, int EpisodeCount);
public sealed record ImportedEpisodeOrder(string Id, string Name, NumberingEntry[] Entries);
public sealed record MoviePart(string EditionId, string Name, int Number, int Count);
public sealed record MoviePartsInput(string Name, string[] MediaIds, string? EditionId = null, int ExpectedRevision = 0);
public sealed record MovieEdition(string Id, string ContentId, string Name, int Revision, int PartCount, IReadOnlyList<MediaItem> Files, bool Complete);
public sealed record VideoSequence(IReadOnlyList<MediaItem> Items, int Index, string? EditionId = null);
public sealed record ReleaseEpisodeIdentity(string Mode, int Season, int Episode, string? AirDate = null, int? AbsoluteNumber = null, int? SourceSeason = null, int? SourceEpisode = null);
public sealed record EpisodeCoordinate(int Season, int Episode);
