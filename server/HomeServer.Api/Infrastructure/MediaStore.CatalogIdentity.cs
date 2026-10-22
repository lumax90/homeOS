using System.Text.Json;
using HomeServer.Core;
using Microsoft.Data.Sqlite;

namespace HomeServer.Infrastructure;

public sealed partial class MediaStore
{
    public string CanonicalContentId(string id)
    {
        using var db = Open(); return Scalar(db, "SELECT COALESCE(merged_into,id) FROM content WHERE id=$id", ("$id", id)) as string ?? id;
    }
    public IReadOnlyList<CatalogAlias> GetCatalogAliases(string id)
    {
        id = CanonicalContentId(id); using var db = Open(); return Query(db, "SELECT provider,kind,provider_id,json_extract(metadata_json,'$.title'),json_extract(metadata_json,'$.originalTitle') FROM content_aliases WHERE content_id=$id ORDER BY provider,provider_id",
            r => new CatalogAlias(r.GetString(0), r.GetString(1), r.GetInt32(2), Text(r, 3), Text(r, 4)), ("$id", id));
    }
    public string[] GetCatalogNames(string id, bool localOnly = false)
    {
        using var db = Open(); return Query(db, "SELECT name FROM content_names WHERE content_id=$id AND ($local=0 OR source='local') ORDER BY name", r => r.GetString(0), ("$id", CanonicalContentId(id)), ("$local", localOnly)).ToArray();
    }
    public void SaveCatalogNames(string id, string[] names, string source = "local")
    {
        if (names is null || names.Length > 200 || names.Any(name => string.IsNullOrWhiteSpace(name) || name.Length > 300)) throw new DomainException("Alternatif adlar en fazla 200 adet ve 300 karakter olmalı.");
        lock (_writeGate)
        {
            id = GetContent(id)?.Id ?? throw new DomainException("İçerik bulunamadı.", 404); using var db = Open(); using var tx = db.BeginTransaction();
            using var clear = Command(db, "DELETE FROM content_names WHERE content_id=$id AND source=$source", ("$id", id), ("$source", source)); clear.Transaction = tx; clear.ExecuteNonQuery();
            foreach (var name in names.Select(name => name.Trim()).Distinct(StringComparer.Ordinal))
            { using var insert = Command(db, "INSERT INTO content_names(content_id,name,source,search_text) VALUES($id,$name,$source,$search) ON CONFLICT(content_id,name) DO UPDATE SET source=CASE WHEN $source='local' THEN 'local' ELSE content_names.source END,search_text=$search", ("$id", id), ("$name", name), ("$source", source), ("$search", MediaPaths.SearchKey(name))); insert.Transaction = tx; insert.ExecuteNonQuery(); }
            using var update = Command(db, "UPDATE content SET catalog_revision=catalog_revision+1 WHERE id=$id", ("$id", id)); update.Transaction = tx; update.ExecuteNonQuery(); tx.Commit();
        }
    }
    private IReadOnlyList<(string Id, ProviderEpisode Metadata)> IdentityEpisodes(string id)
    {
        return GetEpisodeCatalog(id).Select(row => (row.Id, row.Metadata)).ToArray();
    }
    public void RefreshCatalogIdentity(string id, int revision, ProviderContent metadata, IReadOnlyList<ProviderEpisode> episodes)
    {
        lock (_writeGate)
        {
            var current = GetContent(id) ?? throw new DomainException("İçerik bulunamadı.", 404);
            if (current.Id != id || current.CatalogRevision != revision || current.Metadata.Provider != metadata.Provider || current.Metadata.ProviderId != metadata.ProviderId) throw new DomainException("Katalog kimliği yenileme sırasında değişti.", 409);
            using var db = Open(); using var tx = db.BeginTransaction(); _ = UpsertContent(db, tx, metadata);
            foreach (var episode in episodes) _ = UpsertEpisode(db, tx, id, episode, metadata.Provider);
            tx.Commit();
        }
    }
    public CatalogMergePreview PreviewCatalogMerge(string targetId, ProviderContent candidate, CatalogEpisodeMap[]? mappings = null)
    {
        var target = GetContent(targetId) ?? throw new DomainException("Hedef içerik bulunamadı.", 404);
        var source = FindContent(candidate.Provider, candidate.Kind, candidate.ProviderId); var evidence = new List<string>(); var conflicts = new List<string>();
        if (candidate.Kind != target.Metadata.Kind) conflicts.Add("Film ve dizi kimlikleri birleştirilemez.");
        if (source?.Id == target.Id) conflicts.Add("Bu sağlayıcı kimliği zaten aynı kayda bağlı.");
        if (candidate.Provider == target.Metadata.Provider && candidate.ProviderId != target.Metadata.ProviderId) conflicts.Add("Aynı sağlayıcının iki farklı yapım kimliği birleştirilemez.");
        if (source is not null && GetCatalogAliases(source.Id).Any(alias => GetCatalogAliases(target.Id).Any(other => other.Provider == alias.Provider && other.Kind == alias.Kind && other.ProviderId != alias.ProviderId)))
            conflicts.Add("Bağlı kaynakta aynı sağlayıcının farklı bir yapım kimliği var; önce yapım kimliği çakışmasını çöz.");
        foreach (var (label, left, right) in new[] { ("IMDb", target.Metadata.ImdbId, candidate.ImdbId), ("TVDB", target.Metadata.TvdbId?.ToString(), candidate.TvdbId?.ToString()) })
        {
            if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right)) continue;
            if (left.Equals(right, StringComparison.OrdinalIgnoreCase)) evidence.Add(label + " kimliği ortak: " + left); else conflicts.Add(label + " kimlikleri çelişiyor.");
        }
        if (evidence.Count == 0) conflicts.Add("Ortak IMDb veya TVDB kimliği doğrulanamadı.");
        if (mappings?.Length > 20000 || mappings?.Select(map => (map.SourceEpisodeId, map.TargetEpisodeId)).Distinct().Count() != mappings?.Length) throw new DomainException("Bölüm eşleme listesi geçersiz.");
        var sourceEpisodes = source is null ? [] : IdentityEpisodes(source.Id); var targetEpisodes = IdentityEpisodes(target.Id);
        if (mappings?.Any(map => !sourceEpisodes.Any(ep => ep.Id == map.SourceEpisodeId) || !targetEpisodes.Any(ep => ep.Id == map.TargetEpisodeId)) == true) throw new DomainException("Bölüm eşlemesi başka bir yapımı içeriyor.");
        var decisions = sourceEpisodes.Select(episode =>
        {
            var explicitMap = mappings?.Where(map => map.SourceEpisodeId == episode.Id).ToArray() ?? [];
            var automatic = targetEpisodes.FirstOrDefault(other => other.Metadata.SeasonNumber == episode.Metadata.SeasonNumber && other.Metadata.EpisodeNumber == episode.Metadata.EpisodeNumber && SameEpisode(other.Metadata, episode.Metadata));
            var targets = explicitMap.Length > 0 ? explicitMap : automatic.Id is not null ? [new CatalogEpisodeMap(episode.Id, automatic.Id)] : Array.Empty<CatalogEpisodeMap>();
            if (targets.Length > 20) throw new DomainException("Bir kaynak bölümü en fazla 20 kanonik bölümle eşlenebilir.");
            var timed = targets.Any(map => map.StartSeconds is not null || map.EndSeconds is not null); double previous = 0;
            foreach (var map in targets)
            {
                if (timed && (map.StartSeconds is not { } start || map.EndSeconds is not { } end || !double.IsFinite(start) || !double.IsFinite(end) || start < previous || end <= start || end > 86400)) throw new DomainException("Kaynak bölüm eşleme zamanları eksik veya çakışıyor.");
                previous = map.EndSeconds ?? 0;
            }
            return new CatalogMergeEpisode(episode.Id, episode.Metadata.SeasonNumber, episode.Metadata.EpisodeNumber, episode.Metadata.Title, episode.Metadata.AirDate, targets.FirstOrDefault()?.TargetEpisodeId, explicitMap.Length == 0 && automatic.Id is not null, targets);
        }).ToArray();
        if (decisions.Any(item => item.TargetId is null)) conflicts.Add("Eşlenmemiş bölümler var; hedef bölüm kataloğunu yenile ve karşılıklarını seç.");
        if (decisions.SelectMany(item => item.Targets ?? []).GroupBy(item => item.TargetEpisodeId).Any(group => group.Count() > 1)) conflicts.Add("Aynı kanonik bölüm birden fazla kaynak bölümüne atanamaz.");
        using var db = Open();
        var files = source is null ? 0 : Convert.ToInt32(Scalar(db, "SELECT COUNT(*) FROM media_matches WHERE content_id=$id", ("$id", source.Id)));
        var requests = source is null ? 0 : Convert.ToInt32(Scalar(db, "SELECT COUNT(*) FROM content_requests WHERE content_id=$id", ("$id", source.Id)));
        if (source is not null)
        {
            if (Scalar(db, "SELECT a.download_id FROM acquisition_links a JOIN downloads d ON d.id=a.download_id WHERE a.content_id IN ($source,$target) AND (d.imported_at IS NULL AND d.desired_state<>'cancelled' OR d.imported_at IS NOT NULL AND a.status<>'linked') LIMIT 1", ("$source", source.Id), ("$target", target.Id)) is string) conflicts.Add("Bu yapım için transfer/import sürüyor; tamamlanması veya iptali beklenmeli.");
            var mapping = decisions.ToDictionary(row => row.Id, row => row.Targets ?? []);
            var sourceOrders = GetCatalogEpisodeOrders(source.Id);
            if (sourceOrders.Count + GetCatalogEpisodeOrders(target.Id).Count > 20) conflicts.Add("Birleşik alternatif sıra sayısı 20 sınırını aşıyor.");
            if (sourceOrders.Any(order => order.Items.Any(item => item.EpisodeIds.Any(id => !mapping.ContainsKey(id)) || item.EpisodeIds.Sum(id => mapping.GetValueOrDefault(id)?.Length ?? 0) > 20))) conflicts.Add("Kaynak alternatif sırasının karşılıkları eksik veya bir girdide 20 bölüm sınırını aşıyor.");
            foreach (var file in GetContentFiles(source.Id))
            {
                if ((file.Episodes ?? []).Sum(segment => mapping.GetValueOrDefault(segment.EpisodeId)?.Length ?? 0) > 20) conflicts.Add("Birleşme bir dosyada 20'den fazla bölüm oluşturuyor.");
                foreach (var segment in file.Episodes ?? [])
                {
                    if (!mapping.TryGetValue(segment.EpisodeId, out var maps)) { conflicts.Add("Dosyanın arşivlenmiş bölüm kimliği için önce açık karşılık oluşturulmalı."); continue; }
                    if (maps.Length > 1 && maps.Any(map => map.EndSeconds is not null) && file.DurationSeconds is not > 0) conflicts.Add("Zamanlı bölüm bölme için kaynak dosyanın süresi okunmalı.");
                    if (maps.Length > 1 && maps.Any(map => map.EndSeconds is { } end && segment.StartSeconds is { } start && start + end > (segment.EndSeconds ?? file.DurationSeconds) + .1)) conflicts.Add("Bölüm bölme zamanları kaynak dosyaya sığmıyor.");
                }
            }
            if (Scalar(db, "SELECT e.id FROM episodes e WHERE e.content_id=$id AND e.retired=1 AND (EXISTS(SELECT 1 FROM content_watch_state w WHERE w.content_id=$id AND w.episode_key=e.id) OR EXISTS(SELECT 1 FROM request_episode_scope s WHERE s.episode_id=e.id)) LIMIT 1", ("$id", source.Id)) is string)
                conflicts.Add("Kaynakta arşivlenmiş bölüm geçmişi var; önce bölüm kimliği onarımıyla karşılığını belirle.");
            if (GetContentPresentation(source.Id).Tags!.Union(GetContentPresentation(target.Id).Tags!).Distinct().Count() > 20) conflicts.Add("Birleşik yerel etiketler 20 sınırını aşıyor; etiketleri düzenle.");
        }
        return new(target.Id, source?.Id, target.CatalogRevision, source?.CatalogRevision, target.Metadata, candidate, evidence.ToArray(), conflicts.Distinct().ToArray(), decisions, files, requests, conflicts.Count == 0);
    }
    private static bool SameEpisode(ProviderEpisode a, ProviderEpisode b) =>
        MediaPaths.SearchKey(a.Title).Trim() == MediaPaths.SearchKey(b.Title).Trim() && !string.IsNullOrWhiteSpace(a.Title)
        && (string.IsNullOrEmpty(a.AirDate) || string.IsNullOrEmpty(b.AirDate) || a.AirDate == b.AirDate);
    public ContentEntry MergeCatalog(string targetId, ProviderContent candidate, CatalogMergeInput input, Account actor)
    {
        lock (_writeGate)
        {
            if (GetAccount(actor.Id) is not { Enabled: true, IsAdmin: true }) throw new DomainException("Katalog birleştirme yetkisi yok.", 403);
            var preview = PreviewCatalogMerge(targetId, candidate, input.Episodes);
            if (!preview.CanApply) throw new DomainException(string.Join(" ", preview.Conflicts), 409);
            if (input.TargetRevision != preview.TargetRevision || input.SourceRevision != preview.SourceRevision) throw new DomainException("Birleştirme önizlemesi eskidi.", 409);
            using var db = Open(); using var tx = db.BeginTransaction(); var now = Now();
            void Run(string sql, params (string, object?)[] args) { using var command = Command(db, sql, args); command.Transaction = tx; command.ExecuteNonQuery(); }
            Run("INSERT INTO catalog_merge_log(id,target_id,source_id,snapshot_json,actor_id,created_at) VALUES($id,$target,$source,$json,$actor,$now)",
                ("$id", Guid.NewGuid().ToString("N")), ("$target", preview.TargetId), ("$source", preview.SourceId), ("$json", JsonSerializer.Serialize(preview, WebJson)), ("$actor", actor.Id), ("$now", now));
            if (preview.SourceId is { } source)
            {
                MergeCatalogEpisodeState(db, tx, preview, actor.Id);
                Run("""
                    INSERT INTO personal_content(user_id,content_id,favorite,rating) SELECT user_id,$target,favorite,rating FROM personal_content WHERE content_id=$source
                    ON CONFLICT(user_id,content_id) DO UPDATE SET favorite=MAX(personal_content.favorite,excluded.favorite),rating=COALESCE(personal_content.rating,excluded.rating);
                    INSERT OR IGNORE INTO collection_items(collection_id,content_id,created_at) SELECT collection_id,$target,created_at FROM collection_items WHERE content_id=$source;
                    INSERT INTO content_names(content_id,name,source,search_text) SELECT $target,name,source,search_text FROM content_names WHERE content_id=$source
                    ON CONFLICT(content_id,name) DO UPDATE SET source=CASE WHEN excluded.source='local' THEN 'local' ELSE content_names.source END;
                    UPDATE notifications SET content_id=$target WHERE content_id=$source;
                    UPDATE acquisition_links SET content_id=$target WHERE content_id=$source;
                    UPDATE content_aliases SET content_id=$target WHERE content_id=$source;
                    INSERT OR IGNORE INTO release_blocklist(content_id,fingerprint,reason,blocked_at) SELECT $target,fingerprint,reason,blocked_at FROM release_blocklist WHERE content_id=$source;
                    INSERT OR IGNORE INTO tracking(content_id,monitor_mode,quality_profile_id,requested_at,next_sync_at,sync_state)
                    SELECT $target,monitor_mode,quality_profile_id,requested_at,$now,'pending' FROM tracking WHERE content_id=$source;
                    UPDATE tracking SET monitor_mode='none',sync_state='idle',revision=revision+1 WHERE content_id=$source;
                    UPDATE content SET merged_into=$target,catalog_revision=catalog_revision+1 WHERE id=$source OR merged_into=$source;
                    """, ("$target", preview.TargetId), ("$source", source), ("$now", now));
                var a = GetContentPresentation(preview.TargetId); var b = GetContentPresentation(source);
                var age = new[] { a.MinimumAge, b.MinimumAge, preview.Target.MinimumAge, preview.Source.MinimumAge }.Where(value => value is not null).Select(value => value!.Value).DefaultIfEmpty(-1).Max();
                Run("""
                    INSERT INTO content_presentation(content_id,title,overview,minimum_age,blocked,tags_json,search_text,updated_at) VALUES($id,$title,$overview,$age,$blocked,$tags,$search,$now)
                    ON CONFLICT(content_id) DO UPDATE SET title=$title,overview=$overview,minimum_age=$age,blocked=$blocked,tags_json=$tags,search_text=$search,updated_at=$now
                    """, ("$id", preview.TargetId), ("$title", a.Title ?? b.Title), ("$overview", a.Overview ?? b.Overview), ("$age", age < 0 ? null : age), ("$blocked", a.Blocked || b.Blocked),
                    ("$tags", JsonSerializer.Serialize((a.Tags ?? []).Union(b.Tags ?? []).ToArray(), WebJson)), ("$search", MediaPaths.SearchKey((a.Title ?? b.Title ?? "") + " " + string.Join(' ', (a.Tags ?? []).Union(b.Tags ?? [])))), ("$now", now));
            }
            var sourceId = preview.SourceId ?? preview.TargetId;
            if (preview.SourceId is null && candidate.MinimumAge is { } incomingAge)
            {
                var presentation = GetContentPresentation(preview.TargetId); var age = Math.Max(incomingAge, Math.Max(presentation.MinimumAge ?? 0, preview.Target.MinimumAge ?? 0));
                Run("INSERT INTO content_presentation(content_id,minimum_age,blocked,tags_json,search_text,updated_at) VALUES($id,$age,0,'[]','',$now) ON CONFLICT(content_id) DO UPDATE SET minimum_age=MAX(COALESCE(content_presentation.minimum_age,0),$age)", ("$id", preview.TargetId), ("$age", age), ("$now", now));
            }
            Run("""
                UPDATE release_results SET catalog_invalidated=1 WHERE content_id IN ($source,$target) AND download_id IS NULL;
                UPDATE release_results SET content_id=$target WHERE content_id=$source;
                UPDATE automation_jobs SET status='cancelled',message='Katalog kimliği değişti.',updated_at=$now WHERE content_id IN ($source,$target) AND status IN ('queued','waiting','running');
                UPDATE automation_jobs SET content_id=$target WHERE content_id=$source;
                UPDATE automation_rules SET enabled=0,auto_grab=0,settings_json=json_set(settings_json,'$.enabled',json('false'),'$.autoGrab',json('false')),revision=revision+1 WHERE content_id IN ($source,$target);
                UPDATE tracking SET revision=revision+1,next_sync_at=$now,sync_state='pending' WHERE content_id=$target;
                UPDATE content SET imdb_id=COALESCE(imdb_id,$imdb),tvdb_id=COALESCE(tvdb_id,$tvdb),catalog_revision=catalog_revision+1 WHERE id=$target;
                """, ("$source", sourceId), ("$target", preview.TargetId), ("$now", now), ("$imdb", candidate.ImdbId), ("$tvdb", candidate.TvdbId));
            WriteCatalogAlias(db, tx, preview.TargetId, candidate); tx.Commit();
            ReconcileRequests(preview.TargetId); return GetContent(preview.TargetId)!;
        }
    }
    private static void WriteCatalogAlias(SqliteConnection db, SqliteTransaction tx, string id, ProviderContent metadata)
    {
        using var write = Command(db, "INSERT INTO content_aliases(provider,kind,provider_id,content_id,metadata_json,search_text,linked_at) VALUES($provider,$kind,$external,$id,$json,$search,$now) ON CONFLICT(provider,kind,provider_id) DO UPDATE SET content_id=$id,metadata_json=$json,search_text=$search",
            ("$provider", metadata.Provider), ("$kind", metadata.Kind), ("$external", metadata.ProviderId), ("$id", id), ("$json", JsonSerializer.Serialize(metadata with { Aliases = null }, WebJson)), ("$search", MediaPaths.SearchKey(metadata.Title + " " + metadata.OriginalTitle)), ("$now", Now())); write.Transaction = tx; write.ExecuteNonQuery();
    }
}
