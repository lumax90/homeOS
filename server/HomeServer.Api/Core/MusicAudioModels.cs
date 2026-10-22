namespace HomeServer.Core;

public sealed record MusicPlaybackPreferences(string ReplayGain = "track", double PreampDb = 0, bool PreventClipping = true);
public sealed record MusicPcmInfo(string MediaId, string Fingerprint, long Frames, int SampleRate, int Channels,
    double? TrackGainDb, double? AlbumGainDb, double? TrackPeak, double? AlbumPeak, double? LoudnessLufs, string Title, string Artist, string? AlbumId);
public sealed record MusicSonicAnalysis(string MediaId, string Fingerprint, int Version, double? LoudnessLufs, double? TruePeakDb,
    double CentroidHz, double ZeroCrossingRate, double[] Bands, string AnalyzedAt);
public sealed record MusicSuggestion(MediaItem Media, string Artist, double Similarity, string Reason);
public sealed record MusicFavoriteInput(bool Favorite);
public sealed record MusicTask(string Id, string MediaId, string LibraryId, string Kind, string Status, int Revision,
    string Message, string CreatedAt, string UpdatedAt, string? PlanJson, bool HistoryAvailable = false);
public sealed record MusicTagInput(string Fingerprint, bool Rename = true, bool Artwork = false, string? ExpectedMetadataHash = null);
public sealed record MusicTagPreview(string MediaId, string Fingerprint, bool Writable, string CurrentPath, string ProposedPath,
    AudioTags Current, AudioTags Proposed, string? Reason, string? MetadataHash = null);
public sealed record MusicTagPlan(string MediaId, string LibraryId, int LibraryRevision, string OriginalPath, string TargetPath,
    string Fingerprint, AudioTags Tags, bool Artwork, string? ReleaseMbid, string? OriginalHash = null, string? TargetHash = null, string? RestoreJobId = null, string? MetadataHash = null);
