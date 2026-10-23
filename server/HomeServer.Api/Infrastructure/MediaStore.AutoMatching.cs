using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HomeServer.Core;

namespace HomeServer.Infrastructure;

public sealed partial class MediaStore
{
    public void QueueAutoMatch(string mediaId, int libraryRevision, bool force = false)
    {
        lock (_writeGate)
        {
            var media = GetMedia(mediaId);
            if (media is null || media.Content is not null || media.Kind != "video") return;
            var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(media.RelativePath + "\n" + JsonSerializer.Serialize(GetLocalMetadata(mediaId), WebJson))));
            using var db = Open(); Execute(db, """
                INSERT INTO auto_match_jobs(media_id,library_id,library_revision,size_bytes,modified_at,metadata_key,status,next_attempt)
                VALUES($id,$library,$revision,$size,$modified,$key,'queued',$now)
                ON CONFLICT(media_id) DO UPDATE SET library_revision=$revision,size_bytes=$size,modified_at=$modified,metadata_key=$key,status='queued',message='',next_attempt=$now
                WHERE $force=1 OR auto_match_jobs.library_revision<>$revision OR auto_match_jobs.size_bytes<>$size OR auto_match_jobs.modified_at<>$modified OR auto_match_jobs.metadata_key<>$key
                """, ("$id", mediaId), ("$library", media.LibraryId), ("$revision", libraryRevision), ("$size", media.SizeBytes), ("$modified", media.ModifiedAt), ("$key", key), ("$now", Now()), ("$force", force));
        }
    }
    private const string MatchJobSelect = "SELECT j.media_id,j.library_id,j.library_revision,j.size_bytes,j.modified_at,j.metadata_key,j.status,j.message,j.next_attempt FROM auto_match_jobs j";
    private static AutoMatchJob ReadMatchJob(Microsoft.Data.Sqlite.SqliteDataReader r) => new(r.GetString(0), r.GetString(1), r.GetInt32(2), r.GetInt64(3), r.GetString(4), r.GetString(5), r.GetString(6), r.GetString(7), r.GetString(8));
    public IReadOnlyList<AutoMatchJob> GetAutoMatchJobs()
    {
        using var db = Open(); return Query(db, MatchJobSelect + " ORDER BY next_attempt DESC LIMIT 100", ReadMatchJob);
    }
    public AutoMatchJob? ClaimAutoMatch()
    {
        lock (_writeGate)
        {
            if (ContainerUpdateBlocked) return null;
            using var db = Open();
            var job = Query(db, MatchJobSelect + " JOIN libraries l ON l.id=j.library_id WHERE j.status='queued' AND j.next_attempt<=$now AND l.enabled=1 AND l.revision=j.library_revision AND json_extract(l.options_json,'$.autoMatch')=1 ORDER BY j.next_attempt LIMIT 1", ReadMatchJob, ("$now", Now())).FirstOrDefault();
            if (job is not null) Execute(db, "UPDATE auto_match_jobs SET status='running' WHERE media_id=$id", ("$id", job.MediaId));
            return job;
        }
    }
    public void RecoverAutoMatches()
    {
        lock (_writeGate) { using var db = Open(); Execute(db, "UPDATE auto_match_jobs SET status='queued' WHERE status='running'"); }
    }
    public void FinishAutoMatch(AutoMatchJob job, string status, string message, DateTimeOffset? retry = null)
    {
        lock (_writeGate) { using var db = Open(); Execute(db, "UPDATE auto_match_jobs SET status=$status,message=$message,next_attempt=$next WHERE media_id=$id AND library_revision=$revision AND metadata_key=$key",
            ("$id", job.MediaId), ("$revision", job.LibraryRevision), ("$key", job.MetadataKey), ("$status", status), ("$message", message), ("$next", (retry ?? DateTimeOffset.UtcNow).ToString("O"))); }
    }
    public bool ApplyAutoMatch(AutoMatchJob job, ProviderContent content, ProviderEpisode? episode, ProviderEpisode[]? episodes = null)
    {
        lock (_writeGate)
        {
            var media = GetMedia(job.MediaId); var config = GetLibraryConfiguration(job.LibraryId);
            using var db = Open();
            var key = Scalar(db, "SELECT metadata_key FROM auto_match_jobs WHERE media_id=$id AND status='running'", ("$id", job.MediaId)) as string;
            if (media is null || !media.Available || media.Content is not null || media.SizeBytes != job.Size || media.ModifiedAt != job.ModifiedAt
                || config.Revision != job.LibraryRevision || !config.Options.AutoMatch || key != job.MetadataKey) return false;
            if (episodes is { Length: > 1 }) MatchMediaEpisodes(job.MediaId, content, episodes.Select(value => (value, (double?)null, (double?)null)).ToArray(), "", media.MatchRevision);
            else MatchMedia(job.MediaId, content, episode, "", media.MatchRevision);
            FinishAutoMatch(job, "matched", "Doğrulanmış sağlayıcı ve bölüm kapsamıyla eşleşti."); return true;
        }
    }
}
