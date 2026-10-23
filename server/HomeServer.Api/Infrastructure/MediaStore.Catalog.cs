using System.Globalization;
using System.Text.Json;
using HomeServer.Core;
using Microsoft.Data.Sqlite;

namespace HomeServer.Infrastructure;

public sealed partial class MediaStore
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);
    private const string EpisodeWanted = """
        EXISTS(SELECT 1 FROM content_requests wanted WHERE wanted.content_id=e.content_id AND wanted.status IN ('approved','fulfilled')
          AND ($admin=1 OR wanted.requested_by=$viewer)
          AND wanted.monitor_mode<>'none' AND e.retired=0 AND
          (wanted.scope_explicit=1 AND EXISTS(SELECT 1 FROM request_episode_scope scope WHERE scope.request_id=wanted.id AND scope.episode_id=e.id)
            OR wanted.scope_explicit=0 AND (wanted.season_number IS NULL OR wanted.season_number=e.season_number) AND (wanted.episode_number IS NULL OR wanted.episode_number=e.episode_number))
          AND (wanted.monitor_mode<>'future' OR e.air_date>=substr(wanted.created_at,1,10)))
        """;
    private static readonly string ContentSelect = $"""
        SELECT c.id,c.provider,c.provider_id,c.kind,COALESCE(cp.title,c.title),c.original_title,COALESCE(cp.overview,c.overview),c.release_date,
          c.poster_url,c.backdrop_url,c.provider_url,c.genres_json,c.imdb_id,c.tvdb_id,c.rating,c.runtime_minutes,
          COALESCE(t.monitor_mode,'none'),COALESCE(t.quality_profile_id,'balanced'),t.requested_at,t.last_sync_at,t.last_error,
          (SELECT COUNT(*) FROM media_matches mm JOIN media m ON m.id=mm.media_id JOIN libraries l ON l.id=m.library_id
             WHERE mm.content_id=c.id AND m.available=1 AND l.enabled=1 AND {LibraryAccess} AND {MediaAccess} AND {WholeMovieReady}),
          (SELECT COUNT(*) FROM episodes e WHERE e.content_id=c.id AND e.retired=0),
          (SELECT COUNT(*) FROM episodes e WHERE e.content_id=c.id AND e.air_date IS NOT NULL AND e.air_date<>'' AND e.air_date<=date('now')
            AND t.monitor_mode<>'none' AND {EpisodeWanted} AND (t.monitor_mode<>'future' OR e.air_date>=substr(t.requested_at,1,10))
            AND NOT EXISTS(SELECT 1 FROM media_episode_segments ms JOIN media m ON m.id=ms.media_id JOIN libraries l ON l.id=m.library_id
              WHERE ms.episode_id=e.id AND m.available=1 AND l.enabled=1 AND {LibraryAccess} AND {MediaAccess})),
          EXISTS(SELECT 1 FROM content_requests r WHERE r.content_id=c.id AND r.status IN ('pending','approved','fulfilled') AND (r.requested_by=$viewer OR $admin=1)),COALESCE(t.sync_state,'idle'),
          (SELECT r.status FROM content_requests r WHERE r.content_id=c.id AND (r.requested_by=$viewer OR $admin=1) AND r.status<>'cancelled' ORDER BY r.created_at DESC LIMIT 1),{ContentAge},c.age_label,c.catalog_revision,
          (SELECT json_group_array(json_object('provider',a.provider,'kind',a.kind,'providerId',a.provider_id,'title',a.title,'originalTitle',a.original_title)) FROM
            (SELECT provider,kind,provider_id,json_extract(metadata_json,'$.title') AS title,json_extract(metadata_json,'$.originalTitle') AS original_title FROM content_aliases WHERE content_id=c.id
             UNION ALL SELECT 'name',c.kind,0,name,name FROM content_names WHERE content_id=c.id) a)
        FROM content c LEFT JOIN tracking t ON t.content_id=c.id LEFT JOIN content_presentation cp ON cp.content_id=c.id
        """;

    public ContentEntry? GetContent(string id, Account? viewer = null)
    {
        id = CanonicalContentId(id);
        using var db = Open();
        return Query(db, ContentSelect + " WHERE c.id=$id AND " + ContentAccess, ReadContent, [("$id", id), ..ViewerArgs(viewer)]).FirstOrDefault();
    }
    public ContentEntry? FindContent(string provider, string kind, int providerId, Account? viewer = null)
    {
        using var db = Open();
        return Query(db, ContentSelect + " WHERE EXISTS(SELECT 1 FROM content_aliases ca WHERE ca.content_id=c.id AND ca.provider=$provider AND ca.kind=$kind AND ca.provider_id=$external) AND " + ContentAccess, ReadContent,
            [("$provider", provider), ("$kind", kind), ("$external", providerId), ..ViewerArgs(viewer)]).FirstOrDefault();
    }
    public IReadOnlyList<ContentEntry> GetTracked(Account? viewer = null)
    {
        using var db = Open();
        return Query(db, ContentSelect + " WHERE " + ContentAccess + " AND EXISTS(SELECT 1 FROM content_requests r WHERE r.content_id=c.id AND r.status IN ('pending','approved','fulfilled') AND (r.requested_by=$viewer OR $admin=1)) ORDER BY t.requested_at DESC", ReadContent, ViewerArgs(viewer));
    }
    public IReadOnlyList<MediaItem> GetContentFiles(string id, Account? viewer = null)
    {
        id = CanonicalContentId(id);
        using var db = Open();
        return Query(db, MediaSelect + " WHERE mm.content_id=$id AND l.enabled=1 AND " + LibraryAccess + " AND " + MediaAccess + " ORDER BY e.season_number,e.episode_number,m.height DESC", ReadMedia, [("$id", id), ..ViewerArgs(viewer)]);
    }

    public ContentEntry RequestContent(ProviderContent content, string mode, string profile, Account? viewer = null, int? season = null, int? episode = null)
    {
        ValidateTracking(mode, profile);
        if (season is < 0 or > 1000 || episode is < 1 or > 10000 || episode is not null && season is null || content.Kind == "movie" && (season is not null || episode is not null))
            throw new DomainException("İstek sezon/bölüm kapsamı geçersiz.");
        lock (_writeGate)
        {
            if (viewer is not null)
            {
                viewer = GetAccount(viewer.Id) ?? throw new DomainException("Hesap bulunamadı.", 403);
                if (!viewer.Enabled || !viewer.CanRequest || !viewer.IsAdmin && !viewer.CanDiscover) throw new DomainException("Bu hesap içerik isteyemez.", 403);
                if (!CanSeeProviderContent(content, viewer)) throw new DomainException("Bu içerik profilinin erişim kapsamı dışında.", 403);
            }
            var userId = viewer?.Id ?? "1";
            var scope = season is null ? "all" : $"s{season}:e{episode?.ToString() ?? "all"}";
            var priorContent = FindContent(content.Provider, content.Kind, content.ProviderId, viewer);
            using var db = Open();
            if (priorContent is not null && Scalar(db, "SELECT id FROM content_requests WHERE content_id=$id AND requested_by=$user AND scope_key=$scope AND status IN ('pending','approved','fulfilled')",
                ("$id", priorContent.Id), ("$user", userId), ("$scope", scope)) is string) return priorContent;
            if (viewer is { IsAdmin: false, MonthlyRequestLimit: > 0 })
            {
                var month = new DateTimeOffset(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, TimeSpan.Zero).ToString("O");
                if (Convert.ToInt32(Scalar(db, "SELECT COUNT(*) FROM content_requests WHERE requested_by=$user AND created_at>=$month", ("$user", userId), ("$month", month))) >= viewer.MonthlyRequestLimit)
                    throw new DomainException("Bu ayki içerik isteği kotana ulaştın.", 429);
            }
            using var tx = db.BeginTransaction();
            var id = UpsertContent(db, tx, content);
            var requestId = Guid.NewGuid().ToString("N");
            var status = viewer is null || viewer.IsAdmin || viewer.AutoApprove ? "approved" : "pending";
            using var request = Command(db, """
                INSERT INTO content_requests(id,content_id,requested_by,status,scope_key,season_number,episode_number,monitor_mode,quality_profile_id,created_at,updated_at)
                  VALUES($request,$id,$user,$status,$scope,$season,$episode,$mode,$profile,$now,$now);
                INSERT INTO tracking(content_id,monitor_mode,quality_profile_id,requested_at,next_sync_at,sync_state)
                  VALUES($id,CASE WHEN $status='approved' THEN $mode ELSE 'none' END,$profile,$now,$now,'pending')
                ON CONFLICT(content_id) DO UPDATE SET monitor_mode=CASE WHEN $status='approved' AND $admin=1 THEN $mode WHEN $status='approved' AND monitor_mode='none' THEN 'all' ELSE monitor_mode END,
                  quality_profile_id=CASE WHEN $status='approved' AND $admin=1 THEN $profile ELSE quality_profile_id END,next_sync_at=$now,sync_state='pending',revision=revision+1;
                """, ("$request", requestId), ("$id", id), ("$now", Now()), ("$mode", mode), ("$profile", profile), ("$user", userId), ("$status", status),
                ("$scope", scope), ("$season", season), ("$episode", episode), ("$admin", viewer is null || viewer.IsAdmin));
            request.Transaction = tx; request.ExecuteNonQuery();
            AddRequestEvent(db, tx, requestId, userId, status, "İstek oluşturuldu.");
            AddNotification(db, tx, userId, "request", content.Title, status == "pending" ? "İsteğin yönetici onayı bekliyor." : "İsteğin onaylandı; takip listesine eklendi.", id, requestId + ":created");
            if (status == "pending")
                foreach (var admin in GetAccounts().Where(u => u.IsAdmin && u.Enabled))
                    AddNotification(db, tx, admin.Id, "approval", content.Title, "Onay bekleyen yeni içerik isteği var.", id, requestId + ":approval");
            tx.Commit();
            return GetContent(id, viewer)!;
        }
    }
    public ContentEntry UpdateTracking(string id, TrackingUpdate input)
    {
        id = CanonicalContentId(id);
        ValidateTracking(input.MonitorMode, input.QualityProfileId);
        lock (_writeGate)
        {
            using var db = Open();
            if (GetContent(id) is not { Requested: true }) throw new DomainException("İçerik önce takip listesine eklenmeli.", 409);
            Execute(db, "UPDATE tracking SET monitor_mode=$mode,quality_profile_id=$profile,next_sync_at=$now,sync_state=CASE WHEN $mode='none' THEN 'idle' ELSE 'pending' END,revision=revision+1 WHERE content_id=$id",
                ("$id", id), ("$mode", input.MonitorMode), ("$profile", input.QualityProfileId), ("$now", Now()));
            return GetContent(id)!;
        }
    }
    public void CancelContentRequest(string id, Account? viewer = null)
    {
        id = CanonicalContentId(id);
        lock (_writeGate)
        {
            using var db = Open();
            var requests = Query(db, "SELECT id FROM content_requests WHERE content_id=$id AND requested_by=$user AND status IN ('pending','approved','fulfilled')", r => r.GetString(0), ("$id", id), ("$user", viewer?.Id ?? "1"));
            foreach (var request in requests) DecideRequest(request, "cancelled", "", viewer);
        }
    }
    public (ContentEntry Entry, int Revision)? ClaimTrackingSync()
    {
        lock (_writeGate)
        {
            if (ContainerUpdateBlocked) return null;
            using var db = Open();
            var id = Scalar(db, "SELECT content_id FROM tracking WHERE (monitor_mode<>'none' OR sync_state='pending') AND next_sync_at<=$now ORDER BY next_sync_at LIMIT 1", ("$now", Now())) as string;
            if (id is null) return null;
            var revision = Convert.ToInt32(Scalar(db, "SELECT revision FROM tracking WHERE content_id=$id", ("$id", id)), CultureInfo.InvariantCulture);
            Execute(db, "UPDATE tracking SET sync_state='syncing',next_sync_at=$next WHERE content_id=$id", ("$id", id), ("$next", DateTimeOffset.UtcNow.AddMinutes(15).ToString("O")));
            return (GetContent(id)!, revision);
        }
    }
    public void RecoverTrackingSync()
    {
        lock (_writeGate)
        {
            using var db = Open();
            Execute(db, "UPDATE tracking SET sync_state=CASE WHEN monitor_mode='none' THEN 'idle' ELSE 'pending' END,next_sync_at=$now WHERE sync_state='syncing'", ("$now", Now()));
        }
    }
    public void CompleteTrackingSync(string id, int revision, ProviderContent content, IReadOnlyList<ProviderEpisode> episodes)
    {
        lock (_writeGate)
        {
            using var db = Open();
            if (Convert.ToInt32(Scalar(db, "SELECT revision FROM tracking WHERE content_id=$id", ("$id", id)), CultureInfo.InvariantCulture) != revision) return;
            using var tx = db.BeginTransaction();
            _ = UpsertContent(db, tx, content);
            foreach (var episode in episodes) _ = UpsertEpisode(db, tx, id, episode);
            using var cmd = Command(db, "UPDATE tracking SET last_sync_at=$now,last_error=NULL,sync_state='idle',next_sync_at=$next WHERE content_id=$id AND revision=$revision",
                ("$id", id), ("$revision", revision), ("$now", Now()), ("$next", DateTimeOffset.UtcNow.AddHours(6).ToString("O")));
            cmd.Transaction = tx; cmd.ExecuteNonQuery(); tx.Commit();
        }
    }
    public void FailTrackingSync(string id, int revision, string message)
    {
        lock (_writeGate)
        {
            using var db = Open();
            Execute(db, "UPDATE tracking SET last_error=$error,sync_state='failed',next_sync_at=$next WHERE content_id=$id AND revision=$revision",
                ("$id", id), ("$revision", revision), ("$error", message), ("$next", DateTimeOffset.UtcNow.AddMinutes(30).ToString("O")));
        }
    }
    public IReadOnlyList<EpisodeEntry> GetEpisodes(string? contentId = null, string? from = null, string? to = null)
        => GetEpisodePage(contentId, from, to, null, null, 0, 20000).Items;

    public EpisodePage GetEpisodePage(string? contentId, string? from, string? to, int? season, string? state, int offset, int limit, Account? viewer = null)
    {
        if (contentId is not null) contentId = CanonicalContentId(contentId);
        if (season is < 0 or > 1000 || state is not (null or "" or "missing" or "available" or "upcoming" or "unknown" or "unmonitored"))
            throw new DomainException("Bölüm filtresi geçerli değil.");
        offset = Math.Max(0, offset);
        limit = Math.Clamp(limit, 1, 20000);
        using var db = Open();
        var select = $"""
            WITH episode_files AS (
            SELECT e.id,e.content_id,COALESCE(cp.title,c.title) AS content_title,e.season_number,e.episode_number,e.title,e.air_date,
              COALESCE(t.monitor_mode,'none') AS monitor_mode,t.requested_at,{EpisodeWanted} AS wanted,
              (SELECT COUNT(*) FROM media_episode_segments ms JOIN media m ON m.id=ms.media_id JOIN libraries l ON l.id=m.library_id
                WHERE ms.episode_id=e.id AND m.available=1 AND l.enabled=1 AND {LibraryAccess} AND {MediaAccess}) AS file_count
            FROM episodes e JOIN content c ON c.id=e.content_id LEFT JOIN tracking t ON t.content_id=c.id LEFT JOIN content_presentation cp ON cp.content_id=c.id
            WHERE {ContentAccess} AND e.retired=0 AND ($id='' OR e.content_id=$id) AND ($from='' OR e.air_date>=$from) AND ($to='' OR e.air_date<=$to)
              AND ($id<>'' OR EXISTS(SELECT 1 FROM content_requests r WHERE r.content_id=c.id AND r.status IN ('approved','fulfilled') AND (r.requested_by=$viewer OR $admin=1)))
            ), episode_status AS (
            SELECT *,CASE WHEN file_count>0 THEN 'available' WHEN air_date IS NULL OR air_date='' THEN 'unknown'
              WHEN air_date>date('now') THEN 'upcoming' WHEN wanted=0 OR monitor_mode='none' OR (monitor_mode='future' AND air_date<substr(requested_at,1,10))
              THEN 'unmonitored' ELSE 'missing' END AS state FROM episode_files
            )
            """;
        const string filter = " WHERE ($season IS NULL OR season_number=$season) AND ($state='' OR state=$state)";
        (string, object?)[] args = [("$id", contentId ?? ""), ("$from", from ?? ""), ("$to", to ?? ""), ("$season", season),
            ("$state", state ?? ""), ("$offset", offset), ("$limit", limit), ..ViewerArgs(viewer)];
        var total = Convert.ToInt32(Scalar(db, select + "SELECT COUNT(*) FROM episode_status" + filter, args));
        var rows = Query(db, select + "SELECT id,content_id,content_title,season_number,episode_number,title,air_date,state,file_count FROM episode_status"
            + filter + " ORDER BY " + (contentId is null ? "air_date," : "") + "season_number,episode_number,id LIMIT $limit OFFSET $offset",
            r => new EpisodeEntry(r.GetString(0), r.GetString(1), r.GetString(2), r.GetInt32(3), r.GetInt32(4), r.GetString(5), Text(r, 6), r.GetString(7), r.GetInt32(8)), args);
        var seasons = contentId is null ? [] : Query(db, "SELECT DISTINCT season_number FROM episodes WHERE content_id=$id AND retired=0 ORDER BY season_number", r => r.GetInt32(0), ("$id", contentId));
        return new EpisodePage(rows, total, offset, limit, seasons);
    }

    public IReadOnlyList<QualityProfile> GetQualityProfiles()
    {
        using var db = Open();
        return Query(db, "SELECT id,name,settings_json FROM quality_profiles ORDER BY name", r =>
            new QualityProfile(r.GetString(0),r.GetString(1),JsonSerializer.Deserialize<QualityRules>(r.GetString(2),WebJson)!));
    }
    public QualityProfile GetQualityProfile(string id) => GetQualityProfiles().FirstOrDefault(p => p.Id == id) ?? throw new DomainException("Kalite profili bulunamadı.", 404);
    public static void ValidateQualityProfile(QualityProfileInput input)
    {
        if (input.Rules is not null) Sources.ReleaseEvaluator.ValidatePreferences(input.Rules);
        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Length > 80 || input.Rules is null || input.Rules.Resolutions is null || input.Rules.Resolutions.Length is < 1 or > 8
            || input.Rules.Resolutions.Any(r => r is not (2160 or 1080 or 720 or 576 or 480)) || input.Rules.MaxSizeGiB is <= 0 or > 1000
            || !double.IsFinite(input.Rules.MaxSizeGiB) || input.Rules.MinSeeders is < 0 or > 10000
            || !double.IsFinite(input.Rules.MinSizeGiB) || input.Rules.MinSizeGiB < 0 || input.Rules.MinSizeGiB >= input.Rules.MaxSizeGiB
            || input.Rules.Resolutions.Distinct().Count() != input.Rules.Resolutions.Length)
            throw new DomainException("Kalite profili değerlerini kontrol et.");
        foreach (var terms in new[] { input.Rules.RequiredTerms, input.Rules.ForbiddenTerms, input.Rules.PreferredTerms })
            if (terms is null || terms.Length > 30 || terms.Any(t => string.IsNullOrWhiteSpace(t) || t.Length > 80)) throw new DomainException("Profil sözcükleri geçerli değil.");
        if (input.Rules.Languages is { } languages && (languages.Length > 20 || languages.Any(l => string.IsNullOrWhiteSpace(l) || l.Length > 30))
            || input.Rules.Sources is { } sources && (sources.Length > 10 || sources.Any(s => s is not ("remux" or "bluray" or "web-dl" or "webrip" or "hdtv" or "unknown"))))
            throw new DomainException("Dil veya yayın kaynağı profili geçerli değil.");
    }
    public QualityProfile SaveQualityProfile(string? id, QualityProfileInput input)
    {
        ValidateQualityProfile(input);
        lock (_writeGate)
        {
            if (id is null && GetQualityProfiles().Count >= 100) throw new DomainException("En fazla 100 kalite profili saklanabilir.");
            using var db = Open(); id ??= Guid.NewGuid().ToString("N");
            Execute(db, "INSERT INTO quality_profiles(id,name,settings_json,updated_at) VALUES($id,$name,$rules,$now) ON CONFLICT(id) DO UPDATE SET name=$name,settings_json=$rules,updated_at=$now",
                ("$id", id), ("$name", input.Name.Trim()), ("$rules", JsonSerializer.Serialize(input.Rules,WebJson)), ("$now", Now()));
            Execute(db, """
                UPDATE automation_rules SET revision=revision+1,next_run=$now WHERE content_id IN (SELECT content_id FROM tracking WHERE quality_profile_id=$id);
                UPDATE automation_jobs SET status='cancelled',message='Kalite profili değişti.' WHERE status IN ('queued','waiting','running') AND content_id IN (SELECT content_id FROM tracking WHERE quality_profile_id=$id);
                """, ("$id", id), ("$now", Now()));
            return GetQualityProfile(id);
        }
    }
    private void ValidateTracking(string mode, string profile)
    {
        if (mode is not ("all" or "future" or "missing" or "none")) throw new DomainException("Geçersiz takip kapsamı.");
        _ = GetQualityProfile(profile);
    }

    private static string UpsertContent(SqliteConnection db, SqliteTransaction tx, ProviderContent content)
    {
        using var find = Command(db, "SELECT content_id FROM content_aliases WHERE provider=$p AND kind=$k AND provider_id=$external", ("$p",content.Provider),("$k",content.Kind),("$external",content.ProviderId));
        find.Transaction = tx;
        var id = find.ExecuteScalar() as string ?? Guid.NewGuid().ToString("N");
        using var primary = Command(db, "SELECT provider,provider_id,imdb_id,tvdb_id FROM content WHERE id=$id", ("$id", id)); primary.Transaction = tx;
        using (var row = primary.ExecuteReader())
        {
            if (row.Read() && (row.GetString(0) != content.Provider || row.GetInt32(1) != content.ProviderId))
            {
                if (content.ImdbId is not null && Text(row, 2) is { } imdb && imdb != content.ImdbId || content.TvdbId is not null && !row.IsDBNull(3) && row.GetInt32(3) != content.TvdbId)
                    throw new DomainException("Bağlı sağlayıcının dış kimliği değişti; katalog eşleştirmesini incele.", 409);
                row.Close(); WriteCatalogAlias(db, tx, id, content); return id;
            }
        }
        using var cmd = Command(db, """
            INSERT INTO content(id,provider,provider_id,kind,title,original_title,search_text,overview,release_date,poster_url,backdrop_url,genres_json,provider_url,updated_at,imdb_id,tvdb_id,rating,runtime_minutes,minimum_age,age_label)
            VALUES($id,$p,$external,$kind,$title,$original,$search,$overview,$date,$poster,$backdrop,$genres,$url,$now,$imdb,$tvdb,$rating,$runtime,$age,$ageLabel)
            ON CONFLICT(provider,kind,provider_id) DO UPDATE SET title=$title,original_title=$original,search_text=$search,
              overview=$overview,release_date=$date,poster_url=$poster,backdrop_url=$backdrop,genres_json=$genres,provider_url=$url,
              updated_at=$now,imdb_id=$imdb,tvdb_id=$tvdb,rating=$rating,runtime_minutes=$runtime,minimum_age=COALESCE($age,minimum_age),age_label=COALESCE($ageLabel,age_label),catalog_revision=catalog_revision+1
            """, ("$id",id),("$p",content.Provider),("$external",content.ProviderId),("$kind",content.Kind),("$title",content.Title),
            ("$original",content.OriginalTitle),("$search",MediaPaths.SearchKey(content.Title+" "+content.OriginalTitle)),("$overview",content.Overview),
            ("$date",content.ReleaseDate),("$poster",content.PosterUrl),("$backdrop",content.BackdropUrl),("$genres",JsonSerializer.Serialize(content.Genres)),
            ("$url",content.ProviderUrl),("$now",Now()),("$imdb",content.ImdbId),("$tvdb",content.TvdbId),("$rating",content.Rating),("$runtime",content.RuntimeMinutes),("$age",content.MinimumAge),("$ageLabel",content.AgeLabel));
        cmd.Transaction=tx; cmd.ExecuteNonQuery(); WriteCatalogAlias(db, tx, id, content); return id;
    }
    private static string UpsertEpisode(SqliteConnection db, SqliteTransaction tx, string contentId, ProviderEpisode episode, string? provider = null)
    {
        ValidateProviderEpisode(episode);
        using var source = Command(db, "SELECT provider FROM content WHERE id=$id", ("$id", contentId)); source.Transaction = tx;
        var primaryProvider = (string)source.ExecuteScalar()!; provider ??= primaryProvider;
        if (EpisodeValue(db, tx, "SELECT provider_id FROM episode_aliases WHERE provider=$provider AND provider_id=$external AND retired=1", ("$provider", provider), ("$external", episode.ProviderId)) is not null)
            throw new DomainException("Bu sağlayıcı bölüm kimliği artık etkin değil; katalog değişikliğini incele.", 409);
        using var parent = Command(db, "SELECT content_id FROM episode_aliases WHERE provider=$provider AND provider_id=$external", ("$provider", provider), ("$external", episode.ProviderId)); parent.Transaction = tx;
        if (parent.ExecuteScalar() is string parentId && parentId != contentId) throw new DomainException("Sağlayıcı bölüm kimliği başka bir diziye bağlı.", 409);
        using var alias = Command(db, "SELECT episode_id FROM episode_aliases WHERE provider=$provider AND provider_id=$external AND content_id=$content", ("$provider", provider), ("$external", episode.ProviderId), ("$content", contentId)); alias.Transaction = tx;
        var known = alias.ExecuteScalar() as string;
        if (provider != primaryProvider)
        {
            if (known is not null)
            {
                if (Convert.ToInt32(EpisodeValue(db, tx, "SELECT COUNT(*) FROM episode_alias_targets WHERE provider=$provider AND provider_id=$external", ("$provider", provider), ("$external", episode.ProviderId))) != 1)
                    throw new DomainException("Bu sağlayıcı bölümü birden fazla kanonik bölüme karşılık geliyor; bölüm kapsamı akışını kullan.", 409);
                return known;
            }
            using var candidate = Command(db, "SELECT id,title,air_date FROM episodes WHERE content_id=$id AND season_number=$s AND episode_number=$e AND retired=0", ("$id", contentId), ("$s", episode.SeasonNumber), ("$e", episode.EpisodeNumber)); candidate.Transaction = tx;
            using var row = candidate.ExecuteReader();
            if (!row.Read() || !SameEpisode(new ProviderEpisode(0, episode.SeasonNumber, episode.EpisodeNumber, row.GetString(1), "", Text(row, 2), null), episode)) throw new DomainException("Alternatif sağlayıcı bölümünün kanonik karşılığı doğrulanamadı.", 409);
            known = row.GetString(0); row.Close();
            WriteEpisodeAlias(db, tx, provider, episode.ProviderId, contentId, [(known, (double?)null, (double?)null)]); return known;
        }
        using var find=Command(db,"SELECT id FROM episodes WHERE content_id=$id AND season_number=$s AND episode_number=$e AND retired=0",("$id",contentId),("$s",episode.SeasonNumber),("$e",episode.EpisodeNumber));
        find.Transaction=tx; var byPosition = find.ExecuteScalar() as string;
        if (known is not null && byPosition != known) throw new DomainException("Sağlayıcının bölüm sırası değişti; mevcut kimlikler korundu.", 409);
        if (known is null && byPosition is not null)
        {
            using var old = Command(db, "SELECT provider_id FROM episodes WHERE id=$id", ("$id", byPosition)); old.Transaction = tx;
            if (Convert.ToInt32(old.ExecuteScalar()) != episode.ProviderId) throw new DomainException("Bu bölüm konumunun sağlayıcı kimliği değişti; önceki eşleşme korundu.", 409);
        }
        var id = byPosition ?? Guid.NewGuid().ToString("N");
        using var cmd=Command(db,"""
            INSERT INTO episodes(id,content_id,provider_id,season_number,episode_number,title,overview,air_date,still_url,runtime_minutes)
            VALUES($id,$content,$external,$s,$e,$title,$overview,$date,$still,$runtime)
            ON CONFLICT(content_id,season_number,episode_number) WHERE retired=0 DO UPDATE SET provider_id=$external,title=$title,overview=$overview,air_date=$date,still_url=$still,runtime_minutes=$runtime
            """,("$id",id),("$content",contentId),("$external",episode.ProviderId),("$s",episode.SeasonNumber),("$e",episode.EpisodeNumber),
            ("$title",episode.Title),("$overview",episode.Overview),("$date",episode.AirDate),("$still",episode.StillUrl),("$runtime",episode.RuntimeMinutes));
        cmd.Transaction=tx;cmd.ExecuteNonQuery();
        WriteEpisodeAlias(db, tx, provider, episode.ProviderId, contentId, [(id, (double?)null, (double?)null)]); return id;
    }
    private static ContentEntry ReadContent(SqliteDataReader r) => new(r.GetString(0),new ProviderContent(
        r.GetString(1),r.GetInt32(2),r.GetString(3),r.GetString(4),r.GetString(5),r.GetString(6),Text(r,7),Text(r,8),Text(r,9),r.GetString(10),
        JsonSerializer.Deserialize<string[]>(r.GetString(11))??[],Text(r,12),r.IsDBNull(13)?null:r.GetInt32(13),r.IsDBNull(14)?null:r.GetDouble(14),r.IsDBNull(15)?null:r.GetInt32(15),r.IsDBNull(27)?null:r.GetInt32(27),Text(r,28),JsonSerializer.Deserialize<CatalogAlias[]>(r.GetString(30), WebJson)),
        r.GetString(16),r.GetString(17),Text(r,18),Text(r,19),Text(r,20),r.GetInt32(21),r.GetInt32(22),r.GetInt32(23),r.GetBoolean(24),r.GetString(25),Text(r,26),r.GetInt32(29));
}
