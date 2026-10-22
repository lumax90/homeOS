using HomeServer.Core;
using Microsoft.Data.Sqlite;

namespace HomeServer.Infrastructure;

public sealed partial class MediaStore
{
    private const string SetupSelect="SELECT id,user_id,login_id,user_version,provider_id,definition_hash,request_hash,indexer_id,existing_indexer_id,expected_revision,enabled,status,revision,lease,error,expires_at FROM source_setups";
    private static SourceSetup ReadSetup(SqliteDataReader row)=>new(row.GetString(0),row.GetString(1),row.GetString(2),row.GetInt32(3),row.GetString(4),row.GetString(5),row.GetString(6),Text(row,7),Text(row,8),row.IsDBNull(9)?null:row.GetInt32(9),row.GetBoolean(10),row.GetString(11),row.GetInt32(12),Text(row,13),Text(row,14),row.GetString(15));
    public SourceSetup? GetSourceSetup(string id){using var db=Open();return Query(db,SetupSelect+" WHERE id=$id",ReadSetup,("$id",id)).FirstOrDefault();}
    public void RequirePublishedIndexer(string id)
    {
        using var db=Open();if(Scalar(db,"SELECT id FROM indexers WHERE id=$id AND setup_pending=0",("$id",id)) is null)throw new DomainException("Kaynak bulunamadı veya kurulumu henüz tamamlanmadı.",404);
    }
    private AccountSecret RequireSourceSetupActor(string userId,string loginId)
    {
        var user=GetAccountSecret(userId);
        if(user is not{Account.Enabled:true,Account.IsAdmin:true}||ValidateLoginSession(loginId,userId) is null)throw new DomainException("Kaynak kurulumu için etkin yönetici oturumu gerekli.",403);
        return user;
    }
    public SourceSetup OwnSourceSetup(string id,string userId,string loginId)
    {
        var user=RequireSourceSetupActor(userId,loginId);var setup=GetSourceSetup(id)??throw new DomainException("Kaynak kurulumu bulunamadı.",404);
        if(setup.UserId!=userId||setup.LoginId!=loginId||setup.UserVersion!=user.Version)throw new DomainException("Kaynak kurulumu bu yönetici oturumuna ait değil.",403);
        return setup;
    }
    public SourceSetup BeginSourceSetup(SourceSetupInput input,IndexerInput connection,string protectedSecret,string? adapter,string protectedNetwork,string definitionHash,string requestHash,string userId,string loginId)
    {
        _=AppFiles.Id(input.CommandId);lock(_writeGate)
        {
            var actor=RequireSourceSetupActor(userId,loginId);PruneSourceSetups();using var db=Open();var old=GetSourceSetup(input.CommandId);
            if(old is not null)
            {
                _=OwnSourceSetup(old.Id,userId,loginId);
                if(old.RequestHash!=requestHash||old.DefinitionHash!=definitionHash)throw new DomainException("Kurulum komutu farklı bağlantı bilgileriyle kullanılmış; yeni test başlat.",409);
                if(old.Status is "ready" or "committed")return old;
                if(old.Status=="testing")throw new DomainException("Bu bağlantı testi zaten çalışıyor.",409);
                if(old.Status=="cancelled"||DateTimeOffset.Parse(old.ExpiresAt)<=DateTimeOffset.UtcNow||old.IndexerId is null)throw new DomainException("Kurulum isteği sona erdi; yeni test başlat.",410);
                Execute(db,"UPDATE source_setups SET status='testing',revision=revision+1,lease=$lease,error=NULL,updated_at=$now WHERE id=$id; UPDATE indexers SET caps_json=NULL,retry_after=NULL,last_error=NULL WHERE id=$source AND setup_pending=1",
                    ("$id",old.Id),("$source",old.IndexerId),("$lease",Guid.NewGuid().ToString("N")),("$now",Now()));return GetSourceSetup(old.Id)!;
            }
            if(input.ExistingIndexerId is{}existing)
            {
                RequirePublishedIndexer(existing);if(GetIndexer(existing).Info.Revision!=input.ExpectedRevision)throw new DomainException("Düzenlenen kaynak değişti; ayarlarını yeniden aç.",409);
            }
            if(Convert.ToInt32(Scalar(db,"SELECT COUNT(*) FROM source_setups WHERE status<>'committed' AND user_id=$user",("$user",userId)))>=8||Convert.ToInt32(Scalar(db,"SELECT COUNT(*) FROM source_setups WHERE status<>'committed'"))>=64)throw new DomainException("Bekleyen kaynak testi sınırı dolu; eski testleri kapat.",429);
            var source=Guid.NewGuid().ToString("N");using var tx=db.BeginTransaction();
            MusicSql(db,tx,"""
                INSERT INTO indexers(id,name,endpoint,protocol,secret,enabled,priority,categories,allowed_hosts,created_at,setup_pending,provider_id)
                VALUES($source,$name,$endpoint,$protocol,$secret,0,$priority,$categories,$hosts,$now,1,$provider);
                INSERT INTO source_transports(indexer_id,adapter_id,protected_settings) VALUES($source,$adapter,$network);
                INSERT INTO source_setups(id,user_id,login_id,user_version,provider_id,definition_hash,request_hash,indexer_id,existing_indexer_id,expected_revision,enabled,status,lease,expires_at,created_at,updated_at)
                VALUES($id,$user,$login,$version,$provider,$definition,$hash,$source,$existing,$expected,$enabled,'testing',$lease,$expires,$now,$now);
                """,("$source",source),("$name",connection.Name),("$endpoint",connection.Endpoint),("$protocol",connection.Protocol),("$secret",protectedSecret),("$priority",connection.Priority),("$categories",connection.Categories),("$hosts",connection.AllowedHosts),
                ("$provider",input.ProviderId),("$adapter",adapter),("$network",protectedNetwork),("$id",input.CommandId),("$user",userId),("$login",loginId),("$version",actor.Version),("$definition",definitionHash),("$hash",requestHash),("$existing",input.ExistingIndexerId),("$expected",input.ExpectedRevision),("$enabled",input.Enabled),
                ("$lease",Guid.NewGuid().ToString("N")),("$expires",DateTimeOffset.UtcNow.AddMinutes(10).ToString("O")),("$now",Now()));
            tx.Commit();return GetSourceSetup(input.CommandId)!;
        }
    }
    public SourceSetup RequireSourceSetupWork(SourceSetup work)
    {
        var current=OwnSourceSetup(work.Id,work.UserId,work.LoginId);
        if(current.Status!="testing"||current.Lease!=work.Lease||current.Revision!=work.Revision||DateTimeOffset.Parse(current.ExpiresAt)<=DateTimeOffset.UtcNow)throw new OperationCanceledException();
        if(current.ExistingIndexerId is{}id&&GetIndexer(id).Info.Revision!=current.ExpectedRevision)throw new DomainException("Kaynak test sırasında başka bir işlemle değişti.",409);
        return current;
    }
    public SourceSetup CompleteSourceSetup(SourceSetup work)
    {
        lock(_writeGate)
        {
            _=RequireSourceSetupWork(work);var source=GetIndexer(work.IndexerId!);
            if(source.Info.Capabilities is null||source.Info.LastSuccessAt is null)throw new DomainException("Kaynak testi başarıyla tamamlanmadı.",409);
            using var db=Open();Execute(db,"UPDATE source_setups SET status='ready',revision=revision+1,lease=NULL,error=NULL,updated_at=$now WHERE id=$id",("$id",work.Id),("$now",Now()));return GetSourceSetup(work.Id)!;
        }
    }
    public void FailSourceSetup(SourceSetup work,string message)
    {
        lock(_writeGate){using var db=Open();Execute(db,"UPDATE source_setups SET status='failed',revision=revision+1,lease=NULL,error=$error,updated_at=$now WHERE id=$id AND status='testing' AND revision=$revision AND lease=$lease",("$id",work.Id),("$revision",work.Revision),("$lease",work.Lease),("$error",message),("$now",Now()));}
    }
    public SourceSetup ConnectSourceSetup(SourceSetupConnect command,string definitionHash,string userId,string loginId)
    {
        lock(_writeGate)
        {
            var setup=OwnSourceSetup(command.Id,userId,loginId);if(setup.Status=="committed")return setup;
            if(setup.Status!="ready"||setup.Revision!=command.Revision||setup.DefinitionHash!=definitionHash||DateTimeOffset.Parse(setup.ExpiresAt)<=DateTimeOffset.UtcNow||setup.IndexerId is null)throw new DomainException("Bağlantı testi eskidi veya tanım değişti; yeniden test et.",409);
            var source=GetIndexer(setup.IndexerId);var network=GetSourceTransport(setup.IndexerId);
            if(source.Info.Capabilities is null)throw new DomainException("Bağlantının arama yeteneği doğrulanmamış.",409);
            var destination=setup.ExistingIndexerId??setup.IndexerId;
            if(setup.ExpectedRevision is not null&&setup.ExistingIndexerId is null)throw new DomainException("Düzenlenen kaynak artık bulunamıyor.",409);
            if(setup.ExistingIndexerId is{}existing){RequirePublishedIndexer(existing);if(GetIndexer(existing).Info.Revision!=setup.ExpectedRevision)throw new DomainException("Düzenlenen kaynak arada değişti.",409);}
            else
            {
                if(GetIndexers().Count>=30)throw new DomainException("En fazla 30 kaynak bağlanabilir.");
                if(GetIndexers().Any(value=>value.ProviderId==setup.ProviderId&&value.Endpoint==source.Info.Endpoint))throw new DomainException("Bu site zaten bağlı; mevcut kaynağı düzenleyebilirsin.",409);
            }
            using var db=Open();using var tx=db.BeginTransaction();
            if(setup.ExistingIndexerId is not null)
            {
                MusicSql(db,tx,"""
                    UPDATE indexers SET name=$name,endpoint=$endpoint,protocol=$protocol,secret=$secret,priority=$priority,categories=$categories,allowed_hosts=$hosts,revision=revision+1 WHERE id=$destination;
                    INSERT INTO source_transports(indexer_id,adapter_id,protected_settings) VALUES($destination,$adapter,$network)
                    ON CONFLICT(indexer_id) DO UPDATE SET adapter_id=$adapter,protected_settings=$network,revision=revision+1;
                    """,("$destination",destination),("$name",source.Info.Name),("$endpoint",source.Info.Endpoint),("$protocol",source.Info.Protocol),("$secret",source.Secret),("$priority",source.Info.Priority),("$categories",source.Info.Categories),("$hosts",source.Info.AllowedHosts),("$adapter",network.Adapter),("$network",network.Settings));
            }
            MusicSql(db,tx,"""
                UPDATE indexers SET enabled=$enabled,setup_pending=0,provider_id=$provider,caps_json=$caps,last_checked_at=$checked,last_success_at=$checked,last_error=NULL,retry_after=NULL,failure_count=0,revision=revision+1 WHERE id=$destination;
                UPDATE source_setups SET status='committed',revision=revision+1,indexer_id=$destination,lease=NULL,error=NULL,updated_at=$now WHERE id=$id;
                """,("$destination",destination),("$enabled",setup.Enabled),("$provider",setup.ProviderId),("$caps",System.Text.Json.JsonSerializer.Serialize(source.Info.Capabilities,WebJson)),("$checked",source.Info.LastSuccessAt),("$id",setup.Id),("$now",Now()));
            if(setup.ExistingIndexerId is not null)MusicSql(db,tx,"DELETE FROM indexers WHERE id=$draft AND setup_pending=1",("$draft",setup.IndexerId));
            tx.Commit();return GetSourceSetup(setup.Id)!;
        }
    }
    public void CancelSourceSetup(string id,string userId,string loginId)
    {
        lock(_writeGate){var setup=OwnSourceSetup(id,userId,loginId);if(setup.Status=="committed")return;using var db=Open();Execute(db,"UPDATE source_setups SET status='cancelled',revision=revision+1,lease=NULL,updated_at=$now WHERE id=$id",("$id",id),("$now",Now()));}
    }
    public void PruneSourceSetups()
    {
        lock(_writeGate)
        {
            using var db=Open();using var tx=db.BeginTransaction();
            MusicSql(db,tx,"""
                DELETE FROM indexers WHERE setup_pending=1 AND id IN (SELECT indexer_id FROM source_setups WHERE expires_at<$now OR status='cancelled');
                DELETE FROM source_setups WHERE (status<>'committed' AND indexer_id IS NULL) OR updated_at<$old;
                """,("$now",Now()),("$old",DateTimeOffset.UtcNow.AddDays(-1).ToString("O")));
            tx.Commit();
        }
    }
    public void RecoverSourceSetups()
    {
        lock(_writeGate){using var db=Open();Execute(db,"UPDATE source_setups SET status='failed',revision=revision+1,lease=NULL,error='Sunucu yeniden başladı; bağlantıyı tekrar test et.' WHERE status='testing'");PruneSourceSetups();}
    }
}
