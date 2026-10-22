using System.Text.Json;
using HomeServer.Core;

namespace HomeServer.Infrastructure;

public sealed partial class MediaStore
{
    public TransferPolicy GetTransferPolicy(string id)
    {
        using var db = Open(); var json = Scalar(db, "SELECT settings_json FROM transfer_policies WHERE download_id=$id", ("$id", id)) as string;
        return json is null ? new() : JsonSerializer.Deserialize<TransferPolicy>(json, WebJson)!;
    }
    public SourceSeedRules GetSourceSeedRules(string downloadId)
    {
        using var db = Open(); var json = Scalar(db, "SELECT r.seed_rules_json FROM acquisition_links a JOIN release_results r ON r.id=a.release_id WHERE a.download_id=$id UNION ALL SELECT r.seed_rules_json FROM music_download_links a JOIN music_release_results r ON r.id=a.result_id WHERE a.download_id=$id LIMIT 1", ("$id", downloadId)) as string;
        return json is null ? new() : JsonSerializer.Deserialize<SourceSeedRules>(json, WebJson)!;
    }
    public TransferDefaults GetTransferDefaults(IConfiguration configuration) => GetSetting("downloads.defaults") is { } json
        ? JsonSerializer.Deserialize<TransferDefaults>(json, WebJson)! : new(configuration.GetValue("Home:Downloads:MaximumDownloadKiB", 0), configuration.GetValue("Home:Downloads:MaximumUploadKiB", 512));
    public void SaveTransferDefaults(TransferDefaults input)
    {
        if (input.DownloadKiB is < 0 or > 1000000 || input.UploadKiB is < 0 or > 1000000 || input.MaximumConnections is < 1 or > 500) throw new DomainException("Global aktarım sınırları geçersiz.");
        SetSetting("downloads.defaults", JsonSerializer.Serialize(input, WebJson));
    }
    public TransferPolicy SaveTransferPolicy(string id, TransferPolicy input)
    {
        if (input.Priority is < 1 or > 100 || input.DownloadKiB is < 0 or > 1000000 || input.UploadKiB is < 0 or > 1000000
            || input.SeedRatio is { } ratio && (!double.IsFinite(ratio) || ratio is < 0 or > 100) || input.SeedMinutes is < 0 or > 525600 || input.SeedStop is not ("either" or "both")
            || input.Files?.Length > 20000 || input.Files?.Distinct(StringComparer.OrdinalIgnoreCase).Count() != input.Files?.Length) throw new DomainException("Aktarım tercihi geçersiz.");
        if (input.FilePriorities?.Count > 20000 || input.FilePriorities?.Any(value => value.Value is < 1 or > 5) == true
            || input.FilePriorities?.Keys.Distinct(StringComparer.OrdinalIgnoreCase).Count() != input.FilePriorities?.Count) throw new DomainException("Dosya öncelikleri geçersiz.");
        lock (_writeGate)
        {
            var download = GetDownload(id) ?? throw new DomainException("İndirme bulunamadı.", 404);
            var previous = GetTransferPolicy(id);
            if (download.ImportedAt is not null && !Enumerable.SequenceEqual(previous.Files ?? [], input.Files ?? [], StringComparer.OrdinalIgnoreCase))
                throw new DomainException("Yayımlanmış paketin dosya seçimi değiştirilemez.", 409);
            using var db = Open(); using var tx = db.BeginTransaction();
            using var command = Command(db, """
                INSERT INTO transfer_policies(download_id,settings_json,updated_at) VALUES($id,$json,$now) ON CONFLICT(download_id) DO UPDATE SET settings_json=$json,updated_at=$now;
                UPDATE downloads SET priority=$priority,revision=revision+1,status=CASE WHEN desired_state='running' THEN 'queued' ELSE desired_state END,
                  message='Yeni aktarım tercihi uygulanacak.',download_speed=0,upload_speed=0,budget_wait_reason=NULL,budget_retry_at=NULL,updated_at=$now WHERE id=$id;
                """, ("$id", id), ("$json", JsonSerializer.Serialize(input, WebJson)), ("$priority", input.Priority), ("$now", Now()));
            command.Transaction = tx; command.ExecuteNonQuery(); tx.Commit(); return input;
        }
    }
    public void RecordTransferCounters(string id, int revision, string run, long uploaded, long received, long seedSeconds, DownloadBudgetRun? budget = null)
    {
        if (uploaded < 0 || received < 0 || seedSeconds < 0) return;
        if (budget is not null)
        {
            if (budget.DownloadId != id || budget.DownloadRevision != revision) throw new DomainException("Aktarım sayacı farklı işe ait.", 409);
            RecordBudgetTransferCounters(budget, run, uploaded, received, seedSeconds, DateTimeOffset.UtcNow); return;
        }
        lock (_writeGate)
        {
            using var db = Open(); Execute(db, """
                UPDATE downloads SET uploaded_bytes=uploaded_bytes+MAX(0,$up-CASE WHEN telemetry_run=$run THEN run_uploaded ELSE 0 END),
                  received_bytes=received_bytes+MAX(0,$down-CASE WHEN telemetry_run=$run THEN run_received ELSE 0 END),
                  seed_seconds=seed_seconds+MAX(0,$seed-CASE WHEN telemetry_run=$run THEN run_seed_seconds ELSE 0 END),
                  telemetry_run=$run,run_uploaded=MAX($up,CASE WHEN telemetry_run=$run THEN run_uploaded ELSE 0 END),
                  run_received=MAX($down,CASE WHEN telemetry_run=$run THEN run_received ELSE 0 END),run_seed_seconds=MAX($seed,CASE WHEN telemetry_run=$run THEN run_seed_seconds ELSE 0 END)
                WHERE id=$id AND revision=$revision AND desired_state='running'
                """, ("$id", id), ("$revision", revision), ("$run", run), ("$up", uploaded), ("$down", received), ("$seed", seedSeconds));
        }
    }
    public void FinishSeeding(string id, int revision)
    {
        lock (_writeGate) { using var db = Open(); Execute(db, "UPDATE downloads SET desired_state='paused',status='completed',message='Paylaşım hedefi karşılandı; payload korunuyor.',revision=revision+1,download_speed=0,upload_speed=0,updated_at=$now WHERE id=$id AND revision=$revision AND desired_state='running'",
            ("$id", id), ("$revision", revision), ("$now", Now())); }
    }
    public void ArchiveDownload(string id)
    {
        lock (_writeGate)
        {
            var record = GetDownload(id) ?? throw new DomainException("İndirme bulunamadı.", 404);
            if (record.DesiredState == "running") throw new DomainException("İndirmeyi önce duraklat veya iptal et.", 409);
            using var db = Open(); Execute(db, "UPDATE downloads SET archived=1 WHERE id=$id", ("$id", id));
        }
    }
    public void RestoreDownload(string id)
    {
        lock (_writeGate)
        {
            using var db = Open();
            var record = GetDownload(id) ?? throw new DomainException("İndirme bulunamadı.", 404);
            if (!record.Archived) return;
            if (Convert.ToInt32(Scalar(db, "SELECT COUNT(*) FROM downloads WHERE archived=0")) >= 200) throw new DomainException("Etkin indirme geçmişi sınırına ulaşıldı.", 409);
            Execute(db, "UPDATE downloads SET archived=0 WHERE id=$id", ("$id", id));
        }
    }
    public void CleanDownloadPayload(string id, int revision, Action clean)
    {
        lock (_writeGate)
        {
            var record = GetDownload(id) ?? throw new DomainException("İndirme bulunamadı.", 404);
            if (record.Revision != revision || record.DesiredState == "running") throw new DomainException("İndirme komutu değişti; temizlik uygulanmadı.", 409);
            clean();
            using var db = Open(); Execute(db, "UPDATE downloads SET payload_cleaned_at=$now,budget_generation=budget_generation+1,budget_charged_bytes=0,budget_wait_reason=NULL,budget_retry_at=NULL,message='Yönetilen staging temizlendi. Kütüphane dosyaları korunuyor.' WHERE id=$id", ("$id", id), ("$now", Now()));
        }
    }
}
