namespace HomeServer.Core;

public sealed record Owner(string DisplayName, string PasswordHash);
public sealed record Credentials(string DisplayName, string Password);
public sealed record AddLibrary(string Name, string Path, string Kind);
public sealed record PlaybackProgress(double PositionSeconds, double? DurationSeconds, string? ClientId = null, long? Sequence = null, string? ExpectedUserId = null, int? ExpectedMatchRevision = null);
public sealed record Library(
    string Id, string Name, string Path, string Kind, bool IsDemo,
    string CreatedAt, int ItemCount, bool Enabled = true);
public sealed record MediaItem(
    string Id, string LibraryId, string LibraryName, string Title,
    string Kind, string RelativePath, string MimeType, long SizeBytes,
    double? DurationSeconds, int? Width, int? Height, string? VideoCodec,
    string? AudioCodec, bool Available, string AddedAt, string ModifiedAt,
    bool HasThumbnail, double PositionSeconds, bool IsWatched, bool IsDemo,
    string SourceTitle = "", ContentMatch? Content = null, int MatchRevision = 0, MediaEpisodeSegment[]? Episodes = null, MoviePart? MoviePart = null, CatalogGroup? CatalogGroup = null);
public sealed record CatalogGroup(string Key,string Kind,string Title,int FileCount,int EpisodeCount,int SeasonCount,string AddedAt);
public sealed record CatalogPage(IReadOnlyList<MediaItem> Items, int Total, int Offset, int Limit, int? FileTotal = null);
public sealed record SeriesFile(MediaItem File,int? Season,int? Episode,string Title);
public sealed record LibrarySeriesPage(MediaItem Item,IReadOnlyList<SeriesFile> Items,int Total,int Offset,int Limit,int[] Seasons);
public sealed record LibraryStats(int Libraries, int Items, int Videos, int Audio, long SizeBytes, int ActiveJobs);
public sealed record ScanJob(
    string Id, string LibraryId, string LibraryName, string Status,
    int Processed, int Added, int Attempt, string Message,
    string CreatedAt, string? StartedAt, string? FinishedAt, bool CancelRequested);
public sealed record ProbeResult(double? Duration, int? Width, int? Height, string? VideoCodec, string? AudioCodec, string[]? AudioLanguages = null,
    MediaStreamInfo[]? Streams = null, MediaChapter[]? Chapters = null, string? Container = null, long? BitRate = null, double StartTime = 0, AudioTags? Music = null, int AnalysisVersion = 0);
public sealed record SourceFile(string RelativePath, string Title, string Kind, string MimeType, long Size, string ModifiedAt);
public sealed record HomeEvent(string Type, string At);
public sealed record HomePaths(string Data, string Thumbnails, string? Demo, string? StatePath = null, string? NativeRuntimePath = null)
{
    public string State => StatePath ?? Data;
}

public sealed record ContentMatch(string Id, string Kind, string Title, string OriginalTitle,
    string Overview, string? ReleaseDate, string? PosterUrl, string? BackdropUrl,
    string Provider, int ProviderId, string ProviderUrl, string[] Genres,
    int? SeasonNumber, int? EpisodeNumber, string? EpisodeTitle, string? EpisodeOverview,
    string? StillUrl, string Edition, string MatchedAt, int VersionCount);
public sealed record ProviderContent(string Provider, int ProviderId, string Kind, string Title,
    string OriginalTitle, string Overview, string? ReleaseDate, string? PosterUrl,
    string? BackdropUrl, string ProviderUrl, string[] Genres, string? ImdbId = null,
    int? TvdbId = null, double? Rating = null, int? RuntimeMinutes = null, int? MinimumAge = null, string? AgeLabel = null, CatalogAlias[]? Aliases = null);
public sealed record ProviderEpisode(int ProviderId, int SeasonNumber, int EpisodeNumber,
    string Title, string Overview, string? AirDate, string? StillUrl, int? RuntimeMinutes = null);
public sealed record MatchRequest(string Provider, string Kind, int ProviderId,
    int? SeasonNumber = null, int? EpisodeNumber = null, string Edition = "");
public sealed record MetadataPreferences(string? TmdbToken, bool ClearToken = false, string Language = "tr-TR");
public sealed record MetadataStatus(bool TmdbConfigured, string Language, string[] Providers);
public sealed record FilenameHint(string Query, int? Year, int? SeasonNumber, int? EpisodeNumber, int[]? EpisodeNumbers = null, string? AirDate = null, int? AbsoluteNumber = null, int? EndAbsoluteNumber = null, int? PartNumber = null, bool Ambiguous = false);

public sealed record DownloadRecord(string Id, string InfoHash, string Name, string SourceKind,
    string DesiredState, string Status, double Progress, long TotalBytes,
    long DownloadSpeed, long UploadSpeed, int Peers, string Message,
    string? ImportedLibraryId, string? ScanJobId, string? ImportedAt, string CreatedAt, string UpdatedAt, int Revision,
    AcquisitionStatus? Acquisition = null, int Priority = 25, long UploadedBytes = 0, long ReceivedBytes = 0, long SeedSeconds = 0, bool Archived = false, string? PayloadCleanedAt = null, MusicDownloadStatus? MusicAcquisition = null, DownloadOwnership? Ownership = null);
public sealed record MusicDownloadStatus(string ReleaseId, string Title, string Status, string Message);
public sealed record DownloadSource(DownloadRecord Record, string Source, DownloadBudgetRun? Budget = null);
public sealed record DownloadCommand(string Action);
public sealed record AddMagnet(string Magnet);

public class DomainException(string message, int statusCode = 400, Exception? inner = null) : Exception(message, inner)
{
    public int StatusCode { get; } = statusCode;
}
