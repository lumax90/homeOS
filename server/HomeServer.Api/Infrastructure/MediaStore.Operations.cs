using HomeServer.Core;
using Microsoft.Data.Sqlite;

namespace HomeServer.Infrastructure;

public sealed partial class MediaStore
{
    public const int LatestSchema=44;
    public SystemJob QueueBackup()
    {
        lock (_writeGate)
        {
            using var db = Open();
            var existing = Scalar(db, "SELECT id FROM system_jobs WHERE kind='backup' AND status IN ('queued','running') LIMIT 1") as string;
            if (existing is not null) return GetSystemJobs().Single(job => job.Id == existing);
            var id = Guid.NewGuid().ToString("N");
            Execute(db, "INSERT INTO system_jobs(id,kind,status,message,created_at) VALUES($id,'backup','queued','Yedekleme sırası bekleniyor.',$now)", ("$id", id), ("$now", Now()));
            return GetSystemJobs().Single(job => job.Id == id);
        }
    }
    public IReadOnlyList<SystemJob> GetSystemJobs()
    {
        using var db = Open(); return Query(db, "SELECT id,kind,status,message,created_at,finished_at FROM system_jobs ORDER BY created_at DESC LIMIT 100",
            r => new SystemJob(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), Text(r, 5)));
    }
    public SystemJob? ClaimSystemJob()
    {
        lock (_writeGate)
        {
            if (ContainerUpdateBlocked) return null;
            using var db = Open();
            var id = Scalar(db, "SELECT id FROM system_jobs WHERE status='queued' ORDER BY created_at LIMIT 1") as string;
            if (id is null) return null;
            Execute(db, "UPDATE system_jobs SET status='running',message='Tutarlı uygulama yedeği hazırlanıyor.' WHERE id=$id", ("$id", id));
            return GetSystemJobs().Single(job => job.Id == id);
        }
    }
    public void EndSystemJob(string id, string state, string message)
    {
        lock (_writeGate)
        {
            using var db = Open(); using var tx = db.BeginTransaction(); using var command = Command(db, "UPDATE system_jobs SET status=$state,message=$message,finished_at=$now WHERE id=$id", ("$id", id), ("$state", state), ("$message", message), ("$now", Now()));
            command.Transaction = tx; command.ExecuteNonQuery();
            if (state is "completed" or "failed") foreach (var admin in GetAccounts().Where(user => user.Enabled && user.IsAdmin)) AddNotification(db, tx, admin.Id, "system", "Home · Yedekleme", state == "completed" ? "Sunucu yedeği hazır." : "Yedekleme tamamlanamadı; Home'daki işlem geçmişini kontrol et.", null, "backup:" + id + ":" + state);
            tx.Commit();
        }
    }
    public void RecoverSystemJobs()
    {
        lock (_writeGate) { using var db = Open(); Execute(db, "UPDATE system_jobs SET status='queued',message='Kesilen yedekleme toparlanacak.' WHERE status='running'"); }
    }
    public int SnapshotState(string destination, CancellationToken token)
    {
        lock (_writeGate)
        {
            token.ThrowIfCancellationRequested();
            using var source = Open();
            using (var backup = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(destination, "home.db"), Pooling = false }.ToString()))
            {
                backup.Open(); source.BackupDatabase(backup);
            }
            var keys = Path.Combine(_paths.State, "keys");
            var certificate = Path.Combine(_paths.State, "https-certificate.protected");
            const string emulation="games/server-emulation.json";
            if(File.Exists(Path.Combine(_paths.State,emulation)))
            {
                var file=MediaPaths.ResolveFile(_paths.State,emulation);if(new FileInfo(file).Length>4096)throw new DomainException("Sunucu emülasyon ayarı geçersiz.",409);
                File.Copy(file,Path.Combine(AppFiles.DirectoryIn(destination,"games"),"server-emulation.json"),false);
            }
            if (File.Exists(certificate)) File.Copy(MediaPaths.ResolveFile(_paths.State, "https-certificate.protected"), Path.Combine(destination, "https-certificate.protected"), false);
            if (Directory.Exists(keys))
            {
                var targetKeys = AppFiles.DirectoryIn(destination, "keys");
                foreach (var file in Directory.EnumerateFiles(keys, "*.xml"))
                {
                    token.ThrowIfCancellationRequested();
                    File.Copy(MediaPaths.ResolveFile(_paths.State, Path.GetRelativePath(_paths.State, file)), Path.Combine(targetKeys, Path.GetFileName(file)), false);
                }
            }
            var subtitles = Query(source, "SELECT id,media_id,extension FROM managed_subtitles", r => (Id: r.GetString(0), Media: r.GetString(1), Extension: r.GetString(2)));
            foreach (var item in subtitles)
            {
                token.ThrowIfCancellationRequested();
                var folder = AppFiles.DirectoryIn(destination, "subtitles", AppFiles.Id(item.Media));
                var name = AppFiles.Id(item.Id) + "." + item.Extension;
                File.Copy(MediaPaths.ResolveFile(_paths.State, Path.Combine("subtitles", item.Media, name)), Path.Combine(folder, name), false);
            }
            foreach(var account in GetAccounts())
            {
                if(!Gaming.NativeSystems.SafeSegment(account.Id))throw new DomainException("Müzik bilgisi kapsamı geçersiz.");
                foreach(var kind in new[]{"preferences","radio"})
                {
                    token.ThrowIfCancellationRequested();var relative=Path.Combine("music",kind,account.Id+".json");if(!File.Exists(Path.Combine(_paths.State,relative)))continue;
                    var from=MediaPaths.ResolveFile(_paths.State,relative);if(new FileInfo(from).Length>(kind=="radio"?4*1024*1024:8192))throw new DomainException("Müzik tercihi yedek sınırını aşıyor.",413);
                    var folder=AppFiles.DirectoryIn(destination,"music",kind);File.Copy(from,Path.Combine(folder,account.Id+".json"),false);
                }
            }
            return Convert.ToInt32(Scalar(source, "PRAGMA user_version"));
        }
    }
}
