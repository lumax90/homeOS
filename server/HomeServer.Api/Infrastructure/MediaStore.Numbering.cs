using System.Globalization;
using System.Text.Json;
using HomeServer.Core;

namespace HomeServer.Infrastructure;

public sealed partial class MediaStore
{
    public EpisodeNumbering GetEpisodeNumbering(string id)
    {
        var content = GetContent(id) ?? throw new DomainException("Dizi bulunamadı.", 404); id = content.Id;
        if (content.Metadata.Kind != "show") throw new DomainException("Numaralandırma bir dizi gerektirir.");
        using var db = Open(); var row = Query(db, "SELECT mode,source,revision,order_id FROM episode_numbering WHERE content_id=$id", r => (Mode: r.GetString(0), Source: r.GetString(1), Revision: r.GetInt32(2), Order: Text(r, 3)), ("$id", id)).FirstOrDefault();
        if (row.Order is { } orderId)
        {
            var order = GetCatalogEpisodeOrder(orderId)!; var catalog = GetEpisodeCatalog(id, true).ToDictionary(episode => episode.Id); var valid = true; var mapped = new List<NumberingEntry>(); var number = 0;
            foreach (var item in order.Items)
            {
                number++;
                foreach (var episodeId in item.EpisodeIds)
                {
                    if (!catalog.TryGetValue(episodeId, out var episode)) { valid = false; continue; }
                    valid &= !episode.Retired; mapped.Add(new(number, episode.Metadata.SeasonNumber, episode.Metadata.EpisodeNumber, episode.Metadata.ProviderId, item.Season, item.Episode, episode.Id));
                }
            }
            return new("alternative", order.Name + " · " + order.Source, row.Revision, mapped.ToArray(), order.Id, valid);
        }
        var entries = Query(db, "SELECT n.number,e.season_number,e.episode_number,e.provider_id FROM episode_order_entries n JOIN episodes e ON e.id=n.episode_id WHERE n.content_id=$id ORDER BY n.number", r => new NumberingEntry(r.GetInt32(0), r.GetInt32(1), r.GetInt32(2), r.GetInt32(3)), ("$id", id)).ToArray();
        var legacyValid = Convert.ToInt32(Scalar(db, "SELECT COUNT(*) FROM episode_order_entries n JOIN episodes e ON e.id=n.episode_id WHERE n.content_id=$id AND e.retired=1", ("$id", id))) == 0;
        return new(row.Mode ?? "seasonal", row.Source ?? content.Metadata.Provider, row.Revision, entries, Valid: legacyValid);
    }
    public EpisodeNumbering SaveEpisodeNumbering(string id, NumberingInput input, bool providerVerified = false)
    {
        if (input.Mode is not ("seasonal" or "daily" or "absolute") || input.Entries?.Length > 20000 || input.Mode == "absolute" && input.Entries is not { Length: > 0 }
            || input.Entries?.Any(entry => entry.Number is < 1 or > 20000 || entry.Season is < 0 or > 1000 || entry.Episode is < 1 or > 10000) == true
            || input.Entries?.Select(entry => entry.Number).Distinct().Count() != input.Entries?.Length
            || input.Entries?.Select(entry => (entry.Season, entry.Episode)).Distinct().Count() != input.Entries?.Length) throw new DomainException("Mutlak bölüm listesi eksik, tekrarlı veya geçersiz.");
        lock (_writeGate)
        {
            var content = GetContent(id) ?? throw new DomainException("Dizi bulunamadı.", 404); id = content.Id;
            var previous = GetEpisodeNumbering(id);
            if (previous.Revision != input.ExpectedRevision) throw new DomainException("Numaralandırma başka bir işlemde değişti.", 409);
            var episodes = IdentityEpisodes(id); var entries = input.Entries ?? [];
            var resolved = entries.Select(entry => (Entry: entry, Target: episodes.FirstOrDefault(episode => episode.Metadata.SeasonNumber == entry.Season && episode.Metadata.EpisodeNumber == entry.Episode))).ToArray();
            if (resolved.Any(value => value.Target.Id is null || value.Entry.ProviderId is { } external && value.Target.Metadata.ProviderId != external)) throw new DomainException("Listedeki bölümün kanonik kimliği bulunamadı; bölüm kataloğunu yenile.", 409);
            var source = input.Mode != "absolute" ? content.Metadata.Provider : providerVerified ? input.Source : "manual";
            if (string.IsNullOrWhiteSpace(source) || source.Length > 120) throw new DomainException("Numaralandırma kaynağı geçersiz.");
            using var db = Open(); using var tx = db.BeginTransaction();
            using var save = Command(db, """
                INSERT INTO episode_numbering(content_id,mode,source) VALUES($id,$mode,$source)
                ON CONFLICT(content_id) DO UPDATE SET mode=$mode,source=$source,order_id=NULL,revision=revision+1;
                DELETE FROM episode_order_entries WHERE content_id=$id;
                UPDATE content SET catalog_revision=catalog_revision+1 WHERE id=$id;
                UPDATE automation_rules SET revision=revision+1,next_run=$now WHERE content_id=$id;
                UPDATE automation_jobs SET status='cancelled',message='Bölüm numaralandırması değişti.' WHERE content_id=$id AND status IN ('queued','waiting','running');
                UPDATE release_results SET catalog_invalidated=1 WHERE content_id=$id AND download_id IS NULL;
                """, ("$id", id), ("$mode", input.Mode), ("$source", source), ("$now", Now())); save.Transaction = tx; save.ExecuteNonQuery();
            foreach (var row in resolved)
            {
                using var entry = Command(db, "INSERT INTO episode_order_entries(content_id,number,episode_id) VALUES($id,$number,$episode)", ("$id", id), ("$number", row.Entry.Number), ("$episode", row.Target.Id)); entry.Transaction = tx; entry.ExecuteNonQuery();
            }
            tx.Commit(); return GetEpisodeNumbering(id);
        }
    }
    public LocatedEpisodes LocateEpisodes(string id, EpisodeLocator locator)
    {
        var content = GetContent(id) ?? throw new DomainException("Dizi bulunamadı.", 404); var numbering = GetEpisodeNumbering(content.Id);
        var all = IdentityEpisodes(content.Id); ProviderEpisode[] selected;
        if (!numbering.Valid) throw new DomainException("Etkin bölüm sırası arşivlenmiş kimlik içeriyor; sırayı düzenle.", 409);
        if (locator.Mode == "seasonal")
        {
            if (locator.Season is null or < 0 or > 1000 || locator.Episode is null or < 1 or > 10000 || locator.EndEpisode < locator.Episode || locator.EndEpisode - locator.Episode >= 20) throw new DomainException("Sezon/bölüm aralığı geçersiz.");
            var end = locator.EndEpisode ?? locator.Episode.Value;
            selected = all.Where(ep => ep.Metadata.SeasonNumber == locator.Season && ep.Metadata.EpisodeNumber >= locator.Episode && ep.Metadata.EpisodeNumber <= end).Select(ep => ep.Metadata).ToArray();
            if (selected.Length != end - locator.Episode + 1) throw new DomainException("Aralıktaki tüm bölümler sağlayıcıda doğrulanmadı.", 409);
        }
        else if (locator.Mode == "daily")
        {
            if (!DateOnly.TryParseExact(locator.AirDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) throw new DomainException("Yayın tarihi YYYY-AA-GG biçiminde olmalı.");
            selected = all.Where(ep => ep.Metadata.AirDate == locator.AirDate).Select(ep => ep.Metadata).ToArray();
            if (selected.Length != 1) throw new DomainException(selected.Length == 0 ? "Bu tarihte doğrulanmış bölüm yok; katalog bilgilerini yenile." : "Bu tarihte birden fazla bölüm var; sezon/bölüm karşılığını açıkça seç.", 409);
        }
        else if (locator.Mode == "absolute")
        {
            if (numbering.Mode != "absolute" || locator.AbsoluteNumber is null or < 1 or > 20000) throw new DomainException("Önce açık bir mutlak bölüm listesi kaydet.", 409);
            var end = locator.EndEpisode ?? locator.AbsoluteNumber.Value;
            if (end < locator.AbsoluteNumber || end - locator.AbsoluteNumber >= 20) throw new DomainException("Mutlak bölüm aralığı 1–20 bölüm olmalı.");
            var entries = numbering.Entries.Where(entry => entry.Number >= locator.AbsoluteNumber && entry.Number <= end).ToArray();
            if (entries.Length != end - locator.AbsoluteNumber + 1) throw new DomainException("Mutlak bölüm listesinde aralığın tamamı yok.", 409);
            selected = entries.Select(entry => all.Single(episode => episode.Metadata.SeasonNumber == entry.Season && episode.Metadata.EpisodeNumber == entry.Episode).Metadata).ToArray();
        }
        else if (locator.Mode == "alternative")
        {
            if (numbering.Mode != "alternative" || locator.Season is null or < 0 or > 1000 || locator.Episode is null or < 1 or > 10000) throw new DomainException("Etkin alternatif sırada sezon/bölüm seç.", 409);
            var end = locator.EndEpisode ?? locator.Episode.Value;
            if (end < locator.Episode || end - locator.Episode >= 20) throw new DomainException("Bölüm aralığı en fazla 20 kaynak bölümü olmalı.");
            var numbers = locator.Numbers ?? Enumerable.Range(locator.Episode.Value, end - locator.Episode.Value + 1).ToArray();
            if (numbers.Length is < 1 or > 20 || numbers.Distinct().Count() != numbers.Length) throw new DomainException("Bölüm aralığı geçersiz.");
            var entries = numbering.Entries.Where(entry => entry.SourceSeason == locator.Season && numbers.Contains(entry.SourceEpisode ?? 0)).OrderBy(entry => entry.Number).ToArray();
            if (entries.Select(entry => entry.SourceEpisode).Distinct().Count() != numbers.Length || entries.Length > 20) throw new DomainException("Alternatif sıra aralığın tamamını karşılamıyor veya dosyada 20 bölüm sınırını aşıyor.", 409);
            selected = entries.Select(entry => all.Single(episode => episode.Id == entry.EpisodeId).Metadata).ToArray();
        }
        else throw new DomainException("Numaralandırma modu geçersiz.");
        return new(content.Id, locator.Mode is "absolute" or "alternative" ? numbering.Source : content.Metadata.Provider, selected, content.Metadata.Provider, content.Metadata.ProviderId);
    }
    public ReleaseEpisodeIdentity? ReleaseIdentity(string id, int? season, int? episode)
    {
        var content = GetContent(id)!; if (content.Metadata.Kind != "show") return null;
        var numbering = GetEpisodeNumbering(content.Id);
        if (!numbering.Valid) throw new DomainException("Etkin bölüm sırası onarım bekliyor.", 409);
        if (numbering.Mode == "seasonal") return null;
        if (season is null || episode is null) throw new DomainException("Günlük/mutlak numaralandırmada kaynak araması için bir bölüm seç.", 409);
        var metadata = GetEpisodeMetadata(content.Id, season.Value, episode.Value) ?? throw new DomainException("Kanonik bölüm bulunamadı.", 409);
        var absolute = numbering.Entries.FirstOrDefault(entry => entry.Season == season && entry.Episode == episode)?.Number;
        if (numbering.Mode == "absolute" && absolute is null || numbering.Mode == "daily" && string.IsNullOrEmpty(metadata.AirDate)) throw new DomainException("Bölümün seçilen numaralandırmada karşılığı yok.", 409);
        var alternative = numbering.Entries.FirstOrDefault(entry => entry.Season == season && entry.Episode == episode);
        if (numbering.Mode == "alternative" && alternative is null) throw new DomainException("Bölümün seçili alternatif sırada karşılığı yok.", 409);
        return new(numbering.Mode, season.Value, episode.Value, metadata.AirDate, absolute, alternative?.SourceSeason, alternative?.SourceEpisode);
    }
    private void ValidateCatalogAcquisition(AcquisitionContext context)
    {
        RequireCurrentRelease(context.ReleaseId);
        using var db = Open(); var row = Query(db, "SELECT content_id,catalog_invalidated FROM release_results WHERE id=$id", r => (Id: r.GetString(0), Invalid: r.GetBoolean(1)), ("$id", context.ReleaseId)).FirstOrDefault();
        if (row.Invalid || row.Id is not null && row.Id != context.ContentId || CanonicalContentId(context.ContentId) != context.ContentId) throw new DomainException("Katalog kimliği/numaralandırması değişti; sürümü yeniden ara.", 409);
    }
    public void RequireCurrentRelease(string id)
    {
        using var db = Open();
        if (Scalar(db, "SELECT r.id FROM release_results r JOIN content c ON c.id=r.content_id WHERE r.id=$id AND (r.catalog_invalidated=1 OR r.catalog_revision<>c.catalog_revision OR c.merged_into IS NOT NULL)", ("$id", id)) is string)
            throw new DomainException("Katalog kimliği/numaralandırması arama sırasında değişti; sürümü yeniden ara.", 409);
    }
    private EpisodeCoordinate[] AcquisitionScope(AcquisitionContext context, EpisodeNumbering? numbering)
    {
        if (numbering is null) return [];
        var parsed = GetRelease(context.ReleaseId).Result.Evaluation.Parsed;
        if (numbering.Mode == "alternative") return ResolveAlternativeAcquisition(numbering, parsed);
        if (numbering.Mode == "absolute" && parsed.AbsoluteNumber is { } first)
            return numbering.Entries.Where(entry => entry.Number >= first && entry.Number <= (parsed.EndAbsoluteNumber ?? first)).Select(entry => new EpisodeCoordinate(entry.Season, entry.Episode)).Take(20).ToArray();
        if (numbering.Mode == "daily" && parsed.AirDate is { } date)
            return IdentityEpisodes(context.ContentId).Where(episode => episode.Metadata.AirDate == date).Select(episode => new EpisodeCoordinate(episode.Metadata.SeasonNumber, episode.Metadata.EpisodeNumber)).Take(20).ToArray();
        return parsed.Season is { } season ? (parsed.EpisodeNumbers ?? []).Select(episode => new EpisodeCoordinate(season, episode)).ToArray() : [];
    }
    public EpisodeCoordinate[] ResolveAcquisitionNumbers(AcquisitionWork work, ParsedRelease parsed)
    {
        if (parsed.Ambiguous) throw new DomainException("Dosya numaralandırması belirsiz.", 409);
        if (work.Numbering?.Mode == "alternative") return ResolveAlternativeAcquisition(work.Numbering, parsed);
        if (work.Numbering?.Mode == "absolute")
        {
            if (parsed.AbsoluteNumber is not { } first) throw new DomainException("Dosyada mutlak bölüm numarası yok.", 409);
            var last = parsed.EndAbsoluteNumber ?? first;
            var entries = work.Numbering.Entries.Where(entry => entry.Number >= first && entry.Number <= last).ToArray();
            if (last < first || last - first >= 20 || entries.Length != last - first + 1) throw new DomainException("Dosyanın mutlak aralığı edinme anındaki numara listesinde yok.", 409);
            return entries.Select(entry => new EpisodeCoordinate(entry.Season, entry.Episode)).ToArray();
        }
        if (work.Numbering?.Mode == "daily")
        {
            if (parsed.AirDate is null) throw new DomainException("Dosyada yayın tarihi yok.", 409);
            return LocateEpisodes(work.ContentId, new EpisodeLocator("daily", AirDate: parsed.AirDate)).Episodes.Select(episode => new EpisodeCoordinate(episode.SeasonNumber, episode.EpisodeNumber)).ToArray();
        }
        if (parsed.Season is not { } season || parsed.EpisodeNumbers is not { Length: > 0 and <= 20 }) throw new DomainException("Dosya bölüm aralığı doğrulanamadı.", 409);
        return parsed.EpisodeNumbers.Select(number => new EpisodeCoordinate(season, number)).ToArray();
    }
    private static EpisodeCoordinate[] ResolveAlternativeAcquisition(EpisodeNumbering numbering, ParsedRelease parsed)
    {
        if (parsed.Season is not { } season || parsed.EpisodeNumbers is not { Length: > 0 and <= 20 } numbers) throw new DomainException("Alternatif sıralı dosyada açık sezon/bölüm listesi gerekli.", 409);
        var entries = numbering.Entries.Where(entry => entry.SourceSeason == season && numbers.Contains(entry.SourceEpisode ?? 0)).OrderBy(entry => entry.Number).ToArray();
        if (entries.Select(entry => entry.SourceEpisode).Distinct().Count() != numbers.Length || entries.Length > 20) throw new DomainException("Dosya, edinme anındaki alternatif sıra kapsamına uymuyor.", 409);
        return entries.Select(entry => new EpisodeCoordinate(entry.Season, entry.Episode)).ToArray();
    }
}
