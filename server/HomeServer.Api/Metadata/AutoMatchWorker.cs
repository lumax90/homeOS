using HomeServer.Core;
using HomeServer.Infrastructure;

namespace HomeServer.Metadata;

public sealed class AutoMatchWorker(MediaStore store, MetadataProvider provider, MetadataSettings settings, EventHub events, ILogger<AutoMatchWorker> logger) : BackgroundService
{
    public override Task StartAsync(CancellationToken cancellationToken) { store.RecoverAutoMatches(); return base.StartAsync(cancellationToken); }
    protected override async Task ExecuteAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var job = store.ClaimAutoMatch();
            if (job is null) { try { await Task.Delay(3000, token); } catch (OperationCanceledException) { break; } continue; }
            try
            {
                var media = store.GetMedia(job.MediaId);
                if (media is null || media.Content is not null) { store.FinishAutoMatch(job, "cancelled", "Mevcut eşleştirme korundu veya dosya artık etkin değil."); continue; }
                var local = store.GetLocalMetadata(media.Id); var hint = FilenameHints.Parse(media.RelativePath);
                if (hint.Ambiguous) { store.FinishAutoMatch(job, "review", "Dosya numaralandırması belirsiz; bölüm kapsamını açıkça seç."); continue; }
                var library = store.GetLibrary(media.LibraryId)!;
                var kind = local?.TmdbId is not null ? local.Kind : library.Kind == "shows" || hint.SeasonNumber is not null ? "show" : "movie";
                var source = local?.TmdbId is not null || settings.Status.TmdbConfigured || kind == "movie" ? "tmdb" : "tvmaze";
                var season = local?.Season ?? hint.SeasonNumber; var episode = local?.Episode ?? hint.EpisodeNumber;
                ProviderContent content;
                if (local?.TmdbId is { } id) content = await provider.DetailsAsync("tmdb", kind, id, token);
                else
                {
                    var query = kind == "show" ? local?.ShowTitle ?? hint.Query : local?.Title ?? hint.Query;
                    var year = local?.Year ?? hint.Year;
                    if (kind == "movie" && year is null) { store.FinishAutoMatch(job, "review", "Film yılı veya doğrulanmış NFO kimliği gerekli."); continue; }
                    var found = await provider.SearchAsync(source, kind, query, token);
                    var matches = found.Where(candidate => (EqualTitle(query, candidate.Title) || EqualTitle(query, candidate.OriginalTitle))
                        && (kind != "movie" || candidate.ReleaseDate?.StartsWith(year!.Value.ToString(), StringComparison.Ordinal) == true)).ToArray();
                    if (matches.Length != 1) { store.FinishAutoMatch(job, "review", matches.Length == 0 ? "Kesin başlık/yıl eşleşmesi bulunamadı." : "Birden fazla yapım eşleşiyor; manuel seçim gerekli."); continue; }
                    content = await provider.DetailsAsync(source, kind, matches[0].ProviderId, token);
                }
                ProviderEpisode[] episodeSet = [];
                if (kind == "show")
                {
                    if (hint.AirDate is { } date && local?.Season is null) episodeSet = await provider.DailyEpisodesAsync(source, content.ProviderId, date, token);
                    else if (hint.AbsoluteNumber is { } absolute && local?.Season is null)
                    {
                        var saved = store.FindContent(source, "show", content.ProviderId);
                        if (saved is null) { store.FinishAutoMatch(job, "review", "Mutlak numara için diziyi takip listesine ekleyip bölüm sırasını seç."); continue; }
                        episodeSet = store.LocateEpisodes(saved.Id, new EpisodeLocator("absolute", AbsoluteNumber: absolute, EndEpisode: hint.EndAbsoluteNumber)).Episodes; content = saved.Metadata;
                    }
                    else
                    {
                        if (season is null || episode is null) { store.FinishAutoMatch(job, "review", "Bölüm dosyası için açık sezon/bölüm numarası gerekli."); continue; }
                        var numbers = local?.Episode is not null ? [episode.Value] : hint.EpisodeNumbers is { Length: > 0 } ? hint.EpisodeNumbers : [episode.Value];
                        var saved = store.FindContent(source, "show", content.ProviderId);
                        if (saved is not null && store.GetEpisodeNumbering(saved.Id).Mode == "alternative")
                        { episodeSet = store.LocateEpisodes(saved.Id, new("alternative", season, episode, Numbers: numbers)).Episodes; content = saved.Metadata; }
                        else { var values = new List<ProviderEpisode>(); foreach (var number in numbers) values.Add(await provider.EpisodeAsync(source, content.ProviderId, season.Value, number, token)); episodeSet = values.ToArray(); }
                    }
                }
                if (!store.ApplyAutoMatch(job, content, episodeSet.FirstOrDefault(), episodeSet)) store.FinishAutoMatch(job, "cancelled", "Dosya, eşleştirme veya kütüphane niyeti değişti.");
                events.Publish("catalog.changed");
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { store.FinishAutoMatch(job, "queued", "Yeniden açılışta sürdürülecek."); break; }
            catch (DomainException ex) { store.FinishAutoMatch(job, ex.StatusCode is 409 or 429 or 502 or 504 ? "queued" : "review", ex.Message, DateTimeOffset.UtcNow.AddHours(1)); }
            catch (Exception ex) { logger.LogWarning("Otomatik eşleştirme: {Type}", ex.GetType().Name); store.FinishAutoMatch(job, "review", "Otomatik eşleştirme tamamlanamadı."); }
        }
    }
    private static bool EqualTitle(string left, string right) => Normalize(left) == Normalize(right);
    private static string Normalize(string value) => new(MediaPaths.SearchKey(value).Where(char.IsLetterOrDigit).ToArray());
}
