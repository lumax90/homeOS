using System.Text.Json;
using HomeServer.Core;
using HomeServer.Sources;
using Microsoft.Data.Sqlite;

namespace HomeServer.Infrastructure;

public sealed partial class MediaStore
{
    public AutomationRule GetAutomationRule(string contentId)
    {
        contentId = CanonicalContentId(contentId);
        var content = GetContent(contentId) ?? throw new DomainException("İçerik bulunamadı.", 404);
        using var db = Open();
        return Query(db, "SELECT settings_json,revision,last_run,next_run,last_error,updated_by,(SELECT display_name FROM users WHERE id=automation_rules.updated_by) FROM automation_rules WHERE content_id=$id",
            r => new AutomationRule(contentId, content.Metadata.Title, JsonSerializer.Deserialize<AutomationInput>(r.GetString(0), WebJson)!, r.GetInt32(1), Text(r, 2), r.GetString(3), Text(r, 4), Text(r, 5), Text(r, 6)),
            ("$id", contentId)).FirstOrDefault() ?? new AutomationRule(contentId, content.Metadata.Title, new(), 0, null, Now(), null);
    }
    public IReadOnlyList<AutomationRule> GetAutomationRules()
    {
        using var db = Open();
        return Query(db, "SELECT a.content_id FROM automation_rules a JOIN content c ON c.id=a.content_id WHERE c.merged_into IS NULL ORDER BY a.updated_at DESC", r => r.GetString(0)).Select(GetAutomationRule).ToArray();
    }
    public AutomationRule SaveAutomationRule(string id, AutomationInput input, string userId = "1")
    {
        id = CanonicalContentId(id);
        _ = GetContent(id) ?? throw new DomainException("İçerik bulunamadı.", 404);
        if (input.CutoffResolution is not (480 or 576 or 720 or 1080 or 2160) || input.SearchIntervalMinutes is < 15 or > 1440
            || input.DelayMinutes is < 0 or > 1440 || input.MinimumAgeHours is < 0 or > 720 || input.MaximumDailyGrabs is < 1 or > 20)
            throw new DomainException("Otomasyon değerleri geçersiz.");
        lock (_writeGate)
        {
            using var db = Open(); using var tx = db.BeginTransaction();
            using var cmd = Command(db, """
                INSERT INTO automation_rules(content_id,settings_json,enabled,auto_grab,next_run,updated_at,updated_by)
                  VALUES($id,$json,$enabled,$grab,$now,$now,$user)
                ON CONFLICT(content_id) DO UPDATE SET settings_json=$json,enabled=$enabled,auto_grab=$grab,revision=revision+1,next_run=$now,last_error=NULL,updated_at=$now,updated_by=$user;
                UPDATE automation_jobs SET status='cancelled',message='Kural değişti.',updated_at=$now WHERE content_id=$id AND status IN ('queued','waiting','running');
                DELETE FROM automation_suppressions WHERE content_id=$id;
                """, ("$id", id), ("$json", JsonSerializer.Serialize(input, WebJson)), ("$enabled", input.Enabled), ("$grab", input.AutoGrab), ("$now", Now()), ("$user", userId));
            cmd.Transaction = tx; cmd.ExecuteNonQuery(); tx.Commit();
            return GetAutomationRule(id);
        }
    }
    public void WakeAutomation(string id)
    {
        id = CanonicalContentId(id);
        lock (_writeGate)
        {
            using var db = Open();
            Execute(db, "UPDATE automation_rules SET next_run=$now WHERE content_id=$id; UPDATE automation_jobs SET due_at=$now WHERE content_id=$id AND status='waiting'; DELETE FROM automation_suppressions WHERE content_id=$id;",
                ("$id", id), ("$now", Now()));
        }
    }
    public void NudgeAutomation(string id, DateTimeOffset now)
    {
        lock (_writeGate)
        {
            using var db = Open();
            Execute(db, "UPDATE automation_rules SET next_run=$now WHERE content_id=$id; UPDATE automation_jobs SET due_at=$now WHERE content_id=$id AND status='waiting'", ("$id", id), ("$now", now.ToString("O")));
        }
    }
    public IReadOnlyList<AutomationTarget> AutomationTargets(string id, AutomationInput rule)
    {
        id = CanonicalContentId(id);
        var content = GetContent(id);
        if (content is null || content.MonitorMode == "none") return [];
        var files = GetContentFiles(id).Where(file => file.Available && file.Kind == "video").ToArray();
        using var db = Open();
        var existing = Query(db, """
            SELECT a.season_number,a.episode_number,a.episodes_json FROM acquisition_links a JOIN downloads d ON d.id=a.download_id
            WHERE a.content_id=$id AND d.imported_at IS NULL AND d.desired_state IN ('running','paused') AND d.status<>'failed'
            """, r => (Season: r.IsDBNull(0) ? (int?)null : r.GetInt32(0), Episode: r.IsDBNull(1) ? (int?)null : r.GetInt32(1), Episodes: r.IsDBNull(2) ? [] : JsonSerializer.Deserialize<EpisodeCoordinate[]>(r.GetString(2), WebJson)!), ("$id", id));
        var result = new List<AutomationTarget>();
        if (content.Metadata.Kind == "movie")
        {
            if (!DateOnly.TryParse(content.Metadata.ReleaseDate, out var date) || date > DateOnly.FromDateTime(DateTime.UtcNow)
                || Scalar(db, "SELECT id FROM content_requests WHERE content_id=$id AND status IN ('approved','fulfilled') AND monitor_mode<>'none' LIMIT 1", ("$id", id)) is not string) return [];
            Add(null, null, files.Where(MovieFileReady).ToArray());
        }
        else
        {
            var episodes = Query(db, $"SELECT e.season_number,e.episode_number FROM episodes e JOIN tracking t ON t.content_id=e.content_id WHERE e.content_id=$id AND e.air_date<>'' AND e.air_date<=date('now') AND {EpisodeWanted} AND (t.monitor_mode<>'future' OR e.air_date>=substr(t.requested_at,1,10)) ORDER BY e.air_date,e.season_number,e.episode_number",
                r => (Season: r.GetInt32(0), Episode: r.GetInt32(1)), [("$id", id), ..ViewerArgs(null)]);
            foreach (var episode in episodes) Add(episode.Season, episode.Episode, files.Where(f => f.Episodes?.Any(segment => segment.Season == episode.Season && segment.Number == episode.Episode) == true).ToArray());
        }
        return result;
        void Add(int? season, int? episode, MediaItem[] versions)
        {
            if (existing.Any(e => e.Season is null || e.Season == season && (e.Episode is null || e.Episode == episode) || e.Episodes.Any(part => part.Season == season && part.Episode == episode))) return;
            var best = versions.Select(file => ReleaseEvaluator.ResolutionFor(file.Width, file.Height) ?? 0).DefaultIfEmpty(0).Max();
            if (versions.Length > 0 && (!rule.AllowUpgrades || best == 0 || best >= rule.CutoffResolution && !rule.SameQualityUpgrades)) return;
            var scores = versions.Where(file => Sources.ReleaseEvaluator.ResolutionFor(file.Width, file.Height) == best).Select(file => ExistingPreferenceScore(file, GetQualityProfile(content.QualityProfileId).Rules)).ToArray();
            var score = scores.Length > 0 && scores.All(value => value is not null) ? scores.Max() : null;
            result.Add(new AutomationTarget(id, season, episode, best, versions.Length > 0, score));
        }
    }
    public int ScheduleAutomation(DateTimeOffset now)
    {
        lock (_writeGate)
        {
            using var db = Open();
            var ids = Query(db, "SELECT content_id FROM automation_rules WHERE enabled=1 AND next_run<=$now ORDER BY next_run LIMIT 10", r => r.GetString(0), ("$now", now.ToString("O")));
            var added = 0;
            foreach (var id in ids)
            {
                var rule = GetAutomationRule(id); var content = GetContent(id)!; var profile = GetQualityProfile(content.QualityProfileId);
                var targets = AutomationTargets(id, rule.Settings);
                foreach (var target in targets)
                {
                    if (Convert.ToInt32(Scalar(db, "SELECT COUNT(*) FROM automation_jobs WHERE status IN ('queued','waiting','running')")) >= 2000) break;
                    if (Scalar(db, "SELECT target_key FROM automation_suppressions WHERE content_id=$id AND target_key=$key AND revision=$revision", ("$id", id), ("$key", target.Key), ("$revision", rule.Revision)) is string) continue;
                    added += Execute(db, """
                        INSERT OR IGNORE INTO automation_jobs(id,content_id,season_number,episode_number,target_key,rule_revision,settings_json,profile_json,status,due_at,created_at,updated_at)
                        VALUES($job,$id,$season,$episode,$key,$revision,$settings,$profile,'queued',$now,$now,$now)
                        """, ("$job", Guid.NewGuid().ToString("N")), ("$id", id), ("$season", target.Season), ("$episode", target.Episode), ("$key", target.Key), ("$revision", rule.Revision),
                        ("$settings", JsonSerializer.Serialize(rule.Settings, WebJson)), ("$profile", JsonSerializer.Serialize(profile, WebJson)), ("$now", now.ToString("O")));
                }
                Execute(db, "UPDATE automation_rules SET last_run=$now,next_run=$next WHERE content_id=$id AND revision=$revision", ("$id", id), ("$revision", rule.Revision), ("$now", now.ToString("O")), ("$next", now.AddMinutes(rule.Settings.SearchIntervalMinutes).ToString("O")));
            }
            Execute(db, "DELETE FROM automation_jobs WHERE download_id IS NULL AND status IN ('suggested','cancelled','failed','blocked','satisfied') AND updated_at<$old", ("$old", now.AddDays(-30).ToString("O")));
            Execute(db, "DELETE FROM release_observations WHERE last_seen<$old", ("$old", now.AddDays(-30).ToString("O")));
            return added;
        }
    }
    private const string AutomationJobSelect = "SELECT id,content_id,season_number,episode_number,target_key,rule_revision,status,attempts,due_at,message,release_id,fingerprint,download_id,settings_json,profile_json,created_at FROM automation_jobs";
    public IReadOnlyList<AutomationJob> GetAutomationJobs(string? id = null)
    {
        using var db = Open();
        return Query(db, AutomationJobSelect + " WHERE ($id IS NULL OR content_id=$id) ORDER BY updated_at DESC LIMIT 100", ReadAutomationJob, ("$id", id));
    }
    public AutomationJob? GetAutomationJob(string id)
    {
        using var db = Open(); return Query(db, AutomationJobSelect + " WHERE id=$id", ReadAutomationJob, ("$id", id)).FirstOrDefault();
    }
    public AutomationJob? ClaimAutomationJob(DateTimeOffset now)
    {
        lock (_writeGate)
        {
            if (ContainerUpdateBlocked) return null;
            using var db = Open();
            var id = Scalar(db, "SELECT j.id FROM automation_jobs j JOIN automation_rules r ON r.content_id=j.content_id WHERE j.status IN ('queued','waiting') AND j.due_at<=$now AND r.enabled=1 AND r.revision=j.rule_revision ORDER BY j.due_at,j.created_at LIMIT 1", ("$now", now.ToString("O"))) as string;
            if (id is null) return null;
            Execute(db, "UPDATE automation_jobs SET status='running',attempts=attempts+1,lease_until=$lease,updated_at=$now,budget_wait_reason=NULL WHERE id=$id", ("$id", id), ("$lease", now.AddMinutes(5).ToString("O")), ("$now", now.ToString("O")));
            return GetAutomationJob(id);
        }
    }
    public void RecoverAutomationJobs()
    {
        lock (_writeGate) { using var db = Open(); Execute(db, "UPDATE automation_jobs SET status='queued',due_at=$now,lease_until=NULL,message='Kesilen otomasyon işi toparlandı.' WHERE status='running'", ("$now", Now())); }
    }
    public bool AutomationStillWanted(AutomationJob job)
    {
        var rule = GetAutomationRule(job.ContentId);
        return rule.Settings.Enabled && rule.Revision == job.RuleRevision && AutomationTargets(job.ContentId, job.Settings).Any(target => target.Key == job.TargetKey);
    }
    public void FinishAutomationJob(AutomationJob job, string state, string message, DateTimeOffset? retryAt = null, string? release = null, string? fingerprint = null, string? download = null, string? budgetReason = null)
    {
        if (state is not ("waiting" or "suggested" or "grabbed" or "failed" or "cancelled" or "blocked" or "satisfied")) throw new ArgumentOutOfRangeException(nameof(state));
        lock (_writeGate)
        {
            using var db = Open();
            if (Scalar(db, "SELECT status FROM automation_jobs WHERE id=$id AND rule_revision=$revision", ("$id", job.Id), ("$revision", job.RuleRevision)) is not ("running" or "grabbed")) return;
            using var tx = db.BeginTransaction();
            using var cmd = Command(db, """
                UPDATE automation_jobs SET status=$state,message=$message,due_at=$due,lease_until=NULL,release_id=COALESCE($release,release_id),fingerprint=COALESCE($fingerprint,fingerprint),download_id=COALESCE($download,download_id),updated_at=$now,budget_wait_reason=$budget
                  WHERE id=$id AND rule_revision=$revision AND status IN ('running','grabbed');
                UPDATE automation_rules SET last_error=$error WHERE content_id=$content AND revision=$revision;
                """, ("$id", job.Id), ("$revision", job.RuleRevision), ("$content", job.ContentId), ("$state", state), ("$message", message), ("$due", (retryAt ?? DateTimeOffset.UtcNow).ToString("O")),
                ("$release", release), ("$fingerprint", fingerprint), ("$download", download), ("$now", Now()), ("$error", state == "failed" ? message : null), ("$budget", budgetReason));
            cmd.Transaction = tx; cmd.ExecuteNonQuery();
            if (state is "suggested" or "failed")
                foreach (var admin in GetAccounts().Where(a => a.Enabled && a.IsAdmin))
                    AddNotification(db, tx, admin.Id, "automation", GetContent(job.ContentId)!.Metadata.Title, message, job.ContentId, "auto:" + (fingerprint ?? job.Id) + ":" + state);
            tx.Commit();
        }
    }
    public void SelectAutomationRelease(AutomationJob job, ReleaseResult release)
    {
        lock (_writeGate)
        {
            using var db = Open();
            Execute(db, "UPDATE automation_jobs SET release_id=$release,fingerprint=$fp WHERE id=$id AND rule_revision=$revision AND status='running'", ("$release", release.Id), ("$fp", StableFingerprint(release.Fingerprint)), ("$id", job.Id), ("$revision", job.RuleRevision));
        }
    }
    public int AutomaticGrabsToday(string contentId)
    {
        using var db = Open(); return Convert.ToInt32(Scalar(db, "SELECT COUNT(*) FROM automation_jobs WHERE content_id=$id AND grabbed_at>=strftime('%Y-%m-%dT00:00:00.0000000+00:00','now')", ("$id", contentId)));
    }
    public IReadOnlyList<AutomationJob> TransferringAutomationJobs()
    {
        using var db = Open(); return Query(db, AutomationJobSelect + " WHERE status='grabbed' AND download_id IS NOT NULL", ReadAutomationJob);
    }
    public static string StableFingerprint(string value) => value.Contains(':') ? value[(value.IndexOf(':') + 1)..] : value;
    private void ValidateAutomationAcquisition(AcquisitionContext context)
    {
        if (context.Automation is not { } lease) return;
        var job = GetAutomationJob(lease.JobId) ?? throw new DomainException("Otomasyon işi bulunamadı.", 409);
        var rule = GetAutomationRule(job.ContentId);
        using var db = Open();
        if (job.Status != "running" || job.RuleRevision != lease.Revision || rule.Revision != lease.Revision || !rule.Settings.Enabled || !rule.Settings.AutoGrab
            || job.ContentId != context.ContentId || job.Season != context.Season || job.Episode != context.Episode || job.ReleaseId != context.ReleaseId
            || Scalar(db, "SELECT id FROM automation_jobs WHERE id=$id AND lease_until>$now", ("$id", job.Id), ("$now", Now())) is not string || !AutomationStillWanted(job))
            throw new DomainException("Otomasyon niyeti değişti; eski sonuçla indirme başlatılmadı.", 409);
        if (AutomaticGrabsToday(job.ContentId) >= job.Settings.MaximumDailyGrabs) throw new DomainException("Günlük otomatik edinme kotasına ulaşıldı.", 429);
        if (job.Fingerprint is not null && IsReleaseBlocked(job.ContentId, job.Fingerprint)) throw new DomainException("Bu sürüm engel listesinde.", 409);
    }
    public DateTimeOffset ObserveRelease(string fingerprint, DateTimeOffset now)
    {
        lock (_writeGate)
        {
            using var db = Open(); Execute(db, "INSERT INTO release_observations(fingerprint,first_seen,last_seen) VALUES($fp,$now,$now) ON CONFLICT(fingerprint) DO UPDATE SET last_seen=$now", ("$fp", fingerprint), ("$now", now.ToString("O")));
            return DateTimeOffset.Parse((string)Scalar(db, "SELECT first_seen FROM release_observations WHERE fingerprint=$fp", ("$fp", fingerprint))!);
        }
    }
    public bool IsReleaseBlocked(string content, string fingerprint)
    {
        using var db = Open(); return Scalar(db, "SELECT fingerprint FROM release_blocklist WHERE content_id=$id AND fingerprint=$fp", ("$id", content), ("$fp", fingerprint)) is string;
    }
    public IReadOnlyList<BlockedRelease> GetBlockedReleases()
    {
        using var db = Open();
        return Query(db, "SELECT b.content_id,c.title,b.fingerprint,b.reason,b.blocked_at FROM release_blocklist b JOIN content c ON c.id=b.content_id ORDER BY blocked_at DESC LIMIT 200",
            r => new BlockedRelease(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4)));
    }
    public void UnblockRelease(string contentId, string fingerprint)
    {
        lock (_writeGate) { using var db = Open(); Execute(db, "DELETE FROM release_blocklist WHERE content_id=$id AND fingerprint=$fp", ("$id", contentId), ("$fp", fingerprint)); NudgeAutomation(contentId, DateTimeOffset.UtcNow); }
    }
    public void BlockAutomationRelease(AutomationJob job, string reason, bool cancelled)
    {
        lock (_writeGate)
        {
            using var db = Open();
            if (cancelled) Execute(db, "INSERT INTO automation_suppressions(content_id,target_key,revision,reason) VALUES($id,$key,$revision,$reason) ON CONFLICT(content_id,target_key) DO UPDATE SET revision=$revision,reason=$reason", ("$id", job.ContentId), ("$key", job.TargetKey), ("$revision", job.RuleRevision), ("$reason", reason));
            else if (job.Fingerprint is not null) Execute(db, "INSERT INTO release_blocklist(content_id,fingerprint,reason,blocked_at) VALUES($id,$fp,$reason,$now) ON CONFLICT(content_id,fingerprint) DO UPDATE SET reason=$reason,blocked_at=$now", ("$id", job.ContentId), ("$fp", job.Fingerprint), ("$reason", reason), ("$now", Now()));
            FinishAutomationJob(job, cancelled ? "blocked" : "failed", reason);
            if (!cancelled) Execute(db, "UPDATE automation_rules SET next_run=$next WHERE content_id=$id", ("$id", job.ContentId), ("$next", DateTimeOffset.UtcNow.AddMinutes(15).ToString("O")));
        }
    }
    public IReadOnlyList<string> DueRssSources(DateTimeOffset now)
    {
        using var db = Open();
        if (Scalar(db, "SELECT content_id FROM automation_rules WHERE enabled=1 LIMIT 1") is not string) return [];
        return Query(db, "SELECT i.id FROM indexers i LEFT JOIN rss_state s ON s.indexer_id=i.id WHERE i.enabled=1 AND i.setup_pending=0 AND (i.caps_json IS NULL OR json_type(i.caps_json,'$.searches.search')='array') AND (s.next_run IS NULL OR s.next_run<=$now) ORDER BY s.next_run LIMIT 1", r => r.GetString(0), ("$now", now.ToString("O")));
    }
    public void RecordRss(string id, string? error, DateTimeOffset now)
    {
        lock (_writeGate) { using var db = Open(); Execute(db, "INSERT INTO rss_state(indexer_id,next_run,last_run,last_error) VALUES($id,$next,$now,$error) ON CONFLICT(indexer_id) DO UPDATE SET next_run=$next,last_run=$now,last_error=$error", ("$id", id), ("$next", now.AddMinutes(error is null ? 15 : 30).ToString("O")), ("$now", now.ToString("O")), ("$error", error)); }
    }
    private static AutomationJob ReadAutomationJob(SqliteDataReader r) => new(r.GetString(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetInt32(2), r.IsDBNull(3) ? null : r.GetInt32(3), r.GetString(4), r.GetInt32(5), r.GetString(6), r.GetInt32(7), r.GetString(8), r.GetString(9), Text(r, 10), Text(r, 11), Text(r, 12),
        JsonSerializer.Deserialize<AutomationInput>(r.GetString(13), WebJson)!, JsonSerializer.Deserialize<QualityProfile>(r.GetString(14), WebJson)!, r.GetString(15));
}
