using System.Globalization;
using HomeServer.Core;
using Microsoft.Data.Sqlite;

namespace HomeServer.Infrastructure;

public sealed partial class MediaStore
{
    private static string BudgetPeriod(DateTimeOffset now) => now.UtcDateTime.ToString("yyyy-MM", CultureInfo.InvariantCulture);
    public static DateTimeOffset NextBudgetPeriod(DateTimeOffset now) => new DateTimeOffset(now.UtcDateTime.Year, now.UtcDateTime.Month, 1, 0, 0, 0, TimeSpan.Zero).AddMonths(1);
    private static int BudgetExecute(SqliteConnection db, SqliteTransaction? tx, string sql, params (string, object?)[] args)
    { using var command = Command(db, sql, args); command.Transaction = tx; return command.ExecuteNonQuery(); }
    private static long BudgetNumber(SqliteConnection db, SqliteTransaction? tx, string sql, params (string, object?)[] args)
    { using var command = Command(db, sql, args); command.Transaction = tx; var value = command.ExecuteScalar(); return value is null or DBNull ? 0 : Convert.ToInt64(value, CultureInfo.InvariantCulture); }
    public DownloadBudgetPolicy GetDownloadBudget(string userId)
    {
        using var db = Open(); return Query(db, "SELECT enabled,monthly_acquisitions,monthly_payload_bytes,maximum_payload_bytes,maximum_concurrent,revision FROM user_download_budgets WHERE user_id=$user",
            r => new DownloadBudgetPolicy(r.GetBoolean(0), r.IsDBNull(1) ? null : r.GetInt32(1), r.IsDBNull(2) ? null : r.GetInt64(2), r.IsDBNull(3) ? null : r.GetInt64(3), r.GetInt32(4), r.GetInt32(5)), ("$user", userId)).FirstOrDefault() ?? new();
    }
    public DownloadBudgetPolicy SaveDownloadBudget(string userId, DownloadBudgetPolicy input)
    {
        if (input.MonthlyAcquisitions is < 0 or > 100000 || input.MonthlyPayloadBytes is < 0 or > 70368744177664 || input.MaximumPayloadBytes is < 0 or > 4398046511104 || input.MaximumConcurrent is < 0 or > 10)
            throw new DomainException("İndirme sınırları: ayda 0–100.000 edinme, 0–64 TiB paket verisi, paket başına 0–4 TiB ve 0–10 eşzamanlı aktarım.");
        lock (_writeGate)
        {
            _ = GetAccount(userId) ?? throw new DomainException("Hesap bulunamadı.", 404); var current = GetDownloadBudget(userId);
            if (current.Revision != input.Revision) throw new DomainException("Hesap indirme bütçesi değişti; güncel ayarı aç.", 409);
            using var db = Open(); using var tx = db.BeginTransaction();
            BudgetExecute(db, tx, """
                INSERT INTO user_download_budgets(user_id,enabled,monthly_acquisitions,monthly_payload_bytes,maximum_payload_bytes,maximum_concurrent,revision)
                  VALUES($user,$enabled,$count,$bytes,$file,$concurrent,$revision)
                ON CONFLICT(user_id) DO UPDATE SET enabled=$enabled,monthly_acquisitions=$count,monthly_payload_bytes=$bytes,maximum_payload_bytes=$file,maximum_concurrent=$concurrent,revision=$revision;
                UPDATE downloads SET revision=revision+1,status='queued',budget_wait_reason=NULL,budget_retry_at=NULL,download_speed=0,upload_speed=0,message='Yeni hesap indirme bütçesi uygulanacak.'
                  WHERE owner_id=$user AND desired_state='running' AND archived=0 AND status NOT IN ('failed','completed');
                UPDATE automation_jobs SET due_at=$now,budget_wait_reason=NULL WHERE status='waiting' AND budget_wait_reason IS NOT NULL AND content_id IN (SELECT content_id FROM automation_rules WHERE updated_by=$user);
                UPDATE music_automation_jobs SET due_at=$now,budget_wait_reason=NULL WHERE status='waiting' AND budget_wait_reason IS NOT NULL AND monitor_id IN (SELECT id FROM music_monitors WHERE user_id=$user);
                """, ("$user", userId), ("$enabled", input.Enabled), ("$count", input.MonthlyAcquisitions), ("$bytes", input.MonthlyPayloadBytes), ("$file", input.MaximumPayloadBytes), ("$concurrent", input.MaximumConcurrent), ("$revision", current.Revision + 1), ("$now", Now()));
            BudgetEvent(db, tx, userId, null, "Hesap indirme bütçesi", "policy", 0, DateTimeOffset.UtcNow); tx.Commit(); return input with { Revision = current.Revision + 1 };
        }
    }
    public DownloadPrincipal DownloadActor(string userId, string? loginId, string origin = "manual")
    {
        var secret = GetAccountSecret(userId) ?? throw new DomainException("Hesap bulunamadı.", 401);
        var actor = new DownloadPrincipal(userId, secret.Version, loginId, origin); RequireDownloadPrincipal(actor); return actor;
    }
    public Account RequireDownloadPrincipal(DownloadPrincipal principal)
    {
        var user = GetAccountSecret(principal.UserId);
        if (user is null || user.Version != principal.UserVersion || !user.Account.Enabled || !(user.Account.IsAdmin || user.Account.CanAcquire)) throw new DomainException("Edinme hesabı veya yetkisi değişti.", 403);
        if (principal.Origin == "manual" && (principal.LoginId is null || ValidateLoginSession(principal.LoginId, principal.UserId) is null)) throw new DomainException("Edinme giriş oturumu sona erdi.", 401);
        if (principal.Origin is not ("manual" or "video-automation" or "music-automation")) throw new DomainException("Edinme hesap bağlamı geçersiz.");
        return user.Account;
    }
    public DownloadPrincipal VideoBudgetActor(AutomationJob job)
    {
        using var db = Open(); var user = Scalar(db, "SELECT updated_by FROM automation_rules WHERE content_id=$id AND revision=$revision", ("$id", job.ContentId), ("$revision", job.RuleRevision)) as string;
        return user is null ? throw new DomainException("Otomasyon hesabı veya revizyonu değişti.", 409) : DownloadActor(user, null, "video-automation");
    }
    public DownloadPrincipal? ResolveDownloadPrincipal(DownloadPrincipal? actor, AcquisitionContext? video = null, MusicDownloadContext? music = null)
    {
        if (video?.Automation is { } automatic)
        {
            ValidateAutomationAcquisition(video); using var db = Open();
            var userId = Scalar(db, "SELECT r.updated_by FROM automation_jobs j JOIN automation_rules r ON r.content_id=j.content_id AND r.revision=j.rule_revision WHERE j.id=$job", ("$job", automatic.JobId)) as string;
            if (userId is null) throw new DomainException("Otomasyonun edinme hesabı bulunamadı.", 403);
            actor = DownloadActor(userId, null, "video-automation");
        }
        if (music?.Automation is { } musical)
        {
            ValidateMusicAutomationAcquisition(music); var job = GetMusicAutomationJob(musical.JobId)!; var monitor = GetMusicMonitor(job.MonitorId)!;
            actor = DownloadActor(monitor.UserId, null, "music-automation");
        }
        if (actor is not null) RequireDownloadPrincipal(actor);
        return actor;
    }
    public DownloadRecord RequireDownloadAccess(string id, Account viewer)
    {
        var current = GetAccount(viewer.Id); var record = GetDownload(id);
        if (current is not { Enabled: true } || record is null || !current.IsAdmin && record.Ownership?.UserId != current.Id) throw new DomainException("İndirme bulunamadı veya bu hesaba ait değil.", 404);
        return record;
    }
    public void CheckDownloadAdmission(DownloadPrincipal? principal, DateTimeOffset now)
    {
        if (principal is null) return;
        lock (_writeGate) { RequireDownloadPrincipal(principal); using var db = Open(); RequireBudgetAdmission(db, null, principal.UserId, now); }
    }
    private void RequireBudgetAdmission(SqliteConnection db, SqliteTransaction? tx, string userId, DateTimeOffset now)
    {
        var policy = GetDownloadBudget(userId);
        if (!policy.Enabled) throw new DownloadBudgetException("disabled", "Bu hesabın internet edinme izni bütçe ayarında kapalı.");
        var count = BudgetNumber(db, tx, "SELECT acquisitions FROM download_budget_periods WHERE user_id=$user AND period=$period", ("$user", userId), ("$period", BudgetPeriod(now)));
        if (policy.MonthlyAcquisitions is { } maximum && count >= maximum) throw new DownloadBudgetException("monthly-acquisitions", "Hesabın aylık yeni edinme sayısı doldu. UTC ay başında yenilenir.", NextBudgetPeriod(now));
        if (policy.MonthlyPayloadBytes == 0 || policy.MaximumPayloadBytes == 0) throw new DownloadBudgetException("payload-disabled", "Bu hesabın paket boyutu bütçesi sıfır; yeni edinme kapalı.");
    }
    private void DebitDownloadAdmission(SqliteConnection db, SqliteTransaction tx, DownloadPrincipal? principal, string id, string title, DateTimeOffset now)
    {
        if (principal is null) return;
        RequireBudgetAdmission(db, tx, principal.UserId, now);
        BudgetExecute(db, tx, "INSERT INTO download_budget_periods(user_id,period,acquisitions) VALUES($user,$period,1) ON CONFLICT(user_id,period) DO UPDATE SET acquisitions=acquisitions+1", ("$user", principal.UserId), ("$period", BudgetPeriod(now)));
        BudgetEvent(db, tx, principal.UserId, id, title, "admission", 0, now);
    }
    private static void BudgetEvent(SqliteConnection db, SqliteTransaction tx, string user, string? id, string title, string action, long bytes, DateTimeOffset now)
        => BudgetExecute(db, tx, "INSERT INTO download_budget_events(id,user_id,download_id,title,action,bytes,period,created_at) VALUES($event,$user,$id,$title,$action,$bytes,$period,$now)",
            ("$event", Guid.NewGuid().ToString("N")), ("$user", user), ("$id", id), ("$title", title[..Math.Min(title.Length, 200)]), ("$action", action), ("$bytes", bytes), ("$period", BudgetPeriod(now)), ("$now", now.ToString("O")));
    public DownloadRecord AssignDownloadAccount(string id, AssignDownloadOwner input, Account administrator)
    {
        lock (_writeGate)
        {
            if (GetAccount(administrator.Id) is not { Enabled: true, IsAdmin: true }) throw new DomainException("Yönetici yetkisi gerekli.", 403);
            var record = GetDownload(id) ?? throw new DomainException("İndirme bulunamadı.", 404);
            if (record.Ownership?.UserId is not null) throw new DomainException("Hesap ataması yalnız hesabı bilinmeyen eski kayıtlar içindir.", 409);
            if (record.DesiredState == "running" || record.Revision != input.Revision) throw new DomainException("Önce eski aktarımı duraklat; sonra güncel revizyonla hesap ata.", 409);
            var principal = DownloadActor(input.UserId, null, "video-automation"); using var db = Open();
            if (BudgetNumber(db, null, "SELECT COUNT(*) FROM download_budget_runs WHERE download_id=$id AND phase<>'closed'", ("$id", id)) > 0) throw new DomainException("Aktarımın kapanması bekleniyor.", 409);
            using var tx = db.BeginTransaction(); DebitDownloadAdmission(db, tx, principal, id, record.Name, DateTimeOffset.UtcNow);
            BudgetExecute(db, tx, "UPDATE downloads SET owner_id=$user,budget_origin='legacy-assigned',revision=revision+1,budget_wait_reason=NULL,budget_retry_at=NULL WHERE id=$id", ("$user", input.UserId), ("$id", id)); tx.Commit(); return GetDownload(id)!;
        }
    }
    private const string BudgetRunSelect = "SELECT id,download_id,download_revision,user_id,policy_revision,generation FROM download_budget_runs";
    private static DownloadBudgetRun ReadBudgetRun(SqliteDataReader r) => new(r.GetString(0), r.GetString(1), r.GetInt32(2), Text(r, 3), r.GetInt32(4), r.GetInt32(5));
    private DownloadBudgetPolicy BudgetPolicyFor(DownloadRecord record)
    {
        if (record.Ownership?.UserId is not { } id) return new();
        var user = GetAccount(id);
        if (user is not { Enabled: true } || !(user.IsAdmin || user.CanAcquire)) throw new DownloadBudgetException("account-access", "Edinme hesabı devre dışı veya indirme yetkisi kaldırılmış.");
        var policy = GetDownloadBudget(id);
        if (!policy.Enabled) throw new DownloadBudgetException("disabled", "Hesabın indirme bütçesi kapalı; aktarım bekliyor.");
        return policy;
    }
    public DownloadBudgetRun BeginDownloadBudgetRun(string id, int revision, string runId, DateTimeOffset now)
    {
        _ = AppFiles.Id(runId);
        lock (_writeGate)
        {
            var record = GetDownload(id) ?? throw new DomainException("İndirme bulunamadı.", 404);
            if (record.Revision != revision || record.DesiredState != "running" || record.Archived) throw new OperationCanceledException();
            var policy = BudgetPolicyFor(record);
            using var db = Open(); using var tx = db.BeginTransaction();
            if (record.Ownership?.UserId is { } user && BudgetNumber(db, tx, "SELECT COUNT(*) FROM download_budget_runs WHERE user_id=$user AND phase='transfer'", ("$user", user)) >= policy.MaximumConcurrent)
                throw new DownloadBudgetException("concurrency", "Hesabın eşzamanlı indirme sınırı dolu; diğer aktarım tamamlanınca devam edecek.", now.AddSeconds(5));
            if (BudgetNumber(db, tx, "SELECT COUNT(*) FROM download_budget_runs WHERE download_id=$id AND phase<>'closed'", ("$id", id)) > 0) throw new DownloadBudgetException("closing", "Önceki aktarımın kapanması bekleniyor.", now.AddSeconds(2));
            var result = new DownloadBudgetRun(runId, id, revision, record.Ownership?.UserId, policy.Revision, record.Ownership?.Generation ?? 0);
            BudgetExecute(db, tx, """
                INSERT INTO download_budget_runs(id,download_id,download_revision,user_id,policy_revision,generation,phase,period,created_at,lease_until)
                  VALUES($run,$id,$revision,$user,$policy,$generation,$phase,$period,$now,$until);
                UPDATE downloads SET budget_wait_reason=NULL,budget_retry_at=NULL WHERE id=$id;
                """, ("$run", runId), ("$id", id), ("$revision", revision), ("$user", result.UserId), ("$policy", policy.Revision), ("$generation", result.Generation), ("$phase", "transfer"), ("$period", BudgetPeriod(now)), ("$now", now.ToString("O")), ("$until", now.AddSeconds(90).ToString("O")));
            tx.Commit(); return result;
        }
    }
    private (DownloadRecord Record, DownloadBudgetPolicy Policy) RequireDownloadRun(DownloadBudgetRun run, DateTimeOffset now)
    {
        var record = GetDownload(run.DownloadId) ?? throw new OperationCanceledException();
        if (record.Revision != run.DownloadRevision || record.DesiredState != "running" || record.Archived || record.Ownership?.Generation != run.Generation || record.Ownership?.UserId != run.UserId) throw new OperationCanceledException();
        var policy = BudgetPolicyFor(record); if (policy.Revision != run.PolicyRevision) throw new OperationCanceledException();
        using var db = Open();
        if (BudgetNumber(db, null, "SELECT COUNT(*) FROM download_budget_runs WHERE id=$run AND download_id=$id AND download_revision=$revision AND user_id IS $user AND policy_revision=$policy AND generation=$generation AND phase<>'closed' AND lease_until>$now",
            ("$run", run.Id), ("$id", run.DownloadId), ("$revision", run.DownloadRevision), ("$user", run.UserId), ("$policy", run.PolicyRevision), ("$generation", run.Generation), ("$now", now.ToString("O"))) == 0) throw new OperationCanceledException();
        return (record, policy);
    }
    public bool TouchDownloadBudgetRun(DownloadBudgetRun run, DateTimeOffset now)
    {
        lock (_writeGate)
        {
            try
            {
                RequireDownloadRun(run, now); using var db = Open(); Execute(db, "UPDATE download_budget_runs SET lease_until=$until WHERE id=$run AND phase<>'closed'", ("$run", run.Id), ("$until", now.AddSeconds(90).ToString("O"))); return true;
            }
            catch (DownloadBudgetException ex) { HoldDownloadBudget(run.DownloadId, run.DownloadRevision, ex, now); return false; }
            catch (OperationCanceledException) { return false; }
        }
    }
    public void ReserveDownloadPayload(DownloadBudgetRun? run, long bytes, DateTimeOffset now)
    {
        if (run is null) return;
        if (bytes is < 1 or > 4503599627370496) throw new DomainException("Doğrulanmış paket boyutu geçersiz.");
        lock (_writeGate)
        {
            var (record, policy) = RequireDownloadRun(run, now);
            if (run.UserId is not null && policy.MonthlyPayloadBytes == 0) throw new DownloadBudgetException("payload-disabled", "Hesabın paket boyutu bütçesi sıfır; aktarım bekliyor.");
            if (policy.MaximumPayloadBytes is { } maximum && bytes > maximum) throw new DownloadBudgetException("payload-size", "Paket boyutu hesabın tek-paket sınırını aşıyor. Dosya seçimini veya bütçeyi düzenle.");
            using var db = Open(); using var tx = db.BeginTransaction(); var additional = run.UserId is null ? 0 : Math.Max(0, bytes - record.Ownership!.ChargedBytes); var period = BudgetPeriod(now);
            if (run.UserId is { } user && policy.MonthlyPayloadBytes is { } limit)
            {
                var committed = BudgetNumber(db, tx, "SELECT committed_bytes FROM download_budget_periods WHERE user_id=$user AND period=$period", ("$user", user), ("$period", period));
                var reserved = BudgetNumber(db, tx, "SELECT COALESCE(SUM(reserved_bytes),0) FROM download_budget_runs WHERE user_id=$user AND id<>$run AND phase<>'closed' AND period=$period", ("$user", user), ("$run", run.Id), ("$period", period));
                if (committed > limit || reserved > limit - committed || additional > limit - committed - reserved) throw new DownloadBudgetException("monthly-payload", "Hesabın aylık paket boyutu bütçesi yetersiz. UTC ay başında veya bütçe artırıldığında yeniden denenir.", NextBudgetPeriod(now));
            }
            BudgetExecute(db, tx, "UPDATE download_budget_runs SET reserved_bytes=$reserved,payload_bytes=$size,period=$period WHERE id=$run", ("$run", run.Id), ("$reserved", additional), ("$size", bytes), ("$period", period)); tx.Commit();
        }
    }
    public void CommitDownloadPayloadStart(DownloadBudgetRun? run, long bytes, DateTimeOffset now)
    {
        if (run is null) return;
        lock (_writeGate)
        {
            ReserveDownloadPayload(run, bytes, now); var (record, _) = RequireDownloadRun(run, now); var additional = Math.Max(0, bytes - record.Ownership!.ChargedBytes);
            using var db = Open(); using var tx = db.BeginTransaction();
            if (run.UserId is { } user && additional > 0)
            {
                BudgetExecute(db, tx, "INSERT INTO download_budget_periods(user_id,period,committed_bytes) VALUES($user,$period,$bytes) ON CONFLICT(user_id,period) DO UPDATE SET committed_bytes=committed_bytes+$bytes", ("$user", user), ("$period", BudgetPeriod(now)), ("$bytes", additional));
                BudgetEvent(db, tx, user, run.DownloadId, record.Name, "payload", additional, now);
            }
            BudgetExecute(db, tx, "UPDATE downloads SET budget_charged_bytes=MAX(budget_charged_bytes,$bytes) WHERE id=$id; UPDATE download_budget_runs SET reserved_bytes=0 WHERE id=$run", ("$id", run.DownloadId), ("$run", run.Id), ("$bytes", bytes)); tx.Commit();
        }
    }
    public void SetDownloadBudgetSeeding(DownloadBudgetRun run)
    {
        lock (_writeGate) { using var db = Open(); Execute(db, "UPDATE download_budget_runs SET phase='seeding',reserved_bytes=0 WHERE id=$run AND phase='transfer'; UPDATE downloads SET payload_cleaned_at=NULL WHERE id=$id AND revision=$revision", ("$run", run.Id), ("$id", run.DownloadId), ("$revision", run.DownloadRevision)); }
    }
    public void CloseDownloadBudgetRun(DownloadBudgetRun run, DateTimeOffset now)
    {
        lock (_writeGate) { using var db = Open(); Execute(db, "UPDATE download_budget_runs SET phase='closed',reserved_bytes=0,ended_at=$now WHERE id=$run AND phase<>'closed'", ("$run", run.Id), ("$now", now.ToString("O"))); }
    }
    public void HoldDownloadBudget(string id, int revision, DownloadBudgetException reason, DateTimeOffset now)
    {
        lock (_writeGate) { using var db = Open(); Execute(db, """
            UPDATE downloads SET status='budget-waiting',budget_wait_reason=$reason,budget_retry_at=$retry,message=$message,download_speed=0,upload_speed=0,updated_at=$now
              WHERE id=$id AND revision=$revision AND desired_state='running'
            """, ("$id", id), ("$revision", revision), ("$reason", reason.Code), ("$retry", (reason.RetryAt ?? now.AddMinutes(5)).ToString("O")), ("$message", reason.Message), ("$now", now.ToString("O"))); }
    }
    public void RecoverDownloadBudgets(DateTimeOffset now)
    {
        lock (_writeGate) { using var db = Open(); Execute(db, "UPDATE download_budget_runs SET phase='closed',reserved_bytes=0,ended_at=$now WHERE phase<>'closed'", ("$now", now.ToString("O"))); }
    }
    public void PruneDownloadBudgets(DateTimeOffset now)
    {
        lock (_writeGate)
        {
            using var db = Open(); Execute(db, """
                DELETE FROM download_budget_runs WHERE phase='closed' AND ended_at<$runs;
                DELETE FROM download_budget_events WHERE created_at<$events OR id IN (SELECT id FROM download_budget_events ORDER BY created_at DESC LIMIT -1 OFFSET 50000);
                DELETE FROM download_budget_periods WHERE period<$period;
                """, ("$runs", now.AddDays(-2).ToString("O")), ("$events", now.AddDays(-90).ToString("O")), ("$period", BudgetPeriod(now.AddYears(-3))));
        }
    }
    public DownloadBudgetUsage GetDownloadBudgetUsage(string userId, DateTimeOffset now)
    {
        var account = GetAccount(userId) ?? throw new DomainException("Hesap bulunamadı.", 404); using var db = Open(); var period = BudgetPeriod(now);
        var values = Query(db, "SELECT acquisitions,committed_bytes,received_bytes,uploaded_bytes FROM download_budget_periods WHERE user_id=$user AND period=$period", r => (Count: r.GetInt32(0), Committed: r.GetInt64(1), Received: r.GetInt64(2), Uploaded: r.GetInt64(3)), ("$user", userId), ("$period", period)).FirstOrDefault();
        return new(userId, account.DisplayName, GetDownloadBudget(userId), period, NextBudgetPeriod(now).ToString("O"), values.Count, values.Committed,
            BudgetNumber(db, null, "SELECT COALESCE(SUM(reserved_bytes),0) FROM download_budget_runs WHERE user_id=$user AND phase<>'closed' AND period=$period", ("$user", userId), ("$period", period)), values.Received, values.Uploaded,
            (int)BudgetNumber(db, null, "SELECT COUNT(*) FROM download_budget_runs WHERE user_id=$user AND phase='transfer'", ("$user", userId)), (int)BudgetNumber(db, null, "SELECT COUNT(*) FROM downloads WHERE owner_id=$user AND archived=0 AND desired_state='running' AND status='budget-waiting'", ("$user", userId)));
    }
    public DownloadBudgetHistory GetDownloadBudgetHistory(string userId, int offset = 0, int limit = 30)
    {
        offset = Math.Clamp(offset, 0, 50000); limit = Math.Clamp(limit, 1, 100); using var db = Open();
        var rows = Query(db, "SELECT id,download_id,title,action,bytes,period,created_at FROM download_budget_events WHERE user_id=$user ORDER BY created_at DESC,id LIMIT $limit OFFSET $offset",
            r => new DownloadBudgetEvent(r.GetString(0), Text(r, 1), r.GetString(2), r.GetString(3), r.GetInt64(4), r.GetString(5), r.GetString(6)), ("$user", userId), ("$offset", offset), ("$limit", limit));
        return new(rows, (int)BudgetNumber(db, null, "SELECT COUNT(*) FROM download_budget_events WHERE user_id=$user", ("$user", userId)), offset, limit);
    }
    private void RecordBudgetTransferCounters(DownloadBudgetRun run, string receipt, long uploaded, long received, long seedSeconds, DateTimeOffset now)
    {
        lock (_writeGate)
        {
            using var db = Open(); using var tx = db.BeginTransaction();
            if (BudgetNumber(db, tx, "SELECT COUNT(*) FROM download_budget_runs WHERE id=$run AND download_id=$id AND download_revision=$revision AND user_id IS $user AND policy_revision=$policy AND generation=$generation AND phase<>'closed'",
                ("$run", run.Id), ("$id", run.DownloadId), ("$revision", run.DownloadRevision), ("$user", run.UserId), ("$policy", run.PolicyRevision), ("$generation", run.Generation)) == 0) return;
            using var lookup = Command(db, "SELECT budget_run_id,uploaded,received,seed_seconds FROM download_counter_receipts WHERE id=$receipt", ("$receipt", receipt)); lookup.Transaction = tx;
            long oldUp = 0, oldDown = 0, oldSeed = 0;
            using (var reader = lookup.ExecuteReader()) if (reader.Read()) { if (reader.GetString(0) != run.Id) throw new DomainException("Aktarım sayaç oturumu farklı.", 409); oldUp = reader.GetInt64(1); oldDown = reader.GetInt64(2); oldSeed = reader.GetInt64(3); }
            var up = Math.Max(0, uploaded - oldUp); var down = Math.Max(0, received - oldDown); var seed = Math.Max(0, seedSeconds - oldSeed);
            BudgetExecute(db, tx, """
                INSERT INTO download_counter_receipts(id,budget_run_id,uploaded,received,seed_seconds) VALUES($receipt,$run,$up,$down,$seed)
                  ON CONFLICT(id) DO UPDATE SET uploaded=MAX(uploaded,$up),received=MAX(received,$down),seed_seconds=MAX(seed_seconds,$seed);
                UPDATE downloads SET uploaded_bytes=uploaded_bytes+$deltaUp,received_bytes=received_bytes+$deltaDown,seed_seconds=seed_seconds+$deltaSeed WHERE id=$id;
                """, ("$receipt", receipt), ("$run", run.Id), ("$id", run.DownloadId), ("$up", uploaded), ("$down", received), ("$seed", seedSeconds), ("$deltaUp", up), ("$deltaDown", down), ("$deltaSeed", seed));
            if (run.UserId is { } user && (up > 0 || down > 0)) BudgetExecute(db, tx, """
                INSERT INTO download_budget_periods(user_id,period,received_bytes,uploaded_bytes) VALUES($user,$period,$down,$up)
                  ON CONFLICT(user_id,period) DO UPDATE SET received_bytes=received_bytes+$down,uploaded_bytes=uploaded_bytes+$up
                """, ("$user", user), ("$period", BudgetPeriod(now)), ("$down", down), ("$up", up));
            tx.Commit();
        }
    }
}
