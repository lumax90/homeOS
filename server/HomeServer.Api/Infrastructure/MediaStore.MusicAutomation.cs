using System.Text.Json;
using HomeServer.Core;
using HomeServer.Music;
using HomeServer.Playback;
using Microsoft.Data.Sqlite;

namespace HomeServer.Infrastructure;

public sealed partial class MediaStore
{
    private const string MusicMonitorSelect = "SELECT id,user_id,user_version,kind,mbid,name,settings_json,revision,cursor,created_at,next_run,last_run,last_error FROM music_monitors";
    private static MusicMonitor ReadMusicMonitor(SqliteDataReader r) => new(r.GetString(0), r.GetString(1), r.GetInt32(2), r.GetString(3), r.GetString(4), r.GetString(5),
        JsonSerializer.Deserialize<MusicMonitorSettings>(r.GetString(6), WebJson)!, r.GetInt32(7), r.GetInt32(8), r.GetString(9), r.GetString(10), Text(r, 11), Text(r, 12));
    public MusicMonitor? GetMusicMonitor(string id)
    {
        using var db = Open(); return Query(db, MusicMonitorSelect + " WHERE id=$id", ReadMusicMonitor, ("$id", id)).FirstOrDefault();
    }
    public IReadOnlyList<MusicMonitor> GetMusicMonitors(string user)
    {
        using var db = Open(); return Query(db, MusicMonitorSelect + " WHERE user_id=$user ORDER BY name", ReadMusicMonitor, ("$user", user));
    }
    public MusicMonitor SaveMusicMonitor(string kind, string mbid, string name, MusicMonitorInput input, Account actor)
    {
        mbid = MusicBrainzProvider.Id(mbid);
        if (kind is not ("artist" or "album")) throw new DomainException("Müzik takip türü geçersiz.");
        var settings = MusicMonitorPolicy.Validate(input.Settings);
        lock (_writeGate)
        {
            var user = GetAccountSecret(actor.Id) ?? throw new DomainException("Hesap bulunamadı.", 403);
            if (!MusicMonitorAllowed(user.Account) || settings.Search && !user.Account.IsAdmin) throw new DomainException("Bu müzik takip işlemi için yetki yok.", 403);
            using var db = Open();
            var existing = GetMusicMonitors(actor.Id).FirstOrDefault(item => item.Kind == kind && item.Mbid == mbid);
            if ((existing?.Revision ?? 0) != input.ExpectedRevision) throw new DomainException("Takip başka bir ekranda değişti; güncel tercihi yükle.", 409);
            if (existing is null && GetMusicMonitors(actor.Id).Count >= 100) throw new DomainException("En fazla 100 müzik takibi saklanabilir.");
            var id = existing?.Id ?? Guid.NewGuid().ToString("N"); var now = Now();
            using var tx = db.BeginTransaction();
            using var write = Command(db, """
                INSERT INTO music_monitors(id,user_id,user_version,kind,mbid,name,settings_json,enabled,created_at,next_run)
                VALUES($id,$user,$version,$kind,$mbid,$name,$settings,$enabled,$now,$now)
                ON CONFLICT(id) DO UPDATE SET user_version=$version,name=$name,settings_json=$settings,enabled=$enabled,revision=revision+1,cursor=0,next_run=$now,next_search=NULL,last_error=NULL;
                UPDATE music_automation_jobs SET status='cancelled',message='Takip tercihi değişti.',updated_at=$now
                WHERE monitor_id=$id AND status IN ('queued','waiting','running','blocked');
                """, ("$id", id), ("$user", actor.Id), ("$version", user.Version), ("$kind", kind), ("$mbid", mbid), ("$name", name),
                ("$settings", JsonSerializer.Serialize(settings, WebJson)), ("$enabled", settings.Enabled), ("$now", now));
            write.Transaction = tx; write.ExecuteNonQuery(); tx.Commit();
            return GetMusicMonitor(id)!;
        }
    }
    private static bool MusicMonitorAllowed(Account actor) => actor.Enabled && (actor.IsAdmin || actor.CanDiscover && (actor.MaximumAge is null || actor.AllowUnrated));
    public bool MusicMonitorCurrent(MusicMonitor monitor)
    {
        var current = GetMusicMonitor(monitor.Id); var user = GetAccountSecret(monitor.UserId);
        return current is not null && current.Revision == monitor.Revision && current.Settings.Enabled && user is not null
            && user.Version == monitor.UserVersion && MusicMonitorAllowed(user.Account) && (!monitor.Settings.Search || user.Account.IsAdmin);
    }
    public MusicMonitor? DueMusicMonitor(DateTimeOffset now)
    {
        using var db = Open();
        return Query(db, MusicMonitorSelect + " WHERE enabled=1 AND next_run<=$now ORDER BY next_run LIMIT 1", ReadMusicMonitor, ("$now", now.ToString("O"))).FirstOrDefault();
    }
    public void WakeMusicMonitor(string id, int revision, string user)
    {
        lock (_writeGate)
        {
            var monitor = GetMusicMonitor(id);
            if (monitor?.UserId != user || monitor.Revision != revision) throw new DomainException("Takip bulunamadı veya değişti.", 409);
            using var db = Open(); Execute(db, "UPDATE music_monitors SET next_run=$now,cursor=0 WHERE id=$id; UPDATE music_automation_jobs SET due_at=$now WHERE monitor_id=$id AND status='waiting'", ("$id", id), ("$now", Now()));
        }
    }
    public void RecordMusicMonitorPage(MusicMonitor monitor, MusicPage<MusicBrainzAlbum> page, DateTimeOffset now)
    {
        lock (_writeGate)
        {
            if (!MusicMonitorCurrent(monitor) || GetMusicMonitor(monitor.Id)!.Cursor != monitor.Cursor) return;
            if (page.Offset != monitor.Cursor || page.Items.Count == 0 && page.Offset < page.Total || page.Total > 100000)
                throw new DomainException("MusicBrainz sayfası eksik veya takip sınırını aşıyor.", 502);
            using var db = Open();
            var known = GetMonitoredMusicAlbums(monitor.Id, 0, 2001).Items;
            if (known.Count + page.Items.Count(item => !known.Any(old => old.Metadata.Id == item.Id)) > 2000) throw new DomainException("Bir takip en fazla 2000 albüm içerir.");
            var next = page.Offset + page.Items.Count; var done = next >= page.Total;
            using var tx = db.BeginTransaction();
            foreach (var album in page.Items.DistinctBy(album => album.Id))
            {
                _ = MusicBrainzProvider.Id(album.Id);
                var wanted = MusicMonitorPolicy.Wanted(monitor, album);
                using var write = Command(db, """
                    INSERT INTO music_monitor_albums(monitor_id,group_mbid,metadata_json,wanted,first_seen) VALUES($monitor,$group,$json,$wanted,$now)
                    ON CONFLICT(monitor_id,group_mbid) DO UPDATE SET metadata_json=$json,wanted=COALESCE(wanted_override,$wanted)
                    """, ("$monitor", monitor.Id), ("$group", album.Id), ("$json", JsonSerializer.Serialize(album, WebJson)), ("$wanted", wanted), ("$now", now.ToString("O")));
                write.Transaction = tx; write.ExecuteNonQuery();
                if (wanted && !known.Any(item => item.Metadata.Id == album.Id))
                    AddNotification(db, tx, monitor.UserId, "music", album.Title, album.Artist + " · Takip profilinde yeni yayın: " + (album.Date ?? "Tarih bekleniyor"), null, "music-new:" + monitor.Id + ":" + album.Id);
            }
            using var advance = Command(db, "UPDATE music_monitors SET cursor=$cursor,next_run=$next,last_run=CASE WHEN $done=1 THEN $now ELSE last_run END,last_error=NULL WHERE id=$id AND revision=$revision",
                ("$id", monitor.Id), ("$revision", monitor.Revision), ("$cursor", done ? 0 : next), ("$next", (done ? now.AddHours(monitor.Settings.RefreshHours) : now.AddSeconds(2)).ToString("O")), ("$done", done), ("$now", now.ToString("O")));
            advance.Transaction = tx; advance.ExecuteNonQuery(); tx.Commit();
            ScheduleMusicAutomation(monitor, now);
        }
    }
    public void FailMusicMonitor(MusicMonitor monitor, string message, DateTimeOffset now)
    {
        lock (_writeGate) { using var db = Open(); Execute(db, "UPDATE music_monitors SET next_run=$next,last_error=$error WHERE id=$id AND revision=$revision", ("$id", monitor.Id), ("$revision", monitor.Revision), ("$error", message), ("$next", now.AddMinutes(30).ToString("O"))); }
    }
    public MusicPage<MonitoredMusicAlbum> GetMonitoredMusicAlbums(string id, int offset = 0, int limit = 50)
    {
        using var db = Open(); limit = Math.Clamp(limit, 1, 2001); offset = Math.Max(0, offset);
        var total = Convert.ToInt32(Scalar(db, "SELECT COUNT(*) FROM music_monitor_albums WHERE monitor_id=$id", ("$id", id)));
        var rows = Query(db, "SELECT metadata_json,wanted,wanted_override,first_seen FROM music_monitor_albums WHERE monitor_id=$id ORDER BY json_extract(metadata_json,'$.date') DESC,group_mbid LIMIT $limit OFFSET $offset",
            r => new MonitoredMusicAlbum(id, JsonSerializer.Deserialize<MusicBrainzAlbum>(r.GetString(0), WebJson)!, r.GetBoolean(1), r.IsDBNull(2) ? null : r.GetBoolean(2), r.GetString(3)), ("$id", id), ("$offset", offset), ("$limit", limit));
        return new(rows, total, offset, limit);
    }
    public void OverrideMonitoredMusicAlbum(string id, string group, MusicAlbumOverride input, string user)
    {
        lock (_writeGate)
        {
            var monitor = GetMusicMonitor(id);
            if (monitor?.UserId != user || monitor.Revision != input.ExpectedRevision) throw new DomainException("Takip bulunamadı veya değişti.", 409);
            var album = GetMonitoredMusicAlbums(id, 0, 2001).Items.FirstOrDefault(item => item.Metadata.Id == group) ?? throw new DomainException("Albüm takipte bulunamadı.", 404);
            using var db = Open(); using var tx = db.BeginTransaction();
            using var command = Command(db, """
                UPDATE music_monitor_albums SET wanted_override=$override,wanted=$wanted WHERE monitor_id=$id AND group_mbid=$group;
                UPDATE music_monitors SET revision=revision+1,next_run=$now,next_search=NULL WHERE id=$id;
                UPDATE music_automation_jobs SET status='cancelled',message='Albüm kapsamı değişti.',updated_at=$now WHERE monitor_id=$id AND status IN ('queued','waiting','running','blocked');
                """, ("$id", id), ("$group", group), ("$override", input.Wanted), ("$wanted", input.Wanted ?? MusicMonitorPolicy.Wanted(monitor, album.Metadata)), ("$now", Now()));
            command.Transaction = tx; command.ExecuteNonQuery(); tx.Commit();
        }
    }
    public void ScheduleMusicAutomation(MusicMonitor monitor, DateTimeOffset now)
    {
        lock (_writeGate)
        {
            if (!MusicMonitorCurrent(monitor) || !monitor.Settings.Search) return;
            using var db = Open();
            foreach (var album in GetMonitoredMusicAlbums(monitor.Id, 0, 2001).Items.Where(item => item.Wanted && MusicMonitorPolicy.Released(item.Metadata.Date, now)))
            {
                if (Convert.ToInt32(Scalar(db, "SELECT COUNT(*) FROM music_automation_jobs WHERE status IN ('queued','running','waiting','grabbed')")) >= 2000) break;
                if (Scalar(db, "SELECT id FROM music_automation_jobs WHERE monitor_id=$id AND group_mbid=$group AND revision=$revision AND updated_at>$recent LIMIT 1",
                    ("$id", monitor.Id), ("$group", album.Metadata.Id), ("$revision", monitor.Revision), ("$recent", now.AddMinutes(-monitor.Settings.SearchIntervalMinutes).ToString("O"))) is string) continue;
                Execute(db, "INSERT OR IGNORE INTO music_automation_jobs(id,monitor_id,group_mbid,title,revision,status,due_at,updated_at) VALUES($job,$id,$group,$title,$revision,'queued',$now,$now)",
                    ("$job", Guid.NewGuid().ToString("N")), ("$id", monitor.Id), ("$group", album.Metadata.Id), ("$title", album.Metadata.Title), ("$revision", monitor.Revision), ("$now", now.ToString("O")));
            }
            Execute(db, "UPDATE music_monitors SET next_search=$next WHERE id=$id AND revision=$revision", ("$id", monitor.Id), ("$revision", monitor.Revision), ("$next", now.AddMinutes(monitor.Settings.SearchIntervalMinutes).ToString("O")));
        }
    }
    public void ScheduleDueMusicAutomation(DateTimeOffset now)
    {
        lock (_writeGate)
        {
            using var db = Open();
            var due = Query(db, MusicMonitorSelect + " WHERE enabled=1 AND json_extract(settings_json,'$.search')=1 AND (next_search IS NULL OR next_search<=$now) ORDER BY next_search LIMIT 5", ReadMusicMonitor, ("$now", now.ToString("O")));
            foreach (var monitor in due)
            {
                if (MusicMonitorCurrent(monitor)) ScheduleMusicAutomation(monitor, now);
                else Execute(db, "UPDATE music_monitors SET next_search=$next WHERE id=$id", ("$id", monitor.Id), ("$next", now.AddMinutes(30).ToString("O")));
            }
            Execute(db, "DELETE FROM music_automation_jobs WHERE download_id IS NULL AND status IN ('suggested','satisfied','failed','cancelled') AND updated_at<$old", ("$old", now.AddDays(-30).ToString("O")));
        }
    }
    private const string MusicJobSelect = "SELECT id,monitor_id,group_mbid,title,revision,status,attempts,due_at,message,result_id,fingerprint,download_id,release_json FROM music_automation_jobs";
    private static MusicAutomationJob ReadMusicJob(SqliteDataReader r) => new(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetInt32(4), r.GetString(5), r.GetInt32(6), r.GetString(7), r.GetString(8), Text(r, 9), Text(r, 10), Text(r, 11), r.IsDBNull(12) ? null : JsonSerializer.Deserialize<MusicRelease>(r.GetString(12), WebJson));
    public MusicAutomationJob? GetMusicAutomationJob(string id)
    {
        using var db = Open(); return Query(db, MusicJobSelect + " WHERE id=$id", ReadMusicJob, ("$id", id)).FirstOrDefault();
    }
    public IReadOnlyList<MusicAutomationJob> GetMusicAutomationJobs(string user)
    {
        using var db = Open(); return Query(db, MusicJobSelect + " WHERE monitor_id IN (SELECT id FROM music_monitors WHERE user_id=$user) ORDER BY updated_at DESC LIMIT 100", ReadMusicJob, ("$user", user));
    }
    public MusicAutomationJob? ClaimMusicAutomationJob(DateTimeOffset now)
    {
        lock (_writeGate)
        {
            if (ContainerUpdateBlocked) return null;
            using var db = Open();
            var id = Scalar(db, "SELECT j.id FROM music_automation_jobs j JOIN music_monitors m ON m.id=j.monitor_id WHERE j.status IN ('queued','waiting') AND j.due_at<=$now AND m.enabled=1 AND m.revision=j.revision ORDER BY j.due_at,j.id LIMIT 1", ("$now", now.ToString("O"))) as string;
            if (id is null) return null;
            Execute(db, "UPDATE music_automation_jobs SET status='running',attempts=attempts+1,lease_until=$lease,updated_at=$now,budget_wait_reason=NULL WHERE id=$id", ("$id", id), ("$lease", DateTimeOffset.UtcNow.AddMinutes(3).ToString("O")), ("$now", now.ToString("O")));
            return GetMusicAutomationJob(id);
        }
    }
    public void RecoverMusicAutomation()
    {
        lock (_writeGate) { using var db = Open(); Execute(db, "UPDATE music_automation_jobs SET status='queued',due_at=$now,lease_until=NULL,message='Kesilen müzik işi toparlandı.' WHERE status='running'", ("$now", Now())); }
    }
    public bool MusicJobWanted(MusicAutomationJob job)
    {
        var monitor = GetMusicMonitor(job.MonitorId);
        if (monitor is null || monitor.Revision != job.Revision || !MusicMonitorCurrent(monitor) || !monitor.Settings.Search) return false;
        using var db = Open(); return Scalar(db, "SELECT group_mbid FROM music_monitor_albums WHERE monitor_id=$id AND group_mbid=$group AND wanted=1", ("$id", monitor.Id), ("$group", job.GroupId)) is string;
    }
    public int MusicReleaseCursor(string job)
    {
        using var db = Open(); return Convert.ToInt32(Scalar(db, "SELECT release_cursor FROM music_automation_jobs WHERE id=$id", ("$id", job)));
    }
    public void SaveMusicJobRelease(MusicAutomationJob job, MusicRelease? release, int cursor = 0)
    {
        lock (_writeGate) { using var db = Open(); Execute(db, "UPDATE music_automation_jobs SET release_json=$json,release_cursor=$cursor WHERE id=$id AND revision=$revision AND status='running'",
            ("$id", job.Id), ("$revision", job.Revision), ("$json", release is null ? null : JsonSerializer.Serialize(release, WebJson)), ("$cursor", cursor)); }
    }
    public void FinishMusicAutomation(MusicAutomationJob job, string status, string message, DateTimeOffset? retryAt = null, string? budgetReason = null)
    {
        if (status is not ("waiting" or "suggested" or "satisfied" or "cancelled" or "blocked" or "failed")) throw new ArgumentOutOfRangeException(nameof(status));
        lock (_writeGate)
        {
            using var db = Open(); using var tx = db.BeginTransaction();
            using var command = Command(db, "UPDATE music_automation_jobs SET status=$status,message=$message,lease_until=NULL,due_at=$due,updated_at=$now,budget_wait_reason=$budget WHERE id=$id AND revision=$revision AND status IN ('running','grabbed')",
                ("$id", job.Id), ("$revision", job.Revision), ("$status", status), ("$message", message), ("$due", (retryAt ?? DateTimeOffset.UtcNow).ToString("O")), ("$now", Now()), ("$budget", budgetReason));
            command.Transaction = tx; var changed = command.ExecuteNonQuery();
            if (changed > 0 && status is "suggested" or "failed" or "satisfied" && GetMusicMonitor(job.MonitorId) is { } monitor)
                AddNotification(db, tx, monitor.UserId, "music", job.Title, message, null, "music-job:" + job.Id + ":" + status);
            tx.Commit();
        }
    }
    public void SelectMusicAutomationResult(MusicAutomationJob job, MusicSourceResult result)
    {
        lock (_writeGate) { using var db = Open(); Execute(db, "UPDATE music_automation_jobs SET result_id=$result,fingerprint=$fingerprint WHERE id=$id AND revision=$revision AND status='running'", ("$id", job.Id), ("$revision", job.Revision), ("$result", result.Id), ("$fingerprint", result.Fingerprint)); }
    }
    public int MusicGrabsToday(string user, DateTimeOffset now)
    {
        using var db = Open(); return Convert.ToInt32(Scalar(db, "SELECT COUNT(*) FROM music_automation_jobs j JOIN music_monitors m ON m.id=j.monitor_id WHERE m.user_id=$user AND j.grabbed_at>=$start",
            ("$user", user), ("$start", new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero).ToString("O"))));
    }
    public IReadOnlyList<MusicAutomationJob> MusicTransfers()
    {
        using var db = Open(); return Query(db, MusicJobSelect + " WHERE status='grabbed'", ReadMusicJob);
    }
    public bool MusicReleaseBlocked(string group, string fingerprint)
    {
        using var db = Open(); return Scalar(db, "SELECT fingerprint FROM music_release_blocklist WHERE group_mbid=$group AND fingerprint=$fp", ("$group", group), ("$fp", fingerprint)) is string;
    }
    public IReadOnlyList<MusicBlockedRelease> GetMusicBlockedReleases()
    {
        using var db = Open(); return Query(db, "SELECT group_mbid,fingerprint,reason,blocked_at FROM music_release_blocklist ORDER BY blocked_at DESC LIMIT 200", r => new MusicBlockedRelease(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3)));
    }
    public void UnblockMusicRelease(string group, string fingerprint)
    {
        lock (_writeGate) { using var db = Open(); Execute(db, "DELETE FROM music_release_blocklist WHERE group_mbid=$group AND fingerprint=$fp", ("$group", group), ("$fp", fingerprint)); }
    }
    public void BlockMusicRelease(MusicAutomationJob job, string reason)
    {
        lock (_writeGate)
        {
            using var db = Open();
            if (!string.IsNullOrEmpty(job.Fingerprint)) Execute(db, "INSERT INTO music_release_blocklist(group_mbid,fingerprint,reason,blocked_at) VALUES($group,$fp,$reason,$now) ON CONFLICT(group_mbid,fingerprint) DO UPDATE SET reason=$reason,blocked_at=$now",
                ("$group", job.GroupId), ("$fp", job.Fingerprint), ("$reason", reason), ("$now", Now()));
            FinishMusicAutomation(job, "failed", reason);
        }
    }
    public MusicTargetState MusicTarget(MusicRelease release)
    {
        using var db = Open();
        var pending = Scalar(db, "SELECT d.id FROM downloads d JOIN music_download_links a ON a.download_id=d.id WHERE json_extract(a.release_json,'$.metadata.groupId')=$group AND d.desired_state IN ('running','paused') AND d.status<>'failed' AND (d.imported_at IS NULL OR a.status<>'linked') LIMIT 1", ("$group", release.Metadata.GroupId)) is string;
        var rows = Query(db, """
            SELECT m.id,l.path,m.relative_path,m.available,ma.fingerprint,ma.analysis_json,COALESCE(tm.recording_mbid,t.recording_mbid),COALESCE(am.release_mbid,a.release_mbid),COALESCE(tm.disc_number,t.disc_number),COALESCE(tm.track_number,t.track_number)
            FROM music_tracks t JOIN music_albums a ON a.id=t.album_id JOIN media m ON m.id=t.media_id JOIN libraries l ON l.id=m.library_id
            LEFT JOIN music_album_matches am ON am.album_id=a.id LEFT JOIN music_track_matches tm ON tm.media_id=t.media_id LEFT JOIN media_analysis ma ON ma.media_id=m.id
            WHERE l.enabled=1 AND m.excluded=0 AND COALESCE(am.group_mbid,a.release_group_mbid)=$group
            """, r => (Id: r.GetString(0), Root: r.GetString(1), Path: r.GetString(2), Available: r.GetBoolean(3), Fingerprint: Text(r, 4), Json: Text(r, 5), Recording: Text(r, 6), Release: Text(r, 7), Disc: r.GetInt32(8), Track: r.GetInt32(9)), ("$group", release.Metadata.GroupId));
        var covered = new HashSet<string>(); var qualities = new List<MusicAudioQuality>(); var offline = false; var unknown = false;
        foreach (var row in rows)
        {
            try
            {
                if (!row.Available) { offline = true; continue; }
                var path = MediaPaths.ResolveFile(row.Root, row.Path);
                if (row.Fingerprint != MediaAnalysisService.Fingerprint(new FileInfo(path)) || row.Json is null) { unknown = true; continue; }
                var probe = JsonSerializer.Deserialize<ProbeResult>(row.Json, WebJson)!; var quality = MusicReleaseEvaluator.Quality(probe);
                if (quality.Tier == 0 || quality.Tier == 1 && quality.BitrateKbps == 0) unknown = true;
                qualities.Add(quality);
                foreach (var track in release.Tracks.Where(track => row.Recording == track.RecordingId || row.Release == release.Metadata.Id && row.Disc == track.Disc && row.Track == track.Position)) covered.Add(track.Id);
            }
            catch (Exception ex) when (ex is IOException or DomainException or UnauthorizedAccessException) { offline = true; }
        }
        var complete = covered.Count == release.Tracks.Length;
        var best = qualities.OrderByDescending(value => value.Tier).ThenByDescending(value => value.BitDepth).ThenByDescending(value => value.SampleRate).ThenByDescending(value => value.BitrateKbps).FirstOrDefault();
        if (best is not null && best.Tier >= 2) best = best with { BitDepth = qualities.Where(value => value.Tier >= 2).Max(value => value.BitDepth), SampleRate = qualities.Where(value => value.Tier >= 2).Max(value => value.SampleRate) };
        return new(complete, pending, offline && !complete, unknown, best, qualities.ToArray());
    }
    private void ValidateMusicAutomationAcquisition(MusicDownloadContext context)
    {
        if (context.Automation is not { } lease) return;
        var job = GetMusicAutomationJob(lease.JobId) ?? throw new DomainException("Müzik işi bulunamadı.", 409);
        var monitor = GetMusicMonitor(job.MonitorId)!; using var db = Open();
        if (job.Status != "running" || job.Revision != lease.Revision || !MusicJobWanted(job) || !monitor.Settings.AutoGrab
            || job.ResultId != context.ResultId || job.Release?.Metadata.Id != context.Release.Metadata.Id
            || Scalar(db, "SELECT id FROM music_automation_jobs WHERE id=$id AND lease_until>$now", ("$id", job.Id), ("$now", Now())) is not string)
            throw new DomainException("Müzik otomasyon niyeti değişti; eski sonuç edinilmedi.", 409);
        if (MusicGrabsToday(monitor.UserId, DateTimeOffset.UtcNow) >= monitor.Settings.MaximumDailyGrabs) throw new DomainException("Günlük müzik edinme kotası dolu.", 429);
        var target = MusicTarget(context.Release); var candidate = GetMusicSource(context.ResultId).Result;
        if (!MusicAutomationCoordinator.NeedsSearch(target, monitor.Settings) || !MusicAutomationCoordinator.AcceptCandidate(candidate, target, monitor.Settings)
            || MusicReleaseBlocked(job.GroupId, candidate.Fingerprint)) throw new DomainException("Müzik hedefi veya kalite kararı değişti.", 409);
    }
}
