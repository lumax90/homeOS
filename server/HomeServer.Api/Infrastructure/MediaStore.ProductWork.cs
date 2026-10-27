using System.Text.Json;
using HomeServer.Core;

namespace HomeServer.Infrastructure;

public sealed partial class MediaStore
{
    private const string ProductJobSelect = "SELECT id,kind,user_id,user_version,scope_id,input_json,result_json,status,revision,lease,progress,message,attempts,due_at,created_at,updated_at FROM product_jobs";
    private static ProductJob ReadProductJob(Microsoft.Data.Sqlite.SqliteDataReader r) => new(r.GetString(0), r.GetString(1), r.GetString(2), r.GetInt32(3), Text(r,4), JsonSerializer.Deserialize<JsonElement>(r.GetString(5)), r.IsDBNull(6) ? null : JsonSerializer.Deserialize<JsonElement>(r.GetString(6)), r.GetString(7), r.GetInt32(8), Text(r,9), r.GetInt32(10), r.GetString(11), r.GetInt32(12), r.GetString(13), r.GetString(14), r.GetString(15));
    public ProductJob? GetProductJob(string id) { using var db=Open(); return Query(db,ProductJobSelect+" WHERE id=$id",ReadProductJob,("$id",id)).FirstOrDefault(); }
    public int PendingProductJobCount(string? kind=null){using var db=Open();return Convert.ToInt32(Scalar(db,"SELECT COUNT(*) FROM product_jobs WHERE status IN ('queued','running','applying') AND ($kind IS NULL OR kind=$kind)",("$kind",kind)));}
    public IReadOnlyList<ProductJob> GetProductJobs(Account actor,string? kind=null,string? scope=null)
    { using var db=Open(); return Query(db,ProductJobSelect+" WHERE user_id=$user AND ($kind IS NULL OR kind=$kind) AND ($scope IS NULL OR scope_id=$scope) ORDER BY created_at DESC LIMIT 100",ReadProductJob,("$user",actor.Id),("$kind",kind),("$scope",scope)); }
    public ProductJob QueueProductJob(string kind,string? scope,object input,Account actor)
    {
        var json=JsonSerializer.Serialize(input,WebJson); if(json.Length>4*1024*1024) throw new DomainException("İş girdisi boyut sınırını aşıyor.",413);
        lock(_writeGate)
        {
            var current=GetAccountSecret(actor.Id); if(current is not { Account.Enabled:true }) throw new DomainException("Etkin hesap gerekli.",403);
            if(kind=="game-install"&&scope is not null&&GameRemovalPending(scope))throw new DomainException("Oyun kaldırma işlemi sürüyor.",409);
            using var db=Open();
            var old=Query(db,ProductJobSelect+" WHERE user_id=$user AND kind=$kind AND scope_id IS $scope AND input_json=$input AND status IN ('queued','running')",ReadProductJob,("$user",actor.Id),("$kind",kind),("$scope",scope),("$input",json)).FirstOrDefault(); if(old is not null)return old;
            // A native uninstall receipt is its re-import tombstone until consumed. Do not
            // lose retained-save identity/reactivation semantics after routine job cleanup.
            Execute(db,"DELETE FROM product_jobs WHERE status NOT IN ('queued','running','applying') AND updated_at<$old AND NOT(kind='game-uninstall' AND status='completed' AND json_extract(input_json,'$.kind')='native' AND COALESCE(json_extract(result_json,'$.reimported'),0)=0)",("$old",DateTimeOffset.UtcNow.AddDays(-30).ToString("O")));
            if(Convert.ToInt32(Scalar(db,"SELECT COUNT(*) FROM product_jobs WHERE status IN ('queued','running','applying')"))>=128)throw new DomainException("İş kuyruğu dolu.",429);
            var id=Guid.NewGuid().ToString("N"); Execute(db,"INSERT INTO product_jobs(id,kind,user_id,user_version,scope_id,input_json,status,due_at,created_at,updated_at) VALUES($id,$kind,$user,$version,$scope,$input,'queued',$now,$now,$now)",("$id",id),("$kind",kind),("$user",actor.Id),("$version",current.Version),("$scope",scope),("$input",json),("$now",Now())); return GetProductJob(id)!;
        }
    }
    public ProductJob? ClaimProductJob(string[] kinds)
    {
        lock(_writeGate)
        {
            if(ContainerUpdateBlocked)return null;
            using var db=Open();var row=Query(db,ProductJobSelect+" WHERE status='queued' AND kind IN (SELECT value FROM json_each($kinds)) AND due_at<=$now ORDER BY due_at,id LIMIT 1",ReadProductJob,("$kinds",JsonSerializer.Serialize(kinds)),("$now",Now())).FirstOrDefault();if(row is null)return null;
            Execute(db,"UPDATE product_jobs SET status='running',revision=revision+1,lease=$lease,attempts=attempts+1,updated_at=$now WHERE id=$id",("$id",row.Id),("$lease",Guid.NewGuid().ToString("N")),("$now",Now()));return GetProductJob(row.Id);
        }
    }
    public Account RequireProductJob(ProductJob job,bool admin=false)
    {
        var current=GetProductJob(job.Id);var user=GetAccountSecret(job.UserId);
        if(current is null || current.Status is not ("running" or "applying") || current.Revision!=job.Revision || current.Lease!=job.Lease)throw new OperationCanceledException();
        if(user is null || !user.Account.Enabled || user.Version!=job.UserVersion || admin&&!user.Account.IsAdmin)throw new DomainException("İşin hesap yetkisi değişti.",403);
        if(job.Kind is "asr" or "segment-analysis" or "receiver-prepare" && (job.ScopeId is null||GetMedia(job.ScopeId,user.Account) is null))throw new DomainException("Medya işinin dosya erişimi değişti.",403);
        return user.Account;
    }
    public ProductJob ClaimProductApply(string id,int revision,Account actor)
    {
        lock(_writeGate)
        {
            RequireContainerWorkAllowed();
            var job=GetProductJob(id)??throw new DomainException("İş bulunamadı.",404);if(job.UserId!=actor.Id||GetAccountSecret(actor.Id)?.Version!=job.UserVersion)throw new DomainException("İş hesabı değişti.",403);
            if(job.Status=="completed")return job;if(job.Status!="proposed"||job.Revision!=revision)throw new DomainException("İş önerisi değişti.",409);
            using var db=Open();Execute(db,"UPDATE product_jobs SET status='applying',revision=revision+1,lease=$lease,updated_at=$now WHERE id=$id",("$id",id),("$lease",Guid.NewGuid().ToString("N")),("$now",Now()));return GetProductJob(id)!;
        }
    }
    public void ProductProgress(ProductJob job,int progress,string message)
    { lock(_writeGate){using var db=Open();Execute(db,"UPDATE product_jobs SET progress=$progress,message=$message,updated_at=$now WHERE id=$id AND revision=$revision AND lease=$lease AND status='running'",("$id",job.Id),("$revision",job.Revision),("$lease",job.Lease),("$progress",Math.Clamp(progress,0,99)),("$message",message),("$now",Now()));} }
    public void FinishProductJob(ProductJob job,object? result,string status="completed",string message="İş tamamlandı.")
    {
        if(status is not ("completed" or "proposed" or "failed" or "cancelled"))throw new ArgumentOutOfRangeException(nameof(status));
        lock(_writeGate){using var db=Open();Execute(db,"UPDATE product_jobs SET status=$status,result_json=$result,progress=CASE WHEN $status IN ('completed','proposed') THEN 100 ELSE progress END,message=$message,updated_at=$now,lease=NULL WHERE id=$id AND revision=$revision AND lease=$lease AND status IN ('running','applying')",("$id",job.Id),("$revision",job.Revision),("$lease",job.Lease),("$status",status),("$result",result is null?null:JsonSerializer.Serialize(result,WebJson)),("$message",message),("$now",Now()));}
    }
    public void CancelProductJob(string id,int revision,Account actor)
    {
        lock(_writeGate){var job=GetProductJob(id)??throw new DomainException("İş bulunamadı.",404);if(job.UserId!=actor.Id&&!actor.IsAdmin)throw new DomainException("İş bulunamadı.",404);if(job.Revision!=revision)throw new DomainException("İş değişti.",409);using var db=Open();Execute(db,"UPDATE product_jobs SET status='cancelled',revision=revision+1,lease=NULL,message='Kullanıcı iptal etti.',updated_at=$now WHERE id=$id AND status IN ('queued','running','proposed')",("$id",id),("$now",Now()));}
    }
    public void RecoverProductJobs(string[] kinds)
    {lock(_writeGate){using var db=Open();Execute(db,"UPDATE product_jobs SET status='queued',revision=revision+1,lease=NULL,due_at=$now,message='Kesilen iş yeniden başlayacak.' WHERE kind IN (SELECT value FROM json_each($kinds)) AND status IN ('running','applying')",("$kinds",JsonSerializer.Serialize(kinds)),("$now",Now()));}}
}
