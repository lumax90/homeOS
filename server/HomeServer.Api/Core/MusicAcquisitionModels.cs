namespace HomeServer.Core;

public sealed record MusicQualityRules(bool LosslessOnly = false, string[]? Codecs = null, int MinimumBitrateKbps = 0, double MaximumSizeGiB = 5, int MinimumSeeders = 1, bool CompleteAlbum = true,
    int MinimumBitDepth = 0, int MinimumSampleRate = 0);
public sealed record MusicSearchInput(string ReleaseId, MusicQualityRules Rules, int Page = 0);
public sealed record MusicReleaseEvaluation(bool Eligible, int Score, string[] Rejections, string? Codec, bool Lossless, MusicAudioQuality? Quality = null);
public sealed record MusicSourceResult(string Id, string IndexerId, string IndexerName, string Title, string Protocol, long? SizeBytes, int? Seeders, MusicReleaseEvaluation Evaluation, string? DownloadId, string Fingerprint = "");
public sealed record MusicSearchResult(MusicSourceResult[] Items, SearchSourceOutcome[] Sources);
public sealed record StoredMusicResult(MusicSourceResult Result, MusicRelease Release, MusicQualityRules Rules, string ProtectedUrl, string? InfoHash, string ExpiresAt);
public sealed record MusicDownloadContext(string ResultId, MusicRelease Release, MusicQualityRules Rules, MusicAutomationLease? Automation = null, MusicAudioQuality? UpgradeFrom = null);
public sealed record MusicDownloadWork(string DownloadId, MusicRelease Release, MusicQualityRules Rules, string Status, string Message, MusicAudioQuality? UpgradeFrom = null);
public sealed record VerifiedMusicFile(string RelativePath, long Length, int Disc, int Position);
