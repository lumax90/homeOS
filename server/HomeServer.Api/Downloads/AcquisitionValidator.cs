using System.Text.Json;
using HomeServer.Core;
using HomeServer.Infrastructure;
using HomeServer.Metadata;
using HomeServer.Sources;

namespace HomeServer.Downloads;

public static class AcquisitionValidator
{
    public static async Task<TorrentFileSnapshot[]> ValidateAsync(DownloadRecord download, ITorrentSession session,
        MediaStore store, IMediaProbe? probe, MetadataProvider? metadata, CancellationToken token)
    {
        var work = store.GetAcquisition(download.Id);
        if (work is null) return session.Files.Where(file => MediaPaths.IsMedia(file.RelativePath)).ToArray();
        if (probe is null || !probe.FfprobeAvailable) throw new DomainException("Seçilen sürümün medya doğrulaması için ffprobe gerekli.");
        var target = store.GetContent(work.ContentId) ?? throw new DomainException("İstenen içerik bulunamadı.");
        var rules = work.ProfileJson is null ? null : JsonSerializer.Deserialize<QualityRules>(work.ProfileJson, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var files = session.Files.Where(file => MediaPaths.IsVideo(file.RelativePath) && !IsAuxiliary(file.RelativePath)).ToArray();
        if (files.Length == 0) throw new DomainException("İstenen içerik için video bulunamadı. Payload staging alanında tutuldu.");
        if (files.Length > 500) throw new DomainException("Paket 500'den fazla video içeriyor; daha dar bir sezon kapsamı seç.");
        if (target.Metadata.Kind == "movie" && files.Length == 1 && FilenameHints.Parse(files[0].RelativePath).PartNumber is not null) throw new DomainException("Film paketi yalnız bir CD/part içeriyor; eksik parçalı sürüm yayımlanmadı.");
        if (target.Metadata.Kind == "movie" && files.Length > 1)
        {
            var parts = files.Select(file => FilenameHints.Parse(file.RelativePath).PartNumber).Order().ToArray();
            if (files.Length > 20 || !parts.SequenceEqual(Enumerable.Range(1, files.Length).Select(value => (int?)value)))
                throw new DomainException("Film parçaları 1'den başlayan eksiksiz ve tekil CD/part numaraları taşımalı.");
        }
        var size = files.Sum(f => f.Length);
        if (rules is not null && (size > rules.MaxSizeGiB * 1024 * 1024 * 1024 || size < rules.MinSizeGiB * 1024 * 1024 * 1024))
            throw new DomainException("Paketin gerçek boyutu seçilen profile uymuyor; staging alanında tutuldu.");
        var verified = new List<VerifiedAcquisitionFile>();
        var seenEpisodes = new HashSet<(int, int)>();
        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();
            var relative = DownloadFiles.ValidateRelative(file.RelativePath);
            var source = MediaPaths.ResolveFile(session.Directory, relative);
            if (!string.Equals(Path.GetFullPath(source), Path.GetFullPath(file.FullPath), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
                || new FileInfo(source).Length != file.Length) throw new DomainException("İndirilen dosyanın yolu veya boyutu değişti.");
            var filename = Path.GetFileNameWithoutExtension(relative);
            var raw = new RawRelease("", filename, "torrent", file.Length, null, null, "", null, []);
            ReleaseEpisodeIdentity? identity = null;
            if (work.Numbering is { Mode: "daily" or "absolute" or "alternative" } numbering && work.Season is { } expectedSeason && work.Episode is { } expectedEpisode)
                identity = new ReleaseEpisodeIdentity(numbering.Mode, expectedSeason, expectedEpisode, store.GetEpisodeMetadata(target.Id, expectedSeason, expectedEpisode)?.AirDate,
                    numbering.Entries.FirstOrDefault(entry => entry.Season == expectedSeason && entry.Episode == expectedEpisode)?.Number,
                    numbering.Entries.FirstOrDefault(entry => entry.Season == expectedSeason && entry.Episode == expectedEpisode)?.SourceSeason,
                    numbering.Entries.FirstOrDefault(entry => entry.Season == expectedSeason && entry.Episode == expectedEpisode)?.SourceEpisode);
            var identityErrors = ReleaseEvaluator.IdentityRejections(raw, target.Metadata, work.Season, work.Episode, identity);
            if (identityErrors.Length > 0) throw new DomainException("Dosya kimliği doğrulanamadı: " + string.Join(" ", identityErrors));
            var parsed = ReleaseEvaluator.Parse(raw);
            var episodes = new List<ProviderEpisode>();
            if (target.Metadata.Kind == "show")
            {
                foreach (var number in store.ResolveAcquisitionNumbers(work, parsed))
                {
                    if (!seenEpisodes.Add((number.Season, number.Episode))) throw new DomainException("Aynı bölüm için birden fazla dosya bulundu; otomatik sürüm seçimi yapılmadı.");
                    var episode = store.GetEpisodeMetadata(target.Id, number.Season, number.Episode);
                    if (episode is null && metadata is not null) episode = await metadata.EpisodeAsync(target.Metadata.Provider, target.Metadata.ProviderId, number.Season, number.Episode, token);
                    if (episode is null) throw new DomainException("Bölüm sağlayıcıda henüz doğrulanamadı. İçerik bilgileri yenilendikten sonra tekrar dene.");
                    episodes.Add(episode);
                }
            }
            var info = await probe.ProbeAsync(source, token);
            if (info?.VideoCodec is null || info.Duration is null or <= 0)
                throw new DomainException("Video okunamadı. Dosya staging alanında inceleme için tutuldu.");
            var resolution = ReleaseEvaluator.ResolutionFor(info.Width, info.Height);
            if (rules is not null && (resolution is null ? !rules.AllowUnknownResolution : !rules.Resolutions.Contains(resolution.Value)))
                throw new DomainException("Videonun gerçek çözünürlüğü seçilen profile uymuyor; kütüphaneye yayımlanmadı.");
            if (rules?.Languages is { Length: > 0 } && !rules.Languages.Any(language => (info.AudioLanguages ?? []).Contains(ReleaseEvaluator.Language(language))))
                throw new DomainException("Dosyada istenen ses dili doğrulanamadı; kütüphaneye yayımlanmadı.");
            if (rules is not null)
            {
                var evidence = store.GetAcquisitionRelease(download.Id) ?? raw;
                var customErrors = ReleaseEvaluator.ActualPreferenceRejections(evidence, rules, info);
                if (customErrors.Length > 0) throw new DomainException("Dosyanın gerçek codec/HDR profili eşleşmedi: " + string.Join(" ", customErrors));
            }
            verified.Add(new VerifiedAcquisitionFile(relative, file.Length, episodes.FirstOrDefault(), episodes.ToArray(), target.Metadata.Kind == "movie" && files.Length > 1 ? parsed.PartNumber : null));
        }
        store.SaveAcquisitionValidation(download.Id, download.Revision, verified);
        return files;
    }

    private static bool IsAuxiliary(string path)
    {
        var parts = path.Replace('\\', '/').Split('/');
        if (parts[..^1].Any(part => part.ToLowerInvariant() is "sample" or "samples" or "extras" or "featurettes" or "trailers")) return true;
        var name = Path.GetFileNameWithoutExtension(parts[^1]).ToLowerInvariant();
        return name == "sample" || name.EndsWith(".sample", StringComparison.Ordinal) || name.EndsWith("-sample", StringComparison.Ordinal);
    }
}
