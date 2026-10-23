using System.Text.Json;
using HomeServer.Core;
using HomeServer.Playback;
using HomeServer.Subtitles;

namespace HomeServer.Infrastructure;

public sealed partial class MediaStore
{
    public (string? Value, int Revision) GetSubtitleConnectionSnapshot()
    {
        using var db = Open(); return Query(db, "SELECT (SELECT value FROM settings WHERE key='subtitles.connection'),COALESCE((SELECT CAST(value AS INTEGER) FROM settings WHERE key='subtitles.connection.revision'),0)", r => (Text(r, 0), r.GetInt32(1))).Single();
    }
    public bool SetSubtitleConnection(string value, int? expectedRevision)
    {
        lock (_writeGate)
        {
            if (expectedRevision is { } expected && GetSubtitleConnectionSnapshot().Revision != expected) return false;
            using var db = Open(); using var tx = db.BeginTransaction();
            using var command = Command(db, """
                INSERT INTO settings(key,value) VALUES('subtitles.connection',$value) ON CONFLICT(key) DO UPDATE SET value=$value;
                INSERT INTO settings(key,value) VALUES('subtitles.connection.revision','1') ON CONFLICT(key) DO UPDATE SET value=CAST(value AS INTEGER)+1;
                UPDATE subtitle_wanted SET lease_token=NULL,lease_until=NULL,policy_json=NULL,next_run=$now,status='waiting';
                """, ("$value", value), ("$now", Now())); command.Transaction = tx; command.ExecuteNonQuery(); tx.Commit(); return true;
        }
    }
    public void SetSubtitleQuota(int? remaining, string? reset, int expectedRevision, bool clear = false)
    {
        lock (_writeGate)
        {
            if (GetSubtitleConnectionSnapshot().Revision != expectedRevision) return;
            using var db = Open(); using var tx = db.BeginTransaction();
            foreach (var (key, value) in new[] { ("subtitles.remaining", clear ? "" : remaining?.ToString(System.Globalization.CultureInfo.InvariantCulture)), ("subtitles.reset", clear ? "" : reset), ("subtitles.error", clear ? "" : null) })
            {
                if (value is null) continue;
                using var command = Command(db, "INSERT INTO settings(key,value) VALUES($key,$value) ON CONFLICT(key) DO UPDATE SET value=$value", ("$key", key), ("$value", value)); command.Transaction = tx; command.ExecuteNonQuery();
            }
            tx.Commit();
        }
    }
    public EffectiveSubtitlePolicy GetEffectiveSubtitlePolicy(string mediaId)
    {
        lock (_writeGate)
        {
            if (GetMedia(mediaId)?.Kind != "video") throw new DomainException("Video bulunamadı.", 404);
            var global = SubtitleSettings.NormalizeProfile(GetSetting("subtitles.profile") is { } json ? JsonSerializer.Deserialize<SubtitleLanguageProfile>(json)! : new(["tr"]));
            using var db = Open(); var row = Query(db, "SELECT profile_json,revision,hold_upgrades FROM media_subtitle_overrides WHERE media_id=$id", r => (Json: Text(r, 0), Revision: r.GetInt32(1), Hold: r.GetBoolean(2)), ("$id", mediaId)).FirstOrDefault();
            var local = row.Json is null ? null : SubtitleSettings.NormalizeProfile(JsonSerializer.Deserialize<SubtitleLanguageProfile>(row.Json, WebJson)!);
            var effective = local ?? global;
            effective = effective with { Enabled = global.Enabled && effective.Enabled, AllowUpgrades = effective.AllowUpgrades && !row.Hold, MaximumDailyDownloads = Math.Min(global.MaximumDailyDownloads, effective.MaximumDailyDownloads) };
            return new(effective, new(int.TryParse(GetSetting("subtitles.profile.revision"), out var revision) ? revision : 0, row.Revision, GetSubtitleConnectionSnapshot().Revision), local is null, local, row.Hold);
        }
    }
    public EffectiveSubtitlePolicy SaveSubtitleOverride(string mediaId, SubtitleOverrideInput input)
    {
        if (input.Profile is not null) { SubtitleSettings.ValidateProfile(input.Profile); input = input with { Profile = SubtitleSettings.NormalizeProfile(input.Profile) }; }
        lock (_writeGate)
        {
            var current = GetEffectiveSubtitlePolicy(mediaId);
            if (current.Stamp.OverrideRevision != input.ExpectedRevision) throw new DomainException("Medya altyazı tercihi değişti; güncel tercihi yükle.", 409);
            using var db = Open(); using var tx = db.BeginTransaction(); using var command = Command(db, """
                INSERT INTO media_subtitle_overrides(media_id,profile_json,updated_at) VALUES($id,$json,$now)
                ON CONFLICT(media_id) DO UPDATE SET profile_json=$json,revision=revision+1,hold_upgrades=0,updated_at=$now;
                UPDATE subtitle_wanted SET status='waiting',policy_json=NULL,lease_token=NULL,lease_until=NULL,next_run=$now WHERE media_id=$id;
                """, ("$id", mediaId), ("$json", input.Profile is null ? null : JsonSerializer.Serialize(input.Profile, WebJson)), ("$now", Now())); command.Transaction = tx; command.ExecuteNonQuery(); tx.Commit();
            return GetEffectiveSubtitlePolicy(mediaId);
        }
    }
    public void ScheduleSubtitles(string mediaId, EffectiveSubtitlePolicy policy)
    {
        lock (_writeGate)
        {
            if (GetEffectiveSubtitlePolicy(mediaId).Stamp != policy.Stamp) return;
            using var db = Open(); using var tx = db.BeginTransaction();
            using var disable = Command(db, "UPDATE subtitle_wanted SET status='cancelled',lease_token=NULL,lease_until=NULL WHERE media_id=$id AND ($enabled=0 OR language NOT IN (SELECT value FROM json_each($languages)))", ("$id", mediaId), ("$enabled", policy.Profile.Enabled), ("$languages", JsonSerializer.Serialize(policy.Profile.Languages)));
            disable.Transaction = tx; disable.ExecuteNonQuery();
            if (policy.Profile.Enabled)
                foreach (var language in policy.Profile.Languages.Distinct())
                {
                    using var insert = Command(db, """
                        INSERT INTO subtitle_wanted(media_id,language,profile_revision,override_revision,connection_revision,policy_json,next_run)
                        VALUES($id,$language,$global,$local,$connection,$json,$now)
                        ON CONFLICT(media_id,language) DO UPDATE SET profile_revision=$global,override_revision=$local,connection_revision=$connection,policy_json=$json,next_run=$now,status='waiting',lease_token=NULL,lease_until=NULL
                        WHERE subtitle_wanted.policy_json IS NULL OR subtitle_wanted.profile_revision<>$global OR subtitle_wanted.override_revision<>$local OR subtitle_wanted.connection_revision<>$connection OR subtitle_wanted.status='cancelled'
                        """, ("$id", mediaId), ("$language", language), ("$global", policy.Stamp.GlobalRevision), ("$local", policy.Stamp.OverrideRevision), ("$connection", policy.Stamp.ConnectionRevision), ("$json", JsonSerializer.Serialize(policy.Profile, WebJson)), ("$now", Now())); insert.Transaction = tx; insert.ExecuteNonQuery();
                }
            tx.Commit();
        }
    }
    public SubtitleJob? ClaimSubtitleJob(DateTimeOffset now)
    {
        lock (_writeGate)
        {
            if (ContainerUpdateBlocked) return null;
            using var db = Open();
            var row = Query(db, """
                SELECT w.media_id,m.title,w.language,w.status,w.message,w.next_run,w.profile_revision,w.override_revision,w.connection_revision,w.policy_json,w.attempts
                FROM subtitle_wanted w JOIN media m ON m.id=w.media_id JOIN libraries l ON l.id=m.library_id
                WHERE w.policy_json IS NOT NULL AND w.status<>'cancelled' AND w.next_run<=$now AND (w.lease_until IS NULL OR w.lease_until<=$now) AND l.enabled=1 AND m.available=1 AND m.excluded=0
                ORDER BY w.next_run,w.media_id LIMIT 1
                """, r => (Info: new SubtitleWanted(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5)), Stamp: new SubtitlePolicyStamp(r.GetInt32(6), r.GetInt32(7), r.GetInt32(8)), Profile: JsonSerializer.Deserialize<SubtitleLanguageProfile>(r.GetString(9), WebJson)!, Attempts: r.GetInt32(10)), ("$now", now.ToString("O"))).FirstOrDefault();
            if (row.Info is null) return null;
            var policy = GetEffectiveSubtitlePolicy(row.Info.MediaId);
            if (policy.Stamp != row.Stamp || !policy.Profile.Enabled || !policy.Profile.Languages.Contains(row.Info.Language)) { ScheduleSubtitles(row.Info.MediaId, policy); return null; }
            var lease = new SubtitleLease(Guid.NewGuid().ToString("N"), row.Info.MediaId, row.Info.Language, row.Stamp);
            Execute(db, "UPDATE subtitle_wanted SET status='searching',message='Altyazı değerlendiriliyor.',attempts=attempts+1,lease_token=$lease,lease_until=$until WHERE media_id=$media AND language=$language",
                ("$lease", lease.Token), ("$until", DateTimeOffset.UtcNow.AddMinutes(5).ToString("O")), ("$media", lease.MediaId), ("$language", lease.Language));
            return new(row.Info, lease, row.Profile, row.Attempts + 1);
        }
    }
    public void RecoverSubtitleJobs() { lock (_writeGate) { using var db = Open(); Execute(db, "UPDATE subtitle_wanted SET status='waiting',lease_token=NULL,lease_until=NULL,next_run=$now,message='Kesilen altyazı işi toparlandı.' WHERE lease_token IS NOT NULL", ("$now", Now())); } }
    public void RequireSubtitlePolicy(string mediaId, SubtitlePolicyStamp stamp, string language, bool download, SubtitleLease? lease = null)
    {
        var policy = GetEffectiveSubtitlePolicy(mediaId);
        if (policy.Stamp != stamp || !policy.Profile.Enabled || download && !policy.Profile.AutoDownload || !policy.Profile.Languages.Contains(language)) throw new DomainException("Altyazı otomasyon tercihi değişti; eski sonuç uygulanmadı.", 409);
        if (lease is null) return;
        using var db = Open();
        if (lease.MediaId != mediaId || lease.Language != language || lease.Stamp != stamp || Scalar(db, "SELECT media_id FROM subtitle_wanted WHERE media_id=$media AND language=$language AND lease_token=$token AND lease_until>$now AND status='searching'", ("$media", mediaId), ("$language", language), ("$token", lease.Token), ("$now", Now())) is not string)
            throw new DomainException("Altyazı işi artık etkin değil.", 409);
    }
    public void FinishSubtitleJob(SubtitleJob job, string status, string message, DateTimeOffset next)
    {
        lock (_writeGate)
        {
            using var db = Open(); Execute(db, "UPDATE subtitle_wanted SET status=$status,message=$message,next_run=$next,lease_token=NULL,lease_until=NULL WHERE media_id=$media AND language=$language AND lease_token=$token",
                ("$media", job.Lease.MediaId), ("$language", job.Lease.Language), ("$token", job.Lease.Token), ("$status", status), ("$message", message), ("$next", next.ToString("O")));
        }
    }
    public void WakeSubtitle(string mediaId)
    {
        lock (_writeGate) { ScheduleSubtitles(mediaId, GetEffectiveSubtitlePolicy(mediaId)); using var db = Open(); Execute(db, "UPDATE subtitle_wanted SET next_run=$now WHERE media_id=$media AND lease_token IS NULL", ("$media", mediaId), ("$now", Now())); }
    }
    public int AutomaticSubtitleAttemptsToday()
    {
        using var db = Open(); return Convert.ToInt32(Scalar(db, "SELECT COUNT(*) FROM subtitle_download_attempts WHERE automatic=1 AND created_at>=strftime('%Y-%m-%dT00:00:00.0000000+00:00','now')"));
    }
    public void ReserveSubtitleDownload(StoredSubtitleCandidate candidate, SubtitlePublication publication)
    {
        lock (_writeGate)
        {
            ValidateSubtitlePublication(candidate.Candidate.MediaId, candidate.Candidate.Language, publication);
            if (publication.Policy is not null && AutomaticSubtitleAttemptsToday() >= GetEffectiveSubtitlePolicy(candidate.Candidate.MediaId).Profile.MaximumDailyDownloads) throw new DomainException("Günlük otomatik altyazı edinme bütçesi dolu.", 429);
            using var db = Open(); Execute(db, "INSERT INTO subtitle_download_attempts(id,candidate_id,media_id,provider_file_id,connection_revision,automatic,created_at) VALUES($id,$candidate,$media,$file,$connection,$auto,$now)",
                ("$id", Guid.NewGuid().ToString("N")), ("$candidate", candidate.Candidate.Id), ("$media", candidate.Candidate.MediaId), ("$file", candidate.Candidate.FileId), ("$connection", candidate.ConnectionRevision), ("$auto", publication.Policy is not null), ("$now", Now()));
            Execute(db, "DELETE FROM subtitle_download_attempts WHERE created_at<$old", ("$old", DateTimeOffset.UtcNow.AddDays(-90).ToString("O")));
        }
    }
    public void SaveSubtitleTicket(string candidateId, int connectionRevision, string protectedTicket)
    {
        lock (_writeGate)
        {
            if (GetSubtitleConnectionSnapshot().Revision != connectionRevision) throw new DomainException("Altyazı bağlantısı değişti.", 409);
            using var db = Open(); Execute(db, "UPDATE subtitle_candidates SET protected_ticket=$ticket,ticket_expires_at=$expires WHERE id=$id AND connection_revision=$revision", ("$id", candidateId), ("$ticket", protectedTicket), ("$expires", DateTimeOffset.UtcNow.AddMinutes(10).ToString("O")), ("$revision", connectionRevision));
        }
    }
    public void ValidateSubtitlePublication(string mediaId, string language, SubtitlePublication publication)
    {
        var media = GetMedia(mediaId) ?? throw new DomainException("Altyazı medyası bulunamadı.", 404);
        var path = MediaPaths.ResolveFile(GetLibrary(media.LibraryId)!.Path, media.RelativePath);
        if (media.MatchRevision != publication.MatchRevision || MediaAnalysisService.Fingerprint(new FileInfo(path)) != publication.Fingerprint) throw new DomainException("Dosya veya eşleştirme değişti; eski altyazı yayımlanmadı.", 409);
        if (publication.ConnectionRevision is { } connection && GetSubtitleConnectionSnapshot().Revision != connection) throw new DomainException("Altyazı bağlantısı değişti; eski sonuç yayımlanmadı.", 409);
        if (publication.Policy is { } stamp) RequireSubtitlePolicy(mediaId, stamp, language, true, publication.Lease);
        if (publication.Origin == "sync")
        {
            var job = publication.JobId is null ? null : GetSubtitleSync(publication.JobId);
            if (job is null || job.MediaId != mediaId || job.Revision != publication.JobRevision || job.Status != "applying") throw new DomainException("Senkron işi iptal edildi veya değişti.", 409);
        }
        if(publication.Origin=="generated")
        {
            var job=publication.JobId is null?null:GetProductJob(publication.JobId);if(job is null||job.Kind!="asr"||job.ScopeId!=mediaId||job.Revision!=publication.JobRevision||job.Status!="applying")throw new DomainException("Konuşma üretimi iptal edildi veya değişti.",409);_=RequireProductJob(job,true);
        }
        if (publication.SupersedesId is { } previous)
        {
            var old = GetManagedSubtitles(mediaId).FirstOrDefault(subtitle => subtitle.Id == previous) ?? throw new DomainException("Değiştirilecek altyazı artık etkin değil.", 409);
            if (old.Language != language) throw new DomainException("Yükseltilecek altyazının dili farklı.", 409);
            if (publication.Policy is not null)
            {
                var policy = GetEffectiveSubtitlePolicy(mediaId).Profile;
                if (!policy.AllowUpgrades || old.Origin != "provider" || old.Score is null || publication.Score is null || publication.ProviderFileId == old.ProviderFileId || publication.Score < old.Score + policy.MinimumUpgradeDelta || old.Score >= policy.CutoffScore)
                    throw new DomainException("Altyazı artık yükseltme koşullarını karşılamıyor.", 409);
            }
        }
    }
}
