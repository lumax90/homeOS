using HomeServer.Core;
using HomeServer.Downloads;
using HomeServer.Infrastructure;

namespace HomeServer.Music;

public static class MusicAcquisitionValidator
{
    public static async Task<TorrentFileSnapshot[]> ValidateAsync(DownloadRecord download, ITorrentSession session, MusicDownloadWork work, MediaStore store, IMediaProbe? probe, CancellationToken token)
    {
        if (probe?.FfprobeAvailable != true) throw new DomainException("Albüm doğrulaması için ffprobe gerekli.");
        MusicReleaseEvaluator.Validate(work.Rules);
        var files = session.Files.Where(file => MediaPaths.IsMedia(file.RelativePath) && !MediaPaths.IsVideo(file.RelativePath)).ToArray();
        if (files.Length is 0 or > 2000 || files.Sum(file => file.Length) > work.Rules.MaximumSizeGiB * 1024 * 1024 * 1024) throw new DomainException("Albüm dosya sayısı veya boyutu profile uymuyor.");
        var verified = new List<VerifiedMusicFile>(); var seen = new HashSet<(int, int)>();
        foreach (var file in files)
        {
            var path = MediaPaths.ResolveFile(session.Directory, DownloadFiles.ValidateRelative(file.RelativePath));
            if (MediaPaths.Key(path) != MediaPaths.Key(Path.GetFullPath(file.FullPath)) || new FileInfo(path).Length != file.Length) throw new DomainException("Müzik dosyası yolu/boyutu değişti.");
            var info = await probe.ProbeAsync(path, token); var tags = info?.Music;
            if (info?.AudioCodec is null || info.VideoCodec is not null || info.Duration is null or <= 0 || tags is null) throw new DomainException("Ses dosyası veya albüm etiketleri okunamadı.");
            var codec = MusicReleaseEvaluator.Codec(info.AudioCodec); var lossless = MusicReleaseEvaluator.IsLossless(codec);
            var quality = MusicReleaseEvaluator.Quality(info);
            if (work.Rules.LosslessOnly && !lossless || work.Rules.Codecs is { Length: > 0 } && !work.Rules.Codecs.Contains(codec)
                || quality.BitDepth < work.Rules.MinimumBitDepth || quality.SampleRate < work.Rules.MinimumSampleRate
                || !lossless && work.Rules.MinimumBitrateKbps > 0 && (info.Streams?.FirstOrDefault(stream => stream.Kind == "audio")?.BitRate ?? info.BitRate ?? 0) < work.Rules.MinimumBitrateKbps * 1000L)
                throw new DomainException("Dosyanın gerçek ses codec/bitrate kalitesi profile uymuyor.");
            if (work.UpgradeFrom is { } floor && !MusicReleaseEvaluator.AtLeast(quality, floor)) throw new DomainException("Gerçek ses kalitesi önceki sürümden düşük; yükseltme yayımlanmadı.");
            if (tags.ReleaseMbid is not null && tags.ReleaseMbid != work.Release.Metadata.Id || tags.ReleaseGroupMbid is not null && tags.ReleaseGroupMbid != work.Release.Metadata.GroupId)
                throw new DomainException("Dosyanın MusicBrainz yayın kimliği farklı.");
            var strong = tags.ReleaseMbid == work.Release.Metadata.Id || tags.ReleaseGroupMbid == work.Release.Metadata.GroupId;
            var credits = tags.AlbumArtistCredits is { Length: > 0 } ? tags.AlbumArtistCredits : tags.ArtistCredits;
            var expectedCredits = work.Release.Metadata.ArtistCredits;
            if (credits is { Length: > 0 } && expectedCredits is { Length: > 0 } && credits.All(credit => credit.Mbid is not null) && expectedCredits.All(credit => credit.Mbid is not null)
                && !credits.Select(credit => credit.Mbid).SequenceEqual(expectedCredits.Select(credit => credit.Mbid))) throw new DomainException("Dosyanın albüm sanatçı kimlikleri seçilen yayından farklı.");
            if (!strong && (MusicReleaseEvaluator.Normalize(tags.Album) != MusicReleaseEvaluator.Normalize(work.Release.Metadata.Title)
                || MusicReleaseEvaluator.Normalize(string.IsNullOrEmpty(tags.AlbumArtist) ? tags.Artist : tags.AlbumArtist) != MusicReleaseEvaluator.Normalize(work.Release.Metadata.Artist) && !MusicCredits.Equivalent(credits, expectedCredits)))
                throw new DomainException("Dosyanın albüm/sanatçı etiketleri seçilen yayınla eşleşmiyor.");
            var track = work.Release.Tracks.FirstOrDefault(track => track.Disc == tags.Disc && track.Position == tags.Track);
            if (track is null && tags.RecordingMbid is not null)
            {
                var matching = work.Release.Tracks.Where(track => track.RecordingId == tags.RecordingMbid).ToArray(); if (matching.Length == 1) track = matching[0];
            }
            if (track is null || tags.RecordingMbid is not null && tags.RecordingMbid != track.RecordingId) throw new DomainException("Dosyanın recording/disc/parça kimliği doğrulanamadı.");
            if (!strong && MusicReleaseEvaluator.Normalize(tags.Title) != MusicReleaseEvaluator.Normalize(track.Title)) throw new DomainException("Parça başlığı seçilen yayınla eşleşmiyor.");
            if (track.LengthMs is { } length && Math.Abs(info.Duration.Value - length / 1000d) > Math.Max(10, info.Duration.Value * .08)) throw new DomainException("Parça süresi seçilen yayından belirgin biçimde farklı.");
            if (!seen.Add((track.Disc, track.Position))) throw new DomainException("Aynı disc/parça için birden fazla dosya var; dosya seçimini daralt.");
            verified.Add(new VerifiedMusicFile(file.RelativePath, file.Length, track.Disc, track.Position));
        }
        if (work.Rules.CompleteAlbum && work.Release.Tracks.Any(track => !seen.Contains((track.Disc, track.Position)))) throw new DomainException("Seçilen albüm yayınının bazı parçaları eksik.");
        store.SaveMusicValidation(download.Id, download.Revision, verified); return files;
    }
}
