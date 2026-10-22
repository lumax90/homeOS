using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HomeServer.Core;
using Microsoft.Data.Sqlite;

namespace HomeServer.Infrastructure;

public sealed partial class MediaStore
{
    private static int EpisodeSql(SqliteConnection db, SqliteTransaction? tx, string sql, params (string, object?)[] args)
    { using var command = Command(db, sql, args); command.Transaction = tx; return command.ExecuteNonQuery(); }
    private static object? EpisodeValue(SqliteConnection db, SqliteTransaction? tx, string sql, params (string, object?)[] args)
    { using var command = Command(db, sql, args); command.Transaction = tx; var result = command.ExecuteScalar(); return result is DBNull ? null : result; }
    private static List<T> EpisodeRows<T>(SqliteConnection db, SqliteTransaction? tx, string sql, Func<SqliteDataReader, T> read, params (string, object?)[] args)
    { using var command = Command(db, sql, args); command.Transaction = tx; using var reader = command.ExecuteReader(); var rows = new List<T>(); while (reader.Read()) rows.Add(read(reader)); return rows; }
    private static void ValidateProviderEpisode(ProviderEpisode value)
    {
        if (value.ProviderId < 1 || value.SeasonNumber is < 0 or > 1000 || value.EpisodeNumber is < 1 or > 10000 || value.Title is null || value.Title.Length > 1000 || value.Overview is null || value.Overview.Length > 200000 || value.RuntimeMinutes is < 1 or > 1440)
            throw new DomainException("Sağlayıcı bölüm kimliği veya metadata sınırları geçersiz.", 502);
    }
    private static EpisodeCatalogRow ReadEpisodeCatalog(SqliteDataReader r) => new(r.GetString(0), new(r.GetInt32(1), r.GetInt32(2), r.GetInt32(3), r.GetString(4), r.GetString(5), Text(r, 6), Text(r, 7), r.IsDBNull(8) ? null : r.GetInt32(8)), r.GetBoolean(9));
    private const string EpisodeCatalogSelect = "SELECT id,provider_id,season_number,episode_number,title,overview,air_date,still_url,runtime_minutes,retired FROM episodes";
    public IReadOnlyList<EpisodeCatalogRow> GetEpisodeCatalog(string id, bool includeRetired = false)
    { using var db = Open(); return EpisodeRows(db, null, EpisodeCatalogSelect + " WHERE content_id=$id AND ($retired=1 OR retired=0) ORDER BY retired,season_number,episode_number,id", ReadEpisodeCatalog, ("$id", id), ("$retired", includeRetired)); }
    private static void WriteEpisodeAlias(SqliteConnection db, SqliteTransaction tx, string provider, int externalId, string contentId, IReadOnlyList<(string Id, double? Start, double? End)> targets)
    {
        if (targets.Count is < 1 or > 20 || targets.Select(row => row.Id).Distinct().Count() != targets.Count) throw new DomainException("Bölüm karşılığı 1–20 farklı kanonik bölüm içermeli.", 409);
        foreach (var target in targets)
            if (EpisodeValue(db, tx, "SELECT id FROM episodes WHERE id=$episode AND content_id=$content", ("$episode", target.Id), ("$content", contentId)) is null) throw new DomainException("Bölüm karşılığı başka bir yapıma ait.", 409);
        EpisodeSql(db, tx, """
            INSERT INTO episode_aliases(provider,provider_id,content_id,episode_id,retired) VALUES($provider,$external,$content,$episode,0)
              ON CONFLICT(provider,provider_id) DO UPDATE SET content_id=$content,episode_id=$episode,retired=0;
            DELETE FROM episode_alias_targets WHERE provider=$provider AND provider_id=$external;
            """, ("$provider", provider), ("$external", externalId), ("$content", contentId), ("$episode", targets[0].Id));
        for (var position = 0; position < targets.Count; position++) EpisodeSql(db, tx,
            "INSERT INTO episode_alias_targets(provider,provider_id,episode_id,position,source_start,source_end) VALUES($provider,$external,$episode,$position,$start,$end)",
            ("$provider", provider), ("$external", externalId), ("$episode", targets[position].Id), ("$position", position), ("$start", targets[position].Start), ("$end", targets[position].End));
    }
    private static (string Id, double? Start, double? End)[] UpsertEpisodeTargets(SqliteConnection db, SqliteTransaction tx, string contentId, ProviderEpisode episode, string provider)
    {
        var alias = EpisodeRows(db, tx, "SELECT content_id,retired FROM episode_aliases WHERE provider=$provider AND provider_id=$external", r => (Content: r.GetString(0), Retired: r.GetBoolean(1)), ("$provider", provider), ("$external", episode.ProviderId)).FirstOrDefault();
        if (alias.Content is not null && (alias.Content != contentId || alias.Retired)) throw new DomainException("Sağlayıcı bölüm kimliği değişti veya başka bir yapıma ait.", 409);
        var primary = EpisodeValue(db, tx, "SELECT provider FROM content WHERE id=$id", ("$id", contentId)) as string;
        if (primary != provider && alias.Content is not null)
        {
            var targets = EpisodeRows(db, tx, "SELECT t.episode_id,t.source_start,t.source_end,e.retired FROM episode_alias_targets t JOIN episodes e ON e.id=t.episode_id WHERE t.provider=$provider AND t.provider_id=$external ORDER BY t.position",
                r => (Id: r.GetString(0), Start: r.IsDBNull(1) ? (double?)null : r.GetDouble(1), End: r.IsDBNull(2) ? (double?)null : r.GetDouble(2), Retired: r.GetBoolean(3)), ("$provider", provider), ("$external", episode.ProviderId));
            if (targets.Count == 0 || targets.Any(value => value.Retired)) throw new DomainException("Sağlayıcı bölümünün etkin karşılıkları yeniden eşlenmeli.", 409);
            return targets.Select(value => (value.Id, value.Start, value.End)).ToArray();
        }
        return [(UpsertEpisode(db, tx, contentId, episode, provider), null, null)];
    }
    public string[] ResolveProviderEpisodeIds(string id, string provider, ProviderEpisode episode)
    {
        ValidateProviderEpisode(episode); var content = GetContent(id) ?? throw new DomainException("Dizi bulunamadı.", 404); using var db = Open();
        var alias = EpisodeRows(db, null, "SELECT content_id,retired FROM episode_aliases WHERE provider=$provider AND provider_id=$external", r => (Content: r.GetString(0), Retired: r.GetBoolean(1)), ("$provider", provider), ("$external", episode.ProviderId)).FirstOrDefault();
        if (alias.Content is not null)
        {
            if (alias.Content != content.Id || alias.Retired) throw new DomainException("Sağlayıcı bölüm karşılığı farklı veya artık etkin değil.", 409);
            var targets = EpisodeRows(db, null, "SELECT t.episode_id,e.retired FROM episode_alias_targets t JOIN episodes e ON e.id=t.episode_id WHERE t.provider=$provider AND t.provider_id=$external ORDER BY t.position", r => (Id: r.GetString(0), Retired: r.GetBoolean(1)), ("$provider", provider), ("$external", episode.ProviderId));
            if (targets.Count == 0 || targets.Any(target => target.Retired)) throw new DomainException("Bölüm karşılıklarını yenile.", 409);
            return targets.Select(target => target.Id).ToArray();
        }
        var candidate = GetEpisodeCatalog(content.Id).FirstOrDefault(row => row.Metadata.SeasonNumber == episode.SeasonNumber && row.Metadata.EpisodeNumber == episode.EpisodeNumber
            && (content.Metadata.Provider == provider ? row.Metadata.ProviderId == episode.ProviderId : SameEpisode(row.Metadata, episode)));
        return candidate is null ? throw new DomainException("Sıradaki sağlayıcı bölümünün kanonik karşılığı bulunamadı; kimlik eşlemesini tamamla.", 409) : [candidate.Id];
    }
    public void SaveProviderEpisodeMap(string id, EpisodeAliasInput input, ProviderEpisode episode, Account actor)
    {
        lock (_writeGate)
        {
            RequireEpisodeAdministrator(actor); var content = GetContent(id, actor) ?? throw new DomainException("Dizi bulunamadı.", 404); id = content.Id;
            ValidateProviderEpisode(episode);
            if (content.Metadata.Kind != "show" || input.Provider == content.Metadata.Provider || input.ExpectedRevision != content.CatalogRevision || episode.SeasonNumber != input.Season || episode.EpisodeNumber != input.Episode
                || !GetCatalogAliases(id).Any(alias => alias.Provider == input.Provider && alias.ProviderId == input.ProviderId)) throw new DomainException("Bağlı sağlayıcı veya katalog revizyonu değişti.", 409);
            if (input.Targets is not { Length: > 0 and <= 20 }) throw new DomainException("1–20 farklı bölüm karşılığı seç.");
            var coordinates = GetEpisodeCatalog(id).ToDictionary(row => (row.Metadata.SeasonNumber, row.Metadata.EpisodeNumber), row => row.Id);
            input = input with { Targets = input.Targets.Select(target => target.Season is { } season && target.Episode is { } number ? target with { EpisodeId = coordinates.GetValueOrDefault((season, number)) ?? throw new DomainException("Kanonik bölüm koordinatı bulunamadı.", 409) } : target).ToArray() };
            if (input.Targets.Select(target => target.EpisodeId).Distinct().Count() != input.Targets.Length) throw new DomainException("Bölüm karşılıkları tekrarlanamaz.");
            var active = GetEpisodeCatalog(id).Select(row => row.Id).ToHashSet(); if (input.Targets.Any(target => !active.Contains(target.EpisodeId))) throw new DomainException("Karşılık başka diziye ait veya arşivlenmiş.", 409);
            var timed = input.Targets.Any(target => target.StartSeconds is not null || target.EndSeconds is not null); double previous = 0;
            foreach (var target in input.Targets)
            {
                if (timed && (target.StartSeconds is not { } start || target.EndSeconds is not { } end || !double.IsFinite(start) || !double.IsFinite(end) || start < previous || end <= start || end > 86400)) throw new DomainException("Kaynak bölüm zamanları eksik veya çakışıyor.");
                previous = target.EndSeconds ?? 0;
            }
            using var db = Open(); using var tx = db.BeginTransaction();
            var parent = EpisodeValue(db, tx, "SELECT content_id FROM episode_aliases WHERE provider=$provider AND provider_id=$external", ("$provider", input.Provider), ("$external", episode.ProviderId)) as string;
            if (parent is not null && parent != id) throw new DomainException("Sağlayıcı bölüm kimliği farklı bir yapıma ait.", 409);
            WriteEpisodeAlias(db, tx, input.Provider, episode.ProviderId, id, input.Targets.Select(target => (target.EpisodeId, target.StartSeconds, target.EndSeconds)).ToArray());
            InvalidateEpisodeCatalog(db, tx, id, false, false); tx.Commit();
        }
    }
    private static void InvalidateEpisodeCatalog(SqliteConnection db, SqliteTransaction tx, string id, bool disableAutomation, bool mediaChanged = true)
    {
        EpisodeSql(db, tx, """
            UPDATE content SET catalog_revision=catalog_revision+1 WHERE id=$id;
            UPDATE media_matches SET revision=revision+1 WHERE content_id=$id AND $media=1;
            UPDATE tracking SET revision=revision+1,next_sync_at=$now,sync_state='pending' WHERE content_id=$id;
            UPDATE release_results SET catalog_invalidated=1 WHERE content_id=$id AND download_id IS NULL;
            UPDATE automation_rules SET revision=revision+1,next_run=$now,enabled=CASE WHEN $disable=1 THEN 0 ELSE enabled END,auto_grab=CASE WHEN $disable=1 THEN 0 ELSE auto_grab END,
              settings_json=CASE WHEN $disable=1 THEN json_set(settings_json,'$.enabled',json('false'),'$.autoGrab',json('false')) ELSE settings_json END WHERE content_id=$id;
            UPDATE automation_jobs SET status='cancelled',message='Bölüm kimliği veya sırası değişti.',updated_at=$now WHERE content_id=$id AND status IN ('queued','waiting','running');
            """, ("$id", id), ("$now", Now()), ("$disable", disableAutomation), ("$media", mediaChanged));
    }
    private sealed record RequestScopeSnapshot(string Id, string ContentId, string UserId, string Status, string ScopeKey, int? Season, int? Episode, bool Explicit, string[] Episodes);
    private static List<RequestScopeSnapshot> CaptureEpisodeRequestScopes(SqliteConnection db, SqliteTransaction tx, string contentId)
    {
        var rows = EpisodeRows(db, tx, "SELECT id,requested_by,status,scope_key,season_number,episode_number,scope_explicit FROM content_requests WHERE content_id=$content", r =>
            new RequestScopeSnapshot(r.GetString(0), contentId, r.GetString(1), r.GetString(2), r.GetString(3), r.IsDBNull(4) ? null : r.GetInt32(4), r.IsDBNull(5) ? null : r.GetInt32(5), r.GetBoolean(6), []), ("$content", contentId));
        return rows.Select(row => row with { Episodes = row.Explicit
            ? EpisodeRows(db, tx, "SELECT episode_id FROM request_episode_scope WHERE request_id=$id", r => r.GetString(0), ("$id", row.Id)).ToArray()
            : row.Season is null ? [] : EpisodeRows(db, tx, "SELECT id FROM episodes WHERE content_id=$content AND retired=0 AND season_number=$season AND ($episode IS NULL OR episode_number=$episode)", r => r.GetString(0), ("$content", contentId), ("$season", row.Season), ("$episode", row.Episode)).ToArray() }).ToList();
    }
    private static void RebaseEpisodeRequestScopes(SqliteConnection db, SqliteTransaction tx, IEnumerable<RequestScopeSnapshot> requests, string targetId, IReadOnlyDictionary<string, string[]> maps, string actorId)
    {
        var saved = requests.ToArray();
        foreach (var request in saved.Where(request => request.Status is "pending" or "approved" or "fulfilled"))
            EpisodeSql(db, tx, "UPDATE content_requests SET scope_key=$temporary WHERE id=$id", ("$id", request.Id), ("$temporary", "rebase:" + request.Id));
        foreach (var request in saved)
        {
            var ids = request.Episodes.SelectMany(id => maps.TryGetValue(id, out var mapped) ? mapped : [id]).Distinct().Order(StringComparer.Ordinal).ToArray();
            var coordinates = ids.Select(id => EpisodeRows(db, tx, "SELECT season_number,episode_number,retired FROM episodes WHERE id=$id AND content_id=$content", r => (Season: r.GetInt32(0), Episode: r.GetInt32(1), Retired: r.GetBoolean(2)), ("$id", id), ("$content", targetId)).Single()).ToArray();
            var explicitScope = request.Explicit; var season = request.Season; var episode = request.Episode;
            if (!explicitScope && ids.Length > 0 && season is not null)
            {
                if (episode is not null && ids.Length == 1 && !coordinates[0].Retired) { season = coordinates[0].Season; episode = coordinates[0].Episode; }
                else if (episode is null && coordinates.All(row => !row.Retired) && coordinates.Select(row => row.Season).Distinct().Count() == 1) season = coordinates[0].Season;
                else { explicitScope = true; season = null; episode = null; }
            }
            var scope = explicitScope ? "ids:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|', ids)))) : season is null ? "all" : $"s{season}:e{episode?.ToString() ?? "all"}";
            var status = request.Status; string? duplicate = null;
            if (status is "pending" or "approved" or "fulfilled") duplicate = EpisodeValue(db, tx, "SELECT id FROM content_requests WHERE id<>$id AND content_id=$target AND requested_by=$user AND scope_key=$scope AND status IN ('pending','approved','fulfilled') LIMIT 1", ("$id", request.Id), ("$target", targetId), ("$user", request.UserId), ("$scope", scope)) as string;
            if (duplicate is not null)
            {
                if (status is "approved" or "fulfilled") EpisodeSql(db, tx, "UPDATE content_requests SET status='approved',updated_at=$now WHERE id=$id AND status='pending'", ("$id", duplicate), ("$now", Now()));
                status = "cancelled";
            }
            EpisodeSql(db, tx, "UPDATE content_requests SET content_id=$target,scope_key=$scope,scope_explicit=$explicit,season_number=$season,episode_number=$episode,status=$status,merged_into=COALESCE($duplicate,merged_into),note=CASE WHEN $duplicate IS NOT NULL AND note='' THEN 'Kanonik isteğe birleştirildi.' ELSE note END,updated_at=$now WHERE id=$id; DELETE FROM request_episode_scope WHERE request_id=$id",
                ("$id", request.Id), ("$target", targetId), ("$scope", scope), ("$explicit", explicitScope), ("$season", season), ("$episode", episode), ("$status", status), ("$duplicate", duplicate), ("$now", Now()));
            if (explicitScope) foreach (var id in ids) EpisodeSql(db, tx, "INSERT INTO request_episode_scope(request_id,episode_id) VALUES($request,$episode)", ("$request", request.Id), ("$episode", id));
            if (request.ContentId != targetId || request.ScopeKey != scope || duplicate is not null) AddRequestEvent(db, tx, request.Id, actorId, status,
                duplicate is not null ? "Aynı kullanıcının kimlik korumalı isteğine birleştirildi." : explicitScope ? $"Bölüm kimliği değişimi: {ids.Length} kanonik bölüm açık kapsam olarak korundu." : "Bölüm kimlikleri korunarak istek koordinatları güncellendi.");
        }
    }
}
