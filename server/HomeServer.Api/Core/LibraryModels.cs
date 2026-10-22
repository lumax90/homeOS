namespace HomeServer.Core;

public sealed record LibraryOptions(string[]? ExcludedPaths = null, int ScanIntervalMinutes = 0, bool WatchChanges = false, bool ReadLocalMetadata = false, bool AutoMatch = false);
public sealed record LibraryEdit(string Name, string Path, bool Enabled, LibraryOptions Options);
public sealed record LibraryConfiguration(string LibraryId, int Revision, LibraryOptions Options, string? LastScan, string? NextScan);
public sealed record LocalMediaMetadata(string? Title, string? Overview, int? Year, string? ShowTitle, int? Season, int? Episode, int? TmdbId, string? ImdbId, string? Poster, string? Backdrop, string[] Tags, string Source, string Kind = "movie");
public sealed record AutoMatchJob(string MediaId, string LibraryId, int LibraryRevision, long Size, string ModifiedAt, string MetadataKey, string Status, string Message, string NextAttempt);
