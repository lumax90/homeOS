using System.Security.Cryptography;
using System.Text.Json;
using HomeServer.Core;

namespace HomeServer.Infrastructure;

public sealed partial class MediaStore
{
    private sealed record EpisodePlan(string Id, string ContentId, int Revision, string ActorId, ProviderContent Metadata, ProviderEpisode[] Episodes, string ExpiresAt, string? AppliedAt, string? Result);
    private EpisodePlan ReadEpisodePlan(string id)
    {
        _ = AppFiles.Id(id); using var db = Open(); return Query(db, "SELECT id,content_id,catalog_revision,actor_id,metadata_json,episodes_json,expires_at,applied_at,result_json FROM episode_refresh_plans WHERE id=$id",
            r => new EpisodePlan(r.GetString(0), r.GetString(1), r.GetInt32(2), r.GetString(3), JsonSerializer.Deserialize<ProviderContent>(r.GetString(4), WebJson)!, JsonSerializer.Deserialize<ProviderEpisode[]>(r.GetString(5), WebJson)!, r.GetString(6), Text(r, 7), Text(r, 8)), ("$id", id)).FirstOrDefault() ?? throw new DomainException("Bölüm yenileme planı bulunamadı.", 404);
    }
    public EpisodeRefreshPreview CreateEpisodeRefreshPlan(string id, int revision, ProviderContent metadata, IReadOnlyList<ProviderEpisode> episodes, Account actor)
    {
        if (episodes.Count > 20000 || episodes.Sum(episode => (long)(episode.Title?.Length ?? 0) + (episode.Overview?.Length ?? 0)) > 8 * 1024 * 1024) throw new DomainException("Bölüm yenileme listesi sınırı aşıyor.");
        foreach (var episode in episodes) ValidateProviderEpisode(episode);
        if (episodes.Select(episode => episode.ProviderId).Distinct().Count() != episodes.Count || episodes.Select(episode => (episode.SeasonNumber, episode.EpisodeNumber)).Distinct().Count() != episodes.Count) throw new DomainException("Sağlayıcı listesinde tekrarlı kimlik/numara var.", 502);
        lock (_writeGate)
        {
            RequireEpisodeAdministrator(actor); var content = GetContent(id, actor) ?? throw new DomainException("Dizi bulunamadı.", 404);
            if (content.Id != id || content.CatalogRevision != revision || content.Metadata.Kind != "show" || metadata.Kind != "show" || metadata.Provider != content.Metadata.Provider || metadata.ProviderId != content.Metadata.ProviderId) throw new DomainException("Bölüm yenileme sırasında katalog kimliği değişti.", 409);
            using var db = Open(); Execute(db, "DELETE FROM episode_refresh_plans WHERE expires_at<$old OR id IN (SELECT id FROM episode_refresh_plans WHERE applied_at IS NOT NULL OR expires_at<$now ORDER BY created_at DESC LIMIT -1 OFFSET 256)", ("$old", DateTimeOffset.UtcNow.AddDays(-7).ToString("O")), ("$now", Now()));
            if (Convert.ToInt32(Scalar(db, "SELECT COUNT(*) FROM episode_refresh_plans WHERE applied_at IS NULL AND expires_at>$now", ("$now", Now()))) >= 64) throw new DomainException("Bölüm inceleme planı sınırı dolu.", 429);
            var plan = new EpisodePlan(Guid.NewGuid().ToString("N"), id, revision, actor.Id, metadata, episodes.ToArray(), DateTimeOffset.UtcNow.AddMinutes(30).ToString("O"), null, null);
            Execute(db, "INSERT INTO episode_refresh_plans(id,content_id,catalog_revision,actor_id,metadata_json,episodes_json,created_at,expires_at) VALUES($id,$content,$revision,$actor,$metadata,$episodes,$now,$expires)",
                ("$id", plan.Id), ("$content", id), ("$revision", revision), ("$actor", actor.Id), ("$metadata", JsonSerializer.Serialize(metadata, WebJson)), ("$episodes", JsonSerializer.Serialize(episodes, WebJson)), ("$now", Now()), ("$expires", plan.ExpiresAt));
            return PreviewEpisodeRefresh(plan.Id, actor);
        }
    }
    private void RequireEpisodeAdministrator(Account actor)
    { if (GetAccount(actor.Id) is not { Enabled: true, IsAdmin: true }) throw new DomainException("Bölüm kataloğu yönetici yetkisi gerektirir.", 403); }
    public EpisodeRefreshPreview PreviewEpisodeRefresh(string planId, Account actor, EpisodeRepairChoice[]? choices = null)
    {
        lock (_writeGate)
        {
            RequireEpisodeAdministrator(actor); var plan = ReadEpisodePlan(planId);
            if (plan.ActorId != actor.Id) throw new DomainException("Bölüm planı bu hesaba ait değil.", 404);
            if (DateTimeOffset.Parse(plan.ExpiresAt) <= DateTimeOffset.UtcNow) throw new DomainException("Bölüm planı eskidi; yeniden incele.", 410);
            var content = GetContent(plan.ContentId, actor) ?? throw new DomainException("Dizi bulunamadı.", 404);
            var conflicts = new List<string>(); var notes = new List<string>(); var old = GetEpisodeCatalog(content.Id, true);
            if (content.Id != plan.ContentId || content.CatalogRevision != plan.Revision || plan.AppliedAt is not null) conflicts.Add("Katalog revizyonu değişti veya plan uygulandı; yeniden incele.");
            if (GetCatalogAliases(content.Id).Count > 1 && (content.Metadata.ImdbId is { } imdb && plan.Metadata.ImdbId is { } incomingImdb && imdb != incomingImdb || content.Metadata.TvdbId is { } tvdb && plan.Metadata.TvdbId is { } incomingTvdb && tvdb != incomingTvdb)) conflicts.Add("Bağlı sağlayıcıların ortak dış kimliği değişti; yapım eşlemesini incele.");
            if (plan.Episodes.Length == 0 && old.Count > 0) conflicts.Add("Sağlayıcı listesi boş; mevcut bölümler topluca arşivlenmez.");
            if (choices?.Length > 20000 || choices?.Select(choice => choice.ProviderId).Distinct().Count() != choices?.Length || choices?.Any(choice => !plan.Episodes.Any(episode => episode.ProviderId == choice.ProviderId) || choice.ExistingEpisodeId is { } target && !old.Any(row => row.Id == target)) == true) throw new DomainException("Bölüm onarım seçimleri geçersiz.");
            var selected = (choices ?? []).ToDictionary(choice => choice.ProviderId); var incomingIds = plan.Episodes.Select(episode => episode.ProviderId).ToHashSet();
            var decisions = new List<EpisodeRefreshChange>(); using var db = Open();
            foreach (var episode in plan.Episodes)
            {
                var parent = Scalar(db, "SELECT content_id FROM episode_aliases WHERE provider=$provider AND provider_id=$id", ("$provider", content.Metadata.Provider), ("$id", episode.ProviderId)) as string;
                if (parent is not null && parent != content.Id) conflicts.Add("Bir sağlayıcı bölüm kimliği başka bir diziye bağlı.");
                var known = old.FirstOrDefault(row => row.Metadata.ProviderId == episode.ProviderId);
                var atPosition = old.FirstOrDefault(row => !row.Retired && row.Metadata.SeasonNumber == episode.SeasonNumber && row.Metadata.EpisodeNumber == episode.EpisodeNumber);
                var hasChoice = selected.TryGetValue(episode.ProviderId, out var choice);
                if (known is not null && hasChoice && choice!.ExistingEpisodeId != known.Id) conflicts.Add("Bilinen sağlayıcı bölümünün kalıcı kimliği başka bir bölüme taşınamaz.");
                var matched = known ?? (hasChoice && choice!.ExistingEpisodeId is { } selectedId ? old.Single(row => row.Id == selectedId) : null);
                var ambiguous = known is null && !hasChoice && atPosition is not null && !incomingIds.Contains(atPosition.Metadata.ProviderId);
                var action = ambiguous ? "identity-conflict" : matched is null ? "new" : matched.Metadata.ProviderId != episode.ProviderId ? "replace-id" : matched.Retired ? "restore"
                    : matched.Metadata.SeasonNumber != episode.SeasonNumber || matched.Metadata.EpisodeNumber != episode.EpisodeNumber ? "renumber" : "metadata";
                decisions.Add(new(episode, matched?.Id, matched?.Metadata.SeasonNumber ?? atPosition?.Metadata.SeasonNumber, matched?.Metadata.EpisodeNumber ?? atPosition?.Metadata.EpisodeNumber, action, ambiguous));
            }
            if (decisions.Any(change => change.NeedsChoice)) conflicts.Add("Numarası aynı, sağlayıcı kimliği farklı bölümler için eski kimliği koruma veya yeni bölüm seçimi gerekli.");
            if (decisions.Where(change => change.ExistingId is not null).GroupBy(change => change.ExistingId).Any(group => group.Count() > 1)) conflicts.Add("İki sağlayıcı bölümü tek kalıcı kimliğe atanamaz.");
            var retained = decisions.Select(change => change.ExistingId).Where(id => id is not null).ToHashSet(); var retired = old.Where(row => !row.Retired && !retained.Contains(row.Id)).ToArray();
            if ((retired.Length > 0 || decisions.Any(change => change.Action is "renumber" or "replace-id")) && Scalar(db, "SELECT d.id FROM downloads d JOIN acquisition_links a ON a.download_id=d.id WHERE a.content_id=$id AND (d.imported_at IS NULL AND d.desired_state<>'cancelled' OR d.imported_at IS NOT NULL AND a.status<>'linked') LIMIT 1", ("$id", content.Id)) is string)
                conflicts.Add("Bu dizi için transfer/import sürüyor; kimlik onarımından önce tamamlanmasını veya iptalini bekle.");
            if (retired.Length > 0) notes.Add($"Sağlayıcıda görünmeyen {retired.Length} bölüm arşivlenir; dosyaları ve geçmişi korunur.");
            notes.Add("İstekler kalıcı bölüm kimliklerine göre taşınır. Sezonlar bölünürse doğrulanmış bölümler açık kapsam olarak saklanır.");
            notes.Add("Bu dizinin otomasyonu yeniden etkinleştirilmek üzere kapatılır; eski arama/oynatma komutları geçersiz olur.");
            return new(plan.Id, content.Id, plan.Revision, plan.ExpiresAt, decisions.ToArray(), retired, conflicts.Distinct().ToArray(), notes.ToArray(), conflicts.Count == 0);
        }
    }
    public ContentEntry ApplyEpisodeRefresh(EpisodeRepairInput input, Account actor)
    {
        lock (_writeGate)
        {
            RequireEpisodeAdministrator(actor); var plan = ReadEpisodePlan(input.PlanId);
            if (plan.ActorId != actor.Id) throw new DomainException("Bölüm planı bu hesaba ait değil.", 404);
            var hash = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(input, WebJson)));
            if (plan.AppliedAt is not null)
            {
                using var receipt = JsonDocument.Parse(plan.Result!);
                if (receipt.RootElement.GetProperty("hash").GetString() != hash) throw new DomainException("Uygulanan plan farklı seçimlerle yeniden kullanılamaz.", 409);
                return GetContent(plan.ContentId, actor)!;
            }
            var preview = PreviewEpisodeRefresh(plan.Id, actor, input.Choices);
            if (!preview.CanApply || input.ExpectedRevision != plan.Revision) throw new DomainException(string.Join(" ", preview.Conflicts.Append("Önizleme ve revizyonu kontrol et.")), 409);
            using var db = Open(); using var tx = db.BeginTransaction(); var requests = CaptureEpisodeRequestScopes(db, tx, plan.ContentId);
            EpisodeSql(db, tx, "UPDATE episodes SET retired=1 WHERE content_id=$id", ("$id", plan.ContentId));
            foreach (var change in preview.Changes)
            {
                var id = change.ExistingId ?? Guid.NewGuid().ToString("N"); var episode = change.Incoming;
                if (change.ExistingId is not null) EpisodeSql(db, tx, "UPDATE episode_aliases SET retired=1 WHERE content_id=$content AND provider=$provider AND episode_id=$id AND provider_id<>$external", ("$content", plan.ContentId), ("$provider", plan.Metadata.Provider), ("$id", id), ("$external", episode.ProviderId));
                EpisodeSql(db, tx, """
                    INSERT INTO episodes(id,content_id,provider_id,season_number,episode_number,title,overview,air_date,still_url,runtime_minutes,retired)
                    VALUES($id,$content,$external,$season,$episode,$title,$overview,$date,$still,$runtime,0)
                    ON CONFLICT(id) DO UPDATE SET provider_id=$external,season_number=$season,episode_number=$episode,title=$title,overview=$overview,air_date=$date,still_url=$still,runtime_minutes=$runtime,retired=0
                    """, ("$id", id), ("$content", plan.ContentId), ("$external", episode.ProviderId), ("$season", episode.SeasonNumber), ("$episode", episode.EpisodeNumber), ("$title", episode.Title), ("$overview", episode.Overview), ("$date", episode.AirDate), ("$still", episode.StillUrl), ("$runtime", episode.RuntimeMinutes));
                WriteEpisodeAlias(db, tx, plan.Metadata.Provider, episode.ProviderId, plan.ContentId, [(id, null, null)]);
            }
            EpisodeSql(db, tx, "UPDATE episode_aliases SET retired=1 WHERE provider=$provider AND content_id=$content AND episode_id IN (SELECT id FROM episodes WHERE content_id=$content AND retired=1)", ("$provider", plan.Metadata.Provider), ("$content", plan.ContentId));
            RebaseEpisodeRequestScopes(db, tx, requests, plan.ContentId, new Dictionary<string, string[]>(), actor.Id);
            _ = UpsertContent(db, tx, plan.Metadata); InvalidateEpisodeCatalog(db, tx, plan.ContentId, true);
            EpisodeSql(db, tx, "UPDATE episode_refresh_plans SET applied_at=$now,result_json=$result WHERE id=$id", ("$id", plan.Id), ("$now", Now()), ("$result", JsonSerializer.Serialize(new { hash, preview }, WebJson)));
            tx.Commit(); ReconcileRequests(plan.ContentId); return GetContent(plan.ContentId, actor)!;
        }
    }
}
