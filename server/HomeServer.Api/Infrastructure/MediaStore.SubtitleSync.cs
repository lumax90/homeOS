using System.Text.Json;
using HomeServer.Core;
using Microsoft.Data.Sqlite;

namespace HomeServer.Infrastructure;

public sealed partial class MediaStore
{
    private const string SyncSelect = "SELECT id,media_id,track_id,media_fingerprint,subtitle_fingerprint,match_revision,audio_stream,revision,status,progress,message,created_at,updated_at,input_json,result_json,subtitle_id FROM subtitle_sync_jobs";
    private static SubtitleSyncJob ReadSync(SqliteDataReader r) => new(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetInt32(5), r.GetInt32(6), r.GetInt32(7), r.GetString(8), r.GetInt32(9), r.GetString(10), r.GetString(11), r.GetString(12),
        JsonSerializer.Deserialize<SubtitleSyncInput>(r.GetString(13), WebJson)!, r.IsDBNull(14) ? null : JsonSerializer.Deserialize<SubtitleAlignment>(r.GetString(14), WebJson), Text(r, 15));
    public SubtitleSyncJob? GetSubtitleSync(string id) { using var db = Open(); return Query(db, SyncSelect + " WHERE id=$id", ReadSync, ("$id", id)).FirstOrDefault(); }
    public IReadOnlyList<SubtitleSyncJob> GetSubtitleSyncJobs(string? mediaId = null)
    {
        using var db = Open(); return Query(db, SyncSelect + " WHERE ($media IS NULL OR media_id=$media) ORDER BY created_at DESC LIMIT 100", ReadSync, ("$media", mediaId));
    }
    public SubtitleSyncJob QueueSubtitleSync(AnalyzedMedia media, SubtitleSourceInfo source, int audio, SubtitleSyncInput input)
    {
        lock (_writeGate)
        {
            if (GetMedia(media.Media.Id)?.MatchRevision != media.Media.MatchRevision || Playback.MediaAnalysisService.Fingerprint(new FileInfo(media.Path)) != media.Fingerprint) throw new DomainException("Senkron dosyası sıraya alınırken değişti.", 409);
            var json = JsonSerializer.Serialize(input, WebJson); using var db = Open();
            var existing = Query(db, SyncSelect + " WHERE media_id=$media AND status IN ('queued','running','proposed','applying')", ReadSync, ("$media", media.Media.Id)).FirstOrDefault();
            if (existing is not null)
                return existing.Input == input && existing.Fingerprint == media.Fingerprint && existing.SubtitleFingerprint == source.Fingerprint && existing.MatchRevision == media.Media.MatchRevision && existing.AudioStream == audio ? existing : throw new DomainException("Bu medya için başka bir senkron işi var; önce iptal et veya uygula.", 409);
            if (Convert.ToInt32(Scalar(db, "SELECT COUNT(*) FROM subtitle_sync_jobs WHERE status IN ('queued','running','proposed','applying')")) >= 100) throw new DomainException("Altyazı senkron kuyruğu dolu.", 429);
            var id = Guid.NewGuid().ToString("N"); Execute(db, """
                INSERT INTO subtitle_sync_jobs(id,media_id,track_id,media_fingerprint,subtitle_fingerprint,match_revision,audio_stream,status,input_json,created_at,updated_at)
                VALUES($id,$media,$track,$fp,$sub,$revision,$audio,'queued',$json,$now,$now)
                """, ("$id", id), ("$media", media.Media.Id), ("$track", source.Track.Id), ("$fp", media.Fingerprint), ("$sub", source.Fingerprint), ("$revision", media.Media.MatchRevision), ("$audio", audio), ("$json", json), ("$now", Now()));
            return GetSubtitleSync(id)!;
        }
    }
    public SubtitleSyncJob? ClaimSubtitleSync()
    {
        lock (_writeGate)
        {
            using var db = Open(); var id = Scalar(db, "SELECT id FROM subtitle_sync_jobs WHERE status='queued' ORDER BY created_at LIMIT 1") as string;
            if (id is null) return null;
            Execute(db, "UPDATE subtitle_sync_jobs SET status='running',revision=revision+1,progress=0,message='Konuşma etkinliği ölçülüyor.',updated_at=$now WHERE id=$id", ("$id", id), ("$now", Now())); return GetSubtitleSync(id);
        }
    }
    public void SubtitleSyncProgress(SubtitleSyncJob job, int progress)
    {
        lock (_writeGate) { using var db = Open(); Execute(db, "UPDATE subtitle_sync_jobs SET progress=$progress,updated_at=$now WHERE id=$id AND revision=$revision AND status='running'", ("$id", job.Id), ("$revision", job.Revision), ("$progress", Math.Clamp(progress, 0, 99)), ("$now", Now())); }
    }
    public void ProposeSubtitleSync(SubtitleSyncJob job, SubtitleAlignment alignment)
    {
        lock (_writeGate) { using var db = Open(); Execute(db, "UPDATE subtitle_sync_jobs SET result_json=$json,status='proposed',progress=100,message=$message,updated_at=$now WHERE id=$id AND revision=$revision AND status='running'", ("$id", job.Id), ("$revision", job.Revision), ("$json", JsonSerializer.Serialize(alignment, WebJson)), ("$message", alignment.Reason), ("$now", Now())); }
    }
    public SubtitleSyncJob ClaimSubtitleSyncApply(string id, int revision)
    {
        lock (_writeGate)
        {
            var job = GetSubtitleSync(id) ?? throw new DomainException("Senkron işi bulunamadı.", 404);
            if (job.Status == "completed") return job;
            if (job.Revision != revision || job.Status != "proposed" || job.Result?.CanApply != true) throw new DomainException("Senkron önerisi değişti veya yeterli güven eşiğini geçmedi.", 409);
            using var db = Open(); Execute(db, "UPDATE subtitle_sync_jobs SET status='applying',revision=revision+1,updated_at=$now WHERE id=$id", ("$id", id), ("$now", Now())); return GetSubtitleSync(id)!;
        }
    }
    public void CompleteSubtitleSync(SubtitleSyncJob job, string subtitleId)
    {
        lock (_writeGate) { using var db = Open(); Execute(db, "UPDATE subtitle_sync_jobs SET status='completed',progress=100,subtitle_id=$subtitle,message='Senkronlanmış WebVTT sürümü yayımlandı.',updated_at=$now WHERE id=$id AND revision=$revision AND status='applying'", ("$id", job.Id), ("$revision", job.Revision), ("$subtitle", subtitleId), ("$now", Now())); }
    }
    public void FailSubtitleSync(SubtitleSyncJob job, string message)
    {
        lock (_writeGate) { using var db = Open(); Execute(db, "UPDATE subtitle_sync_jobs SET status='failed',message=$message,updated_at=$now WHERE id=$id AND revision=$revision AND status IN ('running','applying')", ("$id", job.Id), ("$revision", job.Revision), ("$message", message), ("$now", Now())); }
    }
    public void CancelSubtitleSync(string id)
    {
        lock (_writeGate) { using var db = Open(); Execute(db, "UPDATE subtitle_sync_jobs SET status='cancelled',revision=revision+1,message='Senkron işi iptal edildi.',updated_at=$now WHERE id=$id AND status IN ('queued','running','proposed','applying')", ("$id", id), ("$now", Now())); }
    }
    public void RecoverSubtitleSync()
    {
        lock (_writeGate)
        {
            using var db = Open(); Execute(db, """
                UPDATE subtitle_sync_jobs SET status='queued',revision=revision+1,message='Kesilen ses analizi yeniden başlayacak.' WHERE status='running';
                UPDATE subtitle_sync_jobs SET status=CASE WHEN EXISTS(SELECT 1 FROM managed_subtitles s WHERE s.sync_job_id=subtitle_sync_jobs.id) THEN 'completed' ELSE 'proposed' END,
                  subtitle_id=(SELECT id FROM managed_subtitles s WHERE s.sync_job_id=subtitle_sync_jobs.id LIMIT 1),revision=revision+1,message='Kesilen senkron yayını toparlandı.' WHERE status='applying';
                """);
        }
    }
}
