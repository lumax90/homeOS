namespace HomeServer.Core;

public sealed record MusicArtistCredit(string? Mbid, string Name, string CreditedName, string JoinPhrase = "", string? SortName = null, string? ArtistId = null);
public sealed record LocalMusicArtist(MusicArtist Artist, int Albums, int Tracks);
public sealed record MusicRelation(string Type, string Direction, string TargetType, string Id, string Title, string ProviderUrl, MusicBrainzAlbum? Album = null, bool Video = false);
public sealed record MusicRecording(string Id, string Title, bool Video, MusicArtistCredit[] ArtistCredits, MusicRelation[] Relations);
public sealed record MusicVideoLink(string Id, MediaItem Video, string? RecordingMbid, string? VideoRecordingMbid, int Revision, bool Current = true);
public sealed record MusicVideoInput(string VideoId, string? ExpectedRecordingMbid, string? VideoRecordingMbid = null);
public sealed record MusicListInput(string Source, string Id, string Kind);
public sealed record MusicListCandidate(string Kind, string Mbid, string Name, MusicBrainzAlbum? Album = null, MusicArtist? Artist = null);
public sealed record MusicCollection(string Id, string Name, string EntityType);
public sealed record MusicCollectionPage(MusicListCandidate[] Items, string[] EntityIds, int Total, int Offset, int Limit);
public sealed record MusicListJob(string Id, string Source, string SourceId, string Kind, string Name, int Revision, string Status,
    int Cursor, int? Total, int Skipped, string Message, string CreatedAt, string ExpiresAt, MusicListCandidate[] Items, MusicListReceipt? Receipt = null);
public sealed record MusicListApply(int Revision, string[] Mbids, bool Monitor = false);
public sealed record MusicListReceipt(int Followed, int AlreadyFollowed, int MonitorsCreated, int MonitorsPreserved);
