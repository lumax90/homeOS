namespace HomeServer.Core;

public sealed record AudioTags(string Title, string Artist, string AlbumArtist, string Album, string? Date, int Disc, int Track,
    string? ArtistMbid, string? ReleaseGroupMbid, string? ReleaseMbid, string? RecordingMbid, string? Lyrics, double? ReplayGainDb, int? BitDepth, bool HasArtwork = false,
    double? AlbumGainDb = null, double? TrackPeak = null, double? AlbumPeak = null, string? AlbumArtistMbid = null,
    MusicArtistCredit[]? ArtistCredits = null, MusicArtistCredit[]? AlbumArtistCredits = null);
public sealed record MusicArtist(string Id, string Name, string? SortName, string? Country, string? Disambiguation, string Source, string? Mbid);
public sealed record MusicAlbum(string Id, string Artist, string Title, string? Date, string? ReleaseGroupMbid, string? ReleaseMbid, int Tracks, int AvailableTracks, string Source, string? ArtworkMediaId = null, MusicArtistCredit[]? ArtistCredits = null);
public sealed record MusicTrack(string MediaId, string AlbumId, string Title, string Artist, int Disc, int Number, string? RecordingMbid, string? Lyrics, double? ReplayGainDb, int? BitDepth, MediaItem Media, MusicArtistCredit[]? ArtistCredits = null);
public sealed record MusicBrainzAlbum(string Id, string Title, string Artist, string? Date, string Type, string ProviderUrl, string[]? SecondaryTypes = null, MusicArtistCredit[]? ArtistCredits = null);
public sealed record MusicPage<T>(IReadOnlyList<T> Items, int Total, int Offset, int Limit);
public sealed record MusicLibraryTrack(string MediaId, string AlbumId, string Title, string Artist, string Album, int Disc, int Number, bool Favorite, MediaItem Media);
public sealed record MusicPlaylist(string Id, string Name, int Count, string UpdatedAt);
public sealed record PlaylistInput(string Name, string[] MediaIds);
public sealed record QueueInput(string[] MediaIds, int Index = 0, bool Shuffle = false, string Repeat = "off", string? ClientId = null, long Sequence = 0, string? ExpectedUserId = null);
public sealed record SavedQueue(IReadOnlyList<MediaItem> Items, int Index, bool Shuffle, string Repeat);
public sealed record MusicReleaseSummary(string Id, string GroupId, string Title, string Artist, string? Date, string? Country, string? Status, string[] Formats, int TrackCount, MusicArtistCredit[]? ArtistCredits = null);
public sealed record MusicReleaseTrack(string Id, string RecordingId, int Disc, int Position, string Number, string Title, string Artist, int? LengthMs, MusicArtistCredit[]? ArtistCredits = null);
public sealed record MusicRelease(MusicReleaseSummary Metadata, MusicReleaseTrack[] Tracks);
public sealed record MusicTrackAssignment(string MediaId, int Disc, int Position);
public sealed record MusicAlbumMatchInput(string ReleaseId, MusicTrackAssignment[]? Assignments = null);
public sealed record MusicTrackDecision(string MediaId, string FileTitle, int Disc, int Position, string? RecordingId, string? ReleaseTrackId, string[] Reasons);
public sealed record MusicMatchPreview(string AlbumId, MusicRelease Release, MusicTrackDecision[] Files, int UnavailableTracks, bool CanApply);
public sealed record MusicAlbumFollow(string GroupId, MusicBrainzAlbum Metadata, string CreatedAt);
