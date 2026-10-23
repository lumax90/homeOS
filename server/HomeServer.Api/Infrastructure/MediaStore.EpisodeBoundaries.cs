using System.Text.Json;
using HomeServer.Core;
using HomeServer.Metadata;

namespace HomeServer.Infrastructure;

public sealed record EpisodeBoundaryWork(EpisodeBoundaryJob Job, string UserId, string Lease, BoundaryEpisode[] Episodes);
public sealed partial class MediaStore
{
    private const string BoundarySelect = "SELECT id,media_id,fingerprint,match_revision,revision,status,progress,message,created_at,result_json FROM episode_boundary_jobs";
    private static EpisodeBoundaryJob ReadBoundary(Microsoft.Data.Sqlite.SqliteDataReader r) => new(r.GetString(0), r.GetString(1), r.GetString(2), r.GetInt32(3), r.GetInt32(4), r.GetString(5), r.GetInt32(6), r.GetString(7), r.GetString(8), r.IsDBNull(9) ? null : JsonSerializer.Deserialize<EpisodeBoundaryResult>(r.GetString(9), WebJson));
    public EpisodeBoundaryJob? GetEpisodeBoundaryJob(string id) { using var db = Open(); return Query(db, BoundarySelect + " WHERE id=$id", ReadBoundary, ("$id", id)).FirstOrDefault(); }
    public IReadOnlyList<EpisodeBoundaryJob> GetEpisodeBoundaryJobs(string mediaId) { using var db = Open(); return Query(db, BoundarySelect + " WHERE media_id=$id ORDER BY created_at DESC LIMIT 5", ReadBoundary, ("$id", mediaId)); }
    public EpisodeBoundaryJob QueueEpisodeBoundary(AnalyzedMedia inspected, int revision, Account actor)
    {
        lock (_writeGate)
        {
            RequireEpisodeAdministrator(actor); var media = GetMedia(inspected.Media.Id, actor) ?? throw new DomainException("Dosya bulunamadı.", 404);
            if (media.MatchRevision != revision || media.Content?.Kind != "show" || media.Episodes is not { Length: >= 2 and <= 20 } || inspected.Probe.Duration is not > 0 or > 28800) throw new DomainException("Süresi okunmuş, 2–20 bölüme eşlenmiş dosya gerekli.", 409);
            if (PlaybackSource(media.Id, actor).Fingerprint != inspected.Fingerprint) throw new DomainException("Dosya analiz sırasında değişti.", 409);
            using var db = Open(); var existing = Query(db, BoundarySelect + " WHERE media_id=$id AND status IN ('queued','running')", ReadBoundary, ("$id", media.Id)).FirstOrDefault();
            if (existing is not null) return existing.MatchRevision == revision && existing.Fingerprint == inspected.Fingerprint ? existing : throw new DomainException("Önceki bölüm sınırı işini iptal et.", 409);
            Execute(db, "DELETE FROM episode_boundary_jobs WHERE status NOT IN ('queued','running') AND updated_at<$old", ("$old", DateTimeOffset.UtcNow.AddDays(-7).ToString("O")));
            if (Convert.ToInt32(Scalar(db, "SELECT COUNT(*) FROM episode_boundary_jobs WHERE status IN ('queued','running')")) >= 32 || Convert.ToInt32(Scalar(db, "SELECT COUNT(*) FROM episode_boundary_jobs")) >= 2000) throw new DomainException("Bölüm sınırı analiz kuyruğu dolu.", 429);
            var catalog = GetEpisodeCatalog(media.Content.Id, true).ToDictionary(row => row.Id); var episodes = media.Episodes.Select(episode => new BoundaryEpisode(episode.EpisodeId, episode.Season, episode.Number, episode.Title, catalog.GetValueOrDefault(episode.EpisodeId)?.Metadata.RuntimeMinutes * 60d)).ToArray();
            if (media.DurationSeconds is null) SaveProbe(media.Id, inspected.Probe, media.HasThumbnail);
            var id = Guid.NewGuid().ToString("N"); Execute(db, "INSERT INTO episode_boundary_jobs(id,media_id,user_id,fingerprint,match_revision,episodes_json,status,message,created_at,updated_at) VALUES($id,$media,$user,$fp,$revision,$episodes,'queued','Bölüm sınırı analizi sırada.',$now,$now)",
                ("$id", id), ("$media", media.Id), ("$user", actor.Id), ("$fp", inspected.Fingerprint), ("$revision", revision), ("$episodes", JsonSerializer.Serialize(episodes, WebJson)), ("$now", Now())); return GetEpisodeBoundaryJob(id)!;
        }
    }
    public EpisodeBoundaryWork? ClaimEpisodeBoundary()
    {
        lock (_writeGate)
        {
            if (ContainerUpdateBlocked) return null;
            using var db = Open(); var row = Query(db, "SELECT id,user_id,episodes_json FROM episode_boundary_jobs WHERE status='queued' ORDER BY created_at LIMIT 1", r => (Id: r.GetString(0), User: r.GetString(1), Episodes: JsonSerializer.Deserialize<BoundaryEpisode[]>(r.GetString(2), WebJson)!)).FirstOrDefault();
            if (row.Id is null) return null; var lease = Guid.NewGuid().ToString("N");
            Execute(db, "UPDATE episode_boundary_jobs SET status='running',lease=$lease,revision=revision+1,progress=5,updated_at=$now,message='Dosyadaki sahne ve ses sınırları inceleniyor.' WHERE id=$id", ("$id", row.Id), ("$lease", lease), ("$now", Now()));
            return new(GetEpisodeBoundaryJob(row.Id)!, row.User, lease, row.Episodes);
        }
    }
    public bool EpisodeBoundaryCurrent(EpisodeBoundaryWork work)
    {
        var job = GetEpisodeBoundaryJob(work.Job.Id); var actor = GetAccount(work.UserId);
        if (job is not { Status: "running" } || job.Revision != work.Job.Revision || actor is not { Enabled: true, IsAdmin: true }) return false;
        using var db = Open(); if (Scalar(db, "SELECT lease FROM episode_boundary_jobs WHERE id=$id", ("$id", job.Id)) as string != work.Lease) return false;
        var media = GetMedia(job.MediaId, actor); return media is not null && media.MatchRevision == job.MatchRevision && (media.Episodes ?? []).Select(episode => episode.EpisodeId).SequenceEqual(work.Episodes.Select(episode => episode.Id));
    }
    public void UpdateEpisodeBoundaryProgress(EpisodeBoundaryWork work, int progress)
    { lock (_writeGate) { using var db = Open(); Execute(db, "UPDATE episode_boundary_jobs SET progress=$progress WHERE id=$id AND revision=$revision AND lease=$lease AND status='running'", ("$id", work.Job.Id), ("$revision", work.Job.Revision), ("$lease", work.Lease), ("$progress", Math.Clamp(progress, 5, 95))); } }
    public void FinishEpisodeBoundary(EpisodeBoundaryWork work, EpisodeBoundaryResult? result, string? error = null)
    {
        lock (_writeGate)
        {
            var current = EpisodeBoundaryCurrent(work); using var db = Open();
            Execute(db, "UPDATE episode_boundary_jobs SET status=$status,result_json=$result,progress=$progress,message=$message,lease=NULL,updated_at=$now WHERE id=$id AND revision=$revision AND lease=$lease AND status='running'",
                ("$id", work.Job.Id), ("$revision", work.Job.Revision), ("$lease", work.Lease), ("$status", !current ? "cancelled" : error is null ? "completed" : "failed"),
                ("$result", current && error is null ? JsonSerializer.Serialize(result, WebJson) : null), ("$progress", error is null && current ? 100 : 0),
                ("$message", !current ? "Dosya, eşleme veya hesap değişti." : error ?? (result?.CanApply == true ? "Bölüm sınırları bulundu; uygulamadan önce incele." : "Güvenilir sınır bulunamadı; elle zaman eşlemesi gerekli.")), ("$now", Now()));
        }
    }
    public void CancelEpisodeBoundary(string id, int revision, Account actor)
    {
        lock (_writeGate)
        {
            RequireEpisodeAdministrator(actor); var job = GetEpisodeBoundaryJob(id) ?? throw new DomainException("Bölüm analizi bulunamadı.", 404);
            if (job.Revision != revision) throw new DomainException("Bölüm analizi değişti.", 409);
            using var db = Open(); Execute(db, "UPDATE episode_boundary_jobs SET status='cancelled',revision=revision+1,lease=NULL,updated_at=$now,message='Bölüm analizi iptal edildi.' WHERE id=$id AND status IN ('queued','running')", ("$id", id), ("$now", Now()));
        }
    }
    public MediaItem ApplyEpisodeBoundary(string id, int revision, Account actor)
    {
        lock (_writeGate)
        {
            RequireEpisodeAdministrator(actor); var job = GetEpisodeBoundaryJob(id) ?? throw new DomainException("Bölüm analizi bulunamadı.", 404);
            var source = PlaybackSource(job.MediaId, actor); var media = source.Media;
            if (job.Revision != revision || job.Status != "completed" || job.Result is not { CanApply: true } || source.Fingerprint != job.Fingerprint || media.MatchRevision != job.MatchRevision || media.DurationSeconds is not > 0
                || !(media.Episodes ?? []).Select(episode => episode.EpisodeId).SequenceEqual(job.Result.Segments.Select(segment => segment.EpisodeId))) throw new DomainException("Dosya veya sınır önerisi değişti; yeni analiz gerekli.", 409);
            double end = 0;
            foreach (var segment in job.Result.Segments)
            {
                if (!double.IsFinite(segment.StartSeconds) || !double.IsFinite(segment.EndSeconds) || Math.Abs(segment.StartSeconds - end) > .01 || segment.EndSeconds <= segment.StartSeconds || segment.EndSeconds > media.DurationSeconds + .1) throw new DomainException("Bölüm aralıkları geçersiz.", 409);
                end = segment.EndSeconds;
            }
            using var db = Open(); using var tx = db.BeginTransaction();
            foreach (var segment in job.Result.Segments) EpisodeSql(db, tx, "UPDATE media_episode_segments SET start_seconds=$start,end_seconds=$end WHERE media_id=$media AND episode_id=$episode", ("$media", media.Id), ("$episode", segment.EpisodeId), ("$start", segment.StartSeconds), ("$end", segment.EndSeconds));
            EpisodeSql(db, tx, "UPDATE media_matches SET revision=revision+1 WHERE media_id=$media; UPDATE episode_boundary_jobs SET status='applied',revision=revision+1,message='Bölüm zamanları uygulandı.',updated_at=$now WHERE id=$id", ("$media", media.Id), ("$id", job.Id), ("$now", Now()));
            foreach (var state in EpisodeRows(db, tx, "SELECT user_id,position_seconds,watched,updated_at,event_client,event_sequence FROM user_watch_state WHERE media_id=$media", r => (User: r.GetString(0), Position: r.GetDouble(1), Watched: r.GetBoolean(2), At: r.GetString(3), Client: Text(r, 4), Sequence: r.IsDBNull(5) ? (long?)null : r.GetInt64(5)), ("$media", media.Id)))
                foreach (var segment in job.Result.Segments.Where(segment => state.Position >= segment.StartSeconds))
                {
                    var position = Math.Clamp(state.Position - segment.StartSeconds, 0, segment.EndSeconds - segment.StartSeconds);
                    EpisodeSql(db, tx, """
                        INSERT INTO content_watch_state(user_id,content_id,episode_key,position_seconds,watched,updated_at,event_client,event_sequence,source_media_id)
                        VALUES($user,$content,$episode,$position,$watched,$at,$client,$sequence,$media)
                        ON CONFLICT(user_id,content_id,episode_key) DO UPDATE SET position_seconds=$position,watched=$watched,updated_at=$at,event_client=$client,event_sequence=$sequence,source_media_id=$media
                        WHERE content_watch_state.updated_at<$at OR content_watch_state.source_media_id=$media
                        """, ("$user", state.User), ("$content", media.Content!.Id), ("$episode", segment.EpisodeId), ("$position", position), ("$watched", position >= (segment.EndSeconds - segment.StartSeconds) * .95), ("$at", state.At), ("$client", state.Client), ("$sequence", state.Sequence), ("$media", media.Id));
                }
            tx.Commit(); ReconcileRequests(media.Content!.Id); return GetMedia(media.Id, actor)!;
        }
    }
    public void RecoverEpisodeBoundaries()
    { lock (_writeGate) { using var db = Open(); Execute(db, "UPDATE episode_boundary_jobs SET status='queued',revision=revision+1,lease=NULL,progress=0,message='Kesilen bölüm analizi yeniden başlayacak.' WHERE status='running'"); } }
}
