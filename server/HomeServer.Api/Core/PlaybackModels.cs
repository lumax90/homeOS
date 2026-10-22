namespace HomeServer.Core;

public sealed record MediaStreamInfo(int Index, string Kind, string Codec, string Language, string Title,
    bool IsDefault, bool Forced, bool HearingImpaired, int? Width, int? Height, int? Channels,
    string? ChannelLayout, int? SampleRate, long? BitRate, string? Profile, string? PixelFormat, string? ColorTransfer, string? ColorPrimaries, bool DolbyVision = false, string? ColorSpace = null);
public sealed record MediaChapter(int Index, double StartSeconds, double EndSeconds, string Title);
public sealed record SubtitleTrack(string Id, string Language, string Title, string Format, string Origin, bool Forced, bool HearingImpaired, bool Playable,
    bool Burnable = false, bool Image = false, int? Score = null);
public sealed record MediaTracks(string MediaId, string Fingerprint, double? DurationSeconds, string? Container, long? BitRate,
    IReadOnlyList<MediaStreamInfo> Streams, IReadOnlyList<MediaChapter> Chapters, IReadOnlyList<SubtitleTrack> Subtitles);
public sealed record AnalyzedMedia(MediaItem Media, string Path, string Fingerprint, ProbeResult Probe);
public sealed record ManagedSubtitle(string Id, string MediaId, string Language, string Title, string Extension, string CreatedAt, bool Forced = false, bool HearingImpaired = false,
    string Origin = "upload", int? Score = null, long? ProviderFileId = null, bool Retired = false, string? Fingerprint = null, int? MatchRevision = null, string? SupersedesId = null);
public sealed record PlaybackClient(string[] Containers, string[] VideoCodecs, string[] AudioCodecs, bool Hls, int MaxAudioChannels = 2);
public sealed record PlaybackStart(string MediaId, PlaybackClient Client, double StartSeconds = 0, int? AudioStream = null,
    int? MaxHeight = null, int? MaxBitrate = null, string? ReplaceSessionId = null, bool ForceCompatible = false, string? ClientId = null, long ClientRevision = 0,
    string? SubtitleId = null, bool BurnSubtitle = false, double SubtitleDelaySeconds = 0, string? ExpectedUserId = null, string? ExpectedSessionId = null, WatchPlaybackBinding? Watch = null);
public sealed record PlaybackDecision(string Mode, string[] Reasons, int? VideoStream, int? AudioStream,
    int? Height, int Bitrate, bool CopyVideo, bool CopyAudio, bool ToneMap = false, SubtitleBurnPlan? Subtitle = null);
public sealed record PlaybackSession(string? Id, string MediaId, string Mode, string Url, double StartSeconds,
    double DurationSeconds, string[] Reasons, int? AudioStream, string State, string? Error = null, double RequestedStartSeconds = 0, string Encoder = "copy", string? SubtitleId = null, bool SubtitleBurned = false);
public sealed record PlaybackHeartbeat(double PositionSeconds, bool Paused = false, string? MediaId = null);
public sealed record PlaybackClientClose(long? Revision = null);
