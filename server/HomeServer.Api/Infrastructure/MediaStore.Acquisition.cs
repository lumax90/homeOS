using HomeServer.Core;
using System.Text.Json;

namespace HomeServer.Infrastructure;

public sealed partial class MediaStore
{
    public RawRelease? GetAcquisitionRelease(string downloadId)
    {
        using var db = Open(); return Query(db, "SELECT r.title,r.protocol,r.size_bytes,r.seeders,r.published_at FROM acquisition_links a JOIN release_results r ON r.id=a.release_id WHERE a.download_id=$id",
            r => new RawRelease("", r.GetString(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetInt64(2), r.IsDBNull(3) ? null : r.GetInt32(3), Text(r, 4), "", null, []), ("$id", downloadId)).FirstOrDefault();
    }
    public int? ExistingPreferenceScore(MediaItem media, QualityRules rules)
    {
        RawRelease? source = null;
        var first = media.RelativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
        if (Guid.TryParseExact(first, "N", out _) && GetDownload(first)?.ImportedLibraryId == media.LibraryId) source = GetAcquisitionRelease(first);
        source ??= new RawRelease("", Path.GetFileNameWithoutExtension(media.RelativePath), "unknown", media.SizeBytes, null, null, "", null, []);
        var parsed = Sources.ReleaseEvaluator.Parse(source);
        return parsed.Source == "unknown" ? null : Sources.ReleaseEvaluator.PreferenceScore(source, rules, Sources.ReleaseEvaluator.ResolutionFor(media.Width, media.Height), media.VideoCodec);
    }
    public AcquisitionWork? GetAcquisition(string downloadId)
    {
        using var db = Open();
        return Query(db, """
            SELECT a.download_id,a.content_id,a.season_number,a.episode_number,COALESCE(a.import_profile_json,r.profile_json),d.imported_library_id,d.scan_job_id,a.numbering_json
            FROM acquisition_links a JOIN downloads d ON d.id=a.download_id LEFT JOIN release_results r ON r.id=a.release_id
            WHERE a.download_id=$id
            """, r => new AcquisitionWork(r.GetString(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetInt32(2),
                r.IsDBNull(3) ? null : r.GetInt32(3), Text(r, 4), Text(r, 5), Text(r, 6),r.IsDBNull(7) ? null : JsonSerializer.Deserialize<EpisodeNumbering>(r.GetString(7), WebJson)), ("$id", downloadId)).FirstOrDefault();
    }

    public ProviderEpisode? GetEpisodeMetadata(string contentId, int season, int episode)
    {
        contentId = CanonicalContentId(contentId);
        using var db = Open();
        return Query(db, """
            SELECT provider_id,season_number,episode_number,title,overview,air_date,still_url,runtime_minutes FROM episodes
            WHERE content_id=$id AND season_number=$season AND episode_number=$episode AND retired=0
            """, r => new ProviderEpisode(r.GetInt32(0), r.GetInt32(1), r.GetInt32(2), r.GetString(3), r.GetString(4), Text(r, 5), Text(r, 6), r.IsDBNull(7) ? null : r.GetInt32(7)),
            ("$id", contentId), ("$season", season), ("$episode", episode)).FirstOrDefault();
    }

    public void SaveAcquisitionValidation(string downloadId, int revision, IReadOnlyList<VerifiedAcquisitionFile> files)
    {
        lock (_writeGate)
        {
            var download = GetDownload(downloadId) ?? throw new DomainException("İndirme bulunamadı.", 404);
            if (download.Revision != revision || download.DesiredState != "running") throw new OperationCanceledException();
            var work = GetAcquisition(downloadId) ?? throw new DomainException("Edinme kaydı bulunamadı.", 404);
            using var db = Open();
            using var tx = db.BeginTransaction();
            using var clear = Command(db, "DELETE FROM acquisition_files WHERE download_id=$id", ("$id", downloadId));
            clear.Transaction = tx;
            clear.ExecuteNonQuery();
            foreach (var file in files)
            {
                var episodeIds = (file.Episodes ?? (file.Episode is null ? [] : [file.Episode])).Select(episode => UpsertEpisode(db, tx, work.ContentId, episode)).ToArray();
                var episodeId = episodeIds.FirstOrDefault();
                using var insert = Command(db, """
                    INSERT INTO acquisition_files(download_id,relative_path,size_bytes,episode_id,episodes_json,part_number) VALUES($id,$path,$size,$episode,$episodes,$part)
                    """, ("$id", downloadId), ("$path", file.RelativePath), ("$size", file.Length), ("$episode", episodeId), ("$episodes", JsonSerializer.Serialize(episodeIds, WebJson)), ("$part", file.PartNumber));
                insert.Transaction = tx;
                insert.ExecuteNonQuery();
            }
            tx.Commit();
        }
    }

    public IReadOnlyList<string> PendingAcquisitionLinks()
    {
        using var db = Open();
        return Query(db, """
            SELECT a.download_id FROM acquisition_links a JOIN downloads d ON d.id=a.download_id
            JOIN jobs j ON j.id=d.scan_job_id
            WHERE a.status='waiting' AND j.status IN ('completed','failed','cancelled') AND d.imported_at IS NOT NULL
            ORDER BY a.created_at LIMIT 20
            """, r => r.GetString(0));
    }

    public void LinkAcquisition(string downloadId)
    {
        lock (_writeGate)
        {
            var work = GetAcquisition(downloadId) ?? throw new DomainException("Edinme kaydı bulunamadı.", 404);
            var scan = work.ScanJobId is null ? null : GetJob(work.ScanJobId);
            if (scan?.Status != "completed") throw new DomainException("Yayınlanan klasörün taraması tamamlanamadı. Katalog bağlantısını yeniden dene.");
            using var db = Open();
            var expected = Query(db, "SELECT relative_path,size_bytes,episode_id,episodes_json,part_number FROM acquisition_files WHERE download_id=$id",
                r => (Path: r.GetString(0), Size: r.GetInt64(1), Episode: Text(r, 2), Episodes: r.IsDBNull(3) ? (r.IsDBNull(2) ? [] : new[] { r.GetString(2) }) : JsonSerializer.Deserialize<string[]>(r.GetString(3), WebJson)!, Part: r.IsDBNull(4) ? (int?)null : r.GetInt32(4)), ("$id", downloadId));
            if (expected.Count == 0) throw new DomainException("Payload için doğrulanmış içerik eşleştirmesi yok. Dosyaları kütüphaneden eşleştir.");
            var mappings = new List<(string MediaId, string? EpisodeId, string[] Episodes, int? Part, bool Existing)>();
            foreach (var file in expected)
            {
                var path = MediaPaths.Key(Path.Combine(downloadId, file.Path));
                var media = Query(db, """
                    SELECT m.id,m.size_bytes,m.available,l.enabled,mm.content_id,mm.episode_id
                    FROM media m JOIN libraries l ON l.id=m.library_id LEFT JOIN media_matches mm ON mm.media_id=m.id
                    WHERE m.library_id=$library AND m.path_key=$path
                    """, r => (Id: r.GetString(0), Size: r.GetInt64(1), Available: r.GetBoolean(2) && r.GetBoolean(3), Content: Text(r, 4), Episode: Text(r, 5)),
                    ("$library", work.LibraryId), ("$path", path));
                if (media.Count != 1 || !media[0].Available || media[0].Size != file.Size)
                    throw new DomainException("Doğrulanan dosya katalogda aynı boyutla bulunamadı. Klasörü yeniden tara.");
                if (media[0].Content is not null && (media[0].Content != work.ContentId || media[0].Episode != file.Episode))
                    throw new DomainException("Dosya kullanıcı tarafından farklı bir içeriğe bağlanmış. Mevcut eşleştirmeyi incele.");
                if (media[0].Content is not null && !(GetMedia(media[0].Id)!.Episodes ?? []).Select(segment => segment.EpisodeId).Order().SequenceEqual(file.Episodes.Order())) throw new DomainException("Dosyanın mevcut bölüm kapsamı farklı; kullanıcı eşleştirmesi korundu.", 409);
                if (file.Part is not null && GetMedia(media[0].Id)!.MoviePart is { } part && part.EditionId != downloadId) throw new DomainException("Dosya başka bir film parça grubuna bağlı.", 409);
                mappings.Add((media[0].Id, file.Episode, file.Episodes, file.Part, media[0].Content is not null));
            }
            using var tx = db.BeginTransaction();
            foreach (var (mediaId, episodeId, episodes, part, existing) in mappings)
            {
                using var match = Command(db, """
                    INSERT OR IGNORE INTO media_matches(media_id,content_id,episode_id,edition,matched_at)
                    VALUES($media,$content,$episode,'',$now)
                    """, ("$media", mediaId), ("$content", work.ContentId), ("$episode", episodeId), ("$now", Now()));
                match.Transaction = tx;
                match.ExecuteNonQuery();
                if (!existing && episodes.Length > 1)
                {
                    using var clear = Command(db, "DELETE FROM media_episode_segments WHERE media_id=$id", ("$id", mediaId)); clear.Transaction = tx; clear.ExecuteNonQuery();
                    for (var position = 0; position < episodes.Length; position++)
                    { using var segment = Command(db, "INSERT INTO media_episode_segments(media_id,episode_id,position) VALUES($media,$episode,$position)", ("$media", mediaId), ("$episode", episodes[position]), ("$position", position)); segment.Transaction = tx; segment.ExecuteNonQuery(); }
                }
            }
            if (mappings.Any(mapping => mapping.Part is not null))
            {
                if (!mappings.Select(mapping => mapping.Part).Order().SequenceEqual(Enumerable.Range(1, mappings.Count).Select(number => (int?)number))) throw new DomainException("Doğrulanan film parça sırası eksik.", 409);
                using var edition = Command(db, "INSERT OR IGNORE INTO movie_editions(id,content_id,name,part_count) VALUES($id,$content,'İçe aktarılan parçalı sürüm',$count)", ("$id", downloadId), ("$content", work.ContentId), ("$count", mappings.Count)); edition.Transaction = tx; edition.ExecuteNonQuery();
                foreach (var part in mappings)
                { using var writePart = Command(db, "INSERT OR IGNORE INTO movie_parts(edition_id,media_id,part_number) VALUES($id,$media,$number)", ("$id", downloadId), ("$media", part.MediaId), ("$number", part.Part)); writePart.Transaction = tx; writePart.ExecuteNonQuery(); }
            }
            using var complete = Command(db, """
                UPDATE acquisition_links SET status='linked',message=$message WHERE download_id=$id;
                """, ("$id", downloadId), ("$content", work.ContentId), ("$message", $"{mappings.Count} doğrulanmış dosya içeriğe bağlandı."), ("$now", Now()));
            complete.Transaction = tx;
            complete.ExecuteNonQuery();
            tx.Commit();
            ReconcileRequests(work.ContentId);
        }
    }

    public void FailAcquisitionLink(string downloadId, string message)
    {
        lock (_writeGate)
        {
            using var db = Open();
            Execute(db, "UPDATE acquisition_links SET status='review',message=$message WHERE download_id=$id", ("$id", downloadId), ("$message", message));
        }
    }

    public void RetryAcquisitionLink(string downloadId)
    {
        lock (_writeGate)
        {
            var work = GetAcquisition(downloadId) ?? throw new DomainException("Edinme kaydı bulunamadı.", 404);
            if (work.LibraryId is null) throw new DomainException("Dosyalar henüz kütüphaneye yayımlanmadı.", 409);
            var job = QueueScan(work.LibraryId);
            using var db = Open();
            Execute(db, """
                UPDATE downloads SET scan_job_id=$job WHERE id=$id;
                UPDATE acquisition_links SET status='waiting',message='Katalog taraması ve içerik bağlantısı bekleniyor.' WHERE download_id=$id;
                """, ("$id", downloadId), ("$job", job.Id));
        }
    }

    public DownloadRecord ReviewImportProfile(string id, string profileId)
    {
        var profile = GetQualityProfile(profileId);
        lock (_writeGate)
        {
            var download = GetDownload(id) ?? throw new DomainException("İndirme bulunamadı.", 404);
            if (download.ImportedAt is not null || download.Progress < 100 || download.Status != "failed" || GetAcquisition(id) is null)
                throw new DomainException("Profil incelemesi yalnız tamamlanmış, doğrulamada durdurulan edinmeler için kullanılabilir.", 409);
            using var db = Open();
            using var tx = db.BeginTransaction();
            using var command = Command(db, """
                UPDATE acquisition_links SET import_profile_json=$profile,reviewed_at=$now,message='Kullanıcının seçtiği profille yeniden doğrulanıyor.' WHERE download_id=$id;
                UPDATE downloads SET desired_state='running',status='queued',revision=revision+1,updated_at=$now,
                  message='Seçilen profille dosya doğrulaması yeniden sıraya alındı.' WHERE id=$id;
                """, ("$id", id), ("$profile", JsonSerializer.Serialize(profile.Rules, WebJson)), ("$now", Now()));
            command.Transaction = tx;
            command.ExecuteNonQuery();
            tx.Commit();
            return GetDownload(id)!;
        }
    }
}
