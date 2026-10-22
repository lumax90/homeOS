using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using HomeServer.Core;
using Microsoft.Data.Sqlite;

namespace HomeServer.Infrastructure;

public sealed partial class MediaStore
{
    private const string IndexerSelect="SELECT id,name,endpoint,protocol,secret,enabled,priority,categories,allowed_hosts,caps_json,last_checked_at,last_success_at,last_error,retry_after,revision,provider_id FROM indexers";
    public IReadOnlyList<IndexerInfo> GetIndexers(){using var db=Open();return Query(db,IndexerSelect+" WHERE setup_pending=0 ORDER BY priority,name",ReadIndexer);}
    public IReadOnlySet<string> SourceConnectionIds(){using var db=Open();return Query(db,"SELECT id FROM indexers",row=>row.GetString(0)).ToHashSet(StringComparer.Ordinal);}
    public IndexerSecret GetIndexer(string id)
    {
        using var db=Open();var info=Query(db,IndexerSelect+" WHERE id=$id",ReadIndexer,("$id",id)).FirstOrDefault()??throw new DomainException("Kaynak bulunamadı.",404);
        return new IndexerSecret(info,(string)Scalar(db,"SELECT secret FROM indexers WHERE id=$id",("$id",id))!);
    }
    public IndexerInfo SaveIndexer(string? id,IndexerInput input,string encryptedSecret)
    {
        lock(_writeGate){using var db=Open();if(id is not null)RequirePublishedIndexer(id);id??=Guid.NewGuid().ToString("N");
            Execute(db,"""
                INSERT INTO indexers(id,name,endpoint,protocol,secret,enabled,priority,categories,allowed_hosts,created_at)
                VALUES($id,$name,$url,$protocol,$secret,$enabled,$priority,$categories,$hosts,$now)
                ON CONFLICT(id) DO UPDATE SET name=$name,endpoint=$url,protocol=$protocol,secret=$secret,enabled=$enabled,
                    priority=$priority,categories=$categories,allowed_hosts=$hosts,caps_json=NULL,last_error=NULL,retry_after=NULL,failure_count=0,revision=revision+1,provider_id=NULL
                """,("$id",id),("$name",input.Name.Trim()),("$url",input.Endpoint.Trim()),("$protocol",input.Protocol),
                ("$secret",encryptedSecret),("$enabled",input.Enabled),("$priority",input.Priority),("$categories",input.Categories),("$hosts",input.AllowedHosts),("$now",Now()));
            return GetIndexer(id).Info;}
    }
    public void RequireIndexerSnapshot(IndexerInfo expected)
    {
        if(GetIndexer(expected.Id).Info.Revision!=expected.Revision)throw new DomainException("Kaynak bağlantısı veya adaptörü değişti; aramayı yenile.",409);
    }
    public void SetIndexerHealth(string id,IndexerCapabilities? caps,string? error,DateTimeOffset? retry=null,int? expectedRevision=null)
    {
        lock(_writeGate)
        {
            if(expectedRevision is{}revision&&GetIndexer(id).Info.Revision!=revision)return;
            using var db=Open();
            if(error is not null && retry is null)
            {
                var until=Scalar(db,"SELECT retry_after FROM indexers WHERE id=$id",("$id",id)) as string;
                if(DateTimeOffset.TryParse(until,out var existing)&&existing>DateTimeOffset.UtcNow)return;
                var failures=Convert.ToInt32(Scalar(db,"SELECT failure_count FROM indexers WHERE id=$id",("$id",id)));
                retry=DateTimeOffset.UtcNow.AddSeconds(Math.Min(3600,15*Math.Pow(2,Math.Min(failures,8))));
            }
            Execute(db,"""
            UPDATE indexers SET caps_json=COALESCE($caps,caps_json),last_checked_at=$now,
                last_success_at=CASE WHEN $error IS NULL THEN $now ELSE last_success_at END,last_error=$error,
                failure_count=CASE WHEN $error IS NULL THEN 0 ELSE failure_count+1 END,
                retry_after=CASE WHEN $error IS NULL THEN NULL ELSE COALESCE($retry,retry_after) END WHERE id=$id
            """,("$id",id),("$caps",caps is null?null:JsonSerializer.Serialize(caps,WebJson)),("$now",Now()),("$error",error),("$retry",retry?.ToString("O")));
        }
    }
    public ReleaseResult SaveRelease(IndexerInfo indexer,ContentEntry target,ReleaseSearchInput search,QualityProfile profile,RawRelease raw,ReleaseEvaluation evaluation,string encryptedUrl)
    {
        var id=Guid.NewGuid().ToString("N");lock(_writeGate){RequireIndexerSnapshot(indexer);using var db=Open();Execute(db,"""
            INSERT INTO release_results(id,indexer_id,content_id,season_number,episode_number,profile_id,release_guid,title,protocol,size_bytes,seeders,published_at,
                protected_url,info_hash,evaluation_json,profile_json,created_at,expires_at,seed_rules_json,catalog_revision)
            VALUES($id,$source,$content,$season,$episode,$profile,$guid,$title,$protocol,$size,$seeders,$date,$url,$hash,$evaluation,$rules,$now,$expiry,$seedRules,$revision)
            """,("$id",id),("$source",indexer.Id),("$content",target.Id),("$season",search.Season),("$episode",search.Episode),("$profile",profile.Id),
            ("$guid",ReleaseFingerprint(raw)),("$title",raw.Title),("$protocol",raw.Protocol),("$size",raw.SizeBytes),("$seeders",raw.Seeders),("$date",raw.PublishedAt),
            ("$url",encryptedUrl),("$hash",raw.InfoHash),("$evaluation",JsonSerializer.Serialize(evaluation,WebJson)),("$rules",JsonSerializer.Serialize(profile.Rules,WebJson)),
            ("$now",Now()),("$expiry",DateTimeOffset.UtcNow.AddMinutes(30).ToString("O")),("$seedRules",JsonSerializer.Serialize(Sources.ReleaseEvaluator.SeedRules(raw),WebJson)),("$revision",target.CatalogRevision));}
        return new ReleaseResult(id,indexer.Id,indexer.Name,raw.Title,raw.Protocol,raw.SizeBytes,raw.Seeders,raw.PublishedAt,evaluation,null,indexer.Id+":"+ReleaseFingerprint(raw));
    }
    public StoredRelease GetRelease(string id)
    {
        using var db=Open();return Query(db,"""
            SELECT r.id,r.indexer_id,i.name,r.title,r.protocol,r.size_bytes,r.seeders,r.published_at,r.evaluation_json,r.download_id,
                r.content_id,r.season_number,r.episode_number,r.protected_url,r.profile_json,r.expires_at,r.info_hash,r.release_guid
            FROM release_results r JOIN indexers i ON i.id=r.indexer_id WHERE r.id=$id
            """,r=>new StoredRelease(new ReleaseResult(r.GetString(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetString(4),
                r.IsDBNull(5)?null:r.GetInt64(5),r.IsDBNull(6)?null:r.GetInt32(6),Text(r,7),JsonSerializer.Deserialize<ReleaseEvaluation>(r.GetString(8),WebJson)!,Text(r,9),r.GetString(1)+":"+r.GetString(17)),
                r.GetString(10),r.IsDBNull(11)?null:r.GetInt32(11),r.IsDBNull(12)?null:r.GetInt32(12),r.GetString(13),r.GetString(14),r.GetString(15),Text(r,16)),("$id",id)).FirstOrDefault()
                ??throw new DomainException("Arama sonucu bulunamadı. Aramayı yenile.",404);
    }
    public void PruneReleaseResults()
    {
        lock(_writeGate)
        {
            using var db=Open();
            Execute(db,"DELETE FROM release_results WHERE download_id IS NULL AND expires_at<$now AND NOT EXISTS(SELECT 1 FROM acquisition_links a WHERE a.release_id=release_results.id) AND NOT EXISTS(SELECT 1 FROM automation_jobs j WHERE j.release_id=release_results.id)",("$now",Now()));
            if(Convert.ToInt32(Scalar(db,"SELECT COUNT(*) FROM release_results"))>=20000)
                throw new DomainException("Arama önbelleği dolu. Süresi geçen sonuçlar temizlendikten sonra yeniden dene.",429);
        }
    }
    private static string ReleaseFingerprint(RawRelease release)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(release.InfoHash??release.Guid))).ToLowerInvariant();
    private static IndexerInfo ReadIndexer(SqliteDataReader r)=>new(r.GetString(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetString(4).Length>0,r.GetBoolean(5),r.GetInt32(6),r.GetString(7),r.GetString(8),
        r.IsDBNull(9)?null:JsonSerializer.Deserialize<IndexerCapabilities>(r.GetString(9),WebJson),Text(r,10),Text(r,11),Text(r,12),Text(r,13),r.GetInt32(14),Text(r,15));
}
